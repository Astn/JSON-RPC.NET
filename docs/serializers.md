# Serializers: how they plug in and how they are configured

JSON-RPC.Net 2.0 splits the work in two. The **core** owns the JSON-RPC envelope: it finds `method`,
`params` and `id`, resolves the method, binds parameters, invokes, and writes
`{"jsonrpc":"2.0","result":…,"id":…}` or the error object. A **serializer** owns values only: it turns
the raw bytes of one JSON value into a CLR value, and a CLR value into JSON bytes. Nothing from a
JSON library leaks into the core, so Json.NET, System.Text.Json and the built-in serializer are
interchangeable and each ships as its own package.

| Package | Serializer | Default? | Notes |
|---|---|---|---|
| `AustinHarris.JsonRpc` | `Jsmn.JsmnSerializer` | yes | no dependencies; span port of the jsmn tokenizer plus a reflection mapper with cached type plans; primitives and `Nullable<T>` bind without boxing |
| `AustinHarris.JsonRpc.Newtonsoft` | `Newtonsoft.NewtonsoftJsonRpcSerializer` | | Json.NET 13; lenient input; honours `JsonSerializerSettings`; the compatibility choice for code that relied on Json.NET behaviour |
| `AustinHarris.JsonRpc.SystemTextJson` | `SystemTextJson.SystemTextJsonRpcSerializer` | | `Utf8JsonReader`/`Utf8JsonWriter` directly on the request bytes; honours `JsonSerializerOptions` |

## The contract

```csharp
public abstract class JsonRpcSerializer
{
    public abstract string Name { get; }
    public virtual bool Lenient => false;                       // envelope reader accepts 'single quotes', bare keys, trailing commas
    public virtual JsonRpcRequestReader CreateReader();          // envelope cursor; default is the jsmn tokenizer
    public abstract T Read<T>(ReadOnlySpan<byte> utf8Json);      // exactly one JSON value in, T out
    public abstract object Read(ReadOnlySpan<byte> utf8Json, Type type);
    public abstract void Write<T>(IBufferWriter<byte> output, T value);
    public abstract void Write(IBufferWriter<byte> output, object value, Type type);
    // string adapters: Deserialize<T>(string), Serialize<T>(T) – transcoding conveniences
}
```

`JsonRpcRequestReader` is the envelope cursor. It parses a document once and hands the core
*slices* of the request (`MethodUtf8`, `IdRaw`, `ParamRaw(i)`, `ParamNameUtf8(i)`), so nothing is
materialised until a parameter is bound. Serializers may override `CreateReader()` with their own
scanner, but the default reader already runs at the speed of the tokenizer and calls back into the
serializer's `Read<T>` for each parameter.

Compiled invokers (one expression tree per registered method) call `reader.ReadParam<T>(i)` per
parameter and `serializer.Write<T>(output, result)` for the return value. Nothing is boxed on that
path; `object[]` and `DynamicInvoke` are gone.

## Choosing a serializer: three levels

Resolution order for every call is: **per-call argument** → **session** → **global** → built-in.

```csharp
// 1. Global default (process-wide; volatile, takes effect for subsequent calls)
Config.SetSerializer(new SystemTextJsonRpcSerializer());
Config.Serializer = null;                      // back to the built-in serializer

// 2. Per session (a Handler is a session)
Handler.GetSessionHandler("legacy-clients").Serializer = new NewtonsoftJsonRpcSerializer(settings);
Config.SetSerializer("legacy-clients", serializer);   // same thing

// 3. Per call (transport decides; wins over both)
string json = JsonRpcProcessor.ProcessSync(sessionId, request, context, serializer);
JsonRpcProcessor.Process(sessionId, requestBytes, output, context, serializer);
```

Use per-session when different endpoints of one process serve different clients (a strict
System.Text.Json API next to a lenient Json.NET one for old clients). Use per-call when the transport
negotiates it (a header, a route, a protocol version). The global default is for the common case of
one serializer everywhere.

Serializers must be thread-safe and are meant to be long-lived: construct one, share it. The
synchronous processor pools an envelope reader per thread. `ProcessAsync` uses separate transferable
leases and a bounded shared pool; a fresh serializer per call defeats reader reuse. A custom reader
must keep the selected request and its backing storage valid until `Release`, which can run on a
continuation thread after the operation terminates. Reads and writes remain synchronous individual
calls: only the service invocation is awaited. No span is carried across an await.

## Library-specific options

Each package accepts its own library's options in its constructor and nowhere else:

| Serializer | Options type | Constructor | Notes |
|---|---|---|---|
| jsmn | `bool lenient`, `int maxDepth` | `new JsmnSerializer(lenient: true, maxDepth: 64)` | `lenient` accepts `'single quotes'`, unquoted keys and trailing commas in the request; `maxDepth` bounds nesting (default 64) |
| Json.NET | `JsonSerializerSettings` | `new NewtonsoftJsonRpcSerializer(settings)` | one `JsonSerializer` is created from the settings and reused; `Lenient` is always on |
| System.Text.Json | `JsonSerializerOptions` | `new SystemTextJsonRpcSerializer(options)` | the package adds its wire-format converters (see below) to a copy of your options when they are missing |

### Nesting depth

Every serializer exposes `MaxDepth` (virtual on `JsonRpcSerializer`, default 64). The envelope reader
rejects a request deeper than that with `-32700` before any hook or binding runs, so recursive parameter
conversion is bounded by the same number the JSON library itself enforces: for jsmn it is the constructor
argument, for System.Text.Json it is `JsonSerializerOptions.MaxDepth` and for Json.NET it is
`JsonSerializerSettings.MaxDepth` (both 64 when unset). The root object and the `params` container each
count as one level.

