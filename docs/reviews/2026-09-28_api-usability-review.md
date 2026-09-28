**Two changes before 2.0.0 final, shipped as 2.0.0-preview.3**: strong-name the four assemblies with a checked-in key, and hand the error handler the exception that was thrown. Everything else the review found is either additive and mapped to a release on the 2.x train, a documentation change, or out of scope.

# API surface and usability review by case study

Reviewed: `master` at `19728f7`, the 2.0.0-preview.2 merge, and the published packages `AustinHarris.JsonRpc`, `.AspNetCore`, `.SystemTextJson` and `.Newtonsoft` 2.0.0-preview.2.
Review date: 2026-09-28. Line numbers apply at `19728f7`.
Tracking: AUS-1014 under AUS-1001 (the 2.0.0 release).

## Why

Before 2.0.0 leaves preview, the public API and its usage patterns were checked against real programs rather than against the test suite. The question for each program was the same: how would it expose a JSON-RPC service with this library, what would it run into, and what does that say about the API. Five programs were chosen, one per host shape, and two reviewers built each of them independently against the published preview.2 packages. A third participant collected the prior art (StreamJsonRpc, vscode-jsonrpc, jsonrpsee), the MCP specification and the adoption figures, and every claim below cites either the repository or one of those sources.

The anchor case is a fork of the official MCP C# SDK (`modelcontextprotocol/csharp-sdk` at `c40ee04`, 2026-09-18) with its inbound request dispatch replaced by this library. MCP is the largest JSON-RPC 2.0 population in .NET today: the SDK's Core package, which holds the dispatch the fork replaces, is downloaded about 134 thousand times a day (34.5 million total), StreamJsonRpc about 36 thousand a day, and this library about 75 a day (321 thousand total, still headlining the 2021 release on nuget.org). Anything that keeps that SDK, or a server built the same way, from adopting the library is the finding that matters most.

## Cases

| Case | Shape | Program | Why it was chosen |
|---|---|---|---|
| A | Anchor: fork of the MCP C# SDK | `modelcontextprotocol/csharp-sdk` `c40ee04` | Replace `RequestHandlers` and the request branch of `McpSessionHandler.HandleMessageAsync`; keep the SDK's transports, outbound messages, notification handlers and pending-request table. Two independent forks, run against the SDK's own test suites. |
| B | Editor loop with main-thread affinity | `CoplayDev/unity-mcp` `8be7d96` (14.5 k stars) | A Unity editor bridge: loopback TCP, eight-byte length-prefixed frames, commands queued and drained on the main thread from `EditorApplication.update`. |
| C | Desktop UI application, UI thread owns the state | `files-community/Files` `0e3c17c` (45.7 k stars) | WinUI 3; a static observable collection of open tabs that throws off the UI thread; 124 call sites marshal through `DispatcherQueue`. |
| D | Headless daemon with an existing control surface | `OpenTabletDriver/OpenTabletDriver` `a126f7b` (4.1 k stars) | Already serves JSON-RPC through StreamJsonRpc over a named pipe with `Content-Length` framing, and pushes four events to its clients. The direct replace-StreamJsonRpc comparison. |
| E | Plugin inside a closed host | `icsharpcode/ILSpy` `ada7928` add-in (26.2 k stars) | MEF add-ins load into the host's default load context; the real `ICSharpCode.ILSpyX` `AssemblyList` belongs to the thread that created it. |

