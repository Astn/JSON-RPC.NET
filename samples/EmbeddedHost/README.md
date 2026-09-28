# EmbeddedHost

The core hosted in a process that is not an ASP.NET Core application: a plugin or add-in, a desktop or editor
process, a daemon. Three hosts over the same four methods, each about a page:

| File | Host | What it has to do itself |
| --- | --- | --- |
| `HttpListenerHost.cs` | HTTP through `System.Net.HttpListener`, which is in the base library | body limit, deadline (no client-abort token), 204 for a notification, status codes |
| `StreamHost.cs` | a named pipe (any `Stream`), newline-delimited documents both ways | framing with `JsonFramer`, a document limit, a read loop and a separate write loop, the reply separator, outbound notifications |
| `UiThread.cs` | a thread that owns the state, as an editor loop or UI thread does | a `SynchronizationContext` that runs every document, and every continuation inside a method, on that thread |

`Operations.cs` binds the methods to a session of its own rather than the default session, which any other
component in the process shares. `notes/get` answers an application error with a code outside the range JSON-RPC
2.0 reserves; `wait` takes its cancellation token between the JSON parameters.

```
dotnet run -- check          # all three hosts in process, driven by a client; exit code 0 when every check passes
dotnet run -- http           # POST http://127.0.0.1:5078/rpc/
dotnet run -- pipe           # named pipe jsonrpc-embedded
dotnet run -- ui             # the pipe host, methods run on the owning thread
```

```
curl -s -X POST http://127.0.0.1:5078/rpc/ -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","method":"add","params":[1,2],"id":1}'
```

The [embedded hosting reference](../../docs/reference.md#embedded-http-without-aspnet-core-a-pipe-without-kestrel-a-thread-that-owns-the-state) walks through the three files.
