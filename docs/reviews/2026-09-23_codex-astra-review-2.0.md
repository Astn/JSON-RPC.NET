**Do not ship** — The shared parser permits unbounded work and invalid wire data, and reproduced failures in dispatch, hooks, and serializer reuse remain outside the passing suite.

# Independent review of the 2.0 rewrite

Reviewed branch: `finish-netstandard-upgrade`, initially `8301ff5`, against `master`.
Original scope: 14 commits, 58 changed files, 8,222 insertions and 834 deletions.
During review the checkout advanced through four additional benchmark commits to `3621c76`.
I also read that delta: README, console benchmark, and console GC settings; none of the findings' source files changed.
Final inspected scope: 18 commits, 58 changed files, 8,363 insertions and 834 deletions.
Review date: 2026-09-23; finding line numbers apply at both revisions.

I read the branch changes file by file, including the required core files, companion packages,
documentation, tests, project files, workflows, and benchmark changes.
I used tree-sitter symbol searches, usage tracing, and structural analysis, and queried Memory
collections `json-rpc.net_code` and `json-rpc.net_docs` for design context.
The findings below are grounded in the checked-out source and local execution, not inferred from those indexes.
Some protocol behaviors are deliberately inherited; they are identified as compatibility decisions.

Reproduction conventions: `ping()` returns `7`, `echo(string s)` returns `s`,
`accept(object o)` returns `7`, and `optional(int a = 9)` returns `a`.
These were registered in an isolated temporary session.
“All three” means built-in jsmn, Newtonsoft, and System.Text.Json with their default settings.
Temporary repro tests were removed after execution; their essential inputs and observations follow.

## Findings, ranked by severity

### 1. blocker — Unbounded nesting makes parsing quadratic and leaves recursive binding unprotected

Location: `Json-Rpc/Jsmn/JsmnTokenizer.cs:107`; related `Json-Rpc/Jsmn/JsmnMapper.cs:380`.
Verification: **verified by test** for accepted depths and timing; **verified by reading** for the unbounded recursion.
Claim: A small, deeply nested request can consume disproportionate CPU before method dispatch, and the built-in reader has no input recursion limit.

Repro: send `{"method":"ping","unused":` followed by N opening brackets, `0`,
N closing brackets, and `,"id":1}`.
Depths 1,000, 2,000, 4,000, and 8,000 all returned success; observed Debug times were approximately
2.6, 5.5, 23.3, and 98 milliseconds, respectively, for at most about 16 KB of input.
Each closing delimiter restarts from the last token and walks its parent chain; deeply nested input therefore takes quadratic work.
The shared envelope parser exposes every serializer to this behavior, including nesting in unused members.
An `accept(object)` call also accepted 1,024 nested arrays; `ReadDynamic` recursively descends them without a guard.
`MaxDepth = 64` in the mapper protects writing only, so sufficiently deep binding can exhaust the process stack.
I did not deliberately crash the test runner with a stack overflow.

Suggested fix: enforce a documented nesting and token budget during tokenization, before hooks or binding;
close containers through the current open-container chain without rescanning closed descendants;
and apply a consistent recursion limit to every recursive reader.
The transport byte limit alone does not bound this CPU cost.

### 2. major — The shared envelope parser accepts invalid JSON and echoes invalid UTF-8

Location: `Json-Rpc/Jsmn/JsmnTokenizer.cs:157`, `Json-Rpc/Jsmn/JsmnTokenizer.cs:227`, `Json-Rpc/Jsmn/JsmnTokenizer.cs:269`.
Verification: **verified by test**, all three serializers.
Claim: “Strict” parsing does not validate complete JSON grammar or UTF-8, and raw ID echo can turn accepted bad input into an invalid response.

Observed inputs and results:

| Input | Observed result |
| --- | --- |
| `{"method":"ping","id":1,}` | Dispatches and returns `7` |
| `{"method":"ping","id":1}{}` | Dispatches the first document |
| `{"method":"ping","ignored":truX,"id":1}` | Dispatches and returns `7` |
| `{"method":"ping","id":01}` | Emits the invalid number `"id":01` |
| `{"method":"ping","id":nxxx}` | Emits the invalid literal `"id":nxxx` |
| Quoted ID containing raw byte `0xff` | Copies `0xff` into the response; strict UTF-8 decoding throws |

Primitive scanning checks delimiters rather than literal/number grammar; separator handling lacks a grammar state;
the reader does not reject trailing roots; string scanning accepts non-ASCII bytes without UTF-8 validation.
`Utf8Json.ClassifyId` and `Handler.WriteIdRaw` then trust these tokens.
Suggested fix: validate separators, literals, number syntax, a single complete root, and UTF-8 before dispatch.
Keep explicitly supported lenient syntax separate from validation needed for safe output.

### 3. major — A failed System.Text.Json write poisons a later call using different options

Location: `AustinHarris.JsonRpc.SystemTextJson/SystemTextJsonRpcSerializer.cs:123`; related line `136`.
Verification: **verified by test**.
Claim: The cached writer retains pending bytes after an exception, then flushes them into an obsolete destination when its options change.

Repro on one thread: serializer A uses defaults; serializer B uses a new `JsonSerializerOptions` instance.
Call `A.Serialize(new Explodes())`, where `Good` returns `1` and the next property getter throws.
Catch that failure, then call `B.Serialize(7)`.
The second call throws `NullReferenceException` from `PooledByteBufferWriter.Advance`, reached through
`Utf8JsonWriter.Flush`, `Dispose`, and `RentWriter` at line 123.
The first `Serialize` has already disposed its pooled output; `ReturnWriter` only clears the in-use flag.

Suggested fix: discard/reset pending writer state and detach the old destination on exceptional exit,
before the owner can rewind or dispose that destination.
Do not evict a failed writer by flushing it into its previous output.
Cover failure followed by both same-options and different-options reuse, including nested writers.

### 4. major — Pre-process mutations no longer control the invoked request

Location: `Json-Rpc/Handler.cs:339`; related lines `327`, `351`, and `359`.
Verification: **verified by test** and comparison with the old dispatch path.
Claim: The boxed path exposes a mutable request to pre-processing but subsequently resolves and binds from the original reader.

Repro: install a pre-handler that sets `request.Method = "ping"` and `request.Params = null`, returning no error.
Send `{"method":"echo","params":["original"],"id":1}`.
The response is `"result":"original"`, proving the original `echo` call still ran, rather than the replacement returning `7`.
Likewise, a hook that sanitizes/replaces parameters can inspect changed data while dispatch consumes the original input.
The old implementation dispatched from the request object after the hook; the upgrade notes do not list this change.

Suggested fix: make the boxed path resolve and bind the post-hook request, with appropriate revalidation.
If mutation is intentionally being removed, make that an explicit API break rather than silently ignoring it;
recommended decision: preserve the existing hook behavior because existing sanitizers and routers depend on it.

### 5. major — Nested processing overwrites the outer context and consumes its error

Location: `Json-Rpc/Handler.cs:550`; related lines `119`, `299`, `318`, and `554`.
Verification: **verified by test**.
Claim: Thread-static context and exception state are single slots, so a same-thread nested call corrupts its caller's invocation state.

Repro: an outer method receives context `"outer"`, calls `RpcSetException(-32001, "outer failure")`,
then synchronously processes an inner `ping` with context `"inner"` and ID `2`.
After the nested call, `RpcContext()` is null; the inner response contains the outer `-32001` error.
The outer response succeeds instead of reporting that error.
`Scratch.Rent` protects byte buffers against this reentrancy, but it does not protect execution context.

Suggested fix: push an invocation frame containing both context and exception state, initialize the nested frame,
and restore the previous frame in `finally` on every path.
If asynchronous service invocation is added, explicitly define context flow across continuations as well.

### 6. major — Default internal-error responses disclose exception messages and stack traces