Cases B and D were built by one reviewer, C and E by the other, each as a scratch project against the published packages exposing three or four real operations of the program, over the transport the program would use, checked end to end with a socket, a pipe or curl. Rejected candidates and the reasons are recorded with the case research (Godot projects have no control surface; DevToys was the least active; ShareX's licence bars copying its types; a Paint.NET effect only runs inside the render callback with no document access).

### Case A: the MCP SDK fork

Both forks compile and pass the SDK's server-side suites on .NET 10 once two workarounds are applied, and both needed the same two workarounds.

**Signing.** The SDK strong-names its assemblies with a checked-in `Open.snk` (`src/Directory.Build.props:18-19`) and builds with warnings as errors (root `Directory.Build.props:4`). The preview.2 packages are unsigned (`PublicKeyToken=null` on every target), so a signed consumer gets CS8002 at compile time. One fork turned signing off (`-p:SignAssembly=false`); the other kept it on and suppressed CS8002, which compiles, and then fails on .NET Framework 4.7.2, the runtime the `netstandard2.0` build exists for:

| Suite, `net472` | Unmodified SDK | Fork with signing on |
|---|---|---|
| `ModelContextProtocol.Tests` | 2081 passed, 0 failed, 288 skipped | 1269 passed, 888 failed, 212 skipped |

863 of the 888 failures are `FileLoadException` 0x80131044, "A strongly-named assembly is required"; the rest are `TypeInitializationException` from the same load. Neither 1.2.3 nor preview.2 carries a key, and the assembly version already moves from 1.2.3.0 to 2.0.0.0 at this release, so 2.0.0 is the cheapest point to add one. The .NET guidance is that open-source libraries targeting .NET Standard should be strong-named and that the key must never change afterwards, because it is part of the assembly identity.

**The error handler sees the wrong exception.** `Handler.cs:918-923` (and `BindingFailure` at `:937-938`) unwraps `TargetInvocationException`, then, for any exception that has an `InnerException`, substitutes that inner exception for the one that was thrown: an inner `JsonRpcException` is promoted, and anything else is wrapped in a new `-32603`. The SDK throws `McpException(message, inner)` (`McpServerImpl.cs:2155-2170`); after the substitution the error handler sees the cause, not the authored error, and the client gets "An error occurred." That is the cost of five streamable-HTTP and SSE tests on .NET 10 in the fork that kept the SDK's error mapping:

| Suite, `net10.0` | Unmodified SDK | Fork |
|---|---|---|
| `ModelContextProtocol.Tests` | 2389 passed, 2 failed (Docker SSE, environment), 4 skipped | 2389 passed, 3 failed, 4 skipped |
| `ModelContextProtocol.AspNetCore.Tests` | 605 passed, 30 failed (conformance fixture start timeouts, environment) | 600 passed, 35 failed |

The one new failure in the first suite is an error `Data` object re-parsed as a `JsonElement`, wire-identical. The five new failures in the second are all the `McpException` message. The other fork sidestepped the problem by catching handler exceptions in the adapter and rethrowing them after processing, which is not a pattern a library should require. StreamJsonRpc strips only `TargetInvocationException` and `AggregateException` (`JsonRpc.cs:2092-2098`) and honours only a directly thrown `LocalRpcException` (`:1686`); the library's own async path already follows that rule (`Handler.Async.cs:245-253`).

**Whole params and `_meta`.** MCP 2026-07-28 requires `params._meta` on every request. Named-parameter binding is strict: `{"name":"x","_meta":{...}}` answers `-32602 Unknown named parameter '_meta'` (`Handler.cs:717-723,758`). Both forks strip `params` and pass the whole request through as the context object. The SDK's own handlers take the whole `JsonRpcRequest` and a `JsonTypeInfo`, so a parameter that receives the whole `params` value, plus an opt-in policy that ignores unknown members, removes the envelope rewrite. StreamJsonRpc has both as opt-ins, off by default (`UseSingleObjectParameterDeserialization`, `AllowFlexibleNamedArgumentMatching`).

**Batches.** MCP has forbidden batches since 2025-06-18. `JsonRpcLimits(maxBatchCount: 1)` still answers `[{...}]` with an array (a count limit, not a policy; 0 means unlimited). jsonrpsee's `BatchRequestConfig::Disabled` answers one error with a null id (`server.rs:1290-1298`); StreamJsonRpc and vscode-jsonrpc implement no batches at all.

**Context.** The SDK's per-request context (transport, principal, protocol version) travels with the request; a typed method parameter loses it. Both forks read `Handler.RpcContext()` in the synchronous prologue of the bound lambda, before the first await, which works with the default `RpcContextFlow.None`. The README does not show that pattern.

**Stateless HTTP.** The SDK's stateless streamable-HTTP handler builds one `McpServer` per POST (`StreamableHttpHandler.cs:503-569`), so the fork binds and destroys a registry session per request, the shape `README.md:482` calls wrong. A method table owned by an instance, which `ProcessAsync` accepts, is the fix; that is the 2.6 duplex engine's shape.

**Disclosure.** `Config.IncludeExceptionDetails` is process-wide. The error handler receives no context for bind errors (`RpcContext()` is null for `-32602`, set for a throwing method), so telemetry needs a side table keyed by id.

**Public read of a bound method.** One fork kept a shadow dictionary because it found no public read path for a bound delegate. There is one: `Handler.GetSessionHandler(id).MetaData.Services[name].Method` (`Handler.cs:62,266`; `SMDService.cs:34,367-370`). The README should say so; no API change.

**Native AOT.** The SDK declares `IsAotCompatible` on Core and AspNetCore (`ModelContextProtocol.Core.csproj:21`) and publishes an AOT test application as a gate. Against the fork that gate fails with 53 ILC analysis errors, all in `AustinHarris.JsonRpc` and `.SystemTextJson`: IL3050 in `RpcMethod.Build`, `BuildAsync`, `AsyncHelper` and `JsmnMapper.TypePlan`, IL2026 in `SystemTextJsonRpcSerializer`. The unmodified SDK passes. README `:824` already says trimming and AOT are unsupported; an AOT-compatible host cannot adopt the library before that work lands.

**Kept SDK-owned, by design.** Inbound notifications, outbound progress, `server/discover`, sampling and the pending-request table stayed the SDK's, since the library is a server until 2.3 and 2.6. Making SMD emit MCP's discovery format is domain-specific and not adopted. Bytes in and bytes out (`README.md:5`) means the fork serializes every response and re-parses it into the SDK's message objects; object-level responses are not adopted.

### Case B: Unity editor bridge

One editor-owned object bound with `BindService(session, instance)`; every operation queues its work and awaits the editor loop before touching state, mirroring the program's own `EditorApplication.update` queue. Simple method signatures need no ambient context. The bridge's transport is loopback TCP with eight-byte big-endian length frames, which `JsonFramer` (back-to-back JSON documents) does not handle; the scratch host wrote the framing by hand on both sides of `ProcessAsync`, and reaching for `JsonFramer` first was the wrong path. Console and scene changes are events the server-only surface cannot push, so callers poll. A later cancel message needs host-maintained id-to-token bookkeeping. SMD names methods and parameter types but cannot say which operations need the editor thread or have side effects; that is description metadata. The sample's `-32001` sits in the range JSON-RPC 2.0 reserves for implementation-defined server errors.

### Case C: Files

WinUI cannot be built in a scratch project, so the host loop is a stub: a single UI thread with a `SynchronizationContext` standing in for `DispatcherQueue.EnqueueOrInvokeAsync`, and state that throws off that thread as `MainPageViewModel.AppInstances` does. Operations `tabs/list`, `tabs/open`, `page/state`, `commands/execute`, `tabs/watch` and a `tabs/changed` notification over a newline-delimited named pipe; a client sent seven pipelined requests and got seven answers plus two notifications, including an authored `{"code":1001,"message":"Folder not found","data":{"Path":"Z:\\nope"}}`.

Findings: there is no dispatch `SynchronizationContext`, so the host marshals whole documents and runs parsing and result serialization on the UI thread too (StreamJsonRpc sets one per connection, `JsonRpc.cs:479-486`). The first sequential read-process-write loop deadlocked against the pipelining client: named-pipe buffers default to zero and Windows `FlushAsync` waits for the peer to read; the sample fixed it with 64 KiB buffers, the real fix is separate reader and writer loops. `JsonFramer.FindDocumentEnd` returns -1 both for "incomplete" and for "does not start with `{` or `[`" (`JsonFramer.cs:77-87`), so a garbage line stalls the connection while the buffer grows, and outside Kestrel nothing bounds it. Replies need a separator the raw handler does not write (`README.md:316`). The notification writer is hand-written under a lock shared with responses, and ordering is the host's problem. Per-request cancellation would reach nothing: `IRichCommand.ExecuteAsync(object?)` takes no token. `commands/execute` takes one of about 200 command codes; a client wants an enum schema. The README's error-handler example uses `-32000` (`README.md:337`), inside the reserved range.

### Case D: OpenTabletDriver

Four contract methods from `IDriverDaemon` (`SetTabletDebug`, `RequestDeviceString`, `ForceResynchronize`, `InstallPlugin`) on a long-lived daemon object, verified over loopback TCP; the named-pipe path compiles but could not be exercised in the reviewer's sandbox. The daemon's existing connection is StreamJsonRpc's default `Content-Length` header framing, which neither `JsonFramer` nor the raw-connection host speaks, so the header reader and writer were written by hand; the README's named-pipe snippet (`AspNetCore/README.md:128-145`) can be mistaken for a wire-compatible replacement, and its own next paragraph says it is not. The contract's four events, delivered today as server-to-client notifications, cannot be pushed; the existing connection cancels by a later message, which needs a router the host has to write. Authored `JsonRpcException` codes and data survived and ordinary exceptions stayed redacted. The sample's `-32010` sits in the reserved range.

### Case E: ILSpy add-in

The real object model: `AssemblyList` belongs to its creating thread and throws from `VerifyAccess` elsewhere (`AssemblyList.cs:42,85-86,487-490`), and a real `CSharpDecompiler`. Operations `assemblies/list`, `assemblies/open`, `types/search`, `types/decompile` over HTTP written by hand on `HttpListener`, because a plugin cannot bring the ASP.NET Core shared framework into a host that is not an ASP.NET Core application. All four answer with curl; a notification gets 204; `"limit":"many"` gets `-32602 {"reason":"conversion","parameter":"limit","index":2,"expectedType":"int32"}`.

Findings: run with a stand-in for another add-in setting `Config.IncludeExceptionDetails = true` for its own debugging, the plugin's private session goes from `data: null` to class name, message and a stack trace with local paths; disclosure needs a per-session setting where the session's own value wins. Add-ins share the default load context, the default session and the `Config` setters without a session id; the README needs a "you are a guest in someone else's process" paragraph. The threading grain varies per operation: reads use the thread-safe `GetAssemblies`, `assemblies/open` marshals only the mutation (off the owner thread `OpenAssembly` defers the insert to `BeginInvoke`, `AssemblyList.cs:323-346`, so a following list would miss it), and decompilation runs on the pool. `HttpListener` exposes no disconnect token, so the plugin arms its own deadline and the `[JsonRpcCancellation]` token flows into the decompiler; positional parameters skip the token correctly, but C# forces optional parameters after it, so the token sits mid-signature, which the README could say is fine. `typeName` uses `FullTypeName` syntax that a client can only guess without a description. The first wrong path was reaching for `AddJsonRpc` and `MapJsonRpc` inside a plugin.

## Decisions

Each item was proposed, answered by the other reviewer, checked against prior art and accepted by all three participants.

| # | Decision | Release | Alternative considered | Reason |
|---|---|---|---|---|
| 1 | Strong-name all four assemblies with one checked-in key, the same key forever after. | 2.0.0-preview.3 | Leave unsigned; sign after 2.0.0. | Signed consumers cannot compile against or, on .NET Framework, load an unsigned dependency (888 net472 failures). Adding a key later changes assembly identity, a binary break; the version already moves at 2.0.0, so this is the cheapest point. |
| 2 | The error handler receives the thrown exception. Unwrap only `TargetInvocationException` and a single-inner `AggregateException`; remove both substitutions at `Handler.cs:920-922` and the same in `BindingFailure` at `:937`; document the rule. | 2.0.0-preview.3 | Keep the promotion of an inner `JsonRpcException`. | An exception constructed with an inner cause must keep its own message and mapping; the SDK's `McpException(message, inner)` lost both. StreamJsonRpc does not promote a nested error exception either. The handler sees a different object afterwards, so the change lands before final. |
| 3 | A `[JsonRpcParams]` parameter that binds the whole `params` value, and an opt-in ignore-unknown-members policy; strict by default. | 2.2 (AUS-1000) | Fork-side envelope rewriting. | MCP requires `_meta` on every request; today it is `-32602`. Strict by default with opt-ins is StreamJsonRpc's shape. |
| 4 | A batch policy with an explicit `Reject` value that answers one error with a null id; `maxBatchCount` stays a count limit and is documented as one. | 2.2 (AUS-1000) | `maxBatchCount: 1`. | A count limit changes the array's content, not the wire shape; MCP forbids batches. jsonrpsee's `Disabled` is the model. |
| 5 | Per-session exception disclosure, `Config.SetIncludeExceptionDetails(sessionId, bool?)`, the session's own value winning; error-handler context for bind errors. | 2.5 (AUS-1000) | Process-wide switch only. | Case E showed one add-in's process-wide switch exposing another's stack traces. StreamJsonRpc scopes its exception strategy per instance. |
| 6 | Method descriptions and schemas, including the operation annotations from case B (thread affinity, side effects) as description metadata. Public read of a bound delegate: not adopted, the path exists. | 2.4 (AUS-999) | Annotations in 2.1. | 2.1 is the trimming-annotation release, not operation metadata. `MetaData.Services[name].Method` is already public; the README gets a line. |
| 7 | Instance-owned method table; a stream host with newline, length-prefix and `Content-Length` framers, separate read and write loops, a document limit, a reply separator, a framer result that distinguishes invalid from incomplete, and outbound notifications; a per-connection `SynchronizationContext`. Per-method scheduling: not adopted. | 2.6 (AUS-987) | Per-method invocation context. | Every scratch host wrote its framing, loops and notification writer by hand, and one deadlocked. No prior art for a per-method scheduler; StreamJsonRpc's is per connection. A method whose work must leave the UI thread dispatches inside its body, as case E does. |
| 8 | Pluggable cancel method name and id member, routing a later cancel message to the request's token by id. | 2.7 (AUS-997) | Host-side id-to-token tables. | Cases B and D each needed the same bookkeeping; MCP's `notifications/cancelled` carries `requestId`, not `$/cancelRequest`. StreamJsonRpc and vscode-jsonrpc both make the strategy pluggable. |
| 9 | AOT-compatible hosting: the 53 ILC errors are the annotation and generator work; the README states now that hosts declaring `IsAotCompatible` cannot adopt before 2.8. | 2.8 (AUS-998), 2.1 annotations prerequisite | Treat 2.1 annotations as sufficient. | Annotations alone cannot make reflection-built invokers AOT-safe; the compiled-invoker path needs the generator. |
| 10 | One documentation pull request before final: the adapter pattern (capture `RpcContext()` before the first await; request as context); named sessions first and the guest-process paragraph; an `HttpListener` sample and a pipe host without Kestrel, noting the pipe snippet is not wire-compatible with `Content-Length` framing; an editor and UI queue sample; the framer's ambiguous -1 and bounding the buffer; the README error example and the B and D samples use application codes outside -32000..-32099; unknown named members are rejected; a signed-host note; the token may sit mid-signature; the public read path from decision 6. | Before 2.0.0 | Spread across the train. | Every "first wrong path" the reviewers took is one of these. |
| 11 | Won't do: MCP-specific `server/discover` generation; unsolicited HTTP push; object-level responses; per-request cancellation for Files commands. | | | Scope lines `README.md:31` (server only until 2.6, and 2.6 is stream-only) and `README.md:5` (bytes are the processor contract); `IRichCommand.ExecuteAsync` takes no token. |

Consequence for the release plan (AUS-1001 Q1): one further preview, 2.0.0-preview.3, carrying decisions 1 and 2 together; the API and wire freeze (AUS-1013) follows it.

## Prior art consulted

| Topic | StreamJsonRpc `a911146` | vscode-jsonrpc 9.0.2 | jsonrpsee 0.26.0 |
|---|---|---|---|
| stdio hosting | `JsonRpc.Attach(stdout, stdin)` | `createMessageConnection(in, out)` | none (HTTP and WebSocket only) |
| Framing | `Content-Length` default, newline and binary length handlers | `Content-Length` only | n/a |
| Outbound notifications | `NotifyAsync`; .NET events become notifications | `sendNotification` | subscriptions only |
| Cancellation | `$/cancelRequest`, pluggable `ICancellationStrategy` | `$/cancelRequest`, pluggable strategy | future dropped on disconnect |
| Threading | `SynchronizationContext` per connection | n/a | connection id and extensions bag per call |
| Exception unwrapping | `TargetInvocationException`, `AggregateException` only | | |
| Unknown named members | `AllowFlexibleNamedArgumentMatching`, default off | | |
| Batches | none | none | `Disabled` / `Limit` / `Unlimited` |
| Native AOT | "partially NativeAOT safe" | | |

## Method and evidence

Each case was reported in eight fixed categories (registration and DI; threading and context; transport and framing; errors; cancellation; notifications; discovery and description; documentation), each friction item classified as fix before final, additive with a release, docs or sample, or won't do with the scope line it violates, and every claim cited to a file and line. The two reviewers then exchanged merged lists, answered each other item by item, and voted on the mediator's resolutions; the researcher supplied the prior-art evidence for the disputed items and voted too. All eleven decisions were accepted unanimously.

Fork evidence: two independent forks of the SDK at `c40ee04`, one built with `dotnet build tests/ModelContextProtocol.Tests/ModelContextProtocol.Tests.csproj -f net10.0 -p:SignAssembly=false` (server and stdio suites 618 passed, 0 failed; streamable HTTP 190 passed, 0 failed, 8 pre-existing skips), the other with signing kept on (table above, `dotnet test -f net10.0` and `-f net472`). The SDK's AOT gate was run with `dotnet publish` of `tests/ModelContextProtocol.AotCompatibility.TestApp`. The scratch projects for B to E build with `dotnet build` against the published packages; their checks are recorded in the reviewers' logs. The research index lists every source consulted, with URL and commit, in the review's working folder; the repository and the SDK clone were read-only throughout.
