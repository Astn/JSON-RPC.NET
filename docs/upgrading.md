# Upgrading from 1.x

Most 1.x services run unchanged. Read the first list before you build, and the second before you deploy next to existing clients.

## Changes that break the build

- `InProcessClient` (obsolete since 1.x); call `JsonRpcProcessor.Process` directly.
- **Session parameter.** The session parameter is spelled `sessionId` everywhere; a named argument `sessionID:` must be updated.
- **MethodInfo names.** `RpcMethod.FromMethod` is `FromMethodInfo` and `RpcInterfaceMethod.Method` is `MethodInfo` (both were new in the 2.0 preview).
- **Serializer.** `JsonRpcProcessor.Process(…, JsonSerializerSettings)` is gone from the core. Use `Config.SetSerializer(new NewtonsoftJsonRpcSerializer(settings))` from the Newtonsoft package, or the helper overloads there that take the settings.
- **Overloads.** The default-session string overloads that take a serializer take it first: `Process(serializer, json, context)` and `ProcessSync(serializer, json, context)`. `ProcessSync(sessionId, json, context, serializer)` makes `context` required, so `ProcessSync(json, null)` still means the default session. `Process` and `ProcessAsync` do not: `Process(json, null)` no longer compiles (it is ambiguous with the `JsonRpcStateAsync` overload), and `ProcessAsync(json, null)` binds to the session overload with `json` as the session id and a null document, which throws `ArgumentNullException`. Write `Process(json)`, `Process(json, context: null)` or `ProcessAsync(json, context: null)`.
- **DTOs.** `JsonRequest`, `JsonResponse` and `JsonRpcException` are plain DTOs without Json.NET attributes. `JsonRequest.Params` is the active serializer's object model, so cast to `JObject`/`JArray` only when the Json.NET serializer is active.
- **SMD.** `SMD.Services` is an `SMDServiceCollection` (an `IDictionary<string, SMDService>`) instead of a `Dictionary<string, SMDService>`, and its setter is gone. Every mutation through it updates the dispatch table at once, so a removed method is unreachable immediately. `SMD.Types` is now `Dictionary<int, Dictionary<string, object>>` and a process-wide registry (it was reset whenever a session was created).
- **Reserved names.** Names beginning with `rpc.` and the name `$/cancelRequest` are refused at registration on every path (`BindInterface` refused `rpc.` alone before).

## Changes clients will see on the wire

- **Version member.** The `jsonrpc` member is checked (`Config.VersionPolicy`, default `Lenient`): a missing member is still accepted, but `"jsonrpc":"1.0"` or a non-string value is now `-32600`. Set `Ignore` for the 1.x behaviour.
- **Parse errors.** Requests nested deeper than 64 levels are `-32700` (configurable per serializer, see [Nesting depth](../README.md#nesting-depth)). Invalid UTF-8 and non-strict JSON (unless the serializer is lenient) are `-32700` as well.
- **Batches.** The empty-batch error code is the spec's `-32600` (it was `3200`). Batches made only of notifications produce an empty response instead of `[]` with a dangling comma. A batch always answers with a JSON array when it produces at least one response; a one-request batch is no longer unwrapped to a bare response object.
- **Notifications.** A notification (a request without an `id`) never gets a wire response, whatever its outcome: method not found, binding failure or an exception in the method produce nothing on the wire (the error handler still runs server-side). An invalid request object is not a notification and still gets `-32600` with `"id":null`.
- **Exceptions.** An unhandled exception is `-32603` with `data: null` by default; 1.x sent the exception's type, message and stack trace. `Config.IncludeExceptionDetails = true` sends the full description; an error handler can author something in between. See [Exception disclosure](../README.md#exception-disclosure).
- **Conversion errors.** A parameter value the serializer cannot convert (`"abc"` for an `int`, `"not-a-guid"` for a `Guid`) is `-32602` with `data = {"reason":"conversion","parameter":…,"index":…,"expectedType":…}`; it was `-32603` with the exception. An exception of the same type thrown inside the method is still `-32603`. A type the built-in serializer cannot handle at all stays `-32603` (now a `NotSupportedException`).
- **Method not found.** `-32601`'s `data` is `{"method":"<name>"}` instead of the fixed sentence, and a method-not-found error for a notification now reaches the error handler (the wire still gets nothing).
- **Named parameters.** They are checked against the method's parameter list: a supplied name that matches no parameter, or a name supplied twice, is `-32602` (it used to be ignored, so `optional(int a = 9)` called with `{"typo":4}` returned 9). Defaults fill only the names that are absent.
- **Dates and non-finite numbers.** `DateTime` and `DateTimeOffset` are written the way Json.NET writes them by every serializer (fraction only when non-zero, `Z`/offset/nothing by `Kind`); `NaN` and the infinities are written as the quoted strings `"NaN"`, `"Infinity"`, `"-Infinity"` and read back from them.

## Behaviour inside your server

- **Async methods.** Task-returning methods are supported again through `ProcessAsync`, together with `ValueTask` and `ValueTask<T>`. Synchronous `Process`/`ProcessSync` reject them at call time without invoking them. `async void` remains rejected at registration. See [Asynchronous methods and cancellation](../README.md#asynchronous-methods-and-cancellation).
- **Request id.** The invocation frame also carries the request id: `Handler.RpcRequestId()` / `JsonRpcContext.CurrentRequestId()`, `Handler.RpcRequestIdKind()` and `Handler.RpcRequestIdRaw()`, see [The request id](../README.md#the-request-id).
- **Binding.** `ServiceBinder.BindMethod(sessionId, name, delegate)` registers any delegate; it refuses a name that is already registered, unlike `Handler.RegisterFuction`, which keeps replacing silently.
- **Pre-process handlers.** A pre-process handler may replace `JsonRequest.Method`, `Params` or `Id`; the replaced request is what gets dispatched (as in 1.x). Assign a new `Params` value rather than editing the serializer's object model in place: a request the handler leaves untouched is dispatched straight from the request bytes.
- **Context.** `JsonRpcContext.Current()` / `Handler.RpcContext()` and `JsonRpcContext.SetException` are per invocation: a method that synchronously processes another request through `JsonRpcProcessor` gets its own context and exception state back afterwards.
- **Sessions.** A request for a session id that was never registered no longer creates the session; it answers `-32601`. Bind services or call `Handler.GetSessionHandler(sessionId)` before serving a session. `Config.SetBeforeProcessHandler(sessionId, …)` is now `Config.SetPreProcessHandler(sessionId, …)` (the old name still compiles, with an obsolete warning), and `Config.SetPostProcessHandler(sessionId, …)` exists.
- **`JsonRpcService`.** The AspNetCore host binds a subclass to the configured session even when that is the default session; a subclass can pass `base(false)` to skip binding itself.
- **`Handler.Handle(JsonRequest)`** still works; it round-trips the request through the serializer and the boxed path.

Every change is recorded per version in the [changelog](../CHANGELOG.md).