Location: `Json-Rpc/Serialization/ExceptionInfo.cs:23`; related `Json-Rpc/Handler.cs:545` and `Json-Rpc/Handler.cs:517`.
Verification: **verified by test**.
Claim: An ordinary service exception is sent to the client with its private message, source assembly, and full stack trace by default.

Repro: a method throws `InvalidOperationException("private database /server/secret")`.
The `-32603` response contains that exact message and the throwing method's stack frame;
the Debug run also exposes the local source path and line number through its symbols.
Exception normalization makes the shape portable; it does not redact the information.
Inner exception details are copied recursively as well.

Suggested fix: make internal-error wire data safe by default and send diagnostics to a server-side logging hook.
Needs decision: preserve legacy diagnostic disclosure vs require explicit opt-in;
recommended opt-in because the new HTTP/TCP hosting package makes this a network-facing default.
Keep explicitly authored `JsonRpcException` application data under a separately documented policy.

### 7. major — Installing a hook moves deserialization failures outside the error boundary

Location: `Json-Rpc/Handler.cs:327`; related lines `535` and `562`.
Verification: **verified by test**.
Claim: Materializing the request for hooks can throw out of `JsonRpcProcessor` instead of producing a JSON-RPC error.

Repro: with System.Text.Json, call `accept(object)` with a parameter containing 70 nested arrays.
Without hooks, the value-conversion exception becomes the library's current `-32603` response.
Add a no-op pre-handler and send the identical request: `System.Text.Json.JsonException` escapes the processor.
`reader.ParamsValue` runs before the protected invocation; error-hook construction repeats the same risky conversion.
In a transport this can terminate processing of the HTTP request or connection, including the rest of a batch.

Suggested fix: cover materialization, hook execution, binding, and invocation with an explicit error boundary.
When conversion itself failed, error reporting must not depend on successfully repeating that conversion.
Use a safe request representation or unavailable-params marker for that error path.

### 8. major — Lenient string IDs alias scratch storage used for method decoding

Location: `Json-Rpc/Jsmn/JsmnRequestReader.cs:229`; related `Json-Rpc/Handler.cs:249`.
Verification: **verified by test**, lenient jsmn and default Newtonsoft.
Claim: A normalized single-quoted ID is overwritten while resolving an escaped method name.

Repro input: `{method:'\u0065cho',params:['x'],id:'abc'}`.
Actual output: `{"jsonrpc":"2.0","result":"x","id":echo"}`.
`IdRaw` returns a span into `_scratch`; `HandleRequest` keeps it while `MethodUtf8` writes the decoded name
into the same array, then emits the stale span as the ID.
Escaped parameter-name decoding uses that array too.

Suggested fix: give normalized IDs stable storage distinct from transient name decoding,
or normalize/copy them at a point where no subsequent reader operation can overwrite them.
Add cases that combine escaped method/parameter names with short and long lenient IDs.

### 9. major — A write-only POCO member causes invalid JSON output

Location: `Json-Rpc/Jsmn/JsmnMapper.cs:565`.
Verification: **verified by test**.
Claim: The built-in POCO writer bases comma placement on member index instead of the number of emitted members.

Repro type: `class Shape { public int Hidden { set { } } public int Visible => 3; }`.
The built-in serializer emits `{,"Visible":3}`; Newtonsoft and System.Text.Json emit `{"Visible":3}`.
`BuildMembers` retains the write-only property, the writer skips it because its getter is null,
and the following readable member receives a leading comma.
An RPC returning this shape therefore produces an invalid result document without any exception to trigger rewind.

Suggested fix: track whether a readable member has actually been written, or precompute a separate readable-member list.
Cover skipped members before, between, and after readable fields/properties.

### 10. major — Non-finite floats violate both JSON syntax and serializer parity

Location: `Json-Rpc/Serialization/Utf8Json.cs:118`; related `AustinHarris.JsonRpc.SystemTextJson/JsonRpcConverters.cs:301`.
Verification: **verified by test** for NaN; **verified by reading** for the corresponding infinity branches.
Claim: Built-in and System.Text.Json output bare non-finite symbols while default Newtonsoft writes JSON strings.

