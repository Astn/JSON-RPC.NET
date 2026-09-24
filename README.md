
![Screenshot](http://i.imgur.com/rxHaXLb.png)

json-rpc.net
============
![Build Master](https://github.com/Astn/JSON-RPC.NET/workflows/Build%20Master/badge.svg) ![NuGet Badge](https://buildstats.info/nuget/AustinHarris.JsonRpc)

JSON-RPC.Net is a high performance [JSON-RPC 2.0](https://www.jsonrpc.org/specification) server for .NET. It turns a JSON-RPC request into a JSON-RPC response and stays out of the way of your transport: bytes in, bytes out. Host it in Kestrel, a console app, sockets, pipes, or inside the browser as WebAssembly.

Version 2.0 rebuilt the pipeline around UTF-8 bytes and made the JSON serializer pluggable. The core has no JSON library dependency; Json.NET and System.Text.Json ship as separate packages, and a built-in serializer needs neither. On one core it answers a small request in about 250 ns with no allocation; a Kestrel host on an 8-core desktop answers over 14 million requests per second over pipelined TCP.

Documentation site: [astn.github.io/JSON-RPC.NET](https://astn.github.io/JSON-RPC.NET/).

- [Packages](#packages)
- [Requirements](#requirements)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Hosting modes](#hosting-modes)
- [Configuration](#configuration)
- [Benchmarks](#benchmarks)
- [Upgrading from 1.x](#upgrading-from-1x)
- [Building](#building)

## Packages

| Package | What it is |
| --- | --- |
| `AustinHarris.JsonRpc` | The server. Envelope parsing, method dispatch, parameter binding, error mapping, sessions. Ships with a dependency-free serializer (a span port of [jsmn](https://github.com/zserge/jsmn) plus a cached reflection mapper). |
| `AustinHarris.JsonRpc.Newtonsoft` | Json.NET 13 serializer. The compatibility choice: `JsonSerializerSettings`, `[JsonProperty]`, converters, lenient input. |
| `AustinHarris.JsonRpc.SystemTextJson` | System.Text.Json serializer. `JsonSerializerOptions`, `Utf8JsonReader`/`Utf8JsonWriter` straight on the request bytes. |
| `AustinHarris.JsonRpc.AspNetCore` | Kestrel hosting: an HTTP endpoint on `PipeReader`/`BodyWriter`, a `ConnectionHandler` for raw TCP/Unix-socket/named-pipe connections, and DI registration of services. |

## Requirements

`AustinHarris.JsonRpc`, `.Newtonsoft` and `.SystemTextJson` target:

| Target | Covers |
| --- | --- |
| `netstandard2.0` | .NET Framework 4.6.1+, .NET Core 2.0+, Mono 5.4+, Xamarin, Unity 2018.1+ |
| `netstandard2.1` | .NET Core 3.0+, Mono 6.4+, Xamarin |
| `net8.0` | .NET 8 (LTS) |
| `net10.0` | .NET 10 (LTS) |

`AustinHarris.JsonRpc.AspNetCore` targets `net8.0` and `net10.0`.

Core dependencies: `NonBlocking` 2.1.2 (lock-free dictionary for the session registry) and, on `netstandard` only, `System.Memory`; `netstandard2.0` also references `System.Threading.Tasks.Extensions` for `ValueTask`. The core uses no reflection emit, so it runs under the WebAssembly interpreter, AOT and trimmed builds.

## Installation

```
dotnet add package AustinHarris.JsonRpc
```

Add `AustinHarris.JsonRpc.Newtonsoft` or `AustinHarris.JsonRpc.SystemTextJson` if you want that serializer, and `AustinHarris.JsonRpc.AspNetCore` to host in Kestrel.

To host inside classic ASP.NET (System.Web) there is also `AustinHarris.JsonRpc.AspNet`, which targets .NET Framework 4.0 only and is built from its own legacy project.

## Getting started

### 1. Declare a service

Derive from `JsonRpcService` and mark the methods you want to expose with `[JsonRpcMethod]`. Constructing the service registers it with the default session, so you only need to keep the instance alive.

```csharp
using AustinHarris.JsonRpc;

public class CalculatorService : JsonRpcService
{
    [JsonRpcMethod]                 // exposed as "add"
    private double add(double l, double r) => l + r;

    [JsonRpcMethod("multiply")]     // exposed under an explicit name
    public int Multiply(int l, int r) => l * r;

    [JsonRpcMethod]
    public string StringMe(string x) => x;
}
```

Methods can be `private`; parameters may be positional (`"params":[1,2]`) or named (`"params":{"l":1,"r":2}`). Optional parameters with default values are honoured, and a parameter's JSON name can be overridden with `[JsonRpcParam("name")]`. Any class works, not only `JsonRpcService` subclasses: bind an instance with `ServiceBinder.BindService(sessionId, instance)`.

A method does not need a class at all. Any delegate becomes a method with `ServiceBinder.BindMethod`; a lambda keeps its parameter names for named params:

```csharp
ServiceBinder.BindMethod("add", (double l, double r) => l + r);
ServiceBinder.BindMethod("greet", (string who) => "hello " + who);                            // {"method":"greet","params":{"who":"you"}}
ServiceBinder.BindMethod(sessionId, "scale", (double v, double f) => v * f,
    parameterNames: new[] { "value", null }, defaults: new Dictionary<string, object> { ["f"] = 10 });
ServiceBinder.UnbindMethod("add");
```

A name already registered on the session is an error (unbind it first); a delegate whose parameter names cannot be recovered (a closed delegate created with `Delegate.CreateDelegate`) gets `arg1`, `arg2`, ... unless you pass names. Task and ValueTask delegates are supported through `ProcessAsync`, see [Asynchronous methods](#asynchronous-methods).

An interface can define the exposed contract, including a tree of interface-typed properties. Implementation-only methods stay private to the host; parameter names, `[JsonRpcParam]`, aliases and optional defaults come from the interface:

```csharp
public interface IWorld { ICharacter Character { get; } IAdmin Admin { get; } }
public interface ICharacter { void MoveAndRotate(float distance, float roll = 0, int ticks = 1); }
public interface IAdmin { ICharacter Character { get; } }

RpcBinding binding = ServiceBinder.BindInterface<IWorld>(sessionId, world);
// Character.MoveAndRotate and Admin.Character.MoveAndRotate
binding.Dispose(); // unbinds this tree, leaving later replacements alone

using var characterOnly = ServiceBinder.BindInterface<IWorld>(sessionId, world,
    new RpcInterfaceBindingOptions { Include = m => m.Path.Length == 1 });
// Include can also inspect m.Method for the host's own interface attributes.
```

Recursion defaults to on. Readable, non-indexed interface properties are evaluated **once per mount at registration**, so getters may have side effects; changing a property afterwards does not replace the captured child. The tree is flattened and compiled at registration, adding no tree traversal or binding cost per request. `Recursive = false` binds only root methods. `Prefix`, `Separator` and invariant `Casing = RpcNameCasing.CamelCase` control generated names; explicit `[JsonRpcMethod("alias")]` leaves remain literal. `NameRule` can return the complete wire name instead. Inherited and explicit implementations and closed generic interfaces work; generic methods and default interface bodies are rejected. Task and ValueTask members are asynchronous registrations served by `ProcessAsync`; `[JsonRpcMethod(ContextFlow = RpcContextFlow.Flow)]` on the interface member opts into context flow across awaits (see [Asynchronous methods](#asynchronous-methods)).

Registration publishes the whole tree at once or throws without exposing any of it. Empty names, reserved `rpc.` names, duplicates and names already in the session are errors, as are null children, throwing getters, cycles and paths deeper than 32 properties. Getter side effects cannot be rolled back. Keep the handle for unbinding: `Dispose` is idempotent and does not dispose your objects; calls already resolved can finish on their captured implementation.

### 2. Process requests

```csharp
using AustinHarris.JsonRpc;

var service = new CalculatorService();

// Strings, asynchronous invocation.
string response = await JsonRpcProcessor.ProcessAsync("{\"jsonrpc\":\"2.0\",\"method\":\"add\",\"params\":[1,2],\"id\":1}");
// {"jsonrpc":"2.0","result":3.0,"id":1}

// Strings, synchronous, on the calling thread.
string sync = JsonRpcProcessor.ProcessSync("{\"method\":\"multiply\",\"params\":{\"l\":6,\"r\":7},\"id\":2}");
// {"jsonrpc":"2.0","result":42,"id":2}

// Bytes. This is the native path; the string overloads transcode into it.
var output = new ArrayBufferWriter<byte>();
JsonRpcProcessor.Process(Handler.DefaultSessionId(), requestBytes /* ReadOnlySpan, ReadOnlyMemory or ReadOnlySequence<byte> */, output);
```

Batches (`[{...},{...}]`) and notifications (requests without an `id`) are handled per the spec: a batch answers with an array, a notification produces nothing.

## Hosting modes

The core is transport-agnostic. Pick whichever of these fits, or build your own on the byte entry point.

### In-process (strings or bytes)

The calls above. The byte overloads take what a `PipeReader` gives you (`ReadOnlySequence<byte>`) and write to what a `PipeWriter`, a socket buffer or `HttpResponse.BodyWriter` is (`IBufferWriter<byte>`). Nothing is written for a notification, so check `output.WrittenCount` before sending. For transports that carry several documents per connection, `JsonFramer.TryReadDocument` slices complete documents out of a byte stream without parsing them.

### Kestrel HTTP endpoint

```csharp
using AustinHarris.JsonRpc.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddJsonRpc();
builder.Services.AddJsonRpcService<CalculatorService>();   // built by DI; any class with [JsonRpcMethod] works, controllers included

var app = builder.Build();
app.MapJsonRpc("/rpc");                                    // POST /rpc; compose with RequireAuthorization() etc.
app.Run();
```

The endpoint reads the body from `PipeReader` and writes the response into `BodyWriter`; nothing becomes a string on the way through. A request or batch answers `200 application/json`, a notification `204`. Inside a method `JsonRpcContext.Current().Value` is the `HttpContext`. Enable async service methods with `builder.Services.AddJsonRpc(o => o.EnableAsyncMethods = true)` (default false). HTTP processing passes `RequestAborted` to `ProcessAsync` and keeps the body reader leased until completion. Options (session selection per request, serializer, body size limit, content type) are on `AddJsonRpc(o => ...)`; see the [package README](AustinHarris.JsonRpc.AspNetCore/README.md).

### Kestrel raw connections (TCP, Unix socket, named pipe)

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(9000, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenUnixSocket("/tmp/rpc.sock", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenNamedPipe("rpc", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
});
```

Clients write JSON documents back to back on the connection (whitespace or a newline between them is fine) and read responses in order; notifications produce nothing. The framer that splits the stream into documents only understands strict JSON: over a raw connection, single-quoted strings and other lenient syntax are not supported even with the Json.NET serializer. With `EnableAsyncMethods = true`, each framed document finishes before the next begins, and completed replies are flushed before waiting for a suspended document. The read buffer stays leased throughout invocation. See [Benchmarks](#benchmarks).

### Blazor WebAssembly

The core runs inside the browser. [samples/WasmHost](samples/WasmHost) is a Blazor WebAssembly app where JavaScript hands a request document to a `[JSExport]`/`[JSInvokable]` method that calls `JsonRpcProcessor.ProcessSync` and returns the response, with no HTTP involved. The same service class then serves both the browser and the server. The sample page also benchmarks JSON-RPC against plain Blazor interop; the numbers are in its README.

### Classic ASP.NET

`AustinHarris.JsonRpc.AspNet` hosts the 1.x-style `JsonRpcHandler` in System.Web on .NET Framework 4.0. It is built from its own project and unchanged in this release.

## Configuration

Everything is on `Config` (process-wide) with per-session overrides. Resolution is per call, then per session, then global.

### Serializer

```csharp
// Global
Config.SetSerializer(new SystemTextJsonRpcSerializer(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

// Per session
Config.SetSerializer("legacy-clients", new NewtonsoftJsonRpcSerializer(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

// Per call
string json = JsonRpcProcessor.ProcessSync(sessionId, request, context, serializer);
```

The built-in serializer is the default and reproduces Json.NET's wire conventions (member order, `.0` on whole floats, ISO dates, nulls written), so switching is invisible to clients. Library options go into the serializer's constructor and nowhere else:

| Serializer | Constructor | Notes |
| --- | --- | --- |
| built-in | `new JsmnSerializer(lenient: false, maxDepth: 64)` | `lenient` accepts `'single quotes'`, unquoted keys and trailing commas |
| Json.NET | `new NewtonsoftJsonRpcSerializer(settings)` | one `JsonSerializer` is built from the settings and reused; input is always lenient |
| System.Text.Json | `new SystemTextJsonRpcSerializer(options)` | the package adds its wire-format converters to a copy of your options when they are missing |

The full contract, what the core fixes versus what a serializer decides, is in [docs/serializers.md](docs/serializers.md).

### Nesting depth

Every serializer exposes `MaxDepth` (default 64). A request nested deeper is answered `-32700` before any hook or binding runs, so recursive parameter conversion is bounded by the same number the JSON library itself enforces: the built-in serializer's constructor argument, `JsonSerializerOptions.MaxDepth`, or `JsonSerializerSettings.MaxDepth`.

### The `jsonrpc` member

```csharp
Config.VersionPolicy = JsonRpcVersionPolicy.Lenient;          // process default
Config.SetVersionPolicy("strict-clients", JsonRpcVersionPolicy.Strict);   // per session; null follows the global
```

| Policy | Missing member | `"2.0"` | Anything else |
| --- | --- | --- | --- |
| `Lenient` (default) | accepted | accepted | `-32600 Invalid Request` |
| `Ignore` | accepted | accepted | accepted |
| `Strict` | `-32600 Invalid Request` | accepted | `-32600 Invalid Request` |

The default keeps tool harnesses that omit the member working while a client speaking another version is told so. `Ignore` is for talking to anything at all.

### Exception details

An ordinary exception thrown by a method reaches the client as `-32603` with `error.data = {ClassName, Message}`. Set `Config.IncludeExceptionDetails = true` to also send `Source`, `StackTraceString`, `HResult` and the `InnerException` chain. A `JsonRpcException` thrown by the application always keeps the `data` it was given.

### Sessions and context

Sessions let you host independent sets of services (for example one per connected client or tenant):

```csharp
ServiceBinder.BindService("client-42", new CalculatorService());   // any object with [JsonRpcMethod] members
string response = await JsonRpcProcessor.Process("client-42", request, context);
Handler.DestroySession("client-42");
```

Pass an arbitrary context object through to your methods and read it with `Handler.RpcContext()` or `JsonRpcContext.Current().Value` (the Kestrel package passes the `HttpContext` or `ConnectionContext`):

```csharp
await JsonRpcProcessor.Process(request, context: httpContext);

[JsonRpcMethod]
private string WhoAmI() => ((HttpContext)Handler.RpcContext()).User.Identity.Name;
```

The request's `id` is available the same way, read on demand from the request bytes, so a method that never asks pays nothing:

```csharp
[JsonRpcMethod]
private string Track()
{
    JsonRpcRequestId id = JsonRpcContext.CurrentRequestId();   // or Handler.RpcRequestId(): an owned snapshot, keep it anywhere
    if (id.TryGetInt64(out long n)) { /* integer id */ }
    string text = id.GetString();                               // string ids (decoded); null otherwise
    string digits = id.GetIntegerText();                        // integers, including ones wider than Int64
    JsonRpcIdKind kind = Handler.RpcRequestIdKind();            // Integer, String, Null, or Absent for a notification
    ReadOnlySpan<byte> raw = Handler.RpcRequestIdRaw();         // the id's JSON as sent (`12`, `"abc"`, `null`); a borrow, use it before returning
    return id.ToString();
}
```

The snapshot is a small struct: an integer id allocates nothing, a string id allocates its decoded string, and the raw span never allocates. Synchronous dispatch keeps context per invocation and per thread; async Flow registrations carry it across sequential awaits (see [Asynchronous methods](#asynchronous-methods)). Take snapshots before parallel work. Nested dispatch sees its own id and restores the parent. A pre-process handler that replaces `JsonRequest.Id` changes what the method sees. A parameter named `id` is an ordinary parameter and binds from `params` only.

### Errors and hooks

Return a spec-compliant error by throwing `JsonRpcException(code, message, data)`, or shape errors globally or per session:

```csharp
Config.SetErrorHandler((request, exception) => new JsonRpcException(-32000, "Server error", exception.data));
Config.SetParseErrorHandler((rawJson, exception) => exception);
Config.SetPreProcessHandler((request, context) => null);              // return a JsonRpcException to reject; may replace Method/Params/Id
Config.SetPostProcessHandler((request, response, context) => null);   // return a JsonRpcException to replace the result
```

Registering a pre- or post-process handler switches the affected session onto a slower path that materialises `JsonRequest`/`JsonResponse` objects for the handler; leave them unset when you do not need them.

The errors the library raises itself carry structured `data`, identical for every serializer, and the error handler receives the same object:

| Code | `error.data` | Object seen by the error handler |
| --- | --- | --- |
| `-32601` Method not found | `{"method":"<name as requested>"}` | `MethodNotFoundInfo` |
| `-32602` Invalid params: count, missing, unknown or repeated named parameter | a sentence, e.g. `"Named parameter 'b' was not present."` | `string` |
| `-32602` Invalid params: a value the serializer could not convert | `{"reason":"conversion","parameter":"b","index":1,"expectedType":"int32"}` plus `"message"` when `Config.IncludeExceptionDetails` is on; the value sent is never echoed | `ParameterErrorInfo` (with the serializer's exception in `Cause`) |
| `-32603` Internal error: the method threw, or a parameter's type is one the serializer cannot handle | `{ClassName, Message, ...}`, see [Exception details](#exception-details) | `Exception` |

"Could not convert" means the serializer refused the value (`JsonRpcBindException`, `FormatException`, `OverflowException`, `InvalidCastException`, or any `JsonException` from System.Text.Json or Json.NET); what each serializer accepts (say `"7"` for an `int`) is its own decision, see [docs/serializers.md](docs/serializers.md).

### Asynchronous methods

Methods may return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`. Call `JsonRpcProcessor.ProcessAsync` to await the operation and serialize its eventual result; the non-generic forms answer JSON `null`. Completed operations run inline. The byte overloads return `Task.CompletedTask` when the whole document completes successfully inline. Batches execute sequentially, and notifications are awaited too.

```csharp
[JsonRpcMethod("lookup")]
public async Task<Item> Lookup(int id, [JsonRpcCancellation] CancellationToken cancellationToken)
    => await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);

await JsonRpcProcessor.ProcessAsync(sessionId, requestMemory, output,
    context: requestContext, cancellationToken: cancellationToken);
string response = await JsonRpcProcessor.ProcessAsync(sessionId, requestJson,
    context: requestContext, cancellationToken: cancellationToken);
```

The byte APIs accept `ReadOnlyMemory<byte>`, `ReadOnlySequence<byte>` (by value), or `ReadOnlySpan<byte>`. Keep borrowed request bytes immutable and valid and the output writer exclusive until the task completes. The span overload copies before returning; segmented sequences are copied too. No output spans are held across awaits. The task covers response writing, not transport flushing.

The processor token is injected only into a `[JsonRpcCancellation] CancellationToken` parameter; it is excluded from JSON parameters and SMD. An unmarked token parameter is rejected at registration. Synchronous methods may also request injection through `ProcessAsync`; ordinary synchronous processing passes the default token. Cancellation is checked before invocation, between batch elements, and before the staged document is committed. It discards the entire staged response, observes an in-flight operation to completion, releases its resources, and returns a canceled task. A method that ignores the token can therefore delay cancellation. Cancellation cannot undo service side effects. A method's own `OperationCanceledException` follows ordinary error mapping when the processor token has not been canceled.

Async methods default to `RpcContextFlow.None`: the ambient accessors (`Handler.RpcContext()`, `Handler.RpcRequestId()`, `JsonRpcContext.Current().Value`, `Handler.RpcSetException()`) are valid in the synchronous part of the method, before its first suspension, and an operation that completes inline costs the same as a synchronous call and allocates nothing. Opt a method into `RpcContextFlow.Flow` when it needs the ambient context after an await; then the accessors work across sequential awaits and nested dispatch, each invocation owns a frame that is cleared at terminal completion, and every invocation pays an allocation for the execution-context bridge, completed tasks included:

```csharp
[JsonRpcMethod(ContextFlow = RpcContextFlow.Flow)]
public async Task<Item> Lookup(int id)
{
    var item = await repository.FindAsync(id);
    if (item == null) Handler.RpcSetException(new JsonRpcException(-32000, "Not found", null));
    return item;
}

ServiceBinder.BindMethod(sessionId, "lookup", new Func<int, Task<Item>>(LookupAsync),
    contextFlow: RpcContextFlow.Flow);
```

In `None` mode the initial synchronous portion can capture the context and an owned `JsonRpcRequestId` snapshot; ambient state does not flow after suspension. Throw authored errors instead of setting ambient error state after an await. For parallel child branches in either mode, capture snapshots and avoid sharing the mutable ambient frame. Background work must retain snapshots because the invocation frame is cleared when the RPC finishes. Use raw ID spans only during the immediate call that reads them; reacquire them after awaiting.

`async void`, custom awaitables, nested awaitables and asynchronous streams are rejected at registration. Async registrations cannot have by-ref parameters, including the legacy trailing `ref JsonRpcException`. A null returned `Task` is an internal error. Synchronous `Process`/`ProcessSync` reject an async registration at call time with `-32603` and an instruction to use `ProcessAsync`, without invoking it. The older `Task<string> Process(...)` family retains its scheduled synchronous execution through `Task.Factory.StartNew`; it does not await async service methods.

A job ticket remains useful for work that should outlive the request:

```csharp
[JsonRpcMethod] private string startExport(string filter) { var job = Jobs.Start(() => ExportAsync(filter)); return job.Id; }
[JsonRpcMethod] private ExportStatus exportStatus(string jobId) => Jobs.Status(jobId);
```

The client gets a ticket immediately and polls, or the transport pushes a notification when the job finishes.

## Benchmarks

`TestServer_Console` is the benchmark harness. It binds one service with five small methods (`add`, `addInt`, `NullableFloatToNullableFloat`, `Test2`, `StringMe`), drives the same five requests through the server, checks every response is a `result` rather than an error, and ends each mode with a bar chart of RPC/s.

```
dotnet run -c Release --project TestServer_Console -- --async 3 1   # real async invocation, Flow and None separately
dotnet run -c Release --project TestServer_Console -- --sync 3      # library only, 1..N threads (add a thread count, e.g. --sync 3 1, for one row)
dotnet run -c Release --project TestServer_Console -- --kestrel 3   # through the AspNetCore package, HTTP and TCP
dotnet run -c Release --project TestServer_Console -- --compare 3   # the same calls through StreamJsonRpc and gRPC for .NET, side by side
dotnet run -c Release --project TestServer_Console -- --sweep 2 benchmarks/charts/sweep.json   # every library and transport at 1, 2, 4, 8, 16 connections; one file per run
dotnet run -c Release --project TestServer_Console                  # menu: Enter = Task mode, s = sync, k = Kestrel, x = compare, q = quit
dotnet run --project samples/WasmHost                               # browser: "Run benchmark" on the page
```

All numbers below are from an AMD Ryzen 7 7800X3D (8 cores / 16 threads, 4.2 GHz), 64 GB, Windows 11, .NET 10, Release, Server GC, with the built-in serializer, measured 2026-09-23. Where a row gives two figures they are the spread over that day's runs on an otherwise idle machine; the WSL virtual machine, which takes 15 to 25 % of the box when idle, was shut down for the Kestrel and comparison runs. A single benchmark thread on this machine varies with whatever else lands on its core's SMT sibling, so the 1-thread rows are from runs on an idle core.

### Sync: the library alone

`--sync` calls the byte-level `JsonRpcProcessor.Process` in a loop from 1, 2, 4, ... threads up to the core count, so it measures parsing, dispatch, binding and response writing with no scheduler in the way.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/sync-threads-dark.svg">
  <img alt="JSON-RPC.Net alone, by worker threads: aggregate requests per second as a low-to-high band, and the reported ns per request per thread" src="benchmarks/charts/sync-threads.svg">
</picture>

| Threads | RPC/s | ns per request per thread | Allocations per request |
| ---: | ---: | ---: | --- |
| 1 | 4.5 M to 4.6 M | 217 | 0 bytes for numeric shapes, one string for `StringMe` |
| 2 | 7.6 M to 9.5 M | 222 | |
| 4 | 16.4 M to 17.4 M | 230 | |
| 8 | 25.1 M to 26.8 M | 298 | |
| 16 | 30.6 M to 35.8 M | 446 | |

Per-thread cost rises with thread count because the 16 threads share 8 physical cores.

The 2026-09-23 performance pass (compiled invokers that read the tokens and write the pooled buffer through direct calls instead of virtual, delegate and interface calls; a tokenizer that keeps its scanner state in locals; a last-session cache; envelope keys matched by length; a flat method table) was measured A/B in one session: the same seven runs of `--sync 2 1` went from 3.2 M to 4.1 M (median 3.6 M) before to 4.0 M to 4.8 M (median 4.4 M) after, about 20 to 25 % more on one thread. The transport rows below are bound by the loopback round trips rather than by the library and moved less.

### Asynchronous invocation

`--async [seconds] [workers]` uses `ProcessAsync` with separate sessions and the same five wire names as `--sync`. It reports synchronous returns through the new API, completed `Task<T>`, and inline `ValueTask<T>`, with Flow and None registrations reported separately. A separate `yieldsOnce` shape awaits `Task.Yield()`. Responses and IDs are validated before timing. Inline allocation totals use current-thread accounting; the timed runs also report process-wide allocations to include suspended continuations. These totals include allocations made by the service methods. No throughput figures are published here yet.

### Task: scheduled synchronous execution

The default mode submits batches through the `Task`-returning `Process` overload from every core at once, the way an async host would, so it pays for the thread-pool hop, a `Task`, a result string and a continuation per request. Each batch size is repeated for at least half a second after a one-second warm-up. Throughput peaks once a batch is large enough to keep every core busy and falls off again when hundreds of thousands of requests are queued at once:

| Batch size | RPC/s |
| ---: | ---: |
| 50 | 1.5 M |
| 300 | 7.8 M |
| 6,000 | 10.9 M |
| 36,000 | 12.0 M |
| 252,000 | 7.6 M |
| 2,016,000 | 7.2 M |

Sync beats Task mode because Task mode measures the .NET thread pool and per-request garbage as much as the library; Kestrel awaits transport reads and flushes without dedicating a thread to each connection.

### Kestrel: through the AspNetCore package

`--kestrel` starts a real Kestrel on loopback with `MapJsonRpc` and `JsonRpcConnectionHandler`, then drives it with 16 clients on the same machine, so every figure is an upper bound for one box talking to itself. The in-process rows are the same requests through the byte entry point with no transport at all, for scale.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/kestrel-transports-dark.svg">
  <img alt="JSON-RPC.Net by transport: HTTP single, HTTP batch of 100 and TCP pipelined, as low-to-high intervals on a log axis, with the in-process figure for scale" src="benchmarks/charts/kestrel-transports.svg">
</picture>

| Transport | RPC/s | Note |
| --- | ---: | --- |
| in-process, 16 threads | 30.8 M to 31.3 M | |
| HTTP, 1 request per POST | 128 k to 168 k | 95 to 125 µs per round trip per client depending on the run; HTTP/1.1 request-response is the cost, not the server |
| HTTP, batch of 100 per POST | 12.7 M to 13.7 M | |
| TCP, 256 pipelined | 15.2 M to 15.5 M | ring-buffer clients, one thread each, streaming framer |

The TCP client keeps 256 requests in flight per connection and refills from a precomputed ring of request bytes with one `Send` per refill; the server side is the same `Process` call the HTTP endpoint makes, fed by `JsonFramer`.

### Versus StreamJsonRpc and gRPC

[StreamJsonRpc](https://www.nuget.org/packages/StreamJsonRpc) is Microsoft's JSON-RPC library, the one behind Visual Studio and the language-server stack. `--compare` hosts both libraries on the same Kestrel TCP listener and drives them with the same pipelining client (16 connections, 256 requests in flight each), so the only variable is the library answering. StreamJsonRpc requires the `jsonrpc` member, so every request in this mode carries `"jsonrpc":"2.0"`, which is why the JSON-RPC.Net rows are a little below the other tables. The same mode also hosts [gRPC for .NET](https://learn.microsoft.com/aspnet/core/grpc/) (HTTP/2, protobuf) on the same Kestrel, answering the same five calls from [calculator.proto](TestServer_Console/Protos/calculator.proto), driven by its own client with the same shape: 16 channels, 256 calls in flight each. Each row was run twice for 3 s; both results are shown.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/compare-streamjsonrpc-dark.svg">
  <img alt="JSON-RPC.Net vs StreamJsonRpc vs gRPC for .NET at 16 connections: low-to-high intervals on a log axis, grouped by library, with the in-process paths under a rule" src="benchmarks/charts/compare-streamjsonrpc.svg">
</picture>

| Library and path | RPC/s |
| --- | ---: |
| JSON-RPC.Net over Kestrel TCP, raw documents | 13.7 M to 14.6 M |
| StreamJsonRpc over Kestrel TCP, newline framing, System.Text.Json formatter | 1.38 M to 1.44 M |
| StreamJsonRpc over Kestrel TCP, `Content-Length` framing, System.Text.Json formatter | 1.41 M to 1.45 M |
| StreamJsonRpc over Kestrel TCP, `Content-Length` framing, Json.NET formatter (its default) | 625 k |
| gRPC for .NET, unary calls over HTTP/2 (Grpc.Net.Client, 16 channels × 256 in flight) | 192 k to 198 k |
| gRPC for .NET, one bidirectional stream per channel, 256 in flight, batched writes | 200 k to 209 k |

StreamJsonRpc 2.25.29, defaults apart from the formatter and framing named in each row. It is a full bidirectional RPC framework (client proxies, cancellation, progress, marshaled objects, events). The comparison is of the server side answering the same five requests; on that measure JSON-RPC.Net is about 10× faster on the same connections with the same JSON library underneath.

<details>
<summary>In-process paths: a direct call, a Pipe pair and a typed proxy (different boundaries, not comparable with the rows above)</summary>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/inprocess-paths-dark.svg">
  <img alt="In-process paths: JSON-RPC.Net direct call, StreamJsonRpc Pipe pair and StreamJsonRpc typed proxy, as low-to-high intervals on a log axis" src="benchmarks/charts/inprocess-paths.svg">
</picture>

| Path | RPC/s |
| --- | ---: |
| JSON-RPC.Net in-process, 1 thread (direct call, bytes in, bytes out) | 2.6 M to 3.6 M |
| StreamJsonRpc in-process, 1 client over a `Pipe` pair, newline framing, System.Text.Json formatter, 256 pipelined | 117 k to 142 k |
| StreamJsonRpc typed proxy, sequential `await` per call, in-process pipes | 96 k to 97 k (10 µs per round trip) |

StreamJsonRpc's server side has no "document in, document out" call, so its in-process row is a pair of `System.IO.Pipelines` pipes, the closest it has to a direct call; the proxy row is one call at a time, so it measures a round trip, not throughput. The direct-call row is from the comparison session and sits below the sync table's newer 1-thread figure.

</details>

gRPC for .NET 2.84.0 with default settings apart from Kestrel's `MaxStreamsPerConnection` (raised to 256 so the pipeline depth is not capped at 100). protobuf has no `decimal`, so `Test2` carries the units/nanos `DecimalValue` message the gRPC docs recommend; nullable values use proto3 `optional`. The gRPC rows are a different kind of measurement from the rows above them: there is no cheap raw client for HTTP/2 + protobuf, so the client is Grpc.Net.Client on the same 8 cores as the server, and the figure is what a .NET caller and a .NET service get end to end. One channel alone reaches about 130 k unary calls per second; sixteen channels do not scale much further because client and server compete for the same cores. The streaming row batches its writes the way the TCP client does (BufferHint on every message but the last of a refill), and the server flushes only when its input runs dry, the same once-per-read-group flush `JsonRpcConnectionHandler` does.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/compare-connections-dark.svg">
  <img alt="Every library and transport by client connections, 1 to 16: three panels on a shared log axis, one per library, with a marker shape and dash per setting and whiskers spanning two runs" src="benchmarks/charts/compare-connections.svg">
</picture>

`--sweep` runs every one of those paths at 1, 2, 4, 8 and 16 client connections (gRPC: channels) and writes one JSON file per run; the chart above is five 2 s runs per point, the marker at the median and the whisker from the lowest to the highest run. It is a separate session from the tables: the WSL virtual machine was running and other work was active, so its absolute figures sit below the table rows (JSON-RPC.Net over TCP 10.9 M to 13.0 M at 16 connections against 13.7 M to 14.6 M in the table), and its gRPC unary figure runs higher (360 k to 404 k against 192 k to 198 k; the cause is not pinned down, and the table keeps the `--compare` figure). What the sweep adds is the shape: JSON-RPC.Net over TCP and batched HTTP climb almost linearly with connections, StreamJsonRpc gains 8 to 10× from one connection to sixteen, and gRPC's .NET client is flat from two channels on because it competes with the server for the same eight cores.

The [benchmark explorer](https://astn.github.io/JSON-RPC.NET/benchmarks/charts/explorer.html) is the same data as an interactive page: toggle series, hover or tab to a point for the exact low, median, high and every run, switch the axis between log and linear, and download the data. It is one self-contained HTML file, [benchmarks/charts/explorer.html](benchmarks/charts/explorer.html), so it also works saved to disk.

### WebAssembly: in the browser

The [WasmHost sample](samples/WasmHost/README.md) compares JSON-RPC through JS interop with plain Blazor interop for the same `add(1, 2)` in Chrome, under the .NET 10 interpreter and AOT-compiled; its README has the table and a chart. Interpreted, a plain `DotNet.invokeMethod` add costs about 64 µs (the JSON marshalling Blazor does), a JSON-RPC document written as UTF-8 straight into WebAssembly memory and run through a `[JSExport]` costs 53 µs, and a batch of 100 that way reaches 27 k RPC/s. AOT-compiled (`dotnet publish` with the `wasm-tools` workload) the same three are 15 µs, 7 µs and 210 k RPC/s; a typed `[JSExport]` add takes 0.3 µs either way.

### simdjson

simdjson was evaluated as a fourth parser and not adopted: through the only maintained .NET binding its parse alone costs more than the whole built-in envelope read, and walking the result is 5 to 6 times slower with 350 bytes or more of garbage per request. The harness and numbers are in [benchmarks/SimdJsonEval/RESULTS.md](benchmarks/SimdJsonEval/RESULTS.md).

The charts, the explorer page and the figures in this file come from one data file, [benchmarks/charts/benchmarks.json](benchmarks/charts/benchmarks.json): the tables above transcribed with their published precision and conditions, plus the `--sweep` run files. `python benchmarks/charts/render.py` (plain Python, no packages) renders every chart in a light and a dark variant, which the README picks between with a `<picture>` element, and `render.py --check` fails if a committed chart is stale or a figure in this file no longer matches the data; the pull-request build runs it. GitHub serves README images through a proxy as plain `<img>`, so the SVGs carry no scripts, hover text or links, and every range is drawn as an interval with its figures beside it; the interactive parts live on the explorer page.

For comparison, under the previous harness (one pass per batch, workstation GC) the two-million batch ran at about 525,000 RPC/s on 1.3 and 1,584,906 RPC/s on 2.0 on the same machine. (The 1.x figure published earlier in this README was measured while the benchmark service was not bound, so every request took the "method not found" path; the benchmark now prints the responses so that cannot go unnoticed.)

## Upgrading from 1.x

- `JsonRpcProcessor.Process(…, JsonSerializerSettings)` is gone from the core. Use `Config.SetSerializer(new NewtonsoftJsonRpcSerializer(settings))` from the Newtonsoft package.
- The default-session string overloads that take a serializer take it first: `Process(serializer, json, context)` / `ProcessSync(serializer, json, context)`. The session overloads keep `(sessionId, json, context, serializer)` with `context` required, so `Process(json, null)` still means the default session.
- `JsonRequest`, `JsonResponse` and `JsonRpcException` are plain DTOs without Json.NET attributes. `JsonRequest.Params` is the active serializer's object model, so cast to `JObject`/`JArray` only when the Json.NET serializer is active.
- The `jsonrpc` member is checked (`Config.VersionPolicy`, default `Lenient`): a missing member is still accepted, but `"jsonrpc":"1.0"` or a non-string value is now `-32600`. Set `Ignore` for the 1.x behaviour.
- Requests nested deeper than 64 levels are `-32700` (configurable per serializer, see [Nesting depth](#nesting-depth)). Invalid UTF-8 and non-strict JSON (unless the serializer is lenient) are `-32700` as well.
- The empty-batch error code is the spec's `-32600` (it was `3200`). Batches made only of notifications produce an empty response instead of `[]` with a dangling comma.
- A batch always answers with a JSON array when it produces at least one response; a one-request batch is no longer unwrapped to a bare response object.
- A notification (a request without an `id`) never gets a wire response, whatever its outcome: method not found, binding failure or an exception in the method produce nothing on the wire (the error handler still runs server-side). An invalid request object is not a notification and still gets `-32600` with `"id":null`.
- Exception details are redacted by default; see [Exception details](#exception-details).
- Task-returning methods are supported again through `ProcessAsync`, together with `ValueTask` and `ValueTask<T>`. Synchronous `Process`/`ProcessSync` reject them at call time without invoking them. `async void` remains rejected at registration. See [Asynchronous methods](#asynchronous-methods).
- A parameter value the serializer cannot convert (`"abc"` for an `int`, `"not-a-guid"` for a `Guid`) is `-32602` with `data = {"reason":"conversion","parameter":…,"index":…,"expectedType":…}`; it was `-32603` with the exception. An exception of the same type thrown inside the method is still `-32603`. A type the built-in serializer cannot handle at all stays `-32603` (now a `NotSupportedException`).
- `-32601`'s `data` is `{"method":"<name>"}` instead of the fixed sentence, and a method-not-found error for a notification now reaches the error handler (the wire still gets nothing).
- The invocation frame also carries the request id: `Handler.RpcRequestId()` / `JsonRpcContext.CurrentRequestId()`, `Handler.RpcRequestIdKind()` and `Handler.RpcRequestIdRaw()`, see [Sessions and context](#sessions-and-context).
- `ServiceBinder.BindMethod(sessionId, name, delegate)` registers any delegate; it refuses a name that is already registered, unlike `Handler.RegisterFuction`, which keeps replacing silently.
- Named parameters are checked against the method's parameter list: a supplied name that matches no parameter, or a name supplied twice, is `-32602` (it used to be ignored, so `optional(int a = 9)` called with `{"typo":4}` returned 9). Defaults fill only the names that are absent.
- `SMD.Services` is an `SMDServiceCollection` (an `IDictionary<string, SMDService>`) instead of a `Dictionary<string, SMDService>`, and its setter is gone. Every mutation through it updates the dispatch table at once, so a removed method is unreachable immediately.
- `SMD.Types` is a process-wide registry (it was reset whenever a session was created).
- A pre-process handler may replace `JsonRequest.Method`, `Params` or `Id`; the replaced request is what gets dispatched (as in 1.x). Assign a new `Params` value rather than editing the serializer's object model in place: a request the handler leaves untouched is dispatched straight from the request bytes.
- `JsonRpcContext.Current()` / `Handler.RpcContext()` and `JsonRpcContext.SetException` are per invocation: a method that synchronously processes another request through `JsonRpcProcessor` gets its own context and exception state back afterwards.
- `DateTime` and `DateTimeOffset` are written the way Json.NET writes them by every serializer (fraction only when non-zero, `Z`/offset/nothing by `Kind`); `NaN` and the infinities are written as the quoted strings `"NaN"`, `"Infinity"`, `"-Infinity"` and read back from them.

## Building

Requires the .NET 10 SDK (pinned in `global.json`) and the .NET 8 runtime for the `net8.0` test target.

```
dotnet build AustinHarris.JsonRpc.sln
dotnet test AustinHarris.JsonRpcTestN
```

The test suite runs its protocol cases once per serializer (built-in, Json.NET, System.Text.Json) plus the parser, dispatch, version-policy and Kestrel integration tests, on both `net8.0` and `net10.0`. Building a package project in Release produces its NuGet package in `bin/Release/`. The WebAssembly sample builds without the `wasm-tools` workload; add it for AOT.

##### License
JSON-RPC.net is licensed under The MIT License (MIT), check the [LICENSE](https://github.com/Astn/JSON-RPC.NET/blob/master/LICENSE) file for details.

##### Documentation

This README and the package guides are also published as a site at [astn.github.io/JSON-RPC.NET](https://astn.github.io/JSON-RPC.NET/), built from the same files.

##### Old Project Site

We have to github lately and host our [issues section](https://github.com/Astn/JSON-RPC.NET/issues) here, though you can still check the previous [issues](https://jsonrpc2.codeplex.com/workitem/list/basic) and [discussions](https://jsonrpc2.codeplex.com/discussions) over our old project site.