The 1.x `JsonSerializerSettings` parameter on `JsonRpcProcessor.Process*` is gone from the core
because the core no longer references Json.NET. The Newtonsoft package provides the same overloads as
static helpers that build a `NewtonsoftJsonRpcSerializer(settings)` and forward to the core.

## What the core fixes and what the serializer decides

| Fixed by the core (identical for every serializer) | Decided by the serializer |
|---|---|
| envelope member order `jsonrpc, result|error, id`; compact output | how a result value / parameter / `error.data` is written and read |
| error object `{"code":…,"message":…,"data":…}` with `data` always present (null when absent) | POCO member naming and ordering (all three follow declaration order, PascalCase, nulls written) |
| id echoed byte-for-byte; `null` when the request had none or was invalid | numeric formatting (all three write whole float/double/decimal with `.0`) |
| batch shape: an array of responses whenever the batch produced one (a single response stays wrapped); a batch of notifications only produces nothing | coercions (number→bool, number→char, integer→float/decimal, ISO string→DateTime) |
| notifications (no `id`) never get a response, whatever the outcome; an invalid request object is not a notification and gets `-32600` with `"id":null` | leniency of the *values* (Json.NET accepts single-quoted strings; the others do not) |
| error codes: -32700 parse, -32600 invalid request/id, -32601 method (`data = {"method":…}`), -32602 params (missing/extra/count, unknown or repeated named parameter, or a value the serializer could not convert: `data = {"reason":"conversion","parameter":…,"index":…,"expectedType":…}`), -32603 method exception or a type the serializer cannot handle | how `JsonRequest.Params` looks to pre/post handlers (`JObject`/`JArray`, `JsonElement`, or `Dictionary<string,object>`/`List<object>`) |
| what counts as "could not convert": `JsonRpcBindException`, `FormatException`, `OverflowException`, `InvalidCastException` and any `JsonException` family (System.Text.Json's, Json.NET's) thrown while reading an argument | which values convert at all (Json.NET and the built-in serializer read `7` as the string `"7"` and `true` as `1`; System.Text.Json refuses the number) |
| `Exception` in `error.data` normalised to `ExceptionInfo {ClassName, Message, Source, StackTraceString, HResult, InnerException}`; `Source`, `StackTraceString`, `HResult` and `InnerException` are null/0 unless `Config.IncludeExceptionDetails` is true | |
| the error boundary: a hook, the materialisation of `JsonRequest.Params` for a hook, binding and the method itself all fail into a JSON-RPC error (`-32602` for an argument the serializer refused, `-32603` otherwise, unless the error handler maps it); nothing throws out of `JsonRpcProcessor` | |
| the request id as a method sees it (`Handler.RpcRequestId()` and friends): the reader's own JSON of the id, so a lenient single-quoted `'x'` reads as `"x"` | |
| case-insensitive envelope keys (`Method`, `ID`); exact-name matching of named parameters | case-insensitive member names inside POCOs (all three do this) |

The built-in serializer reproduces Json.NET's conventions so that a switch is invisible on the
wire; the test suite runs all 163 cases against each serializer to keep that true.

## Bytes in, bytes out

The native entry points take what a `PipeReader` gives you and write to what a `PipeWriter` or
`HttpResponse.BodyWriter` is:

```csharp
void Process(string sessionId, in ReadOnlySequence<byte> request, IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null);
void Process(string sessionId, ReadOnlyMemory<byte> request,      IBufferWriter<byte> output, …);
void Process(string sessionId, ReadOnlySpan<byte> request,        IBufferWriter<byte> output, …);
```

Nothing is written for a notification. Responses are rendered into a per-thread pooled buffer (so a
half-written result can be discarded when a method throws) and copied once into `output`.
`JsonFramer.TryReadDocument` slices complete documents out of a pipe buffer for raw-connection
transports. The `string` overloads (`ProcessSync`, `Task<string> Process`) transcode into the same
pooled buffers at the edge.

## Migrating from 1.x

- Replace `Process(…, JsonSerializerSettings settings)` with either `Config.SetSerializer(new NewtonsoftJsonRpcSerializer(settings))` or the helper overloads in the Newtonsoft package.
- `JsonRequest`, `JsonResponse`, `JsonRpcException` no longer carry Json.NET attributes; they are plain DTOs. `JsonRequest.Params` is whatever the active serializer's object model is; cast to `JObject`/`JArray` only when the Json.NET serializer is active.
- `SMD.Types` is now `Dictionary<int, Dictionary<string, object>>` and is a process-wide registry (it used to be reset every time a session was created).
- `Handler.Handle(JsonRequest)` still works; it round-trips the request through the serializer and the boxed path.
- The empty-batch error code is now the spec's `-32600` (it was `3200`).
- Batches: a trailing notification no longer leaves a dangling comma; a batch consisting only of notifications returns an empty string.
- A value the serializer cannot convert to the parameter's type is `-32602` with structured `data` (it was `-32603` carrying the exception); the serializers' own conversion exceptions are recognised without any change to a custom serializer, which may also throw `JsonRpcBindException` to say "the client's value is wrong".
- A type the built-in serializer does not support, or a class without a parameterless constructor, throws `NotSupportedException` from `Read` (it was `JsonRpcBindException`) and stays `-32603` on the wire.