Repro: serialize `double.NaN`.
Built-in and System.Text.Json produce `NaN`; Newtonsoft produces `"NaN"`.
The same code paths write bare `Infinity` and `-Infinity`.
Consequently an RPC returning a non-finite number produces JSON that strict clients cannot parse;
the comment claiming this matches default Json.NET behavior is incorrect.

Suggested fix: use the legacy default's quoted representation consistently, or explicitly reject non-finite values
and return a valid mapped error; recommended quoted values if byte compatibility is the release contract.
Do not feed non-JSON tokens to `WriteRawValue(..., skipInputValidation: true)`.

### 11. major — DI skips binding a JsonRpcService subclass to the configured session

Location: `AustinHarris.JsonRpc.AspNetCore/JsonRpcServiceCollectionExtensions.cs:95`.
Verification: **verified by test**.
Claim: The self-binding shortcut ignores `JsonRpcOptions.SessionId` and can leave a service exposed in the default session instead of the configured one.

Repro: `AddJsonRpc(o => o.SessionId = "tenant")`, then `AddJsonRpcService<AutoService>()`,
where `AutoService : JsonRpcService` uses the default base constructor.
After starting the hosted binder, calling its method in `tenant` returns `-32601`.
The constructor binds to the default session, but the binder skips it because registration-level `SessionId` is null.
The effective session computed on line 92 is never applied.

Suggested fix: base the shortcut on the actual effective binding, not just the presence of a registration override.
Define how constructor binding and DI binding cooperate, including subclasses using the explicit-session base constructor.
Recommended decision: provide one deliberate binding owner for DI services so configuring isolation cannot silently expose a second session.

### 12. major — Removed metadata services remain callable through the cached lookup

Location: `Json-Rpc/SMDService.cs:86`; related public `Services` property at line `32`.
Verification: **verified by test**.
Claim: Direct edits to the public service dictionary do not invalidate successful UTF-8 cache hits, contrary to the stated compatibility behavior.

Repro: bind `ping`, execute `handler.MetaData.Services.Remove("ping")`, then send a normal `ping` request.
It still returns `7`.
`Find(ReadOnlySpan<byte>)` returns a cached hit before checking dictionary count.
A same-count replacement also cannot be detected by that count check, even after a miss.
An application removing a method through the retained public dictionary has therefore not actually revoked dispatch access.

Suggested fix: make all supported mutations versioned and update the dispatch table atomically,
or validate cache hits against the public dictionary while this mutable API remains supported.
If the dictionary becomes read-only, document the breaking change and provide a supported mutation API.

### 13. major — Errors from valid notifications are sent back to the client

Location: `Json-Rpc/Handler.cs:274`; related lines `266`, `281`, `296`, and `303`.
Verification: **verified by test**, all three serializers.
Claim: Response suppression applies only to successful notifications.

