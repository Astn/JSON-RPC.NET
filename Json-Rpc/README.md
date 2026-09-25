# `AustinHarris.JsonRpc`

`AustinHarris.JsonRpc` is a JSON-RPC 2.0 server library for .NET.
Give it a UTF-8 request document and receive the response document: bytes in, bytes out.
It handles parsing, method dispatch, parameter binding, batches and error responses.
You supply the transport.

The core has no JSON library dependency.
Its built-in serializer works on its own; companion packages add Json.NET,
`System.Text.Json` or ASP.NET Core hosting.

Targets `netstandard2.0`, `netstandard2.1`, `net8.0` and `net10.0`.
This is a server library, with no client proxies or server-to-client calls.

## Install

2.0 is a prerelease. Include `--prerelease` when installing:

```sh
dotnet add package AustinHarris.JsonRpc --prerelease
```

Coming from 1.x? Read [What is new in 2.0](https://astn.github.io/JSON-RPC.NET/changelog.html) and [Upgrading from 1.x](https://astn.github.io/JSON-RPC.NET/upgrading.html) first.

## Getting started

### Declare a service

Save this as `server.cs`, a .NET 10 file-based app: one C# file with no project file.
The `#:sdk` and `#:package` directives select the web SDK and package.
`ServiceBinder.BindMethod` registers lambdas served by Kestrel at `/rpc`.

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:package AustinHarris.JsonRpc.AspNetCore@2.0.0-preview.1

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

Run `dotnet run server.cs`; Kestrel prints its listening URL.
Use `dotnet run server.cs -- --urls http://127.0.0.1:5077` to pin it for this request from another terminal:

```bash
curl -s -X POST http://127.0.0.1:5077/rpc -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","method":"add","params":[1,2],"id":1}'
```

```json
{"jsonrpc":"2.0","result":3.0,"id":1}
```

On .NET 8, use the same code in `Program.cs` in an ordinary ASP.NET Core project, install with `dotnet add package AustinHarris.JsonRpc.AspNetCore --prerelease`, and drop the two `#:` lines.

For a service class, create `CalculatorService.cs`.
Derive from `JsonRpcService` and mark exposed methods with `[JsonRpcMethod]`.
Constructing the service registers its methods in the default session.

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

Methods can be `private`. Parameters can be positional or named.
Optional parameter defaults are honoured; `[JsonRpcParam("name")]` overrides a parameter's JSON name.
Keep the service instance alive; it serves concurrent requests, so its state must be thread-safe.

Both examples use the default session (`Handler.DefaultSessionId()`).
Lambdas and classes can be mixed in one session when their method names differ; both examples register `add`, so keep one of them.
The next step drives `CalculatorService` in process, without a transport.

A method is a callable identified by the `method` member of a request; its implementation is a delegate, a `[JsonRpcMethod]` member of a class, or a member of a bound interface.

### Process requests

Put this code in `Program.cs` in a console project targeting `net8.0` or `net10.0`.
It calls the service through both string overloads and the byte entry point.
The string overloads transcode into the byte pipeline.

```csharp
using System;
using System.Buffers;
using System.Text;
using AustinHarris.JsonRpc;

var service = new CalculatorService();   // binds itself to the default session; keep a reference

// Strings, asynchronous invocation.
string response = await JsonRpcProcessor.ProcessAsync("""{"jsonrpc":"2.0","method":"add","params":[1,2],"id":1}""");
// {"jsonrpc":"2.0","result":3.0,"id":1}

// Strings, synchronous, on the calling thread. Named parameters.
string sync = JsonRpcProcessor.ProcessSync("""{"method":"multiply","params":{"l":6,"r":7},"id":2}""");
// {"jsonrpc":"2.0","result":42,"id":2}

// Bytes: the native path. The string overloads transcode into it.
var output = new ArrayBufferWriter<byte>();
JsonRpcProcessor.Process(Handler.DefaultSessionId(), """{"method":"add","params":[2,3],"id":3}"""u8, output);
Console.WriteLine(Encoding.UTF8.GetString(output.WrittenSpan));   // nothing is written for a notification
```

The string responses appear in the comments above. The byte call prints:

```json
{"jsonrpc":"2.0","result":5.0,"id":3}
```

A batch returns an array when it contains calls that need responses.
A notification has no `id` and produces no response.
A `"""..."""u8` literal is a `ReadOnlySpan<byte>` (C# 11 and later).
A bare `byte[]` is ambiguous between the memory and span overloads on C# 12 and 13; pass it as `AsSpan()` there.

Use `JsonRpcProcessor.ProcessAsync` for methods returning `Task` or `ValueTask`.
The synchronous entry points do not await those methods.

## Performance

These results compare 1.2.3 and 2.0 with the same five requests on the same machine in one session.
The 2.0 runs used the built-in serializer on an AMD Ryzen 7 7800X3D with .NET 10, Release and Server GC.

![JSON-RPC.Net 1.2.3 and 2.0 throughput through the string and byte entry points](https://raw.githubusercontent.com/Astn/JSON-RPC.NET/master/benchmarks/charts/headline-1x-vs-2.svg)

| Path | RPC/s | Against 1.2.3 |
| --- | ---: | ---: |
| 1.2.3, `Task<string> Process(string)`, thread pool, best batch size | 3.08 M | |
| 2.0, the same string API and the same loop | 13.3 M | 4.3× |
| 2.0, `Process(bytes)`, 16 dedicated threads | 31.7 M | 10.3× |
| 2.0, `ProcessAsync(bytes)`, 16 awaited workers | 32.1 M | 10.4× |

These measurements cover the library without a transport.
The asynchronous byte row uses methods that complete inline.
For Kestrel TCP with 256 requests in flight per connection and `EnableAsyncMethods = false`,
the measured range is 14.3 M to 16.5 M RPC/s on loopback, with clients and server on the same machine.

See the [benchmark tables](https://astn.github.io/JSON-RPC.NET/#benchmarks) for conditions
and the [benchmark explorer](https://astn.github.io/JSON-RPC.NET/benchmarks/charts/explorer.html) for the data.

## Companion packages

Install only the integrations your host needs. Use matching package versions.

- [`AustinHarris.JsonRpc.Newtonsoft`](https://astn.github.io/JSON-RPC.NET/newtonsoft.html): Json.NET converters, contract resolvers, `[JsonProperty]`, `JsonSerializerSettings` and lenient input.
- [`AustinHarris.JsonRpc.SystemTextJson`](https://astn.github.io/JSON-RPC.NET/systemtextjson.html): `System.Text.Json` conversion with `JsonSerializerOptions`, reading and writing UTF-8 values.
- [`AustinHarris.JsonRpc.AspNetCore`](https://astn.github.io/JSON-RPC.NET/aspnetcore.html): HTTP endpoints, raw Kestrel connections over TCP, Unix sockets or named pipes, and dependency injection.

## Upgrading from 1.x

Most service methods can stay as they are. Review these changes before switching:

- Json.NET is now a companion package. Use it for `JsonSerializerSettings` and settings-based
  compatibility helpers; `JsonRequest.Params` follows the selected serializer's object model.
- Review string overloads, especially calls with a positional null context.
  Name the `context` argument explicitly; serializer-taking overloads have changed.
- Check client-visible behaviour: notifications never return responses, responding batches stay arrays,
  and version values, named parameters and conversion failures are validated.
- Unhandled exceptions now return internal errors with exception details hidden by default.

[Upgrading from 1.x](https://astn.github.io/JSON-RPC.NET/upgrading.html) covers every API and wire change, and
[What is new in 2.0](https://astn.github.io/JSON-RPC.NET/changelog.html) is the full changelog.

## Documentation

- [Server guide](https://astn.github.io/JSON-RPC.NET/)
- [Hosting](https://astn.github.io/JSON-RPC.NET/#hosting)
- [Configuration](https://astn.github.io/JSON-RPC.NET/#configuration)
- [Errors and exception handling](https://astn.github.io/JSON-RPC.NET/#errors)
- [Asynchronous methods and cancellation](https://astn.github.io/JSON-RPC.NET/#asynchronous-methods-and-cancellation)
- [Security and host responsibilities](https://astn.github.io/JSON-RPC.NET/#security)
- [What is new](https://astn.github.io/JSON-RPC.NET/changelog.html)
- [Upgrading from 1.x](https://astn.github.io/JSON-RPC.NET/upgrading.html)
- [Source repository](https://github.com/Astn/JSON-RPC.NET)
- [MIT license](https://github.com/Astn/JSON-RPC.NET/blob/master/LICENSE)
