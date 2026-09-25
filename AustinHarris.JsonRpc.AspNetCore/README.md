# `AustinHarris.JsonRpc.AspNetCore`

`AustinHarris.JsonRpc.AspNetCore` hosts
[`AustinHarris.JsonRpc`](https://www.nuget.org/packages/AustinHarris.JsonRpc) 2.0 in ASP.NET Core
and registers services through dependency injection.
Choose it for an HTTP endpoint or raw Kestrel connections over TCP, Unix sockets or named pipes.

The HTTP endpoint reads with `PipeReader` and writes to `Response.BodyWriter`
without converting the document to a string.

## Install

```sh
dotnet add package AustinHarris.JsonRpc.AspNetCore --prerelease
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

`AddJsonRpcService<T>()` registers `T` as a singleton, or reuses an existing singleton registration of `T`. When
the host starts, each singleton service is resolved once from the root container and bound; that one instance then
serves every HTTP request and every raw connection, concurrently. So `T` and its dependencies must be thread-safe,
and `T` cannot take scoped dependencies such as an EF Core `DbContext`: with scope validation on (the default in
Development), the host fails at startup; with it off, the dependency leaks. A singleton that needs per-request
services uses `IDbContextFactory<T>`, or resolves them from `((HttpContext)Handler.RpcContext()).RequestServices`
captured before its first `await` (the ambient context is per thread, see the main README).

`AddJsonRpcService<T>(ServiceLifetime.Scoped)` and `ServiceLifetime.Transient` bind the type instead of an
instance. Nothing is resolved at startup; right before each call the service is resolved from the request's
`IServiceProvider`, so a `DbContext` or any other scoped dependency goes in the constructor as usual:

- **HTTP:** the provider is `HttpContext.RequestServices`, the request scope ASP.NET Core already owns.
- **Raw connections:** the connection handler opens one scope per document from `IServiceScopeFactory`, publishes
  it on the connection as `IServiceProvidersFeature`, and disposes it once the document's response is written,
  before it is flushed. The scope never spans documents, so a connection cannot accumulate state. The cost is one
  scope per document, paid only when a scoped or transient service is bound to the handler's session.
- Every call of a batch shares the document's scope; a transient service is still created per call.
- Static `[JsonRpcMethod]` methods never resolve anything.
- There is no parameter injection: a method takes its dependencies through the service's constructor, or reads
  the context (`JsonRpcContext.Current().Value`) before its first `await`.

The lifetime you pass must match any registration of `T` already in the container: a mismatch throws from
`AddJsonRpcService`, or at startup when the conflicting registration is added later. A class deriving from
`JsonRpcService` binds itself in its constructor and is accepted as a singleton only. The host does not probe
services at startup; the container's own `ValidateOnBuild` and `ValidateScopes` catch a scoped dependency inside a
singleton.

When `ContextFactory` replaces the `HttpContext` as the RPC context, also set `ServiceProviderSelector` so the host
can find the request's provider from your context. With a scoped or transient service registered and no selector,
the host refuses to start (options from `AddJsonRpc`) or `MapJsonRpc(pattern, options)` refuses to map. A selector
that returns null falls through to the built-in one; when no provider is found the call fails with `-32603` and the
error handler sees an `InvalidOperationException` naming the service. The root provider is never used.

```csharp
builder.Services.AddScoped<OrdersContext>();                                   // an EF Core DbContext
builder.Services.AddJsonRpcService<OrdersService>(ServiceLifetime.Scoped);    // takes OrdersContext in its constructor
```

`AddJsonRpcServicesFromAssembly(assembly)` does the same for every non-abstract class in the assembly that
declares a `[JsonRpcMethod]`, as singletons unless you pass a `ServiceLifetime`. Private methods count, so the
attribute is the whole access list, and an MVC controller that carries it is registered too.

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
  15.0 M to 15.4 M against 14.3 M to 16.5 M for the synchronous mode, inside its day-to-day spread. A method that suspends pays for its own async state,
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
| `ServiceProviderSelector` | `HttpContext.RequestServices`, or the raw document scope | HTTP and raw | where scoped and transient services are resolved from, given the RPC context; required with `ContextFactory` when such a service is registered |
| `MaxRequestBytes` | 4 MB | HTTP body, or one raw document | larger bodies get 413; a larger raw document aborts the connection |
| `ResponseContentType` | `application/json` | HTTP | |
| `NoContentForNotifications` | true | HTTP | 204 for notifications, otherwise 200 with an empty body |

For raw connections the RPC context is always the `ConnectionContext`; `SessionSelector`, `ContextFactory`,
`ResponseContentType` and `NoContentForNotifications` are not used.