Repro: `{"jsonrpc":"2.0","method":"missing"}` produces a `-32601` response with `"id":null`.
This is a valid notification whose method is unavailable, not an invalid request object.
The same structure writes responses for binding and invocation errors and adds those responses to batches.
Clients using notifications receive unsolicited messages; HTTP notification status also depends on success.
The protocol requires no response to notifications, including those in batches. [JSON-RPC specification](https://www.jsonrpc.org/specification#notification)

Suggested fix: after distinguishing a valid notification from an invalid request, suppress its response on every dispatch outcome.
Preserve server-side error hooks/logging without emitting a wire response.
Needs decision: compatibility vs conformance; recommend compliant defaults with an explicit legacy mode if required.

### 14. major — A batch with one response loses its array wrapper

Location: `Json-Rpc/JsonRpcProcessor.cs:211`.
Verification: **verified by test**, all three serializers.
Claim: Response shape is selected by response count rather than whether the input was a batch.

Repro: `[{"jsonrpc":"2.0","method":"ping","id":1}]` returns
`{"jsonrpc":"2.0","result":7,"id":1}` instead of a one-element array.
The same branch also unwraps a larger batch containing one call and successful notifications.
A client deserializing batch replies as arrays fails despite receiving a successful method result.
The protocol uses an array for a batch with responses. [JSON-RPC specification](https://www.jsonrpc.org/specification#batch)

Suggested fix: retain brackets whenever a batch produces at least one response; emit nothing when it produces none.
This is explicitly retained legacy behavior, also asserted by `AustinHarris.JsonRpcTestN/Test.cs:1496`.
Needs decision: legacy shape vs protocol shape; recommend fixing the default in the major release and documenting migration.

### 15. major — Escaped envelope member names are not recognized

Location: `Json-Rpc/Jsmn/JsmnRequestReader.cs:114`.
Verification: **verified by test** for method; **verified by reading** for the same ID lookup path.
Claim: Envelope member matching compares encoded token bytes without decoding JSON string escapes.

Repro: `{"m\u0065thod":"ping","id":1}` returns `-32600`, “Missing property 'method'”.
The decoded member name is `method`, so this is the same request as the normally spelled form.
An ID spelled `"\u0069d"` is likewise missed and the request is treated as having no ID.
Changing value serializers cannot fix this because all three share this reader.

Suggested fix: keep the direct ASCII comparison for unescaped names, but decode escaped keys before matching them.
Apply the same semantic lookup to every envelope field and test escaped names in both standalone and batch requests.

### 16. major — Async return types are serialized as task objects

Location: `Json-Rpc/Invocation/RpcMethod.cs:137`.
Verification: **verified by test** for `ValueTask<int>`; **verified by reading** for dispatch's lack of awaitable handling.
Claim: Registration accepts awaitable-returning methods but passes their awaitables directly to value serialization.

Repro: a registered method returns `new ValueTask<int>(7)`.
All three serializers return an object containing `IsCompleted`, `IsCompletedSuccessfully`,
`IsFaulted`, `IsCanceled`, and `Result:7`, rather than the intended result `7`.
The compiled path only distinguishes `void` from every other return type; the Task-returning processor API
does not make a service method asynchronous.

Suggested fix: either await and unwrap supported return types through an asynchronous dispatch pipeline,
or reject `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` at registration with an actionable error.
Needs decision: implement async methods now vs explicitly support synchronous methods only;
recommended rejection for this release if a correct async pipeline is out of scope.

### 17. major — The release workflow never publishes the new companion packages

Location: `.github/workflows/build_publish_master.yml:34`; related project matrix at line `14`.
Verification: **verified by reading**; no publishing was attempted.
Claim: The configured master release pushes only the core package even though the 2.0 installation instructions require separately published companions.

Failure scenario: merge/version the four packages as `2.0.0` and rely on the existing master workflow.
Its push glob is `Json-Rpc/bin/Release/*.nupkg`; Newtonsoft, System.Text.Json, and AspNetCore packages live elsewhere.
Their locally generated packages are never selected, so the documented companion install commands cannot obtain
this release through that workflow.
The PR workflow has the same core-only publishing selection.

Suggested fix: pack and validate all four release packages and explicitly publish each package from the approved release job.
Build the whole solution so companion netstandard assets are checked in CI too.
If publishing is deliberately manual, record that release procedure and ownership before opening the PR.

### 18. minor — Ordinary mutable structs cannot be deserialized by the built-in mapper

Location: `Json-Rpc/Jsmn/JsmnMapper.cs:685`.
Verification: **verified by test**.
Claim: The POCO plan rejects value types whose default constructor is implicit, even though `MakeCreator` supports them.

Repro: `struct Pair { public int X { get; set; } }`, then deserialize `{"X":7}`.
Built-in jsmn throws `JsonRpcBindException` saying the type has no parameterless constructor;
Newtonsoft and System.Text.Json return a value with `X == 7`.
Switching an existing struct parameter to the new default serializer therefore converts a working call into an error.

Suggested fix: allow `IsValueType` through the creation gate and use the existing value-type creator and boxed setters.
Cover mutable struct fields/properties, nullable structs, and structs nested inside containers.

### 19. minor — Unknown named parameters can silently become defaults

Location: `Json-Rpc/Handler.cs:423`.
Verification: **verified by test**, all three serializers.
Claim: Named binding checks supplied count and missing required parameters but never verifies that every supplied name matched.

Repro: `optional(int a = 9)` receives `{"method":"optional","params":{"typo":4},"id":1}`.
The response is `9`; the supplied argument is silently discarded.
This contradicts the documented `-32602` handling of extra parameters and conceals client spelling mistakes.
Existing extra-parameter tests cover excess counts, which do not exercise this case.

Suggested fix: reject unmatched supplied names independently of total parameter count,
then fill defaults only for genuinely absent optional parameters.
Define duplicate-name behavior alongside that validation.

### 20. minor — The null-context Process overload trap remains

Location: `Json-Rpc/JsonRpcProcessor.cs:97` and `Json-Rpc/JsonRpcProcessor.cs:121`.
Verification: **verified by test** using a compile-only repro.
Claim: `JsonRpcProcessor.Process(json, null)` is still ambiguous after the serializer-first overload fix.

Repro: compile `void Repro(string json) { JsonRpcProcessor.Process(json, null); }`.
The compiler reports CS0121 between the `(string, JsonRpcStateAsync, object, JsonRpcSerializer)`
and `(string, string, object, JsonRpcSerializer)` overloads.
Both admit two arguments, and both second-argument reference types are more specific than `object`.
This is a remaining public API usability problem, not evidence that the serializer-first change failed its narrower purpose.

Suggested fix: disambiguate both session overload families, using explicit session-oriented names or required arguments,
and document the migration; making only the string-session context mandatory is insufficient.
Add compile fixtures for default/session calls with null, string, and object contexts and explicit serializers.

## What I checked and found sound

- The architecture split is real: core package references contain no JSON library; value conversion is delegated,
  while the shared reader, binding, dispatch, and envelope formatting remain in core.
- The native processor accepts `ReadOnlySequence<byte>` and `IBufferWriter<byte>`.
  Both Kestrel entry points call it directly; string overloads transcode at their boundaries.
  Multi-segment input is copied into pooled scratch, and responses are staged before copying to the destination;
  “byte-first” is accurate, but it is not a claim of zero copying for every input.
- Serializer selection follows per-call, session, global, built-in precedence.
  `Config` stores its global override in a volatile reference; I found no missing visibility barrier there.
- The normal scratch-buffer copy precedes scratch reuse; reader release is in `finally`.
  `Rewind` and `RemoveAt` bounds/overlap handling are sound in themselves; the defects above concern their callers or retained writer state.
- Positional identity maps, `-1` optional-default entries, reordered named parameters, custom parameter names,
  `void` results, and the special trailing `ref JsonRpcException` have coherent compiled paths and passing coverage.
- Existing tests cover envelope order, ordinary numeric/null/string shapes, default values, and DateTime fraction trimming.
  I found the DateTime follow-up consistent with those intended wire conventions; finding 10 limits the general float-parity claim.
- Newtonsoft's reader disposal and decoder/encoder state handling, including the netstandard branches, were inspected.
  Existing long-string, Unicode, settings/converter, and successful reentrant-converter tests pass.
  System.Text.Json appends missing converters after user converters; its type-info cache stores an options/info pair,
  not an ever-growing dictionary keyed by every options instance.
- HTTP method rejection, normal JSON error bodies, successful-notification 204, ordinary DI construction,
  TCP concatenation/split-document handling, and context exposure pass the existing host tests.
  Reading found byte-limit checks for complete and incomplete HTTP bodies and complete/incomplete TCP frames.
  A separate approximately 2 MiB request returned over HTTP and began returning over TCP;
  I did not reproduce the suspected large-request backpressure deadlock.
- Package versions and declared frameworks align at 2.0.0: core and serializers target netstandard2.0,
  netstandard2.1, net8.0, net10.0; AspNetCore targets net8.0/net10.0.
  The full build compiled those assets; no unavailable netstandard API was found in the inspected conditional branches.

## Verification record and limits

The requested initial `dotnet build AustinHarris.JsonRpc.sln -c Debug` exited 1 without useful diagnostics
(it printed zero warnings/errors). The serial no-restore build below succeeded, including after repro removal.
Its four warnings were existing unreachable-code/unused-variable warnings in the test project.

```text
dotnet build AustinHarris.JsonRpc.sln -c Debug --no-restore -m:1 -v:minimal
  Succeeded: all solution target assets, 0 errors, 4 warnings.
dotnet test AustinHarris.JsonRpcTestN --no-build -f net8.0
  Passed: 544; failed: 0; skipped: 0.
dotnet test AustinHarris.JsonRpcTestN --no-build -f net10.0
  Passed: 544; failed: 0; skipped: 0.
dotnet build TestServer_Console -c Debug --no-restore -m:1 -v:minimal
  Succeeded at final inspected tip 3621c76: 0 errors, 0 warnings.
```

Fourteen temporary exploratory tests exercised the counterexamples and host probe on net10.0.
Their assertions/output established the observations above; “passed” for those repros meant the bad behavior was reproduced,
not that the implementation met the desired contract.
The overload repro separately produced the expected CS0121 compiler failure.
I changed no production source, project, package version, SDK pin, or existing test.
No performance benchmark was rerun to certify 273 ns or zero allocation, and no crash-inducing depth test was attempted.
The netstandard assets were compiled and read, not loaded into a separate older-runtime test host.

## Questions for the author

1. Which protocol deviations are contractual legacy behavior for 2.0?
   In addition to findings 13–14, tests observed `jsonrpc:"9.0"` being dispatched, numeric ID `1e3` rejected,
   and a valid primitive root `1` reported as parse error rather than invalid request.
   `JsmnRequestReader.cs:115` never validates `jsonrpc`; `Utf8Json.ClassifyId` restricts numeric syntax.
   Needs decision: explicit legacy mode vs strict 2.0 defaults; recommend strict defaults and a migration table.
   Version and ID requirements are defined by the [JSON-RPC specification](https://www.jsonrpc.org/specification#request_object).
2. What resource budget is intended beyond request bytes: depth, token count, batch entries, response size,
   and retained per-thread scratch capacity? The code has no single policy governing these.
   Framing also rescans an incomplete document from its beginning on each read (`JsonFramer.cs:18`).
   I did not run a sustained slow-fragment or thousands-of-large-results load test.
3. Is lenient Newtonsoft input promised over raw connections as well as HTTP?
   `JsonFramer.cs:55` tracks double-quoted strings only; the envelope reader also accepts single quotes.
   Needs decision: align supported framing syntax or explicitly limit raw-connection syntax; recommend alignment if leniency is a transport-independent promise.
4. Are service registries expected to support concurrent creation/destruction/rebinding during traffic,
   and must DI binding precede arbitrary application hosted services?
   Normal startup is covered; I did not establish lifecycle linearizability or every custom hosted-service ordering.
   Define the intended lifecycle before treating concurrent mutation as supported.
5. Which additional method signatures are supported: generic methods, general `ref`/`out`, and expanded `params` arrays?
   `RpcMethod.cs:107` constructs generic readers from parameter types, with special handling only for trailing `ref JsonRpcException`;
   the current surface needs an explicit supported-signature contract and registration diagnostics.
6. Are the public implementation helpers (`RpcMethod`, invoker delegates, `Utf8Json`, and pooled writer) intentional long-term API?
   Many public members lack XML summaries. Decide their compatibility commitment before publishing 2.0,
   and expand “Upgrading from 1.x” to cover the decisions and behavior changes resolved from this review.

The working tree initially contained an untracked `.claude/` directory; it was left untouched.
This review document is the only retained file created by the review. No commits, branch changes, pushes,
memgraph writes, or Linear writes were performed.
