# Changelog

The four packages (`AustinHarris.JsonRpc`, `AustinHarris.JsonRpc.Newtonsoft`, `AustinHarris.JsonRpc.SystemTextJson`,
`AustinHarris.JsonRpc.AspNetCore`) share one version number and are released together. This file is the record
of what changed in each version; [Upgrading from 1.x](docs/upgrading.md) explains how to move a 1.x server, and
the package pages on NuGet link here.

Versions follow [Semantic Versioning](https://semver.org/) for the public API and the documented wire
behaviour: a breaking change to either means a new major version.

## 2.0.0 (in preview: `2.0.0-preview.1`)

### Added

- `ServiceBinder.BindInterface` registers interface trees atomically, with contract naming, filtering, defaults and ownership-aware disposal (`RpcBinding`).
- `ServiceBinder.BindMethod` registers any delegate as a method without attributes or a service class.
- `JsonRpcProcessor.ProcessAsync` awaits `Task` and `ValueTask` methods with typed result writing, sequential batches and cooperative cancellation; `[JsonRpcCancellation]` injects the processor's token.
- `RpcContextFlow` on `[JsonRpcMethod]`: the ambient context does not flow across awaits by default (`None`); `Flow` opts a method in.
- Byte-first pipeline: `ReadOnlySequence<byte>` / `ReadOnlyMemory<byte>` / `ReadOnlySpan<byte>` in, `IBufferWriter<byte>` out; parameters bind straight from the request bytes through compiled invokers. The string overloads remain.
- Pluggable serializers (`AustinHarris.JsonRpc.Serialization.JsonRpcSerializer`): the built-in jsmn serializer is the default; Json.NET and System.Text.Json ship as the `.Newtonsoft` and `.SystemTextJson` packages.
- `AustinHarris.JsonRpc.AspNetCore`: `MapJsonRpc` endpoint (`PipeReader` in, `BodyWriter` out), a raw Kestrel `ConnectionHandler`, DI registration (`AddJsonRpcService<T>`, `AddJsonRpcServicesFromAssembly`), `EnableAsyncMethods` for asynchronous HTTP and ordered raw-connection processing.
- The request id is available inside a method (`Handler.RpcRequestId`, `JsonRpcContext.CurrentRequestId`, kind and raw bytes), read on demand at no cost to methods that do not ask.
- `Config.SetPreProcessHandler(sessionId, …)` and `Config.SetPostProcessHandler(sessionId, …)`, symmetric with the default-session setters. `Config.SetBeforeProcessHandler(sessionId, …)` remains as an obsolete alias.
- `ServiceBinder.BindService(sessionId, serviceType, resolve)` binds a type whose instance is resolved per call from the RPC context: the seam for container lifetimes, with no container dependency in the core.
- `AddJsonRpcService<T>(ServiceLifetime, sessionId)` and the matching `AddJsonRpcServicesFromAssembly` overload: scoped and transient services are resolved per call from `HttpContext.RequestServices`, or from a scope the raw connection handler opens per document and publishes as `IServiceProvidersFeature`; a batch shares one scope. `JsonRpcOptions.ServiceProviderSelector` locates the provider from a custom context. A lifetime that conflicts with the container's registration, a non-singleton `JsonRpcService` subclass, and a `ContextFactory` without a selector are refused at registration or startup.
- `protected JsonRpcService(bool autoBind)`: a subclass constructed with `base(false)` binds itself nowhere, for services that a host or an explicit `BindService` call binds.
- `SECURITY.md` (private vulnerability reporting) and this changelog.
- `TestServer_Console --scale` is the release gate for the `ProcessAsync` path. It measures the inline rows at 1, 2 and N workers in three paired runs, takes the medians and fails when N/1 is below the threshold. `--kestrel [seconds] async` runs the host with `EnableAsyncMethods = true`. The README adds `--async` rows for `ProcessAsync` at 1 and 16 workers. The 1.x string overloads' thread-pool benchmark is now the `t` menu entry and no longer the default.

### Changed

- `RpcMethod.FromMethod` is `RpcMethod.FromMethodInfo`; `RpcInterfaceMethod.Method` is `RpcInterfaceMethod.MethodInfo`. A `MethodInfo` is always spelled out; "method" means the JSON-RPC method.
- The session parameter is spelled `sessionId` on every overload (`BindService`, `Handler.RegisterInstance` and the `JsonRpcService` constructor used `sessionID`).
- `Handler.RegisterFuction` and `UnRegisterFunction` are obsolete; use `ServiceBinder.BindMethod` and `UnbindMethod` (which throw on a duplicate name instead of replacing it).
- The core no longer depends on Json.NET.
- `ProcessAsync` no longer serializes the process on one lock per document. Each thread now caches one async scratch (input copy, reader, staged output) in front of the shared pool, which handles only misses and overflow. At 16 workers, the inline rows went from about 4 M to 22.2 M to 32.1 M RPC/s across the registrations, and the row with a real suspension from 3.9 M to 8.96 M (one run per row, 2026-09-25). The library retains one scratch per thread that has run `ProcessAsync` plus 64 shared, with buffers at most 64 KiB each.
- The request path looks sessions up without creating them. A request for a session id that was never registered answers `-32601` for every call and leaves the registry untouched; sessions are created by binding and by the per-session `Config` setters. Registration adds the session before publishing the registry version, so a thread that misses its snapshot consults the master registry and cannot answer `-32601` for a session that exists.
- The AspNetCore host binds every registered service, `JsonRpcService` subclasses included, to its effective session (the registration's session, then `JsonRpcOptions.SessionId`, then the default). It no longer skips a subclass on the default session.
- The core package's description says "no JSON library dependency" instead of "no dependencies". The session registry uses the framework's `ConcurrentDictionary`; the `NonBlocking` package reference is gone, so the core has no dependencies on `net8.0` and `net10.0` (measured with `SessionRegistryBenchmarks`: unknown-id lookups and register/destroy cycles got faster, stable lookups and dispatch are unchanged).
- `SMD.Services` is an `SMDServiceCollection`; every mutation through it updates the dispatch table at once. `SMD.Types` is a process-wide registry.
- Registration refuses reserved method names (`rpc.`-prefixed and `$/cancelRequest`) on every path, including `BindMethod`, attribute binding and direct additions to `SMDServiceCollection`; `BindInterface` refused `rpc.` alone before.
- The `jsonrpc` member is checked (`Config.VersionPolicy`, default `Lenient`): a missing member is accepted, `"jsonrpc":"1.0"` or a non-string value is `-32600`.
- A parameter value the serializer cannot convert is `-32602` with structured data naming the parameter (it was `-32603`); `-32601` names the requested method in its data.
- Named parameters are checked against the method's parameter list: an unknown or repeated name is `-32602`.
- Batches: the empty-batch error is `-32600`; a batch made only of notifications produces nothing; a batch always answers with an array when it produces at least one response.
- Notifications never get a wire response, whatever their outcome.
- Dates and non-finite numbers are written the same way by every serializer (fraction only when non-zero, `Z`/offset/nothing by `Kind`; `NaN` and the infinities as quoted strings).

### Removed

- `InProcessClient` (obsolete since 1.x); call `JsonRpcProcessor.Process` directly.
- The `JsonSerializerSettings` overloads of `JsonRpcProcessor.Process*` (pass a serializer instead; the Newtonsoft package has settings-based helpers).
- Json.NET attributes on `JsonRequest`, `JsonResponse` and `JsonRpcException`.
- The 1.x projects that 2.0 did not build: `AustinHarris.JsonRpc.Client`, `AustinHarris.JsonRpc.AspNet`, the Windows Phone 7 client, `JsonRpcTest` and `TestClient`. They were .NET Framework 4.0 `packages.config` projects outside the solution, referencing packages with open advisories; their source is in the git history before 2.0.

### Fixed

- A trailing notification in a batch no longer leaves a dangling comma.
- `async void` methods are rejected at registration.
- A method registered with `RpcContextFlow.Flow` that suspends under a pre- or post-processing hook and completes on another thread no longer gives the completing thread the starting thread's frame. Previously, the two threads then shared one frame, so `RpcContext`, `RpcRequestId` and `RpcSetException` on either could read or clear the other's state during concurrent dispatch. The new cross-thread `ProcessAsync` test found this bug. The frame is now restored through the ambient value alone.

### Security

- With `Config.IncludeExceptionDetails` off (the default), an unhandled exception is answered as `-32603` with `data: null`: the exception's type name and message are no longer sent. Error handlers still receive the exception itself and can author what the client sees. The same applies to an exception thrown while writing a result. `ExceptionInfo.ForResponse` returns null when details are off.
- The session registry no longer grows from untrusted session ids on the request path (see Changed).

## 1.3.0 (2026-09-22)

- Targets `netstandard2.0`, `netstandard2.1`, `net8.0` and `net10.0` (drops the end-of-life `netcoreapp3.1`; `netstandard2.0` still covers it).
- Newtonsoft.Json 13.0.4 (fixes GHSA-5crp-9r3c-p9vr in 12.0.3).
- Lock-free session handler registry.
- Packaging moved fully to the SDK-style project files: MIT license expression, README in the package, repository metadata.
- Closes out the .NET Standard work from PR #90 / issue #89.

Earlier versions were published without a changelog; their history is in the repository's commit log.
