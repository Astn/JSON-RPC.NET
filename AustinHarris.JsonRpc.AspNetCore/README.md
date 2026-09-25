# AustinHarris.JsonRpc.AspNetCore

Hosts [JSON-RPC.Net](https://github.com/Astn/JSON-RPC.NET) in ASP.NET Core. The core processes requests as
UTF-8 bytes, so the HTTP endpoint reads the body with `PipeReader` and writes straight into
`Response.BodyWriter`; nothing is turned into a string on the way through. A `ConnectionHandler` does the
same for JSON-RPC over a raw Kestrel connection (TCP, Unix socket, named pipe).

## Install

```
dotnet add package AustinHarris.JsonRpc.AspNetCore
```

Targets `net8.0` and `net10.0`; depends on the `AustinHarris.JsonRpc` core package and the ASP.NET Core shared
framework.

## HTTP endpoint

```csharp
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddJsonRpc(o =>
{
    // o.EnableAsyncMethods = true;                       // await Task and ValueTask service methods
    // o.Serializer = new SystemTextJsonRpcSerializer();   // optional, default is the built-in serializer
    // o.SessionSelector = http => http.Request.RouteValues["session"] as string;
});
builder.Services.AddJsonRpcService<CalculatorService>();   // any class with [JsonRpcMethod] methods, built by DI

var app = builder.Build();
app.MapJsonRpc("/rpc");
app.Run();

public class CalculatorService
{
    private readonly ILogger<CalculatorService> _log;
    public CalculatorService(ILogger<CalculatorService> log) => _log = log;

    [JsonRpcMethod]
    public double add(double l, double r)
    {
        _log.LogDebug("add {L} {R}", l, r);
        return l + r;
    }
}
```

`POST /rpc` with a request or a batch answers `200 application/json`; a notification answers `204`.
Inside a method `JsonRpcContext.Current().Value` is the `HttpContext` (override with `ContextFactory`).

Because it is an ordinary endpoint, `RequireAuthorization()`, rate limiting, output caching and the rest of
the middleware pipeline compose with it:

```csharp
app.MapJsonRpc("/rpc").RequireAuthorization("api");
```

`MapJsonRpc` adds no authorization, TLS requirement, rate limit or request deadline by itself; apply those
policies explicitly. `MaxRequestBytes` limits the HTTP body, but there is no batch-count or response-size limit.
Keep `Config.IncludeExceptionDetails` off for untrusted clients: by default an unhandled exception is answered as
`-32603` with `data: null`, see [Exception disclosure](https://github.com/Astn/JSON-RPC.NET#exception-disclosure)
in the main README.

`MapJsonRpc(pattern = "/jsonrpc", options = null)` uses the options from `AddJsonRpc` unless you pass your own,
so two endpoints can serve two sessions, for example a strict API next to a lenient one for older clients:

```csharp
app.MapJsonRpc("/rpc");
app.MapJsonRpc("/legacy", new JsonRpcOptions { SessionId = "legacy-clients", Serializer = new NewtonsoftJsonRpcSerializer() });
```

## Services and lifetime

`AddJsonRpcService<T>()` registers `T` as a singleton unless `T` is already registered. When the host starts,
it resolves each registered service once from the root container and binds it. That one instance then serves
every HTTP request and every raw connection concurrently. So `T` and its dependencies must be thread-safe.
`T` cannot take scoped dependencies such as an EF Core `DbContext`. With scope validation on, the host fails
at startup. With it off, the dependency leaks. On HTTP, resolve per-request services inside the method from
`((HttpContext)Handler.RpcContext()).RequestServices`. A raw connection's context is the
`ConnectionContext`, which has no request scope. Do not inject request-scoped state into a service.
Read per-request data from the context instead.

`AddJsonRpcServicesFromAssembly(assembly)` does the same for every non-abstract class in the assembly that
declares a `[JsonRpcMethod]`. Private methods count, so the attribute is the whole access list. An MVC
controller that carries it becomes a singleton too.

The host binds every registered service to its effective session: the session given to `AddJsonRpcService`, else
`JsonRpcOptions.SessionId`, else the default. A class deriving from `JsonRpcService` also binds itself to the
default session in its parameterless constructor, so with `SessionId` set it is reachable in both; write
`: base(false)` in the subclass to leave that to the host.

## Raw connection (TCP, Unix socket, named pipe)

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenLocalhost(9000, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenUnixSocket("/tmp/rpc.sock", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
    // k.ListenNamedPipe("rpc", l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
});
```

Clients write JSON documents back to back (whitespace or newlines between them are fine) and read the responses
in the same order, also back to back with no newline or `Content-Length` prefix, so the client must parse one
complete JSON value at a time. Notifications produce nothing. The `ConnectionContext` is the RPC context.

The framer accepts strict JSON only, even with the Json.NET serializer or a lenient `JsmnSerializer` selected.
A document larger than `MaxRequestBytes` aborts the connection. Documents on one connection are processed one at
a time, in order; separate connections run concurrently.

A raw connection does not pass through the HTTP middleware pipeline, so it has no authentication, authorisation
or rate limiting. Listen on loopback or a Unix socket, or configure transport security, authentication and
connection limits at the Kestrel listener or in a surrounding protocol.

## Async methods

Set `EnableAsyncMethods = true` to serve `Task` and `ValueTask` methods through `ProcessAsync`. With it off (the
default), requests are processed synchronously and an async method is answered with `-32603` without being
invoked.

- **HTTP:** the call is cancelled when the client disconnects (`HttpContext.RequestAborted`). Notifications are
  awaited and still answer `204`. The body reader stays leased until the invocation finishes.
- **Raw connections:** documents are processed one at a time, in order. On one connection, 256 pipelined requests
  are 256 sequential invocations, not 256 concurrent suspensions. Concurrency comes from connections.
  Replies already finished are flushed before the connection waits on a slow method. When the connection closes,
  the handler waits for the running method to finish and discards its response.
- **Cost:** every document then goes through `ProcessAsync`. With methods that complete inline, the TCP row measures
  about 7 % below the synchronous mode (15.4 M against 16.5 M). A method that suspends pays for its own async state,
  the library's completion state (about 560 B) and a continuation per request. The main README's Kestrel table has
  both rows, measured with `TestServer_Console --kestrel 3 async`.

A method receives the token by declaring a `[JsonRpcCancellation] CancellationToken` parameter; see
[Asynchronous methods and cancellation](https://github.com/Astn/JSON-RPC.NET#asynchronous-methods-and-cancellation)
in the main README.

## Options

| Option | Default | Scope | Meaning |
|---|---|---|---|
| `EnableAsyncMethods` | false | HTTP and raw | use `ProcessAsync` for `Task`/`ValueTask` methods, with host cancellation |
| `SessionId` | default session | HTTP and raw | which session's methods answer |
| `SessionSelector` | null | HTTP | pick the session per request from the `HttpContext`; an id that was never registered answers `-32601` and creates nothing |
| `Serializer` | session, then `Config.Serializer` | HTTP and raw | serializer for this host |
| `ContextFactory` | `HttpContext` | HTTP | what `JsonRpcContext.Current()` returns |
| `MaxRequestBytes` | 4 MB | HTTP body, or one raw document | larger bodies get 413; a larger raw document aborts the connection |
| `ResponseContentType` | `application/json` | HTTP | |
| `NoContentForNotifications` | true | HTTP | 204 for notifications, otherwise 200 with an empty body |

For raw connections the RPC context is always the `ConnectionContext`; `SessionSelector`, `ContextFactory`,
`ResponseContentType` and `NoContentForNotifications` are not used.
