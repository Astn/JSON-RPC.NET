# JSON-RPC.Net

![Build Master](https://github.com/Astn/JSON-RPC.NET/workflows/Build%20Master/badge.svg) ![NuGet](https://img.shields.io/nuget/v/AustinHarris.JsonRpc)

A JSON-RPC server has a short job description: read a request, call a method, write a response. The work around that call can be considerably larger. JSON-RPC.Net 2.0 keeps the core at the document boundary: UTF-8 bytes go in, UTF-8 bytes come out, and your application chooses how they travel. The same service can sit behind Kestrel, a pipe, a socket, or a browser page.

For example, send `{"jsonrpc":"2.0","method":"add","params":[1,2],"id":1}` and the `add` method returns `{"jsonrpc":"2.0","result":3.0,"id":1}`. Below, you can run that exchange as a complete server. What does keeping this path in bytes buy us?

## Performance

The same five small methods were measured through the last 1.x release and through 2.0 in one job on one 32-core host. The ranges show the low and high of that job's runs. Through the **same string API and loop**, 2.0's median throughput was 6.0× that of 1.2.3. Through 2.0's new byte entry point, the median comparison was 24.3×. Those are different call paths; the table names each one.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/headline-1x-vs-2-dark.svg">
  <img alt="JSON-RPC.Net 1.2.3 and 2.0 on one machine, low to high over the runs of one job on 2026-09-28: 1.29 M to 2.41 M requests per second through the 1.2.3 string API, 9.83 M to 11.1 M through the same API on 2.0, and 44.2 M to 45.7 M and 36.3 M to 40.5 M through the 2.0 byte entry points" src="benchmarks/charts/headline-1x-vs-2.svg">
</picture>

<!-- benchmarks:headline -->
| Path | RPC/s | Against 1.2.3 |
| --- | ---: | ---: |
| 1.2.3, `Task<string> Process(string)`, thread pool, best batch size | 1.29 M to 2.41 M | |
| 2.0, the same string API and the same loop | 9.83 M to 11.1 M | 6.0× |
| 2.0, `Process(bytes)`, 32 dedicated threads | 44.2 M to 45.7 M | 24.3× |
| 2.0, `ProcessAsync(bytes)`, 32 awaited workers | 36.3 M to 40.5 M | 20.7× |

<!-- /benchmarks:headline -->

The byte path parses the request and writes the reply without making a result string or scheduling a `Task` for every call. On one thread, the five-request mix averaged a reported 315 ns per request; numeric shapes allocated 0 bytes in the library. Over pipelined Kestrel TCP, the host reached 18.5 M RPC/s. The async and HTTP results in [Benchmarks](#benchmarks) show what each boundary adds.

## Getting started

Here is a server you can put in one file. Save it as `server.cs`; the `#:` directives make it a .NET 10 file-based app. Two lambdas become RPC methods, and Kestrel gives them an HTTP endpoint.

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:package AustinHarris.JsonRpc.AspNetCore@2.0.0

using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.AspNetCore;

ServiceBinder.BindMethod("add", (double l, double r) => l + r);
ServiceBinder.BindMethod("greet", (string who) => "hello " + who);

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddJsonRpc();

var app = builder.Build();
app.MapJsonRpc("/rpc");
app.Run();
```

Run `dotnet run server.cs -- --urls http://127.0.0.1:5077`, then call it from another terminal:

```bash
curl -s -X POST http://127.0.0.1:5077/rpc -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","method":"add","params":[1,2],"id":1}'
```

```json
{"jsonrpc":"2.0","result":3.0,"id":1}
```

That is the whole round trip. A request without an `id` is a notification and gets no response; a batch is an array of requests and gets an array of responses, omitting notifications. On .NET 8, put the same C# code in an ASP.NET Core `Program.cs`, remove the two `#:` lines, and install `AustinHarris.JsonRpc.AspNetCore`.

### Calling the core directly

If your transport already gives you a JSON document, you can call the processor yourself. The byte overload is the native path; the string overload is convenient when the document is already a string.

```csharp
using System.Buffers;
using System.Text;
using AustinHarris.JsonRpc;

ServiceBinder.BindMethod("add", (int l, int r) => l + r);
var output = new ArrayBufferWriter<byte>();
JsonRpcProcessor.Process(Handler.DefaultSessionId(),
    """{"method":"add","params":[2,3],"id":3}"""u8, output);
Console.WriteLine(Encoding.UTF8.GetString(output.WrittenSpan));
```

The processor does not open a listener or choose framing. A host that reads several documents from one stream can use `JsonFramer`; [the embedded host sample](samples/EmbeddedHost) shows a complete pipe host. The [reference](docs/reference.md#in-process-strings-or-bytes) covers buffer ownership, cancellation and deadlines.

## Where it fits

JSON-RPC.Net is a **server** for [JSON-RPC 2.0](https://www.jsonrpc.org/specification). It binds methods, reads documents and writes replies. It does not provide client proxies or server-to-client calls. For a bidirectional RPC framework, [StreamJsonRpc](https://www.nuget.org/packages/StreamJsonRpc) serves a different set of needs; the [comparison](#versus-streamjsonrpc-and-grpc) explains what each benchmark measures.

The core has no JSON-library dependency. Its built-in serializer is the default; Json.NET and System.Text.Json are optional packages. The four packages share a version and are MIT licensed:

| Package | Choose it for |
| --- | --- |
| `AustinHarris.JsonRpc` | The document-in, document-out server, with the built-in serializer. |
| `AustinHarris.JsonRpc.AspNetCore` | HTTP or raw connections hosted by Kestrel, plus service registration through DI. |
| `AustinHarris.JsonRpc.Newtonsoft` | Json.NET settings, converters, attributes or lenient input. |
| `AustinHarris.JsonRpc.SystemTextJson` | System.Text.Json options and converters. |

For an existing project, install the core with `dotnet add package AustinHarris.JsonRpc`, then add the serializer or ASP.NET Core package you need.

## Defining methods

A delegate is enough for a small API, as the first example shows. When methods belong together, mark members of a class with `[JsonRpcMethod]`:

```csharp
public class CalculatorService : JsonRpcService
{
    [JsonRpcMethod]
    private double add(double l, double r) => l + r;

    [JsonRpcMethod("multiply")]
    public int Multiply(int l, int r) => l * r;
}
```

Constructing `CalculatorService` registers it in the default session; keep that instance alive. The attribute makes even a `private` method callable by a client, so review every annotated member as part of your public API. Parameters can be positional or named, and optional defaults work for either. One service instance may handle concurrent requests, so shared state needs to be thread-safe. If you prefer to make the wire contract explicit, `ServiceBinder.BindInterface<T>` can expose methods through an interface tree. The [binding reference](docs/reference.md#defining-methods) has the registration rules, names, lifetimes and interface options.

## Hosting

Choose a host for the boundary you already have:

| Boundary | Start here |
| --- | --- |
| ASP.NET Core HTTP | `app.MapJsonRpc("/rpc")`; add authentication and authorization as usual. Enable async methods for cooperative request timeouts. [Hosting options](AustinHarris.JsonRpc.AspNetCore/README.md). |
| Raw TCP, Unix socket or named pipe in Kestrel | `JsonRpcConnectionHandler` reads consecutive JSON documents. Clients must frame complete JSON replies themselves. [Connection setup](docs/reference.md#kestrel-raw-connections-tcp-unix-socket-named-pipe). |
| A transport you own | Pass request bytes and an output writer to `JsonRpcProcessor`; the [embedded examples](samples/EmbeddedHost) show HTTP without ASP.NET Core, a newline-delimited pipe and a UI thread. |
| Blazor WebAssembly | Call the processor from browser interop; [WasmHost](samples/WasmHost/README.md) runs the same service in a browser. |

The core leaves transport policy to the host. A raw Kestrel connection does not run HTTP middleware, so it needs authentication and limits at the listener or in front of it. [Hosting details](docs/reference.md#hosting) cover framing, service lifetimes and cancellation.

## Asynchronous methods and cancellation

Methods may return `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>`. Use `ProcessAsync` to await them. In the ASP.NET Core host, set `builder.Services.AddJsonRpc(o => o.EnableAsyncMethods = true)`; the default host uses the synchronous path. The older `Task<string> Process(...)` compatibility overload schedules synchronous work and does not await an async method.

```csharp
[JsonRpcMethod("lookup")]
public async Task<Item> Lookup(int id, [JsonRpcCancellation] CancellationToken cancellationToken)
    => await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
```

Cancellation is cooperative. The processor waits for a running method to finish, even after its token fires, and cannot undo effects the method has already made. A host sets the deadline; the processor has none of its own. By default, ambient RPC context is available until the first real `await`. Capture what you need before it, or opt a method into `RpcContextFlow.Flow` if its accessors must work afterward. Flow has an allocation cost measured in the [async benchmark](#async-processasync-awaited-workers). The [async reference](docs/reference.md#asynchronous-methods-and-cancellation) covers ownership of buffers and the exact cancellation rules.

## Errors

Throw `JsonRpcException(code, message, data)` for an error your client should see. Binding failures use `-32602`, a missing method uses `-32601`, and an unhandled method failure becomes `-32603`.

### Exception disclosure

By default, an unhandled exception sends `-32603 Internal Error` with `data: null`: its type and message stay on the server. `Config.IncludeExceptionDetails = true` includes exception details for trusted development clients. Application-authored `JsonRpcException` data is sent as supplied. The [error reference](docs/reference.md#errors) lists the structured error data and the per-session hooks for reshaping responses.

## Sessions and context

A session is a named method table with its own serializer and handlers. The default session is enough for an application that owns its process. A plugin or multi-tenant host can bind its own session, route calls to it, and destroy it when the tenant or connection goes away. A session ID chooses methods; it does not authorize a caller. Requests for an unknown session answer “method not found” without creating one. See the [session reference](docs/reference.md#sessions-and-context) for registration and lifetime rules.

The host may pass a context object with each call. Under ASP.NET Core it is an `HttpContext` or `ConnectionContext`; a method reads it with `Handler.RpcContext()`. Capture it before the first `await` unless context flow is enabled.

### The request id

A method can read the request's `id` through `Handler.RpcRequestId()` or `JsonRpcContext.CurrentRequestId()` when it needs to correlate work. The [request-id reference](docs/reference.md#the-request-id) covers string, integer, null and notification IDs, including the borrowed raw-byte accessor.

## Configuration

The built-in serializer is ready to use. Choose Json.NET or System.Text.Json when your application needs their converters or options; test client-visible requests before switching because accepted values and supported CLR types differ. A serializer can be selected per call, per session or for the process. The `jsonrpc` version policy and limits are also configurable. By default the core accepts documents up to 4 MiB and batches up to 1024 entries. [Configuration details](docs/reference.md#configuration) lists the scopes and constructors, and the [serializer guide](docs/serializers.md) describes the wire behavior.

### Nesting depth

Every serializer defaults to a maximum JSON depth of 64. The Kestrel host also limits bytes while receiving, before the core sees them. [Limits and error behavior](docs/reference.md#limits) explains which bound applies first.

## Security

The library redacts unexpected exceptions, rejects oversized documents and batches, and does not create a session for an unknown session ID. Every `[JsonRpcMethod]` is exposed regardless of C# visibility. One service instance can serve concurrent calls.

The host owns authentication, authorization, TLS, rate limiting, logging and deadlines. On HTTP, use ASP.NET Core middleware and endpoint metadata; raw connections need equivalent controls at the listener. A session ID is routing, not a permission check. The [security reference](docs/reference.md#security) gives the defaults and boundaries in detail.

## Requirements

The core and both serializer packages target `netstandard2.0`, `netstandard2.1`, `net8.0` and `net10.0`. The ASP.NET Core package targets `net8.0` and `net10.0`. CI tests .NET 8 and .NET 10 on Windows and Linux; the other runtimes can load the `netstandard` assets but are outside that test matrix.

The reflection binder is not supported under trimming or Native AOT. A trimmed application can lose bound methods. The WebAssembly sample runs in its documented interpreter and AOT configurations, which do not validate `PublishTrimmed` or `PublishAot` for the library. [Versioning and support](#versioning-and-support) has the release policy.


## Benchmarks

How much of a request's cost belongs to the server, and how much belongs to everything around it? These results follow the same small calls from a direct byte invocation to awaited methods, Kestrel, and competing frameworks. The summary is a map; each section below says which work its number includes.

<!-- benchmarks:summary -->
| What | API and mode | RPC/s | Details |
| --- | --- | ---: | --- |
| Library alone, 32 threads | `Process(bytes)`, dedicated threads | 44.2 M to 45.7 M | [Sync](#sync-the-library-alone) |
| Library alone, 32 workers | `ProcessAsync(bytes)`, awaited workers, methods that complete inline | 26.7 M to 40.5 M | [Async](#async-processasync-awaited-workers); the spread is across registrations, not runs |
| Library alone, 32 workers, one real suspension per request | `ProcessAsync(bytes)`, `yieldsOnce` | 2.55 M | |
| Kestrel TCP, 256 pipelined | `EnableAsyncMethods = false` | 18.5 M | [Kestrel](#kestrel-through-the-aspnetcore-package) |
| Kestrel TCP, 256 pipelined | `EnableAsyncMethods = true`, methods that complete inline | 17 M to 17.1 M | |
| Kestrel TCP, 256 pipelined | `EnableAsyncMethods = true`, methods that suspend once | 2.86 M to 2.87 M | |
| Kestrel HTTP, batch of 100 per POST | `EnableAsyncMethods = false` | 14.4 M to 14.8 M | |
| Kestrel HTTP, one request per POST | `EnableAsyncMethods = false` | 156 k to 162 k | HTTP/1.1 round trips dominate |
| Legacy string API, thread pool | `Task<string> Process(string)`, batches of 6,000 | 9.83 M to 11.1 M | [Legacy](#legacy-string-api-scheduled-synchronous-execution); the 1.x overloads, not the byte path |

<!-- /benchmarks:summary -->

The main tables and charts come from one Hugging Face Jobs `cpu-performance` run: AMD EPYC 7R13, 32 cores (a cgroup quota of all 32 host CPUs), Ubuntu 24.04.5 LTS, .NET 10.0.12, Release, Server GC, built-in serializer. The publish job ran on 2026-09-28 (job 6ab9b3936b030d633f69b0fb, commit 6c3c272). It ran the scaling gate once, the legacy string mode three times, every other mode and `benchmarks/Baseline` twice, and the connection sweep five times, in sequence. Ranges are the low and high over those runs; a single figure is one that every run rounded to. Each Sync ns figure is the median of the runs' reported costs. The summary's inline Async range also spans different registrations. The [explorer](https://astn.github.io/JSON-RPC.NET/benchmarks/charts/explorer.html) shows the medians and individual runs.

`TestServer_Console` binds five small methods (`add`, `addInt`, `NullableFloatToNullableFloat`, `Test2` and `StringMe`), sends the same five requests through each path, and checks that every reply is a result rather than an error. The WebAssembly figures come from a separate desktop browser run on 2026-09-23. To repeat the publish job or inspect its exact commands, see [the benchmark guide](benchmarks/hf/README.md); [micro-benchmarks](benchmarks/Micro/README.md) show one-request cost and allocations.

### Sync: the library alone

Start with the library alone. `--sync` calls the byte-level `JsonRpcProcessor.Process` on dedicated threads, measuring parsing, dispatch, binding and response writing without a transport or a per-call scheduler hop. What changes when more cores join the loop?

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/sync-threads-dark.svg">
  <img alt="JSON-RPC.Net alone, by worker threads: aggregate requests per second as a low-to-high band, and the reported ns per request per thread" src="benchmarks/charts/sync-threads.svg">
</picture>

<!-- benchmarks:sync -->
| Threads | RPC/s | ns per request per thread | Allocations per request |
| ---: | ---: | ---: | --- |
| 1 | 3.17 M to 3.18 M | 315 | 0 bytes for numeric shapes, one string for `StringMe` |
| 2 | 4.31 M to 4.66 M | 446 | |
| 4 | 10.7 M to 12 M | 355 | |
| 8 | 18.4 M to 19.8 M | 419 | |
| 16 | 34.5 M to 35.8 M | 456 | |
| 32 | 44.2 M to 45.7 M | 712 | |

<!-- /benchmarks:sync -->

One thread handled 3.17 M to 3.18 M RPC/s at a reported 315 ns per request. At 32 threads the aggregate reached 44.2 M to 45.7 M RPC/s, while the reported per-thread cost rose to 712 ns as every available core was occupied. Numeric shapes allocated 0 bytes in the library; `StringMe` returned its own string.

### Async: ProcessAsync, awaited workers

The next question is what `ProcessAsync` costs when a method can await. `--async` starts `Task.Run` workers once, then each worker awaits calls over the same request set as `--sync`. Most registrations complete inline; `yieldsOnce` really awaits `Task.Yield()` on every request. The last column includes the service method's own allocations at one worker, so it measures the whole call shape.

<!-- benchmarks:async -->
| Registration | 1 worker | 32 workers | B per request |
| --- | ---: | ---: | ---: |
| synchronous methods, None | 2.51 M to 2.52 M | 26.7 M to 28.7 M | 6 |
| `Task<T>`, Flow | 1.98 M | 27 M to 27.6 M | 251 |
| `Task<T>`, None | 2.63 M to 2.66 M | 34.4 M to 35.5 M | 67 |
| `ValueTask<T>`, Flow | 2.05 M to 2.06 M | 28.6 M to 31.4 M | 190 |
| `ValueTask<T>`, None | 2.77 M to 2.82 M | 36.3 M to 40.5 M | 6 |
| `yieldsOnce`, Flow | 563 k to 589 k | 2.74 M to 2.78 M | 740 |
| `yieldsOnce`, None | 544 k to 565 k | 2.55 M | 548 |

<!-- /benchmarks:async -->

The 32-worker inline rows mix the five requests equally; `yieldsOnce` is one request shape. The per-method allocation table below makes the source of those bytes easier to see. Each value is bytes per request at one worker, including method allocations.

| Registration | `add` | `addInt` | nullable float | decimal | `StringMe` |
| --- | ---: | ---: | ---: | ---: | ---: |
| synchronous methods, None | 0 | 0 | 0 | 0 | 32 |
| `Task<T>`, Flow | 256 | 184 | 256 | 272 | 288 |
| `Task<T>`, None | 72 | 0 | 72 | 88 | 104 |
| `ValueTask<T>`, Flow | 184 | 184 | 184 | 184 | 216 |
| `ValueTask<T>`, None | 0 | 0 | 0 | 0 | 32 |

With `RpcContextFlow.None`, the dispatcher adds no allocation when a method completes inline. The `Task<T>` None row includes the service's `Task.FromResult` allocations (`Task<int>` for 8 uses the runtime cache); `StringMe` allocates its 32-byte result string. Flow allocates invocation and execution-context state even for an inline completion. An actual suspension also needs completion state in the method, result writer, handler and processor: `yieldsOnce` under None measured 548 B per request at one worker, including the method.

A lock explains why the async path needed a scaling test. Before 2.0.0, one lock on a shared scratch pool capped `ProcessAsync` near 4 M RPC/s at every worker count; a single-thread benchmark could not reveal it. A per-thread scratch slot removed that bottleneck. The scaling gate measured 1.3 with the lock and 12.1 to 13.1 at 32 workers with the cache (medians on 2026-09-28). It tests inline methods across worker counts and serializers before release; [the benchmark guide](benchmarks/hf/README.md) has the gate and diagnostic commands.

### Legacy string API: scheduled synchronous execution

The 1.x compatibility overload pays for work the byte path avoids. The `t` mode sends batches through `Task<string> Process(string)` from every core. Each request pays for transcoding, a thread-pool hop, a `Task`, a result string and a continuation. The table varies batch size after a one-second warm-up, timing each size for at least half a second. More queued work helps until the queue itself becomes the cost.

<!-- benchmarks:legacy -->
| Batch size | RPC/s |
| ---: | ---: |
| 50 | 1.08 M to 1.44 M |
| 100 | 1.43 M to 1.51 M |
| 300 | 3.72 M to 4.1 M |
| 1,200 | 7.55 M to 7.8 M |
| 6,000 | 9.83 M to 11.1 M |
| 36,000 | 10.3 M to 10.8 M |
| 252,000 | 5.96 M to 6.78 M |
| 2,016,000 | 3.4 M to 6.15 M |

<!-- /benchmarks:legacy -->

That peak is below the byte paths because this mode measures the thread pool and string API as well as dispatch. The ASP.NET Core host uses bytes and awaits transport reads and flushes; its results bring the network boundary into view.

### Kestrel: through the AspNetCore package

What changes when the call goes through a real host? `--kestrel` starts Kestrel on loopback with the HTTP endpoint and raw connection handler, then drives it from one client per core (32 on this host). The client and server share a loopback machine and compete for its cores, so these are local results rather than remote end-to-end predictions. The in-process row provides scale. The default host calls `Process`; the async rows enable `ProcessAsync`, first with methods that complete inline and then with methods that await `Task.Yield()`.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/kestrel-transports-dark.svg">
  <img alt="JSON-RPC.Net by transport: HTTP single, HTTP batch of 100 and TCP pipelined, as low-to-high intervals on a log axis, with the in-process figure for scale" src="benchmarks/charts/kestrel-transports.svg">
</picture>

<!-- benchmarks:kestrel -->
| Transport | RPC/s | Note |
| --- | ---: | --- |
| in-process, 32 threads | 47.3 M to 49.6 M | |
| HTTP, 1 request per POST | 156 k to 162 k | 197 to 205 µs per round trip per client depending on the run; HTTP/1.1 request-response is the cost, not the server |
| HTTP, batch of 100 per POST | 14.4 M to 14.8 M | |
| TCP, 256 pipelined | 18.5 M | ring-buffer clients, one thread each, streaming framer |
| TCP, 256 pipelined, `EnableAsyncMethods = true`, methods that complete inline | 17 M to 17.1 M | `--kestrel 3 async`, two runs |
| TCP, 256 pipelined, `EnableAsyncMethods = true`, methods that suspend once | 2.86 M to 2.87 M | five `async Task<T>` methods awaiting `Task.Yield()` |

<!-- /benchmarks:kestrel -->

One HTTP POST per call spends most of its time on the round trip: 156 k to 162 k RPC/s, versus 14.4 M to 14.8 M when each POST contains a batch of 100. Raw TCP with 256 pipelined requests reached 18.5 M. The host processes documents in order on each connection; enabling async methods preserves that order. When every method actually suspends, each pipelined request pays for its own continuation, and the TCP row falls to 2.86 M to 2.87 M. The publish job tracks that suspended row for regressions; [the benchmark guide](benchmarks/hf/README.md) describes the gate.

### Versus StreamJsonRpc and gRPC

The comparison needs a little care. [StreamJsonRpc](https://www.nuget.org/packages/StreamJsonRpc) is Microsoft's full bidirectional JSON-RPC framework, used by Visual Studio and language-server tooling. `--compare` gives it and JSON-RPC.Net the same five requests on Kestrel TCP, one connection per core (32 here) and 256 requests in flight per connection. StreamJsonRpc requires `"jsonrpc":"2.0"`, so these requests include it and the JSON-RPC.Net row sits below its other TCP measurements. The rows retain each library's named formatter and framing; they compare complete server paths.

The same run hosts [gRPC for .NET](https://learn.microsoft.com/aspnet/core/grpc/) on Kestrel with the corresponding five calls from [calculator.proto](TestServer_Console/Protos/calculator.proto). Its Grpc.Net.Client uses one channel per core and 256 calls in flight per channel. Each row ran twice for 3 s; the table shows both results as a range.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/compare-streamjsonrpc-dark.svg">
  <img alt="JSON-RPC.Net vs StreamJsonRpc vs gRPC for .NET at 32 connections: low-to-high intervals on a log axis, grouped by library, with the in-process paths under a rule" src="benchmarks/charts/compare-streamjsonrpc.svg">
</picture>

<!-- benchmarks:compare -->
| Library and path | RPC/s |
| --- | ---: |
| JSON-RPC.Net over Kestrel TCP, raw documents | 16.4 M to 16.8 M |
| StreamJsonRpc over Kestrel TCP, newline framing, System.Text.Json formatter | 847 k to 879 k |
| StreamJsonRpc over Kestrel TCP, `Content-Length` framing, System.Text.Json formatter | 1.06 M to 1.15 M |
| StreamJsonRpc over Kestrel TCP, `Content-Length` framing, Json.NET formatter (its default) | 560 k to 583 k |
| gRPC for .NET, unary calls over HTTP/2 (Grpc.Net.Client, 32 channels × 256 in flight) | 158 k to 166 k |
| gRPC for .NET, one bidirectional stream per channel, 256 in flight, batched writes | 703 k to 733 k |

<!-- /benchmarks:compare -->

StreamJsonRpc 2.25.29 used its defaults apart from the formatter and framing named in each row. Its fastest row reached 1.06 M to 1.15 M RPC/s; JSON-RPC.Net answered the same five calls at about 15× that rate on the same connections. StreamJsonRpc also provides client proxies, cancellation, progress, marshaled objects and events. JSON-RPC.Net used its built-in serializer while the fastest StreamJsonRpc rows used System.Text.Json. This is a whole-server comparison; JSON-RPC.Net with its System.Text.Json serializer was measured separately and is not in the table.

gRPC for .NET 2.84.0 used default settings except Kestrel's `MaxStreamsPerConnection`, raised to 256 so the pipeline depth was not capped at 100. Protobuf has no `decimal`, so `Test2` used the units/nanos `DecimalValue` message recommended in the gRPC docs; nullable values used proto3 `optional`.

These gRPC rows include Grpc.Net.Client running on the same cores as the server. They measure what a .NET caller and service achieve end to end, while the JSON-RPC TCP rows use a lightweight raw client. In the sweep, one gRPC channel handled 136 k to 143 k unary calls per second and eight handled 338 k to 357 k; additional channels competed with the server for the same cores. The streaming row batched writes (`BufferHint` on every message except the last in a refill) and the server flushed when its input ran dry, matching the TCP handler's once-per-read-group flush.

<details>
<summary>In-process paths: a direct call, a Pipe pair and a typed proxy (different boundaries, not comparable with the rows above)</summary>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/inprocess-paths-dark.svg">
  <img alt="In-process paths: JSON-RPC.Net direct call, StreamJsonRpc Pipe pair and StreamJsonRpc typed proxy, as low-to-high intervals on a log axis" src="benchmarks/charts/inprocess-paths.svg">
</picture>

<!-- benchmarks:inprocess -->
| Path | RPC/s |
| --- | ---: |
| JSON-RPC.Net in-process, 1 thread (direct call, bytes in, bytes out) | 2.97 M to 3.01 M |
| StreamJsonRpc in-process, 1 client over a `Pipe` pair, newline framing, System.Text.Json formatter, 256 pipelined | 133 k to 154 k |
| StreamJsonRpc typed proxy, sequential `await` per call, in-process pipes | 53.7 k to 56.2 k (18 to 19 µs per round trip) |

<!-- /benchmarks:inprocess -->

StreamJsonRpc's server side has no "document in, document out" call, so its in-process row is a pair of `System.IO.Pipelines` pipes, the closest it has to a direct call; the proxy row is one call at a time, so it measures a round trip, not throughput. The direct-call row comes from `--compare` and sits a little below the Sync table's 1-thread figure.

</details>

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="benchmarks/charts/compare-connections-dark.svg">
  <img alt="Every library and transport by client connections, 1 to 32: three panels on a shared log axis, one per library, with a marker shape and dash per setting and whiskers spanning five runs" src="benchmarks/charts/compare-connections.svg">
</picture>

What happens between one connection and 32? `--sweep` repeats the networked library and transport paths at each power-of-two connection count (channels for gRPC). The chart uses five 2 s runs per point, a median marker and a low-to-high whisker. Sweep files also record system, process and dedicated TCP-client CPU shares, because a busy client can limit what reaches the server.

The sweep ran in the same job as the tables, with a 2 s warm-up and 2 s timed window per cell. The TCP result is near its comparison-table row: JSON-RPC.Net measured 17.3 M to 17.8 M at 32 connections, versus 16.4 M to 16.8 M in the comparison table. The gRPC sweep results are higher: unary 269 k to 290 k versus 158 k to 166 k; streaming 893 k to 913 k versus 703 k to 733 k. The cause is not pinned down, so the comparison table keeps its original figures.

The scaling pattern is the reason for this chart. JSON-RPC.Net over TCP rises from a 1.58 M median at one connection to 17.5 M at 32; batched HTTP goes from 744 k to 14.6 M. StreamJsonRpc gains 5 to 6× from one to 32 connections, with its two `Content-Length` paths leveling off after 16. gRPC unary peaks at eight channels, while its stream climbs to 902 k at 32.

The [benchmark explorer](https://astn.github.io/JSON-RPC.NET/benchmarks/charts/explorer.html) is the same data as an interactive page: toggle series, hover or tab to a point for the exact low, median, high and every run, switch the axis between log and linear, and download the data. It is one self-contained HTML file, [benchmarks/charts/explorer.html](benchmarks/charts/explorer.html), so it also works saved to disk.

### WebAssembly: in the browser

The browser changes the boundary again. The [WasmHost sample](samples/WasmHost/README.md) compares the same `add(1, 2)` through JSON-RPC and plain Blazor interop in Chrome, under the .NET 10 interpreter and AOT. Interpreted, plain `DotNet.invokeMethod` took about 64 µs including Blazor's JSON marshalling, a UTF-8 JSON-RPC document written directly into WebAssembly memory through `[JSExport]` took 53 µs, and a batch of 100 reached 27 k RPC/s. AOT changed those three results to 15 µs, 7 µs and 210 k RPC/s; a typed `[JSExport]` add took 0.3 µs either way. The [sample README](samples/WasmHost/README.md) has the table and chart.

### simdjson

One parser experiment did not help. simdjson was evaluated as a fourth parser and not adopted: through the .NET binding tested here, its parse alone costs more than the whole built-in envelope read, and walking the result is 5 to 6 times slower with 350 bytes or more of garbage per request. The harness and numbers are in [benchmarks/SimdJsonEval/RESULTS.md](benchmarks/SimdJsonEval/RESULTS.md).

### History

The current tables come from the 2026-09-28 cloud job. Earlier measurements used a desktop and different harness configurations, so they tell the development story rather than extend the current comparison.

<details>
<summary>Earlier desktop results and a corrected 1.x result</summary>

The charts, the explorer page and the figures in this file come from one data file, [benchmarks/charts/benchmarks.json](benchmarks/charts/benchmarks.json); how they are rendered and checked is under [Building](#charts). Until 2026-09-28 the published figures were measured on a desktop, an AMD Ryzen 7 7800X3D (8 cores / 16 threads), and the dated figures below come from it.

On 2026-09-25 the `ProcessAsync` path was found capped near 4 M RPC/s at every worker count. Every document took one lock on the shared scratch pool, which the single-threaded micro-benchmarks could not detect. A one-slot per-thread cache in front of the pool took the inline rows to 22.2 M to 32.1 M at 16 workers (one run per registration), against 31.7 M for the synchronous entry point in the same session. The `--scale` gate and the request-path allowlist exist to catch the next such limit before a release.

An earlier version of this README reported one desktop session on 2026-09-25. The last 1.x release on NuGet, 1.2.3, was driven by the same loop as the `t` entry ([benchmarks/Baseline](benchmarks/Baseline/Program.cs)). It reached 3.08 M at its best batch size of 1,200 and fell to 1.5 M at two million. In the same session, 2.0 peaked at 13.3 M through the same string API, and the byte entry points ran at 31.7 M and 32.1 M.

The 2026-09-23 performance pass (compiled invokers that read the tokens and write the pooled buffer through direct calls instead of virtual, delegate and interface calls; a tokenizer that keeps its scanner state in locals; a last-session cache; envelope keys matched by length; a flat method table) was measured A/B in one session: the same seven runs of `--sync 2 1` went from 3.2 M to 4.1 M (median 3.6 M) before to 4.0 M to 4.8 M (median 4.4 M) after, about 20 to 25 % more on one thread. The transport rows are bound by the loopback round trips rather than by the library and moved less.

Under the previous harness (one pass per batch, workstation GC) the two-million batch ran at about 525,000 RPC/s on 1.3 and 1,584,906 RPC/s on 2.0 on the same machine. The 1.x figure published earlier in this README was measured while the benchmark service was not bound, so every request took the "method not found" path; the benchmark now prints the responses so that cannot go unnoticed.

</details>

## Upgrading from 1.x

Most 1.x services run unchanged. [Upgrading from 1.x](docs/upgrading.md) lists the changes that break the build, the changes clients will see on the wire and the behaviour changes inside your server. Read the first list before you build and the second before you deploy next to existing clients. [CHANGELOG.md](CHANGELOG.md) is the record of every change per version.

## Versioning and support

The four 2.x packages are released together at one version; use matching versions. Public API and documented wire behavior follow [Semantic Versioning](https://semver.org/). There is no fixed release cadence. A preview (`2.0.0-preview.N`) is tested but may change API before the stable release it precedes; 1.x receives no further releases.

Obsolete members warn with a `JSONRPC0xxx` diagnostic and a link to the [replacement](docs/obsoletions.md). They remain warnings through 2.x and are removed in the next major version. [CHANGELOG.md](CHANGELOG.md) records changes. Report vulnerabilities privately through [SECURITY.md](SECURITY.md); use [GitHub issues](https://github.com/Astn/JSON-RPC.NET/issues) for questions and bugs.

CI tests `net8.0` and `net10.0` on Windows and Linux. Other runtimes can load the `netstandard` assets but are outside that test matrix. Trimming and Native AOT remain unsupported until the reflection binder is annotated and validated in CI, planned alongside a source generator for 2.8; a trimmed host today can lose bound methods.

## Building

Use the .NET 10 SDK pinned in `global.json` and the .NET 8 runtime for the `net8.0` tests:

```
dotnet build AustinHarris.JsonRpc.sln
dotnet test AustinHarris.JsonRpcTestN
```

The suite runs protocol cases across the built-in, Json.NET and System.Text.Json serializers, plus parser, dispatch, version-policy and Kestrel tests, on both targets. A Release build of a package project writes its NuGet package under `bin/Release/`. The WebAssembly sample builds without `wasm-tools`; add that workload for AOT.

### Charts

The tables and charts are generated from [benchmarks/charts/benchmarks.json](benchmarks/charts/benchmarks.json). `python benchmarks/charts/render.py --check` verifies that the published figures and generated assets agree. [The benchmark guide](benchmarks/hf/README.md#republishing) describes the publish, fetch and rendering steps.

## License

MIT. See [LICENSE](LICENSE).
