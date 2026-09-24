# Serializers: how they plug in and how they are configured

JSON-RPC.Net 2.0 splits the work in two. The **core** owns the JSON-RPC envelope: it finds `method`,
`params` and `id`, resolves the method, binds parameters, invokes, and writes
`{"jsonrpc":"2.0","result":…,"id":…}` or the error object. A **serializer** owns values only: it turns
the raw bytes of one JSON value into a CLR value, and a CLR value into JSON bytes. Nothing from a
JSON library leaks into the core, so Json.NET, System.Text.Json and the built-in serializer plug into
the same slot and each ships as its own package.

| Package | Serializer | Default? | Notes |
|---|---|---|---|
| `AustinHarris.JsonRpc` | `Jsmn.JsmnSerializer` | yes | no JSON library; span port of the jsmn tokenizer plus a reflection mapper with cached type plans; primitives and `Nullable<T>` bind without boxing |
| `AustinHarris.JsonRpc.Newtonsoft` | `Newtonsoft.NewtonsoftJsonRpcSerializer` | | Json.NET 13; lenient input; honours `JsonSerializerSettings`; the compatibility choice for code that relied on Json.NET behaviour |
| `AustinHarris.JsonRpc.SystemTextJson` | `SystemTextJson.SystemTextJsonRpcSerializer` | | `Utf8JsonReader`/`Utf8JsonWriter` directly on the request bytes; honours `JsonSerializerOptions` |

## The contract

```csharp
public abstract class JsonRpcSerializer
{
    public abstract string Name { get; }
    public virtual bool Lenient => false;                       // envelope reader accepts 'single quotes', bare keys, trailing commas
    public virtual int MaxDepth => 64;                          // nesting limit enforced by the envelope reader
    public virtual JsonRpcRequestReader CreateReader();          // envelope cursor; the base implementation uses the jsmn tokenizer
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

Resolution order for every call is: **per-call argument** → **session** → **process-wide** → built-in.

```csharp
// 1. Process-wide default (volatile, takes effect for subsequent calls)
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
System.Text.Json API next to a lenient Json.NET one for old clients; the AspNetCore package's
`MapJsonRpc(pattern, options)` overload maps one endpoint per session). Use per-call when the transport
negotiates it (a header, a route, a protocol version). The process-wide default is for the common case
of one serializer everywhere. This order applies to the serializer only; error and processing handlers
are per session, see the main README's [Configuration](../README.md#configuration) table.

Construct a serializer once and share it: serializers must be thread-safe. The synchronous fast path
keeps an envelope reader in per-thread scratch storage, reused while the serializer instance stays the
same (a re-entrant call gets its own scratch instance), so a new serializer per call throws that reuse
away. `ProcessAsync` takes readers from a bounded, transferable pool instead.

**If you write your own reader** (`CreateReader()`): under `ProcessAsync` a reader can be handed
between threads, and `Release` can run on a continuation thread after the call has finished. Keep the
selected request and its backing memory valid until `Release`. Parameter reads and result writes are
still synchronous, and only the service method is awaited, so no span is held across an await.

## Library-specific options

Each package accepts its own library's options in its constructor and nowhere else:

| Serializer | Options type | Constructor | Notes |
|---|---|---|---|
| built-in | `bool lenient`, `int maxDepth` | `new JsmnSerializer(lenient: true, maxDepth: 64)` | `lenient` accepts `'single quotes'`, unquoted keys and trailing commas in the request; `maxDepth` bounds nesting (default 64) |
| Json.NET | `JsonSerializerSettings` | `new NewtonsoftJsonRpcSerializer(settings)` | one `JsonSerializer` is created from the settings and reused; `Lenient` is always on |
| System.Text.Json | `JsonSerializerOptions` | `new SystemTextJsonRpcSerializer(options)` | the package adds its wire-format converters (see below) to a copy of your options when they are missing |

### Nesting depth

Every serializer exposes `MaxDepth` (virtual on `JsonRpcSerializer`, default 64). The envelope reader
rejects a request deeper than that with `-32700` before any handler or binding runs, so recursive parameter
conversion is bounded by the same number the JSON library itself enforces: for the built-in serializer it is
the constructor argument, for System.Text.Json it is `JsonSerializerOptions.MaxDepth` and for Json.NET it is
`JsonSerializerSettings.MaxDepth` (both 64 when unset). The root object and the `params` container each
count as one level.

## What the core decides and what the serializer decides

### The core decides (the same for every serializer)

