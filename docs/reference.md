# API and hosting reference

This page collects the detailed behavior behind the [Overview](../README.md): packages, binding, transports, errors, asynchronous calls, sessions, configuration and security. Start with the Overview for a working server and the measured results.

## Packages

All four packages are MIT licensed and ship together with the same version number.

| Package | What it is |
| --- | --- |
| `AustinHarris.JsonRpc` | The server. Envelope parsing, method dispatch, parameter binding, error mapping, sessions. Ships with the built-in serializer, `JsmnSerializer`: a span port of the [jsmn](https://github.com/zserge/jsmn) tokenizer plus a cached reflection mapper, with no JSON library behind it. |
| `AustinHarris.JsonRpc.Newtonsoft` | Json.NET 13 serializer. The compatibility choice: `JsonSerializerSettings`, `[JsonProperty]`, converters, lenient input. |
| `AustinHarris.JsonRpc.SystemTextJson` | System.Text.Json serializer. `JsonSerializerOptions`, `Utf8JsonReader`/`Utf8JsonWriter` straight on the request bytes. |
| `AustinHarris.JsonRpc.AspNetCore` | Kestrel hosting: an HTTP endpoint on `PipeReader`/`BodyWriter`, a `ConnectionHandler` for raw TCP, Unix socket and named pipe connections, and DI registration of services. |

## Requirements

`AustinHarris.JsonRpc`, `.Newtonsoft` and `.SystemTextJson` target:

| Target | Covers |
| --- | --- |
| `netstandard2.0` | .NET Framework 4.6.1+, .NET Core 2.0+, Mono 5.4+, Xamarin, Unity 2018.1+ |
| `netstandard2.1` | .NET Core 3.0+, Mono 6.4+, Xamarin |
| `net8.0` | .NET 8 (LTS) |
| `net10.0` | .NET 10 (LTS) |

`AustinHarris.JsonRpc.AspNetCore` targets `net8.0` and `net10.0`.

The "Covers" column is what each target framework admits, not what is tested. CI runs the test suite on `net8.0` and `net10.0`; the WebAssembly sample is built in CI and run by hand. The other runtimes can load the `netstandard` assets but are not part of the test matrix. On .NET Framework, 4.7.2 or later avoids the binding redirects that 4.6.1 to 4.7.1 need for `netstandard2.0` libraries.

Dependencies at 2.0.0: none on `net8.0` and `net10.0`; on `netstandard` only, `System.Memory` 4.6.3, and `netstandard2.0` also references `System.Threading.Tasks.Extensions` 4.5.0 for `ValueTask`. The Newtonsoft package depends on Newtonsoft.Json 13.0.4 and the System.Text.Json package on System.Text.Json 10.0.3.

The core uses no reflection emit. The WebAssembly sample runs under the configurations listed in [samples/WasmHost/README.md](../samples/WasmHost/README.md); it validates neither `PublishTrimmed` nor `PublishAot`, which are unsupported: services and their `[JsonRpcMethod]` members are found by reflection and the invokers are compiled expression trees.

## Installation

```
dotnet add package AustinHarris.JsonRpc --prerelease
```

2.0 is published as `2.0.0-preview.3`; without `--prerelease`, NuGet resolves to the last 1.x release. The four assemblies are strong-named with one key (public key token `e6819c02cf4aec44`) that is checked into the repository and does not change between releases, so a signed caller can reference them and .NET Framework loads them.

The packages are not annotated for trimming or Native AOT: a project that sets `IsAotCompatible` or `PublishTrimmed` gets trim warnings from the reflection binder, and a trimmed application can lose the methods it binds. Hosts with that constraint wait for the source generator planned for 2.8; see [Versioning and support](../README.md#versioning-and-support).

Add `AustinHarris.JsonRpc.Newtonsoft` or `AustinHarris.JsonRpc.SystemTextJson` if you want that serializer, and `AustinHarris.JsonRpc.AspNetCore` to host in Kestrel.

## Defining methods

A *method* is a callable identified by the `method` member of a request; its implementation is a delegate, a `[JsonRpcMethod]` member of a class, or a member of a bound interface. `ServiceBinder` never asks for a `MethodInfo`; the same word names the -32601 "Method not found" error. Names beginning with `rpc.` and the name `$/cancelRequest` are reserved and refused at registration.

### Classes

Any class works, not only `JsonRpcService` subclasses: bind an instance with `ServiceBinder.BindService(sessionId, instance)`. A `JsonRpcService` subclass binds itself to the default session in its parameterless constructor. Write `: base(false)` for a subclass that something else binds (the AspNetCore host binds every registered service to its effective session) and `: base(sessionId)` to bind to another session.

An instance bound with `BindService(sessionId, instance)` serves every request on every thread, so it must be thread-safe. `BindService(sessionId, typeof(T), resolve)` binds a type instead: right before each call the resolver is handed the RPC context and returns the instance to invoke, which is how a container's scoped and transient lifetimes reach a method (the AspNetCore package does this for `AddJsonRpcService<T>(ServiceLifetime.Scoped)`; see [Kestrel HTTP endpoint](#kestrel-http-endpoint)). The core takes no dependency on any container: the resolver is a plain delegate, so Microsoft.Extensions.DependencyInjection, Autofac and a hand-written factory all fit. Static methods never resolve.

### Delegates

Bind a delegate as a JSON-RPC method with `ServiceBinder.BindMethod`; a lambda keeps its parameter names for named params:

```csharp
ServiceBinder.BindMethod("add", (double l, double r) => l + r);
ServiceBinder.BindMethod("greet", (string who) => "hello " + who);                            // {"method":"greet","params":{"who":"you"}}
ServiceBinder.BindMethod(sessionId, "scale", (double v, double f) => v * f,
    parameterNames: new[] { "value", null }, defaults: new Dictionary<string, object> { ["f"] = 10 });
ServiceBinder.UnbindMethod("add");
```

A name already registered on the session is an error (unbind it first); a delegate whose parameter names cannot be recovered (a closed delegate created with `Delegate.CreateDelegate`) gets `arg1`, `arg2`, ... unless you pass names. `Task` and `ValueTask` delegates are served by `ProcessAsync`; see [Asynchronous methods and cancellation](#asynchronous-methods-and-cancellation).

### Interfaces

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
// Include can also inspect m.MethodInfo for the host's own interface attributes.
```

Each interface-typed property becomes a name segment, so `IWorld.Character.MoveAndRotate` is exposed as `Character.MoveAndRotate`. The whole tree is walked and compiled when you call `BindInterface`, so a request pays nothing for it.

What happens at registration:

- Each property getter runs once, and the object it returns is the one that serves calls. Setting the property later changes nothing. A getter may have side effects; they happen once and are not undone if registration fails.
- Registration is all or nothing. It throws, exposing no method, on an empty or duplicate name, a name starting with `rpc.`, a name already bound in the session, a null or throwing getter, a cycle, or a path deeper than 32 properties.
- Inherited members, explicit implementations and closed generic interfaces are supported. Generic methods and default interface method bodies are rejected.

Naming, through `RpcInterfaceBindingOptions`:

| Option | Default | Effect |
| --- | --- | --- |
| `Recursive` | `true` | `false` binds only the root interface's methods |
| `Prefix` | `""` | prepended to every generated name |
| `Separator` | `"."` | joins property segments and the method name |
| `Casing` | `Preserve` | `CamelCase` lower-cases the first letter of each generated segment (invariant culture) |
| `Include` | all | a predicate over `RpcInterfaceMethod` (`Path`, `MethodInfo`, `Interface`, `Leaf`, `DefaultName`) |
| `NameRule` | none | returns the complete wire name, replacing the rules above |

An explicit `[JsonRpcMethod("alias")]` on an interface method is used as written. `Task` and `ValueTask` members are served by `ProcessAsync`; `[JsonRpcMethod(ContextFlow = RpcContextFlow.Flow)]` on the interface member opts into context flow across awaits (see [Asynchronous methods and cancellation](#asynchronous-methods-and-cancellation)).

`BindInterface` returns an `RpcBinding`. Disposing it unbinds exactly this tree (not a later registration under the same names), is safe to call twice, and does not dispose your objects. Calls already dispatched finish on the implementation they started with.

### Reading what is bound

Tooling that lists or inspects a session reads `Handler.GetSessionHandler(sessionId).MetaData.Services`, an `IDictionary<string, SMDService>` keyed by wire name. Each entry's `Method` is the compiled `RpcMethod` (`Name`, `Parameters` with their types and defaults, `ReturnType`) and `dele` is the delegate behind it. The collection is a live view: read it, do not modify it, and register and unregister through `ServiceBinder` and `Handler.DestroySession`.

## Hosting

The core is transport-agnostic. Pick whichever of these fits, or build your own on the byte entry point.

### In-process (strings or bytes)

Call the processor yourself, as in [Getting started](../README.md#getting-started). The byte overloads take what a `PipeReader` gives you (`ReadOnlySequence<byte>`) and write to any `IBufferWriter<byte>`: a `PipeWriter`, a socket buffer or `HttpResponse.BodyWriter`. Nothing is written for a notification, so check `output.WrittenCount` before sending. If your transport carries several documents per connection, `JsonFramer.TryReadDocument` cuts complete documents out of the byte stream without parsing them. Its span form, `JsonFramer.FindDocumentEnd`, returns the length of the first complete document and `-1` both when the document is still incomplete and when the buffer does not start with `{` or `[` after whitespace, so a host tells the two apart itself (the first non-whitespace byte) or a line of garbage stalls the connection. Nothing in the core bounds a host's receive buffer: cap it at the document limit you would accept and fail the connection at the cap, as the Kestrel handler does with `MaxRequestBytes`.

The byte overloads also accept `ReadOnlySpan<byte>` and `ReadOnlyMemory<byte>`. On C# 12 and 13, a bare `byte[]` is ambiguous between those overloads; pass `request.AsSpan()` or `request.AsMemory()` to select one.

A host that owns its transport also owns the deadline (the core has none; see [Deadlines](#asynchronous-methods-and-cancellation)). Link a `CancellationTokenSource` to the connection's lifetime token, arm it with `CancelAfter` and pass its token to `ProcessAsync` and to the write of the reply. When the budget expires, abort the transport at once, but still await the call: `ProcessAsync` completes only after the running method has terminated, a cancelled call commits no response bytes, and until the await returns the request memory and the output writer belong to the call, so neither goes back to a pool or is reused before then.

```csharp
// One request document on a connection the host owns: `rented` came from ArrayPool<byte>.Shared,
// `transport` is the connection's stream, `lifetime` is cancelled when the connection closes.
static async Task ServeDocumentAsync(string sessionId, byte[] rented, int length, Stream transport,
    CancellationToken lifetime)
{
    var output = new ArrayBufferWriter<byte>();
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
    deadline.CancelAfter(TimeSpan.FromSeconds(5));
    // When the budget expires, abort the transport at once, even while a method is still running.
    using var abort = deadline.Token.Register(static s => ((Stream)s!).Dispose(), transport);
    try
    {
        await JsonRpcProcessor.ProcessAsync(sessionId, new ReadOnlyMemory<byte>(rented, 0, length), output,
            context: transport, serializer: null, cancellationToken: deadline.Token);
    }
    catch (OperationCanceledException)
    {
        return;   // the call has terminated and wrote no response bytes
    }
    finally
    {
        // Only now that the awaited call has returned may the input go back to the pool
        // (or the output be reused): until then the processor may still read and write them.
        ArrayPool<byte>.Shared.Return(rented);
    }
    if (output.WrittenCount > 0)   // nothing is written for a notification
        await transport.WriteAsync(output.WrittenMemory, deadline.Token);
}
```

### Embedded: HTTP without ASP.NET Core, a pipe without Kestrel, a thread that owns the state

A plugin, an add-in, a desktop or editor process or a daemon cannot always bring the ASP.NET Core shared framework in, and often has a thread that must run the methods. [samples/EmbeddedHost](../samples/EmbeddedHost) is three hosts over one service, each about a page, with a `check` mode that runs them against a client:

- **`HttpListenerHost.cs`**: HTTP through `System.Net.HttpListener`, which is in the base library. What `MapJsonRpc` does for you, this host does itself: a body limit, a deadline (HttpListener has no client-abort token, so the host arms its own and answers `504` when it fires), `204` for a notification, `405` for anything but `POST`.
- **`StreamHost.cs`**: any `Stream` (the sample uses a named pipe) with newline-delimited documents in both directions, which is what MCP's stdio transport and most line-oriented clients expect. The host frames with `JsonFramer`, bounds its buffer, runs documents one at a time in order, and keeps a *separate* write loop fed by a queue: one loop that reads, processes and writes in turn deadlocks against a client that pipelines requests, because both ends end up waiting for the other to read. Server-side events become outbound notifications on the same queue, so they never interleave with a reply mid-write.
- **`UiThread.cs`**: a `SynchronizationContext` over one thread. Every document, and every continuation after an `await` inside a method, runs on that thread, so methods touch UI or editor state without locks. The core has no dispatch context of its own (that arrives with duplex in 2.6), so the host marshals whole documents, not individual calls.

The service passes `HttpListenerContext` or the connection's `Stream` as the RPC context, so a method can read `Handler.RpcContext()` before its first `await` and reach the caller; the same adapter shape works for any transport object.

### Kestrel HTTP endpoint

```csharp
using AustinHarris.JsonRpc.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddJsonRpc();
builder.Services.AddJsonRpcService<HostedCalculatorService>(); // a plain class with [JsonRpcMethod] members

var app = builder.Build();
app.MapJsonRpc("/rpc");                                    // POST /rpc; compose with RequireAuthorization() etc.
app.Run();
```

A request or batch answers `200 application/json`; a notification answers `204`. The body goes from `PipeReader` to `BodyWriter` without becoming a string. Inside a method, `JsonRpcContext.Current().Value` is the `HttpContext`.

`AddJsonRpcService<T>()` registers `T` as a singleton: resolved once from the root container when the host starts, one instance for every request on every thread, so it must be thread-safe and cannot take scoped dependencies such as an EF Core `DbContext` (a singleton uses `IDbContextFactory<T>`, or captures `((HttpContext)Handler.RpcContext()).RequestServices` before its first `await`). `AddJsonRpcService<T>(ServiceLifetime.Scoped)` (or `Transient`) resolves `T` per call from the request's provider instead: `HttpContext.RequestServices` on HTTP, a scope the raw connection handler opens and disposes per document. A `DbContext` then goes in the constructor as usual, every call of a batch shares one scope, and a transient is created per call. A lifetime that disagrees with an existing registration of `T` is refused, at registration or at startup. To await `Task` and `ValueTask` methods, set `o.EnableAsyncMethods = true` in `AddJsonRpc`; the request is then cancelled when the client disconnects (`RequestAborted`). The other options (session per request, serializer, body size limit, content type) and the per-endpoint overload `MapJsonRpc(pattern, options)` are in the [package README](../AustinHarris.JsonRpc.AspNetCore/README.md).

### Kestrel raw connections (TCP, Unix socket, named pipe)

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenLocalhost(9000, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenUnixSocket("/tmp/rpc.sock", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenNamedPipe("rpc", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
});
```

Clients write JSON documents back to back (whitespace or newlines between them are fine) and read the responses in the same order, also back to back: no newline, no `Content-Length` header, so the client must cut one complete JSON value at a time out of the stream. A client that expects newline-delimited or `Content-Length`-framed replies (an LSP or MCP stdio client) needs a host that adds that framing; [the embedded stream host](#embedded-http-without-aspnet-core-a-pipe-without-kestrel-a-thread-that-owns-the-state) shows the newline form. Notifications produce nothing. The framer accepts strict JSON only, so single-quoted strings and other lenient syntax are refused on a raw connection even with the Json.NET serializer.

A raw connection has no authentication, authorisation or rate limiting; those are HTTP middleware and do not run here. Listen on loopback or a Unix socket, or put something in front that authenticates. A document larger than `MaxRequestBytes` (4 MB) aborts the connection. The core applies its own `JsonRpcLimits` to the document the host hands over, so the transport limit is met first and the core limit second.

With `EnableAsyncMethods = true`, documents on one connection are processed one at a time in order, and replies already finished are flushed before the connection waits on a slow method. Separate connections run concurrently.

### Blazor WebAssembly

The core runs inside the browser. [samples/WasmHost](../samples/WasmHost) is a Blazor WebAssembly app where JavaScript hands a request document to a `[JSExport]`/`[JSInvokable]` method that calls the processor and returns the response, with no HTTP involved. The same service class then serves both the browser and the server. The sample page also benchmarks JSON-RPC against plain Blazor interop; the numbers are in its README.

### Classic ASP.NET (System.Web)

`AustinHarris.JsonRpc.AspNet` is a 1.x package and is not part of 2.0. It targets .NET Framework 4.0, and the 2.0 core needs `netstandard2.0` (.NET Framework 4.6.1 or later), so the two cannot be combined. To host in System.Web on 2.0, call `JsonRpcProcessor.ProcessSync` from your own `IHttpHandler`.

## Errors

### Exception disclosure

By default (`Config.IncludeExceptionDetails = false`), an unhandled exception thrown by a method, or thrown while its result is written, becomes `-32603 Internal Error` with `data: null`. Nothing about the exception leaves the process: not its type name, not its message. The redaction happens when the response is written, after the error handler ran, so a handler still sees the original `Exception` in `data` and can decide what the client gets instead:

```csharp
Config.SetErrorHandler((request, error) =>
    error.data is Exception ex ? new JsonRpcException(1000, "Server error", Log(ex)) : error);
```

Set `Config.IncludeExceptionDetails = true` only for trusted development clients: `data` then carries the full `ExceptionInfo` (`ClassName`, `Message`, `Source`, `StackTraceString`, `HResult` and the `InnerException` chain).

A `JsonRpcException` thrown by the application, or returned by an error handler, keeps the `data` it was given; that data is authored, not redacted.

The exception a handler receives is the one the method threw, with its own `InnerException` still attached; the library never substitutes the cause for the error. Only two wrapper types are stripped first, by type and wherever they came from: a `TargetInvocationException` (what reflection adds) and an `AggregateException` holding exactly one exception (what a faulted task observed through `Result` or `Wait` adds). An aggregate of several exceptions is passed whole. A `JsonRpcException` found only inside another exception's `InnerException` is not promoted: the outer exception is an internal error like any other.

### Handlers

Throw `JsonRpcException(code, message, data)` to return an error of your own. Pick the code outside `-32768..-32000`: the specification reserves that range, `-32700..-32600` for the errors in the table below and `-32099..-32000` for the server implementation, and a client that sees `-32000` cannot tell your error from the server's. Positive codes, or negative ones above `-32000`, are yours; the library raises none of them. To reshape errors, or to inspect requests on the way in and out, register handlers. Each handler belongs to one session:

```csharp
// Default session
Config.SetErrorHandler((request, exception) => new JsonRpcException(1000, "Server error", exception.data));
Config.SetParseErrorHandler((rawJson, exception) => exception);
Config.SetPreProcessHandler((request, context) => null);              // return a JsonRpcException to reject; may replace Method/Params/Id
Config.SetPostProcessHandler((request, response, context) => null);   // return a JsonRpcException to replace the result

// Another session: other sessions do not inherit the default session's handlers
Config.SetErrorHandler("client-42", (request, exception) => exception);
Config.SetParseErrorHandler("client-42", (rawJson, exception) => exception);
Config.SetPreProcessHandler("client-42", (request, context) => null);
Config.SetPostProcessHandler("client-42", (request, response, context) => null);
```

The overloads without a session id set the **default session's** handler, not a process-wide one; the overloads with a session id create the session when it does not exist yet, and a null handler clears only that session's. A pre- or post-process handler moves its session onto a slower path, which builds `JsonRequest` and `JsonResponse` objects for the handler to see. Leave them unset unless you need them. (`Config.SetBeforeProcessHandler(sessionId, …)`, the 1.x name, still works and is marked obsolete.)

### Error codes

The errors the library raises itself carry structured `data`, identical for every serializer, and the error handler receives the same object:

| Code | `error.data` | Object seen by the error handler |
| --- | --- | --- |
| `-32601` Method not found | `{"method":"<name as requested>"}` | `MethodNotFoundInfo` |
| `-32600` Invalid Request: the document or batch exceeds a configured limit | `{"limit":"maxDocumentBytes","maximum":4194304}` or `{"limit":"maxBatchCount","maximum":1024}` (the configured maximum) | `LimitExceededInfo` |
| `-32602` Invalid params: count, missing, unknown or repeated named parameter | a sentence, e.g. `"Named parameter 'b' was not present."` | `string` |
| `-32602` Invalid params: a value the serializer could not convert | `{"reason":"conversion","parameter":"b","index":1,"expectedType":"int32"}` plus `"message"` when `Config.IncludeExceptionDetails` is on; the value sent is never echoed | `ParameterErrorInfo` (with the serializer's exception in `Cause`) |
| `-32603` Internal error: the method threw, its result could not be written, or a parameter's type is one the serializer cannot handle | `null`, or the full `ExceptionInfo` when `Config.IncludeExceptionDetails` is on, see [Exception disclosure](#exception-disclosure) | `Exception` |

In the second `-32602` row, "could not convert" means the serializer refused the value (`JsonRpcBindException`, `FormatException`, `OverflowException`, `InvalidCastException`, or any `JsonException` from System.Text.Json or Json.NET); what each serializer accepts (say `"7"` for an `int`) is its own decision, see [docs/serializers.md](serializers.md).

## Asynchronous methods and cancellation

A method may return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`. Call it through `JsonRpcProcessor.ProcessAsync`. The processor awaits the method and writes its result; `Task` and `ValueTask` answer `null`. A method that completes synchronously runs inline and the byte overloads then return `Task.CompletedTask`.

**Cost.** With `RpcContextFlow.None`, the built-in numeric fast path adds no dispatcher allocation when the invocation completes inline. The service's own allocations (a `Task.FromResult`, a result string) are separate and included in the harness figures. Flow allocates an `InvocationState` even when the call completes inline. A method that suspends may allocate its own async state plus completion state in the result writer, the request handler and the document processor. The yielding benchmark measured 548 B per request under None at one worker (the median of two runs on 2026-09-28), including the service's allocations. A suspension also costs a continuation per request. The figures are in [Async](../README.md#async-processasync-awaited-workers).

```csharp
[JsonRpcMethod("lookup")]
public async Task<Item> Lookup(int id, [JsonRpcCancellation] CancellationToken cancellationToken)
    => await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);

await JsonRpcProcessor.ProcessAsync(sessionId, requestMemory, output,
    context: requestContext, cancellationToken: cancellationToken);
string response = await JsonRpcProcessor.ProcessAsync(sessionId, requestJson,
    context: requestContext, cancellationToken: cancellationToken);
```

**Which entry point to use.** `ProcessAsync` is the only entry point that awaits. `Process` and `ProcessSync` answer an async method with `-32603` and a message pointing to `ProcessAsync`, and do not call it. The older `Task<string> Process(…)` overloads run the synchronous path on the thread pool through `Task.Factory.StartNew`; despite returning a `Task`, they do not await async methods either.

**Deadlines.** The server defines no per-call deadline and no client-supplied deadline member. On HTTP, configure the ASP.NET Core request-timeouts middleware (`AddRequestTimeouts`, `UseRequestTimeouts`, `WithRequestTimeout`) on the endpoint; its budget covers the whole HTTP request including a batch, it is observed only with `EnableAsyncMethods = true` (the synchronous endpoint path passes no token), and only by `[JsonRpcCancellation]` parameters and by the processor between and after batch elements, so a completed result can be discarded without the method having observed a token. Raw and in-process hosts own their lifetime tokens; application methods own finer operation budgets. Cancellation is cooperative: the library waits for running methods and does not undo their effects. A host that owns its transport enforces a deadline itself; [In-process (strings or bytes)](#in-process-strings-or-bytes) shows one.

**Order.** A batch runs one request at a time, in order. Notifications are awaited like any other request.

**Cancellation.** To receive the processor's token, a method declares a `CancellationToken` parameter marked `[JsonRpcCancellation]`. That parameter never binds from JSON and is left out of the SMD, so it may sit anywhere in the signature: positional and named parameters are matched as if it were not there. Since C# wants optional parameters last, `(int id, [JsonRpcCancellation] CancellationToken token, int limit = 10)` is the natural shape. A `CancellationToken` parameter without the attribute is rejected at registration. A synchronous method called through `ProcessAsync` receives the token too; through `Process` and `ProcessSync` it receives the default token. When the token fires:

- the processor checks it before each invocation, between batch elements, and before writing the response;
- a method already running is waited for, not abandoned, so a method that ignores the token delays cancellation;
- nothing is written to `output`, the returned task is cancelled, and whatever the method already did stays done.

An `OperationCanceledException` that a method throws while the processor's token has not fired is an ordinary error.

**Buffers.** The byte overloads accept `ReadOnlyMemory<byte>`, `ReadOnlySequence<byte>` or `ReadOnlySpan<byte>`. The span overload and segmented sequences are copied before the first await. For memory you pass in, keep the request bytes unchanged and the output writer to yourself until the task completes. No output span is held across an await. The task finishes when the response is written, not when your transport has flushed it.

**Ambient context after an await.** `Handler.RpcContext()`, `Handler.RpcRequestId()`, `JsonRpcContext.Current()` and `Handler.RpcSetException()` work in an async method only up to its first real await. That default, `RpcContextFlow.None`, allocates nothing. Read what you need at the top of the method:

```csharp
[JsonRpcMethod("lookup")]
public async Task<Item> Lookup(int id)
{
    var http = (HttpContext)Handler.RpcContext();        // before the first await
    JsonRpcRequestId requestId = Handler.RpcRequestId();  // an owned copy, safe to keep
    var item = await repository.FindAsync(id);
    if (item == null) throw new JsonRpcException(1001, "Not found", null);
    return item;
}
```

If a method needs the accessors after awaiting, opt it into `RpcContextFlow.Flow`. The accessors then work across sequential awaits and nested dispatch, and every invocation pays one allocation for the execution-context bridge, completed tasks included:

```csharp
[JsonRpcMethod(ContextFlow = RpcContextFlow.Flow)]
public async Task<Item> Lookup(int id)
{
    var item = await repository.FindAsync(id);
    if (item == null) Handler.RpcSetException(new JsonRpcException(1001, "Not found", null));
    return item;
}

ServiceBinder.BindMethod(sessionId, "lookup", new Func<int, Task<Item>>(LookupAsync),
    contextFlow: RpcContextFlow.Flow);
```

In either mode, give parallel branches and background work their own copies of the context and request id, because the ambient state is cleared when the call finishes. Use `Handler.RpcRequestIdRaw()` only in the statement that reads it.

**Rejected at registration:** `async void`, custom awaitables, a `Task` that returns a `Task`, `IAsyncEnumerable<T>`, and `ref`/`out` parameters on async methods (including the legacy trailing `ref JsonRpcException`). A method that returns a null `Task` is answered with `-32603`.

Work that should outlive the request is better served by a job ticket:

```csharp
[JsonRpcMethod] private string startExport(string filter) { var job = Jobs.Start(() => ExportAsync(filter)); return job.Id; }
[JsonRpcMethod] private ExportStatus exportStatus(string jobId) => Jobs.Status(jobId);
```

The client gets a ticket immediately and polls, or the transport pushes a notification when the job finishes.

## Sessions and context

A *session* is a named set of JSON-RPC methods with its own configuration, stored in a process-wide registry until explicitly destroyed; it has no connection lifetime of its own and no relationship to ASP.NET Core session state. Sessions let you host independent sets of methods, for example one per connected client or tenant:

```csharp
ServiceBinder.BindMethod("client-42", "add", (int l, int r) => l + r);
string response = await JsonRpcProcessor.Process("client-42", request, context);
Handler.DestroySession("client-42");
```

Sessions are stored in a process-wide registry. Binding (`ServiceBinder.BindService`, `BindMethod`, `BindInterface`, a `JsonRpcService` constructor), the per-session `Config` setters and `Handler.GetSessionHandler(sessionId)` create a session; it remains until `Handler.DestroySession(sessionId)` is called. A request for a session id that was never registered creates nothing: every call in it answers `-32601` and the default session's methods are not reachable through it, so an id taken from a route or header cannot grow the registry. Each registration or destruction makes every thread refresh its copy of the registry on its next lookup, so register at startup or when a connection or tenant appears, not per request, and destroy tenant- or connection-scoped sessions when their lifetime ends.

### What sessions are for

A session is an independent method table with its own serializer, `jsonrpc` version policy and handlers, chosen per request by its id. That covers:

- **API versions.** Bind `v1` and `v2` as two sessions with different method names or parameter contracts and serve both at once.
- **Tenants.** Bind the same class once per tenant, each session over its own instance, and give some tenants methods the others do not have.
- **Connections.** Bind a session when a client connects and destroy it when the client disconnects, so its methods live exactly as long as the connection.
- **Capability sets.** Offer a small public session and a larger administrative one instead of a mode flag inside every method.
- **Migration and experiments.** Route selected clients to a session bound to a new implementation of the same method names while the rest keep the established one.
- **Wire compatibility.** Set the serializer, version policy and error handlers per session for clients with different expectations.
- **Several surfaces in one process.** An embedded host runs independent method sets side by side, even over one transport: the Kestrel HTTP endpoint picks the session per request through `JsonRpcOptions.SessionSelector`.

### Rough edges

- **Authorisation.** A session id is routing, not a permission. When it selects tenant data or privileged methods, the host decides which id a caller may name; the library guarantees only that an unknown id reaches nothing.
- **Lifetime.** A session stays in the registry until `Handler.DestroySession(sessionId)`. Connection- and tenant-scoped sessions need cleanup the host can rely on, such as a disconnect callback.
- **Registration cost.** Each registration or destruction makes every thread refresh its view of the registry on its next lookup, so a session per request is the wrong shape; a session per connection or tenant is fine.
- **Shared instances.** `BindService(sessionId, instance)` hands the same object to every concurrent call, so it must be thread-safe and must not keep request state in fields. `BindService(sessionId, typeof(T), resolve)` and `AddJsonRpcService<T>(ServiceLifetime.Scoped)` give each call its own instance; see [Classes](#classes).
- **The default session.** A `JsonRpcService` subclass built with the parameterless constructor binds to the default session, and a handler setter on `Config` without a session id changes the default session's handler.
- **Compatibility.** Two versions can expose different signatures, but an old result shape or old semantics still needs its own implementation or an adapter.
- **Configuration scope.** A session is not a whole server: exception disclosure is process-wide and the serializer can also be chosen per call; the scopes are listed under [Configuration](#configuration).

### Context

Pass an arbitrary context object through to your methods and read it with `Handler.RpcContext()` or `JsonRpcContext.Current().Value` (the AspNetCore package passes the `HttpContext` or `ConnectionContext`):

```csharp
await JsonRpcProcessor.Process(request, context: httpContext);

[JsonRpcMethod]
private string WhoAmI() => ((HttpContext)Handler.RpcContext()).User.Identity.Name;
```

A host of your own passes whatever names the caller: the request object, the connection, a client record. Read it at the top of the method, before the first `await` (the default context flow does not cross one; see [Ambient context after an await](#asynchronous-methods-and-cancellation)), and keep what you need in a local. Methods that must not depend on the transport take an interface the host implements and cast to that, so the same service runs under Kestrel, under `HttpListener` and in the tests.

### The request id

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

`JsonRpcRequestId` is a small struct you own: keep it anywhere. An integer id allocates nothing, a string id allocates its decoded string, and `RpcRequestIdRaw()` never allocates but is only valid until the method returns. If a method dispatches another request through `JsonRpcProcessor`, the inner method sees the inner id, and the outer id comes back afterwards. A pre-process handler that replaces `JsonRequest.Id` changes the id the method sees. A method parameter called `id` is unrelated: it binds from `params` like any other.

After an `await`, these accessors follow the rules in [Asynchronous methods and cancellation](#asynchronous-methods-and-cancellation).

## Configuration

Settings live on `Config`. They do not all reach every scope:

| Setting | Per call | Per session | Process-wide |
| --- | --- | --- | --- |
| Serializer | `serializer` argument | `Config.SetSerializer(sessionId, …)` | `Config.SetSerializer(…)` / `Config.Serializer` |
| `jsonrpc` version policy | | `Config.SetVersionPolicy(sessionId, …)` | `Config.VersionPolicy` |
| Exception details | | | `Config.IncludeExceptionDetails` |
| Error, parse-error, pre- and post-process handlers | | `Config.Set…Handler(sessionId, …)` (see [Handlers](#handlers)) | no: the overloads without a session id set the **default session's** handler |

Where more than one scope applies, the narrowest one wins. Context and cancellation are supplied per call.

### Serializer

```csharp
// Process-wide
Config.SetSerializer(new SystemTextJsonRpcSerializer(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

// Per session
Config.SetSerializer("legacy-clients", new NewtonsoftJsonRpcSerializer(new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));

// Per call
string json = JsonRpcProcessor.ProcessSync(sessionId, request, context, serializer);
```

The built-in serializer is the default. All three serializers write the envelope, primitives, dates and plain objects the same way (member order, `.0` on whole floats, ISO dates, nulls written), and the test suite runs its protocol cases against each of them. They are not interchangeable for every request: the coercions they accept, the CLR types they support and the object model they hand to handlers differ, so test client-visible requests and responses before switching. Library options go into the serializer's constructor and nowhere else:

| Serializer | Constructor | Notes |
| --- | --- | --- |
| built-in | `new JsmnSerializer(lenient: false, maxDepth: 64)` | `lenient` accepts `'single quotes'`, unquoted keys and trailing commas |
| Json.NET | `new NewtonsoftJsonRpcSerializer(settings)` | one `JsonSerializer` is built from the settings and reused; input is always lenient |
| System.Text.Json | `new SystemTextJsonRpcSerializer(options)` | the package adds its wire-format converters to a copy of your options when they are missing |

The full contract, what the core fixes versus what a serializer decides, is in [docs/serializers.md](serializers.md).

### Limits

```csharp
Config.SetLimits(new JsonRpcLimits(maxDocumentBytes: 8 * 1024 * 1024, maxBatchCount: 2048));
Config.SetLimits("legacy-clients", JsonRpcLimits.Unlimited);
```

Zero disables either bound; `JsonRpcLimits.Unlimited` disables both. A null per-session value inherits the process-wide limits.

### Nesting depth

Every serializer exposes `MaxDepth` (default 64). A request nested deeper is answered `-32700` before any handler or binding runs, so recursive parameter conversion is bounded by the same number the JSON library itself enforces: the built-in serializer's constructor argument, `JsonSerializerOptions.MaxDepth`, or `JsonSerializerSettings.MaxDepth`.

### The `jsonrpc` member

```csharp
Config.VersionPolicy = JsonRpcVersionPolicy.Lenient;          // process default
Config.SetVersionPolicy("strict-clients", JsonRpcVersionPolicy.Strict);   // per session; null follows the process default
```

| Policy | Missing member | `"2.0"` | Anything else |
| --- | --- | --- | --- |
| `Lenient` (default) | accepted | accepted | `-32600 Invalid Request` |
| `Ignore` | accepted | accepted | accepted |
| `Strict` | `-32600 Invalid Request` | accepted | `-32600 Invalid Request` |

The default keeps tool harnesses that omit the member working while a client speaking another version is told so. `Ignore` is for talking to anything at all.

## Security

What the library does by default:

- **Exception details are off.** An unhandled exception reaches the client as `-32603` with `data: null`: no type name, no message. `Config.IncludeExceptionDetails = true` sends the type, message, stack trace, source, HResult and inner exceptions; use it in development only. See [Exception disclosure](#exception-disclosure).
- **Rejected values are not echoed.** A `-32602` conversion error names the parameter and the expected type, never the value sent.
- **Nesting is limited to 64 levels.** A deeper request is `-32700` before any of your code runs.
- **Document and batch size are limited.** The core rejects a document over `JsonRpcLimits.MaxDocumentBytes` (4 MiB by default) or a batch with more than `MaxBatchCount` entries (1024) with `-32600` and a `data` object naming the limit, before anything is parsed or executed; `Config.SetLimits` changes them, `JsonRpcLimits.Unlimited` disables them. The Kestrel host also bounds bytes while receiving (`MaxRequestBytes`, 4 MB: HTTP answers `413`, a raw connection is aborted), so the first applicable limit wins. There is no response-size limit and no request deadline; a batch runs sequentially, so a batch of small requests ties up one request's worth of server time for all of them.
- **Every `[JsonRpcMethod]` is callable.** Visibility does not matter (private methods are exposed), and `AddJsonRpcServicesFromAssembly` exposes every class in the assembly that carries the attribute.
- **Requests do not create sessions.** An unknown session id answers `-32601` and leaves the registry alone; sessions are created by binding and by the per-session `Config` setters, and live until destroyed; see [Sessions and context](#sessions-and-context).
- **Cancellation is cooperative.** It waits for a running method and cannot undo what the method already did.

What it leaves to you:

Authentication, connection identity, TLS, rate limiting, request logging and deadlines belong to the host. HTTP hosts use ASP.NET Core middleware and endpoint metadata (`RequireAuthorization`, `UseRateLimiter`, the request-timeouts middleware); raw connections bypass that pipeline, so listen on loopback or a Unix socket, authenticate in front of them and use listener limits. Methods enforce authorisation that depends on parameter values. Core limits constrain admitted documents and batches, while transports bound bytes during receipt. Authentication and credential handling remain application responsibilities.

One service instance serves every request concurrently; see [Classes](#classes).

The `jsonrpc` member policy (`Lenient` by default) is a compatibility setting, not a control; see [The `jsonrpc` member](#the-jsonrpc-member).
