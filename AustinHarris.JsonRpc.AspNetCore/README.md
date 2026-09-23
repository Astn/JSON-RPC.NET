# AustinHarris.JsonRpc.AspNetCore

Hosts [JSON-RPC.Net](https://github.com/Astn/JSON-RPC.NET) in ASP.NET Core. The core processes requests as
UTF-8 bytes, so the HTTP endpoint reads the body with `PipeReader` and writes straight into
`Response.BodyWriter`; nothing is turned into a string on the way through. A `ConnectionHandler` does the
same for JSON-RPC over a raw Kestrel connection (TCP, Unix socket, named pipe).

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
    public double add(double l, double r) => l + r;
}
```

`POST /rpc` with a request or a batch answers `200 application/json`; a notification answers `204`.
Inside a method `JsonRpcContext.Current().Value` is the `HttpContext` (override with `ContextFactory`).
Classes deriving from `JsonRpcService` still bind themselves; `AddJsonRpcService<T>()` is for classes that
take constructor dependencies, and `AddJsonRpcServicesFromAssembly(typeof(Program).Assembly)` registers every
class that declares a `[JsonRpcMethod]`, MVC controllers included.

Because it is an ordinary endpoint, `RequireAuthorization()`, rate limiting, output caching and the rest of
the middleware pipeline compose with it:

```csharp
app.MapJsonRpc("/rpc").RequireAuthorization("api");
```

## Raw connection (TCP)

```csharp
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(9000, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
});
```

Clients write JSON documents back to back (a newline between them is fine); each document is answered in
order on the same connection, notifications produce nothing. The `ConnectionContext` is the RPC context.

With `EnableAsyncMethods = true`, HTTP awaits `ProcessAsync` with `HttpContext.RequestAborted`;
the body reader remains leased until invocation finishes. Raw connections await each framed document
before starting the next and flush earlier completed replies before waiting for a slow document.
Notifications are awaited and keep the same HTTP status rules. Connection cancellation is cooperative:
the processor waits for the running method to terminate before releasing input and discards its staged response.
Mark a `CancellationToken` parameter with `[JsonRpcCancellation]` to receive that token.
The default mode preserves synchronous processing and rejects async methods at call time.

## Options

| Option | Default | Meaning |
|---|---|---|
| `EnableAsyncMethods` | false | use ProcessAsync for Task/ValueTask methods, with host cancellation |
| `SessionId` | default session | which session's methods answer |
| `SessionSelector` | null | pick the session per HTTP request |
| `Serializer` | session, then `Config.Serializer` | serializer for this host |
| `ContextFactory` | `HttpContext` | what `JsonRpcContext.Current()` returns |
| `MaxRequestBytes` | 4 MB | larger bodies get 413 (HTTP) or abort the connection |
| `ResponseContentType` | `application/json` | |
| `NoContentForNotifications` | true | 204 for notifications, otherwise 200 with an empty body |