- envelope member order (`jsonrpc`, then `result` or `error`, then `id`); compact output
- error object `{"code":…,"message":…,"data":…}` with `data` always present (null when absent)
- id echoed byte-for-byte; `null` when the request had none or was invalid
- batch shape: an array of responses whenever the batch produced one (a single response stays wrapped); a batch of notifications only produces nothing
- notifications (no `id`) never get a response, whatever the outcome; an invalid request object is not a notification and gets `-32600` with `"id":null`
- error codes: -32700 parse, -32600 invalid request/id, -32601 method (`data = {"method":…}`), -32602 params (missing/extra/count, unknown or repeated named parameter, or a value the serializer could not convert: `data = {"reason":"conversion","parameter":…,"index":…,"expectedType":…}`), -32603 method exception or a type the serializer cannot handle
- what counts as "could not convert": `JsonRpcBindException`, `FormatException`, `OverflowException`, `InvalidCastException` and any `JsonException` family (System.Text.Json's, Json.NET's) thrown while reading an argument
- `Exception` in `error.data` normalised to `ExceptionInfo {ClassName, Message, Source, StackTraceString, HResult, InnerException}`; `Source`, `StackTraceString`, `HResult` and `InnerException` are null/0 unless `Config.IncludeExceptionDetails` is true
- the error boundary: request, binding, handler and method failures become JSON-RPC errors (`-32602` for an argument the serializer refused, `-32603` otherwise, unless the error handler maps it). Invalid arguments (a null document), cancellation, and exceptions from your own error handler, custom reader or output writer reach the caller
- the request id as a method sees it (`Handler.RpcRequestId()` and friends): the reader's own JSON of the id, so a lenient single-quoted `'x'` reads as `"x"`
- case-insensitive envelope keys (`Method`, `ID`); exact-name matching of named parameters

### The serializer decides

- how a result value, a parameter or `error.data` is written and read
- POCO member naming and ordering (all three follow declaration order, PascalCase, nulls written)
- numeric formatting (all three write whole float/double/decimal with `.0`)
- coercions (number→bool, number→char, integer→float/decimal, ISO string→DateTime)
- leniency of the *values* (Json.NET accepts single-quoted strings; the others do not)
- how `JsonRequest.Params` looks to pre/post handlers (`JObject`/`JArray`, `JsonElement`, or `Dictionary<string,object>`/`List<object>`)
- which values convert at all (Json.NET and the built-in serializer read `7` as the string `"7"` and `true` as `1`; System.Text.Json refuses the number)
- case-insensitive member names inside POCOs (all three do this)

The three serializers align the envelope, primitives, dates and plain objects, and the test suite runs
its protocol cases against each of them. They are not behaviour-identical: accepted coercions, supported
CLR types, object models and custom options differ, so test client-visible requests and responses before
changing serializers.

## Bytes in, bytes out

The native entry points take what a `PipeReader` gives you and write to what a `PipeWriter` or
`HttpResponse.BodyWriter` is:

```csharp
void Process(string sessionId, in ReadOnlySequence<byte> request,
    IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null);

void Process(string sessionId, ReadOnlyMemory<byte> request,
    IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null);

void Process(string sessionId, ReadOnlySpan<byte> request,
    IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null);
```

Nothing is written for a notification. Responses are rendered into a per-thread pooled buffer (so a
half-written result can be discarded when a method throws) and copied once into `output`.
`JsonFramer.TryReadDocument` slices complete documents out of a pipe buffer for raw-connection
transports. The `string` overloads (`ProcessSync`, `Task<string> Process`) transcode into the same
pooled buffers at the edge.

## Upgrading from 1.x

The core no longer references Json.NET, so the 1.x `JsonSerializerSettings` parameter on
`JsonRpcProcessor.Process*` is gone. Replace it with `Config.SetSerializer(new NewtonsoftJsonRpcSerializer(settings))`,
or use the helper overloads in the
[Newtonsoft package](../AustinHarris.JsonRpc.Newtonsoft/README.md#settings-based-helpers-1x-compatibility),
which build that serializer and forward to the core.

Two things are specific to serializers: a custom serializer's own conversion exceptions are recognised as
"the client's value is wrong" without any change to it, and it may throw `JsonRpcBindException` to say the
same explicitly; and a type the built-in serializer does not support, or a class without a parameterless
constructor, throws `NotSupportedException` from `Read` (it was `JsonRpcBindException`) and stays `-32603`
on the wire.

For the complete list of protocol, error, batching, async, context and metadata changes, see
[Upgrading from 1.x](../README.md#upgrading-from-1x) in the main README.
