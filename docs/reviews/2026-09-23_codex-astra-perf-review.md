# Independent performance review of JSON-RPC.NET

## 1. Summary

1. Reviewed `finish-netstandard-upgrade` at `ccce23c`, for Astn/JSON-RPC.NET#148; all source locations below refer to that commit.
2. Biggest opportunity: compile an internal built-in invoker that calls typed jsmn readers and writers directly; estimate 12–25 ns/request saved (P1).
3. Second: keep tokenizer scan position in locals and make bounds proofs visible to the JIT; estimate 8–18 ns/request saved (P2).
4. Third: preserve the concrete pooled writer through primitive result writing; estimate 6–12 ns/request saved (P3).
5. Planning estimate for the combined primary-path work, including smaller dispatch improvements: 30–55 ns off the documented 227 ns/request.
6. That means roughly 172–197 ns/request, 5.1–5.8 M RPC/s, 13–24% less time, or 15–32% more throughput on one thread.
7. These are unmeasured estimates with overlapping benefits, not additive promises; individual changes may produce no gain under optimized .NET 10 code generation.
8. Numeric benchmark shapes already allocate zero bytes after warm-up; sealing alone will not materially change the headline number.
9. Reaching 7–10 M on one thread requires saving about 84–127 ns; the evidence does not justify promising that, although the existing process already exceeds that target across threads.
10. Release tests pass 745/745 on both .NET 8 and .NET 10; the solution build has a WebAssembly task-host failure and the optional benchmark stops at denied WMI access.

## 2. Findings, ranked by expected gain

### Evidence, scope, and how to read the estimates

I read the requested README, serializer guide, prior review, all listed core/serializer/host files,
`Invocation/RpcMethod.cs`, the remaining core DTO/client files, and both benchmark implementations.
I inspected `git log --oneline -40` and the registration, invocation, mapping, framing, and buffer callers.
The initial tree had only an untracked `.claude/` directory.
During review another actor added an upstream-provenance comment to `JsmnTokenizer.cs`; I left it untouched.
That comment does not change executable code; this review uses the original HEAD line numbers.

Memory queries ran against `json-rpc.net_code` and `json-rpc.net_docs` for hot path, allocation,
tokenizer, Utf8KeyTable, PooledByteBufferWriter, RpcMethod invocation, and serializer constraints.
Results support these constraints: byte-first processing, rollback-capable output staging,
long-lived serializers, typed compiled invocation, preserved wire conventions, and bounded nesting.
Some nearest-neighbor results were irrelevant comments/configuration; I did not treat those as design evidence.
The checked-in serializer guide and implementation are the authority where index descriptions differ.
In particular, `Scratch.GetReader` caches the **last serializer**, not a dictionary of readers per serializer.
The historical simdjson evaluation was read for its decision, not used as a current tokenizer profile.
Its base predates parser hardening, so its 77 ns parse number cannot partition today's 227 ns total.

Tree-sitter `analyze_code`, `search_code`, and `find_usage` were used for the proposed types and removals.
The index returned false dead-file warnings and omitted references inside large files such as
`Handler.cs` and `JsmnMapper.cs`; for example, `BindMap` returned zero despite two visible callers.
I cross-checked with repository-wide `rg --no-ignore` searches excluding generated output, and read those callers.
Caller inventories below describe local source coverage; external NuGet consumers cannot be enumerated.
No absent local caller is sufficient evidence to delete a public API.

P1–P6 are ordered by expected contribution to the five-shape sync workload.
P7–P14 cover other workloads, ordered by practical opportunity; each explicitly states zero headline contribution.
Times are planning estimates on the README machine/runtime, not results from modified implementations.
No proposed implementation was installed or benchmarked in this review.
Byte sizes marked “estimate” assume ordinary x64 managed layouts, excluding application-owned results.
Each implementation needs an A/B run, per-shape allocations, and byte-for-byte response comparison.

### P1 — Compile a built-in invocation path with direct typed reads and writes

**Location:** `Json-Rpc/Invocation/RpcMethod.cs:129,160,168`; `Json-Rpc/Jsmn/JsmnRequestReader.cs:300`;
`Json-Rpc/Jsmn/JsmnMapper.cs:47,89`; `Json-Rpc/Handler.cs:358`.
**Now:** each compiled argument calls virtual `ReadParam<T>`, which constructs a cursor and invokes
`JsmnReader<T>.Read`; results call virtual `JsonRpcSerializer.Write<T>`, then `JsmnWriter<T>.Write`.
Nullable adapters add another cached delegate invocation; argument maps also retain a default branch.
**Cost:** the service call itself is already compiled, but surrounding indirect calls impede inlining.
Do not replace it with `MethodInfo.Invoke`, `DynamicInvoke`, or a boxed `object[]` invoker.
**Change:** retain public `StreamingInvoker` and add an internal specialization selected once per invocation:

```csharp
internal delegate void BuiltInInvoker(
    JsmnRequestReader reader, int[] map, PooledByteBufferWriter output);
// In JsmnRequestReader; used only after successful Select/BindMap:
internal JsmnCursor CursorAt(int i) => new JsmnCursor(_doc.Span, _tok.Tokens, _paramVals[i]);
// A helper the generated expression calls directly:
internal static int ReadInt32(JsmnRequestReader reader, int i)
{
    var cursor = reader.CursorAt(i);
    return JsmnMapper.ReadInt32(ref cursor);
}
// In Handler, inside the existing exception/context/rollback boundary:
if (reader is JsmnRequestReader jr && serializer is JsmnSerializer && method.InvokeBuiltIn != null)
    method.InvokeBuiltIn(jr, map, output);
else
    method.Invoke(reader, map, serializer, output);
```

Generate analogous direct expressions for the existing primitive readers, nullable tests, and writers.
Reuse the existing target `Expression.Call`, default expressions, and trailing-ref-exception handling.
Only generate a specialization where its semantics exactly match the existing built-in path.
Unsupported shapes, custom readers, Newtonsoft, STJ, and hook-driven boxed dispatch keep their current path.
**Gain:** estimate 12–25 ns/request on the mixed sync benchmark, 0 B/request; lower confidence without disassembly.
This estimate excludes P3's elimination of writer-interface calls; combined savings still overlap through inlining.
**Risk:** code size, registration cost, and interpreter/AOT behavior; do not generate every possible arity/type permutation eagerly.
**Affected:** `ServiceBinder.BindService` and both `SMD.AddService` registration routes construct `RpcMethod`;
`Handler.HandleRequest` consumes streaming invokers; `InvokeBoxed` must retain hook semantics.
**Verify:** compare generated-code disassembly and per-shape timings, including nullable null/non-null,
optional arguments, private/static/delegate methods, ref errors, custom serializers, and nested calls.
An open `Func<T1,T2,TResult>` delegate is not automatically faster: it adds a delegate call where `FromMethod` already emits a direct method call.

### P2 — Keep the tokenizer cursor local and expose safe loop bounds

**Location:** `Json-Rpc/Jsmn/JsmnTokenizer.cs:102,111,217,253,374`.
**Now:** the outer byte loop reads/writes `_pos` on the tokenizer object; helpers mutate the same field.
Token allocation and grammar state are interleaved with field loads, checks, and helper calls.
**Cost:** aliasable object fields make register retention and bounds-check elimination harder.
The existing `ParseString` already uses a local `i`; optimize the outer loop before adding SIMD or unsafe indexing.
**Change:** use a local position throughout the scanner, passing it by reference to helpers and publishing `Position` on exit:

```csharp
int pos = 0;
try
{
    for (; pos < js.Length; pos++)
    {
        byte c = js[pos];
        // Existing grammar switch. Helpers take ref pos instead of accessing _pos.
        // Preserve the old position reported on every success and error return.
    }
}
finally { _pos = pos; }
```

Extract cold token-array growth; initialize each new token completely before use.
Cache arrays only across regions where `AllocToken` cannot replace them; reload after growth.
For digit runs, load one byte and use `(uint)(b - '0') <= 9` instead of repeating indexed reads/comparisons.
Keep the length test adjacent to the access; retain strict grammar, surrogate, UTF-8, and depth checks.
**Gain:** estimate 8–18 ns/request, 0 B/request; a JIT already retaining fields well may erase this benefit.
**Risk:** medium: error positions, partial input, token growth, and malformed input are easy to regress.
**Affected:** `JsmnRequestReader.TryParse`, standalone `JsmnSerializer.Tokenize`, parser tests, and the simdjson comparison harness.
**Verify:** parser-hardening tests plus randomized differential token streams, lengths around growth thresholds,
and Tier-1 disassembly showing fewer field traffic/range-check instructions. No blanket `Unsafe.Add` conversion.

### P3 — Keep concrete writer calls inside the built-in primitive path

**Location:** `Json-Rpc/Serialization/Utf8Json.cs:23,47,70,84,97,110,193`;
`Json-Rpc/Jsmn/JsmnMapper.cs:89`; `Json-Rpc/Invocation/RpcMethod.cs:101`.
**Now:** `Handler` has a concrete `PooledByteBufferWriter`, but the invoker erases it to `IBufferWriter<byte>`.
Primitive formatting obtains a span and advances through that interface; nullable null writing does the same.
**Cost:** two interface calls per formatted value, with limited inlining across the cached delegate chain.
**Change:** add a small internal concrete writer helper used by P1, while preserving public interface-based helpers:

```csharp
internal static void WriteInt64Pooled(PooledByteBufferWriter output, long value)
{
    Span<byte> span = output.GetSpan(20);
    System.Buffers.Text.Utf8Formatter.TryFormat(value, span, out int written);
    output.Advance(written);
}
```

For float/decimal, share internal span-formatting code with the existing public writer so `.0`,
non-finite values, and netstandard2.0's formatting branch cannot diverge.
Use distinct helper names: adding another public `Utf8Json.WriteNull` overload would break the
name-only `GetMethod(nameof(Utf8Json.WriteNull))` lookup at `RpcMethod.cs:54` unless that lookup changes too.
Leave one interface boundary at `PooledByteBufferWriter.CopyTo(destination)` for arbitrary callers/PipeWriters.
**Gain:** estimate 6–12 ns/request, 0 B/request; verify whether .NET 10 PGO already devirtualizes either call.
**Risk:** low to medium; duplicate formatting logic would turn a local optimization into wire-format drift.
**Affected:** built-in streaming invokers, `JsmnWriter<T>`, and `Utf8Json`; STJ/Newtonsoft and custom output writers retain existing APIs.
**Verify:** same response bytes across all serializers and targets, all numeric edge cases, and a writer that returns exactly its requested span length.

### P4 — Cache the last session hit without hashing its string on every request

**Location:** `Json-Rpc/Handler.cs:53`; `Json-Rpc/JsonRpcProcessor.cs:169`.
**Now:** every document checks a session-registry version and hashes the session ID in a thread-local dictionary.
The benchmark and a raw connection repeatedly pass the same string instance, often a GUID-length default ID.
**Cost:** dictionary/hash work remains even when the previous lookup resolved exactly the same session.
**Change:** add thread-local last-ID/last-handler slots, invalidated alongside the existing local snapshot:

```csharp
// After the existing registry-version validation/rebuild:
if (ReferenceEquals(sessionId, _lastSessionId) && _lastSessionHandler != null)
    return _lastSessionHandler;
if (_sessionHandlersLocal.TryGetValue(sessionId, out var local))
{
    _lastSessionId = sessionId;
    _lastSessionHandler = local;
    return local;
}
// Existing miss/create path, with the same invalidation semantics.
```

Clear both slots whenever the snapshot version changes; never cache a handler permanently in the transport.
Keep the dictionary path for equal-but-distinct strings and interleaved tenants.
**Gain:** estimate 4–10 ns/document, 0 B/request; amortized once per batch, so much less per batch member.
**Risk:** medium: session destroy/recreate must invalidate the shortcut exactly as it invalidates the current cache.
**Affected:** processor entry points, Config setters, ServiceBinder, default/session helpers, and host session selection.
**Verify:** same-instance/equal-string/alternating-session benchmarks and destroy/rebind tests, including other threads.
Do not infer or change the existing registry's concurrency contract as part of this optimization.

### P5 — Dispatch envelope keys by length before comparing their bytes

**Location:** `Json-Rpc/Jsmn/JsmnRequestReader.cs:124,138`; `Json-Rpc/Serialization/Utf8Json.cs:476`.
**Now:** each envelope name tries method, params, id, then jsonrpc with a general ASCII-case-insensitive loop.
**Cost:** repeated helper/length checks and byte-at-a-time comparison on a fixed, tiny vocabulary.
**Change:** decode escaped keys exactly as today, then select the possible name by length/first byte:

```csharp
switch (name.Length)
{
    case 2:
        if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyId)) _idTok = val;
        break;
    case 6:
        if ((name[0] | 0x20) == 'm' && Utf8Json.EqualsIgnoreAsciiCase(name, KeyMethod)) _methodTok = val;
        else if ((name[0] | 0x20) == 'p' && Utf8Json.EqualsIgnoreAsciiCase(name, KeyParams)) _paramsTok = val;
        break;
    case 7:
        if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyJsonRpc)) _versionTok = val;
        break;
}
```

An exact `SequenceEqual` fast path is another candidate, but measure it: an extra comparison can lose on tiny keys.
**Gain:** estimate 3–8 ns/request, 0 B/request; speculative until the generated code is compared.
**Risk:** low if full comparison remains; checking only a prefix or folding punctuation would be incorrect.
**Affected:** every serializer using the default reader; `Select` is called by processor dispatch,
`Handler.Handle`, `InvokeModified`, and the reader benchmark.
**Verify:** arbitrary member order, case variants, escaped keys, unknown same-length names, repeated members,
and version-present requests. Do not stop scanning after three fields: later members are currently significant.

### P6 — Flatten dispatch entries only if the lookup profile warrants it

**Location:** `Json-Rpc/Serialization/Utf8KeyTable.cs:13,27,83,98`.
**Now:** each bucket points to separately allocated linked `Entry` objects; FNV-1a hashes every lookup.
Every mutation clones entries, and `Insert` repeatedly calls `CountEntries` while rebuilding.
**Cost:** pointer chasing on collisions; repeated table walks and many objects at registration/rebinding.
**Change:** use one immutable published snapshot with bucket indices and a contiguous entry array:

```csharp
private readonly struct Entry
{
    internal readonly byte[] Key;
    internal readonly int Hash, Next; // -1 terminates a chain
    internal readonly TValue Value;
    internal Entry(byte[] key, int hash, int next, TValue value)
        => (Key, Hash, Next, Value) = (key, hash, next, value);
}
// Snapshot owns readonly int[] buckets and Entry[] entries; publish one snapshot reference.
// A builder carries count explicitly instead of rescanning all buckets for each insertion.
```

Keep `SequenceEqual` for exact matching and preserve lock-free reads/copy-on-write replacement.
Do not replace it with hand-written 8-byte comparisons without evidence; runtime span equality is already optimized.
**Gain:** estimate 0–5 ns/request for the existing small table; larger savings are registration time and memory.
The benchmark service registers 20 methods, not just its five measured methods; preserve that table when comparing.
**Risk:** medium; array bounds, publication consistency, mutation visibility, and rebuild complexity.
**Affected:** only `SMDServiceCollection` instantiates `Utf8KeyTable<SMDService>`; its Add/indexer/Remove/Clear paths publish changes.
**Verify:** 1/20/100/1,000 methods, colliding buckets, long and escaped names, same-count replacement, and concurrent read/mutation scenarios.
If P6 does not improve steady-state lookup, take just the explicit-count rebuild simplification.

### P7 — Reuse nested scratch instances and bound serializer-switch churn

**Location:** `Json-Rpc/JsonRpcProcessor.cs:235,249,257,272`;
`Json-Rpc/Jsmn/JsmnSerializer.cs:96`; `Json-Rpc/Jsmn/JsmnTokenizer.cs:66,86`.
**Now:** a reentrant processor call creates a fresh Scratch, two 4 KiB rentals, a reader/tokenizer,
and index arrays; `Return` only resets `_inUse`, so the temporary rentals are not returned or reused.
A serializer identity change also replaces the sole cached reader, discarding its retained small token rental.
**Cost:** repeated nested calls can consume roughly 10 KiB of fresh backing storage per call after pool inventory drains.
Switching two long-lived serializers can allocate a new reader/tokenizer/index-array set on each switch.
**Change:** keep a small per-thread stack of reusable Scratch instances, and a bounded reader cache per slot:

```csharp
[ThreadStatic] private static List<Scratch> _slots;
[ThreadStatic] private static int _activeDepth;
// Rent: take/create slot[_activeDepth], then increment depth.
// Return in finally: release document references, decrement depth, retain only a bounded number of slots.
// Dispose overflow slots: return input/output/token rentals through explicit ownership-aware cleanup.
// Cache two recent reader-owner pairs; use a bounded policy, never an unbounded serializer dictionary.
```

An alternative is to dispose every temporary Scratch; that saves lost rentals but still rents and allocates objects per nested call.
**Gain:** **0 ns on the non-reentrant sync benchmark**; potentially several microseconds and about 10 KiB per repeated nested request, estimate.
**Risk:** medium: nested calls must never share a live reader/output; foreign custom readers have only the public Release contract.
Keep disposal distinct from reusable Release, and do not invalidate spans before the outer invocation finishes.
**Affected:** all processor overloads, serializer selection, synchronous nested methods, standalone built-in reads, and converter reentrancy.
**Verify:** repeated depth-2/depth-4 calls, alternating serializers, exceptions at each depth, context restoration,
and allocations/retained heap after a large request followed by small requests.

### P8 — Remove reflection invocation and per-entry arrays from dictionary mapping

**Location:** `Json-Rpc/Jsmn/JsmnMapper.cs:438,636,650,700`.
**Now:** every dictionary entry invokes `MethodInfo.Invoke(d, new[] { k, v })`.
The generic dictionary fallback writer constructs reflection metadata and reads Key/Value reflectively while enumerating.
**Cost:** one two-reference array per inserted entry (estimate 40 B on x64), plus existing value boxing and reflection overhead.
**Change:** compile the dictionary Add call once, as the list path already does:

```csharp
var target = Expression.Parameter(typeof(object), "dictionary");
var key = Expression.Parameter(typeof(object), "key");
var value = Expression.Parameter(typeof(object), "value");
plan.DictionaryAdd = Expression.Lambda<Action<object, object, object>>(
    Expression.Call(Expression.Convert(target, addMethod.DeclaringType), addMethod,
        Expression.Convert(key, plan.KeyType), Expression.Convert(value, plan.ElementType)),
    target, key, value).Compile();
```

Use a once-closed generic dictionary enumerator helper for the fallback, instead of repeated `MakeGenericType`/`GetProperty`/`GetValue`.
That helper may still allocate an iterator and box value-type keys/values; claim only the reflection eliminated.
Ordinary `Dictionary<K,V>` writing takes the earlier non-generic `IDictionary` branch, so it does not benefit from changing only `EnumerateDictionary`.
**Gain:** **0 ns on the scalar sync benchmark**; 40 B per inserted entry removed, and estimate tens to hundreds of ns per entry.
**Risk:** low to medium; preserve duplicate-key behavior and exception-to-wire mapping, which may depend on reflection exception wrappers.
**Affected:** `ReadObject` → `ReadDictionary` → `TypePlan.DictionaryAdd`; dictionary result fallback → `TypePlan.Enumerate`.
**Verify:** dictionaries with reference/value keys and values, interface/read-only declarations, duplicate keys,
custom Add implementations throwing, and byte-identical error data with details enabled/disabled.

### P9 — Return identity maps for ordered named parameters

**Location:** `Json-Rpc/Handler.cs:547,574,631`; `Json-Rpc/Invocation/RpcMethod.cs:171`.
**Now:** positional exact matches return a cached identity array; all named calls rent a map and scan names quadratically.
Missing positional defaults also rent/fill an array even though the map depends only on supplied count.
**Cost:** pool bookkeeping and repeated name comparisons; escaped parameter names can be decoded repeatedly.
**Change:** recognize the common ordered-names case before renting:

```csharp
if (given == expected)
{
    int p = 0;
    for (; p < expected; p++)
        if (!reader.ParamNameUtf8(p).SequenceEqual(parameters[p].NameUtf8)) break;
    if (p == expected) return method.IdentityMap;
}
// Existing fully validated reordered/unknown/duplicate-name path remains.
```

Do this only for methods whose registered JSON parameter names are unique; establish that flag at registration.
For high arities, build a per-method UTF-8 name-to-index table and visit each supplied name once.
For defaults, optionally cache maps by valid supplied count; `ReturnMap` must distinguish borrowed maps from pool rentals.
**Gain:** **0 ns on the five positional benchmark requests**; estimate 10–35 ns on small ordered named requests, 0 steady-state B saved.
**Risk:** medium: unknown/repeated names and missing required parameters must keep their current errors.
**Affected:** both `HandleRequest` and `InvokeBoxed`; `ReturnMap` and compiled map consumers must agree on ownership.
**Verify:** named-order permutations, custom names, duplicates, escaped names, defaults, and same-thread reentrancy.
Do not stackalloc a map and then call `.ToArray()` to satisfy `StreamingInvoker`; that introduces a real allocation.

### P10 — Remove or replace STJ's globally shared one-entry type-info cache

**Location:** `AustinHarris.JsonRpc.SystemTextJson/SystemTextJsonRpcSerializer.cs:66,85,259`.
**Now:** `TypeInfo<T>._last` is one process-wide options/info pair; a miss creates a new Entry.
Two serializers with different options repeatedly replace it, including when different threads each use one stable serializer.
**Cost:** estimate 32 B per missed Entry, redundant lookup, and cross-thread writes to a shared cache line.
**Change:** first compare using STJ's own options cache directly:

```csharp
public override T Read<T>(ReadOnlySpan<byte> json)
    => JsonSerializer.Deserialize<T>(json, _options);
// In Write<T>, retain RentWriter/Flush/ReturnWriter and replace only the Serialize call:
JsonSerializer.Serialize(writer, value, _options);
```

This deletes private `TypeInfo<T>`/Entry. A bounded per-thread two-entry cache is the alternative if homogeneous traffic measurably regresses.
The separate one-writer-per-thread cache also recreates writers when options alternate; evaluate a two-slot writer cache only after measuring that workload.
**Gain:** **0 ns with built-in jsmn**; 32 B per avoided type-info miss, potentially multiple misses per RPC under STJ option contention.
**Risk:** low for delegation to STJ's own cache; homogeneous STJ traffic could lose a few ns.
**Affected:** only generic STJ Read/Write call sites; non-generic reads/writes already use `_options` directly.
**Verify:** one serializer, two options alternating on one thread, and two independent option sets on two threads.
Retain failed-write detachment and reentrant-writer behavior; the previous review's writer fix must remain intact.

### P11 — Reduce oversized string reservations and avoid small escape rentals

**Location:** `Json-Rpc/Serialization/Utf8Json.cs:199,426`; `Json-Rpc/Jsmn/JsmnRequestReader.cs:263`.
**Now:** writing N UTF-16 characters requests `6*N+2` contiguous bytes even for plain ASCII.
Decoding every escaped string rents a byte array; normalizing a single-quoted ID creates a writer object and a decoded string.
**Cost:** a 1 MiB ASCII result requests about 6 MiB capacity, with pool rounding and long-lived scratch retention.
Small escaped values pay Rent/Return even when scratch would fit on the stack.
**Change:** keep the tiny-string loop, but add bounded chunk writing for long strings, preserving surrogate pairs across chunks.
For small escaped decoding on modern targets, use:

```csharp
if (contents.Length <= 256)
{
    Span<byte> bytes = stackalloc byte[256];
    int written = Unescape(contents, bytes);
    return Encoding.UTF8.GetString(bytes.Slice(0, written));
}
// Existing pooled fallback; use the compatible decoding path on netstandard2.0.
```

Normalize lenient IDs directly into reusable dedicated ID storage, preserving its independent lifetime.
Do not reuse `_scratch` for IDs: method/name decoding must not overwrite the response's retained ID span.
**Gain:** headline credit **0 ns**; “Foo” has no escape and already fits the small writer buffer.
For long ASCII output, up to roughly 5*N bytes of unnecessary requested capacity avoided; the CLR result string remains.
For lenient IDs, eliminate a writer object and transient decoded string after capacity warm-up; estimate 32 B plus string size.
**Risk:** medium: UTF-16 surrogates, escapes, exact lowercase hex spellings, and output-span invalidation on growth.
**Affected:** all built-in string results, error text, precomputed property names, ID normalization, and DecodeString callers.
**Verify:** boundary-length Unicode/escape cases, exact-sized writers, long ASCII then tiny requests, and escaped method/parameter names with lenient IDs.

### P12 — Carry framing state across incomplete pipe reads

**Location:** `Json-Rpc/Serialization/JsonFramer.cs:18`; `AustinHarris.JsonRpc.AspNetCore/JsonRpcConnectionHandler.cs:31`.
**Now:** every incomplete read restarts depth/string/escape scanning at the retained document's beginning.
**Cost:** for an N-byte document arriving in k equal fragments, about N*(k+1)/2 bytes are inspected instead of N.
This develops the prior review's open performance question; it is not a repeated parser-correctness finding.
**Change:** add an internal incremental framing state for the connection while keeping the public stateless helper:

```csharp
internal struct FrameScanState
{
    internal long Scanned, StartOffset;
    internal int Depth;
    internal bool Started, InString, Escaped;
}
// Scan only buffer.Slice(state.Scanned), recording bytes scanned and lexical state.
// On a full document: return its slice, advance buffer, reset state for the remainder.
// On an incomplete document: retain its bytes; carry offsets/state, not borrowed spans, across await.
```

Reset offsets relative to the new retained buffer after `AdvanceTo`; test empty/multiple segments explicitly.
Add a single-segment fast path using a local span/index if profiling shows sequence navigation is significant.
**Gain:** **0 ns in-process**; approximately (k+1)/2-fold fewer scanned bytes for equal-sized fragments,
but likely only 0–5% TCP throughput on the current small pipelined workload, estimate.
**Risk:** medium/high: segment boundaries, escaped quotes split across reads, completion/cancellation, and whitespace accounting.
**Affected:** raw `JsonRpcConnectionHandler` only; HTTP waits for the complete body and does not call JsonFramer.
The benchmark's `ResponseCounter` is already incremental, but validates a different contract and is not a drop-in server framer.
**Verify:** every split point in a request, byte-at-a-time 64 KiB input, several documents per read, quoted brackets,
and bounded incomplete-frame handling. Preserve strict raw-connection framing and MaxRequestBytes enforcement.

### P13 — Avoid the HTTP counting wrapper when the BodyWriter exposes its count

**Location:** `AustinHarris.JsonRpc.AspNetCore/JsonRpcEndpoint.cs:58,83`.
**Now:** every POST allocates `CountingBufferWriter` solely to distinguish zero output from a response.
**Cost:** estimate 32 B/POST and a forwarding interface layer; at batch size 100 this is only 0.32 B/RPC.
**Change:** use capability-checked unflushed-byte accounting, with the current wrapper as fallback:

```csharp
var body = http.Response.BodyWriter;
long written;
if (body.CanGetUnflushedBytes)
{
    long before = body.UnflushedBytes;
    JsonRpcProcessor.Process(session, in buffer, body, context, options.Serializer);
    written = body.UnflushedBytes - before;
}
else
{
    var counting = new CountingBufferWriter(body);
    JsonRpcProcessor.Process(session, in buffer, counting, context, options.Serializer);
    written = counting.Written;
}
```

Check the count before the endpoint flush. The API is present in the installed reference pack;
unsupported writers require the fallback. [PipeWriter.UnflushedBytes](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipelines.pipewriter.unflushedbytes?view=net-10.0)
**Gain:** **0 ns in-process/TCP**; estimate 32 B/POST removed on supporting writers, likely well under 1% single-POST throughput.
**Risk:** a service that writes/flushes the HTTP response itself invalidates count-delta assumptions; define that ownership first.
If that behavior must be supported, prefer a private/internal processor result-count bridge, retaining the public void API.
**Affected:** only `JsonRpcEndpoint.HandleAsync`; wrapper construction has one local caller. Middleware/custom BodyWriters need fallback coverage.
**Verify:** notifications, error notifications, normal/error responses, batches, stream-backed BodyWriters, and preexisting buffered bytes.
Changing the wrapper to a struct while passing it as `IBufferWriter<byte>` would box it and break local count observation.

### P14 — Decode date and character inputs without temporary strings

**Location:** `Json-Rpc/Jsmn/JsmnMapper.cs:233,245,304`;
`AustinHarris.JsonRpc.SystemTextJson/JsonRpcConverters.cs:177,188,198`.
**Now:** char/date conversion obtains a managed string before parsing, even for common short unescaped inputs.
**Cost:** estimate 24–96 B per temporary string depending on length; DateTimeOffset's boxed mapper also boxes the value.
**Change:** on modern targets, decode common short values into a bounded stack char buffer and call the existing span TryParse overload:

```csharp
Span<char> chars = stackalloc char[64];
// For validated, unescaped UTF-8 with byte length <= chars.Length:
int n = Encoding.UTF8.GetChars(text, chars);
if (DateTime.TryParse(chars.Slice(0, n), CultureInfo.InvariantCulture,
        DateTimeStyles.RoundtripKind, out var value)) return value;
// Existing DecodeString/TryParse fallback for escapes, long values, and older targets.
```

For char, directly return a single validated ASCII byte; retain Unicode/escaped/coercion fallbacks.
Use a typed DateTimeOffset reader/writer cache only if its workload matters; this also avoids the boxed fallback.
Do not blindly replace permissive DateTime.TryParse with Utf8Parser's narrower accepted formats.
**Gain:** **0 ns on the current benchmark**, which has no date/char requests; one string per eligible parameter avoided.
**Risk:** medium: culture, Kind, offsets, DST transitions, escaped text, and accepted non-canonical dates.
**Affected:** built-in typed/boxed mapping and STJ wire converters, including nested POCO members.
**Verify:** cross-serializer byte/value parity for UTC/local/unspecified, trimmed fractions, positive/negative offsets,
single UTF-16 code units, surrogate pairs, escaped strings, and permissive date spellings.

## 3. Sealing and layout

### Complete disposition of non-static production reference types

Scope here is the core and the three companion packages requested, including private nested helpers.
Tests, benchmark fixtures, generated types, and the separate legacy 1.x projects are not a public sealing migration.
No local subclass was found for the unsealed concrete types below; this says nothing about external consumers.
`callvirt` in IL on a nonvirtual method is commonly a null check, not evidence of virtual dispatch overhead.

| Type and declaration | Recommendation | Local callers/derivers affected |
| --- | --- | --- |
| `Handler`, `Handler.cs:12` | Seal safely: its only instance constructor is private. Expect negligible gain; instance request methods are already nonvirtual. | Processor, Config, ServiceBinder, JsonRpcService, SMD, context helpers, hosts, benchmarks, tests. |
| `JsonRpcContext`, `JsonRpcContext.cs:12` | Seal safely: private constructor. Make Value get-only. | Current constructs it; service/tests and HTTP context users consume it. |
| `SMD`, `SMDService.cs:14` | Technically sealable; public inheritance compatibility decision, no meaningful hot-path gain. | Handler construction/MetaData, dispatch-hardening tests. |
| `SMDService`, `SMDService.cs:290` | Same public compatibility decision; retain as reference object. | SMD registration/collection, Handler resolve, mutation tests. |
| `SMDResult`, `SMDService.cs:358` | Technically sealable; startup metadata only. | SMDService constructor and metadata serialization. |
| `ParameterDefaultValue`, `SMDService.cs:373` | Technically sealable; startup metadata only. | SMDService constructor/defaultValues and metadata serialization. |
| `SMDAdditionalParameters`, `SMDService.cs:391` | Technically sealable; retain mutable metadata shape. | SMD/SMDService/SMDResult construction and recursive type description. |
| `JsonRequest`, `JsonRequest.cs:7` | Technically sealable; recommend retain compatibility. | Handler/hooks, Config delegates, old client sources, protocol/serializer tests. |
| `JsonResponse`, `JsonResponse.cs:6`, and `JsonResponse<T>`, `:20` | Technically sealable; no normal streaming-path allocation to remove. | Handler/post hooks, old clients, tests, generic client responses. |
| `JsonRpcException`, `JsonResponseErrorObject.cs:30` | Do not seal for speed; application exception derivation is a reasonable extension point. | Service methods, Handler mapping, processor errors, tests. |
| `JsonRpcBindException`, `Serialization/JsonRpcSerializer.cs:176` | Technically sealable; preserve custom serializer exception compatibility. | Mapper, Utf8Json, JsmnSerializer, Handler type tests, hardening tests. |
| `JsonRpcStateAsync`, `JsonRpcStateAsync.cs:10` | Technically sealable; compatibility-only, no sync benchmark gain. | Processor continuation, Newtonsoft forwarding helpers, classic ASP.NET handler. |
| `InProcessClient`, `Client/InProcessJsonRpcClient.cs:10` | Obsolete wrapper; sealing/static conversion is an API decision, not a performance project. | No local call to Invoke; public external callers remain possible. |
| `JsonRpcOptions`, `AspNetCore/JsonRpcOptions.cs:8` | Technically sealable; preserve options extensibility unless a major API cleanup is approved. | DI/options configuration, endpoint, connection handler, host tests. |
| `JsonRpcConnectionHandler`, `AspNetCore/JsonRpcConnectionHandler.cs:16` | Technically sealable; overriding OnConnectedAsync may be useful. At most saves dispatch once per connection. | DI registration, Kestrel UseConnectionHandler, both transport benchmarks, host tests. |
| `JsonRpcService`, `JsonRpcService.cs:6` | Must remain abstract/unsealed. | Calculator/test/Wasm/application services derive from it. |
| `JsonRpcSerializer`, `Serialization/JsonRpcSerializer.cs:17` | Must remain abstract/unsealed. | Three built-in adapters plus external/custom serializers; tests exercise extension behavior. |
| `JsonRpcRequestReader`, `Serialization/JsonRpcSerializer.cs:120` | Must remain abstract/unsealed despite one shipped implementation. | Serializer.CreateReader extension contract and processor/handler/invoker consumers. |

Already sealed: `JsmnTokenizer`, `JsmnRequestReader`, `JsmnSerializer`, `PooledByteBufferWriter`,
`Utf8KeyTable<T>`/its Entry, `ExceptionInfo`, `RpcMethod`, `RpcParameter`, `SMDServiceCollection`,
both RPC attributes, `JsmnMapper.MemberPlan`/TypePlan, processor Scratch, and Handler.InvocationState.
Also sealed: `SystemTextJsonRpcSerializer`, its DiscardingBufferWriter and TypeInfo Entry,
all five public STJ converter/factory classes, and all eleven private numeric converter classes.
Also sealed: `NewtonsoftJsonRpcSerializer`, its Scratch, `Utf8CharReader`, `BufferWriterTextWriter`,
`JsonArrayPool`, `CountingBufferWriter`, `JsonRpcServiceRegistration`, and `JsonRpcBinderHostedService`.
All other production helper classes in scope are static; there is no overlooked internal unsealed class to fix.

### Struct decisions

| Type | Decision and reason |
| --- | --- |
| `Utf8KeyTable.Entry` | P6's readonly struct in a flat snapshot is the strongest candidate; a struct cannot retain the current recursive Next-by-value shape. |
| `InvocationState` | A mutable thread-static struct or two thread-static references could remove one per-thread object, not a per-request allocation. Keep the class unless measured; taking a local value copy would break shared exception updates. |
| `MemberPlan` | Could become a readonly struct with six reference fields, but copies are large and FindMember currently uses null as its miss sentinel. Prefer immutable sealed class first; benchmark array locality before changing representation. |
| `RpcParameter` | Could be an internal readonly value record, but public Parameters exposes RpcParameter[] and a class-to-struct change is breaking. Startup-only allocation; do not prioritize. |
| `TypePlan` | Keep sealed class: large, shared, cached, reference-rich plan. A struct would copy many fields and complicate atomic cache publication. |
| `JsonRpcContext` | A readonly struct could save the estimated 24 B allocated by each Current call; it changes public type/null/identity semantics. Needs decision; Handler.RpcContext already provides an allocation-free object accessor. |
| `SMDResult`, `ParameterDefaultValue` | Plausible tiny readonly structs in a new internal metadata model, but public class/array/serialization compatibility makes conversion unjustified here. |
| `CountingBufferWriter` | Do not convert at the existing interface boundary; use P13 or a by-ref generic internal pipeline. |
| `JsonRpcServiceRegistration` | Keep class: DI's class-constrained registration and boxed service storage remove the practical benefit of a struct. |
| STJ `TypeInfo<T>.Entry` | Keep immutable class if retained: options/info are published atomically as one reference. A mutable two-field struct is not an equivalent publication mechanism. |
| Scratch/readers/tokenizer/writers | Keep reference types: reusable ownership, shared state, virtual/interface contracts, and async transport storage. |
| `JsmnCursor`, `JsmnMapper.cs:16` | Already a ref struct. Existing public mutable fields/ref delegate signature rule out a silent readonly conversion. Internal code does not need heap storage. |
| `JsmnToken`, `JsmnTokenizer.cs:22` | Already a struct; must remain mutable while parsing sets End, Size, and flags. |
| DTOs/errors/options/services | Keep classes: mutation, polymorphism, application identity, or exception inheritance is part of their role. |

Token layout deserves a separate experiment: four ints followed by Type/Escaped/IsKey imply a 20-byte
sequential layout rather than the present likely 24 bytes with padding. Confirm with `Unsafe.SizeOf<JsmnToken>()`.
That saves roughly 256 bytes per 64-token array and 16.7% token-array traffic, not 16.7% request time.
Reordering this **public** struct's fields can affect interop/layout consumers; flag it under Needs decision.
Do not add a subtree-end int casually: it can erase the packing win and adds stores to every token.
`Skip` and `JsmnCursor.Next` revisit subtrees, but for tiny scalar requests a side index may cost more than it saves.
For deeply nested/large structured parameters, benchmark an internal end-index sidecar before changing the public token.

### Fields that should become readonly

These are all remaining clear production field candidates in scope; constructor refactoring is required where noted.
Readonly protects the reference, not the contents of an array/dictionary or the thread safety of its elements.

| File:line | Fields / change | Caller or mutation audit |
| --- | --- | --- |
| `Handler.cs:20,22` | `_sessionHandlersMaster` and `_defaultSessionId` → static readonly. | Assigned only in static Handler constructor; registry contents still mutate. Remove volatile from the immutable ID field. |
| `JsonRpcStateAsync.cs:22,23` | `cb`, `asyncState` → readonly. | Assigned only by constructor; completion reads them and changes isCompleted separately. |
| `Invocation/RpcMethod.cs:27` | `RpcParameter.NameUtf8` → readonly through an internal constructor. | Build initializes it; Handler.BindMap/diagnostics only read. Keep public construction compatibility. |
| `Serialization/Utf8KeyTable.cs:15–18` | Entry Key, Hash, Value, Next → readonly constructor fields if retaining linked entries. | Insert constructs complete nodes; nodes are never edited after publication. P6 supersedes this layout. |
| `Jsmn/JsmnMapper.cs:581–586` | MemberPlan Name, NameUtf8, NameJson, Type, Get, Set → readonly constructor fields. | MakeMember is the only initializer; FindMember/ReadPoco/WriteObject read them. |
| `Jsmn/JsmnMapper.cs:593–603` | TypePlan Kind, ElementType, KeyType, Members, ReadableMembers, Create, Add, DictionaryAdd, Enumerate → readonly after Build finishes. | Build currently mutates a temporary plan; construct complete plans per branch. Remove unused Type rather than making it readonly. |

Constructor-only auto-properties can also become get-only: Handler.SessionId, JsonRpcContext.Value,
SMDService.Method/transport/envelope/returns/parameters/defaultValues, SMDResult.__type,
ParameterDefaultValue.Name/Value, and RpcMethod's metadata/invoker/IdentityMap properties.
RpcParameter.Name/Type/HasDefault/DefaultValue can likewise become get-only through an internal constructor;
Build is their only initializer. Retain the existing public parameterless constructor for compatibility.
Preserve public setters and public mutable fields such as SMDService.dele, SMD metadata, DTOs, exceptions,
JsonRpcOptions, and JsmnToken/JsmnCursor. Removing those writes is an API change, not a readonly annotation.
Leave buffer arrays, cache-owner fields, thread-static slots, tokenizer counters/configuration, hooks,
InvocationState fields, CountingBufferWriter.Written, and async completion state mutable.
Existing literal arrays, serializer settings references, mapper cache dictionaries, encoder, DI dependencies,
and singleton converters/pools are already readonly/static readonly.

### Virtual and interface calls on the request path

Processor reader calls: TryParse, IsBatch, Count, Release; Handler calls: Select, IdKind, IdRaw,
VersionKind, HasMethod, ParamsKind, ParamCount, MethodUtf8, and named ParamNameUtf8.
All can have an internal exact-JsmnRequestReader path; their public abstract contract must remain.
`ReadParam<T>` and `serializer.Write<T>` are P1's high-value calls; boxed ReadParam/ParamsValue,
IdValue, Method, and non-generic serializer calls belong mainly to hooks, errors, or compatibility paths.
`Lenient` is virtual once per parse; MaxDepth is read when constructing the default reader, not per parameter.
`CreateReader` is normally a cache miss operation; optimize churn under P7 rather than devirtualizing startup work.
`IBufferWriter.GetSpan/Advance` are P3; GetMemory matters to adapters/transport, not the built-in numeric writer.
PipeReader.ReadAsync/AdvanceTo and PipeWriter.FlushAsync remain legitimate polymorphic transport boundaries.
Newtonsoft's TextReader/TextWriter and STJ's converter calls belong to those libraries' extension contracts.
Do not bypass configured converters merely because their default implementation is sealed.
Reference-type generic constraints alone do not guarantee specialization; exact receiver types/direct helpers do.
PGO may already guard and inline virtual/interface/delegate calls, so verify residual calls in optimized disassembly.
This is consistent with the runtime team's discussion of guarded devirtualization and bounds checks.
[.NET 10 performance engineering](https://devblogs.microsoft.com/dotnet/performance-improvements-in-net-10/)

## 4. Memory and stack

### Remaining allocation inventory

| Request shape | Allocation site and shape | Action / limit |
| --- | --- | --- |
| Four numeric sync shapes | No steady-state managed allocation expected from the built-in path after warm-up. | Preserve the README's zero; startup token/plan/delegate objects do not count as per request. |
| `StringMe("Foo")` | `JsmnMapper.ReadString:141` → Encoding.GetString; one three-character string, estimate 32 B. | The application signature requires a CLR string. No unsafe raw-byte echo shortcut. Mixed five-shape average is about 6.4 B/RPC, not zero. |
| String processor API | `JsonRpcProcessor.ProcessSync:155` creates response string; pooled input transcodes request. | Byte API already avoids the response string. |
| Standalone serializer string adapters | `JsonRpcSerializer.cs:49–79`: Deserialize allocates the UTF-8 byte array; Serialize allocates a writer object and result string. | Convenience APIs, separate from streaming invocation; callers already have byte/span alternatives. |
| netstandard2.0 string decoding | `Utf8Json.ToStringUtf8:463` uses `utf8.ToArray()` before Encoding.GetString. | Extra byte array on that target; the modern span overload avoids it. Do not claim identical allocation counts across TFMs. |
| Byte-array return API | `ProcessBytes:82` → PooledByteBufferWriter.ToArray allocates byte[]. | Intentional ownership transfer; use caller writer to avoid it. |
| Task string API | `JsonRpcProcessor:123–127` creates Tuple plus Task; output string also remains. | Scheduling semantics are a decision, not part of sync tuning. |
| Legacy async-state API | Processor:100 captures async in a continuation, with delegate/closure/continuation Task. | Keep compatibility; no benefit to sync benchmark. |
| JsonRpcContext.Current | `JsonRpcContext.cs:41` creates a wrapper each call, estimate 24 B. | Service code needing only Value can use Handler.RpcContext. |
| Hooks | Handler:312/438/519 creates/materializes IDs, method strings, params, JsonRequest/JsonResponse; value results box. | Hooks intentionally select this path; do not secretly pool mutable objects handed to user code. |
| Modified hook request | Handler:468 creates a writer and reader, serializes then reparses replacement request. | Compatibility work; preserve post-hook dispatch behavior. |
| Ordinary RPC errors | JsonRpcException construction, optional formatting strings; thrown binding/method errors add stack/exception costs. | A non-thrown constructed error has different cost from throwing it. |
| Exception data | Handler:703/707 → ExceptionInfo DTO; detailed mode can create stack strings and inner chain DTOs. | Preserve redaction/details policy; do not pool exceptions shared with hooks. |
| POCO numeric fields | Mapper:474 boxes ReadObject values; :570 getter returns object and boxes value properties. | Typed member reader/writer plans could remove roughly 24 B per boxed int/double, estimate; not exercised by current benchmark. |
| `int[]`/`List<int>` parameter | Mapper:417/432 boxes each value; Array.SetValue or object-typed Add consumes it. | Typed collection factories/readers can remove boxes; result array/list itself remains necessary. |
| Value collections returned | Mapper:522/551 uses non-generic IDictionary/IEnumerable. | Enumerator and value boxing; use typed generated paths only for measured common collection shapes. |
| Dictionary parameter | Mapper:636/650 creates object[2] for each Add, in addition to keys/values/boxes. | P8. |
| Dynamic object parameter | Mapper:377/389 creates List<object>/Dictionary<string,object>, keys, boxed numbers. | Object-model semantics require these representations unless API changes. |
| Date/char/Guid/TimeSpan/base64 | Mapper:238/250/306/312/318/332; writes :513/514/516 allocate formatted strings. | P14; typed Utf8Formatter/Base64 paths are follow-ups, with exact-format tests. |
| Newtonsoft numeric read/write | New JsonTextReader at serializer:93 per parameter; object-based internal conversions and WriteCore box values. | Pooled char buffers do not make this adapter zero-allocation; retain custom settings/converters. |
| STJ mixed options | New TypeInfo Entry at serializer:269; writer recreation at :137 on option changes. | P10. |
| HTTP | CountingBufferWriter at endpoint:58; async infrastructure may allocate on suspension. | P13 removes only the explicit 32 B wrapper estimate. |
| Batch | Reader index arrays grow at RequestReader:87/195; token/output arrays grow with demand. | Usually warm allocations, not one DTO per member. Large token arrays >4096 are deliberately released after each document. |
| Reentrant/serializer-switch calls | Scratch:251/276, fresh JsmnTokenizer, reader arrays and rentals. | P7. |

The processor retains input/output capacities per thread; the reader retains parameter/request/name/ID arrays.
JsmnTokenizer.Release shrinks unusually large token/stack storage, but JsmnSerializer.ReturnTokenizer does not call Release.
Choose a documented high-water retention policy before adding pools; “return to ArrayPool” does not mean process memory immediately shrinks.
Do not put request objects/strings in global caches to manufacture zero-allocation benchmark results.

### Stack, spans, bounds, delegates, and diagnostics

- Stackalloc is appropriate for P11's bounded escape buffer and P14's short char buffer; never size it directly from arbitrary request length.
- Existing STJ numeric/date buffers are only 35–48 bytes and WriteChar uses one char; these are appropriate stack allocations.
- A stack parameter map requires an internal span-aware delegate signature; the public int[] invoker cannot consume stack memory.
- Keep scratch per invocation depth, not a single thread-static mutable map: nested methods can invalidate their caller's map.
- A `ref readonly JsmnToken` local can avoid token copies in read-only helpers; current `in JsmnToken` Slice/RawJson already express that intent.
- Ref-returning a token across token-array growth is unsafe logically even in managed code: it points to the old array. Acquire refs after allocation/growth.
- Do not turn a reader containing ReadOnlyMemory into a ref struct: it is cached as a class and participates in virtual dispatch.
- Public span slices validate their ranges; reserve once and use a local bounded span inside tight formatting loops before considering Unsafe.Add.
- Tokenizer field-loop restructuring is P2; ParseString, hashing, and span equality already use simple local loops or runtime primitives.
- `Skip` and `JsmnCursor.Next` have matching subtree-walk algorithms; a shared helper can reduce duplication, but measure inlining before merging.
- `[SkipLocalsInit]` is not a primary recommendation. It does not eliminate heap-object or array initialization.
- For tiny numeric buffers its benefit is likely 0–2 ns per formatting call, estimate, and may be zero after JIT optimization.
- If tested, apply it to a narrow internal method on supported TFMs, prove every emitted byte initialized, and inspect assembly before/after.
- Never apply it broadly to parser state or use it to justify reading beyond the initialized prefix of a rented/stack buffer.
- RpcMethod expression closures and Jsmn nullable delegates are created at registration/type initialization, not per numeric request.
- TypePlan list/dictionary accessor closures are cached; the dictionary argument array is per entry and is the real P8 allocation.
- The sync harness has one closure/output writer per worker, not per RPC; the stop flag read and input rotation are included in elapsed time.
- SessionSelector/ContextFactory/host route delegates are cached delegates; user callback bodies may allocate, but merely invoking them does not create a closure.
- LINQ in ServiceBinder, RpcMethod reflection selection, TypePlan.BuildMembers, SMD metadata, and converter setup is cold work.
- There is no LINQ enumeration on the normal scalar Handler fast path. Do not advertise removing registration LINQ as a 227 ns improvement.
- Avoid source-level `(T)(object)value` rewrites without disassembly; generic value-type specialization may remove boxing, but reference/shared cases differ.
- Bind errors use string.Format in Handler:559/565/599/623/626/628; mapper Bind:259–260 decodes/truncates then concatenates text.
- Depth errors concatenate the depth in RequestReader:68 and JsmnSerializer:90. These matter under malformed traffic, not successful benchmark traffic.
- STJ Wire.Bind:223 copies the entire token to an array before truncating the resulting string at :231; bound diagnostic decoding first for large bad values.
- Preserve the same first 64 decoded characters and UTF-8 boundary handling if changing that diagnostic path; do not silently change error text.
- For frequent framework-generated errors without hooks, an internal `(code,message,data)` envelope helper could avoid constructing JsonRpcException.
- Keep fresh exception objects when an error hook receives them; changing identity/mutability or sharing thrown instances is not an allocation optimization.
- Strict string/integer IDs already echo raw bytes without materializing an object. The decimal/exponent ID policy must not change for speed.
- Built-in numeric parsing already uses Utf8Parser; writing already uses Utf8Formatter except the deliberate netstandard2.0 float fallback.
- Do not replace whole-float `.0` preservation with default STJ formatting, or DateTime formatting with `O`: both change bytes.
- DateTime formatting already writes directly into output and trims fractions; GetDateParts/TryFormat alternatives are secondary experiments outside the current workload.

## 5. Conciseness

These recommendations distinguish safe internal removal from public API decisions and preserve the hardening work.
Private/internal deletion candidates were searched with tree-sitter and cross-checked in source; public absence means only “unused locally.”

| File:line | Remove or merge | Behavior / affected callers |
| --- | --- | --- |
| `Json-Rpc/Basic.cs:1` | Delete this wholly commented-out MetadataService file and unused usings. | No compiled behavior and no callers; the referenced Handler.Current is historical text. |
| `JsonRpcProcessor.cs:165` | Remove EmptyBatchError. | No reads; avoids one unused startup byte array. Actual empty-batch error text at :191 remains. |
| `Utf8KeyTable.cs:71` | Remove ReplaceWith. | No callers; direct service mutations now go through SMDServiceCollection and update the table immediately. |
| `Utf8KeyTable.cs:83` | Remove Clone's unused TValue argument; change its Set/Remove calls. | No behavior change. |
| `Utf8KeyTable.cs:22,25` | Remove unused exposed Count/_count if no new builder uses it; carry rebuild count locally instead. | Only assigned/read by its own unused property; no SMD consumer. CountEntries still supports current Insert until refactored. |
| `JsmnMapper.cs:594,623` | Remove TypePlan.Type and its initializer. | No reads; plan cache key is already Type. |
| `JsmnMapper.cs:739` | Remove `.Where(f => !f.IsInitOnly || true)`. | Always true; readonly fields still serialize, setter gating at :746 remains. |
| `JsmnMapper.cs:764` | Remove unused MakeMember owner argument and update BuildMembers' field/property calls. | No behavior change; MakeGetter/MakeSetter still require owner. |
| `TestServer_Console/Benchmark.cs:380` | Remove PrintFinalIterationStats. | No callers; active chart/progress output uses other methods. |
| `SMDService.cs:434` | Replace `Where(...).Count() > 0` with Any. | Equivalent startup metadata query; no request throughput claim. |
| `JsmnTokenizer.cs:487`, `JsmnMapper.cs:34` | Consider one internal token-subtree helper for Skip/Next. | Same algorithm; callers are RequestReader and mapper collection/POCO traversal. Keep public methods as forwarding wrappers. |
| `JsonFramer.cs:18,77` | Share lexical-state transitions between sequence/span scanners where it simplifies P12. | FindDocumentEnd has no local callers but is public; malformed-prefix behavior currently differs and must remain explicit. |
| `Utf8Json.cs:179`, `SystemTextJson/JsonRpcConverters.cs:313` | Share internal span-number formatting/decimal-suffix rules as part of P3. | Companion assembly access needs a deliberate internal/shared-source arrangement; do not broaden public API just to save ten lines. |
| `JsmnMapper.cs:484` | Public WriteObject's declaredType parameter is unused. | Keep public signature; remove parameter only from a new private recursive core if useful. Every recursive caller currently passes a type. |
| `PooledByteBufferWriter.cs:66` | Remove stale “unwrap a single-response batch” rationale. | RemoveAt itself has no local callers but is public; deletion needs an API decision. Batch-array behavior stays fixed. |
| `JsonRpcSerializer.cs:163`, `JsmnRequestReader.cs:298` | ParamIsNull is unused by current invokers. | Public abstract contract: do not delete without compatibility review; custom readers may implement/use it. |
| `ServiceBinder.cs:65–73`, `SMDService.cs:293` | Legacy delegate duplicates compiled invocation machinery. | Only stored in public SMDService.dele locally; retain until public compatibility decision. Removing it saves registration work, not RPC time. |
| `Client/InProcessJsonRpcClient.cs:10` | Obsolete InProcessClient forwards directly to Process. | No local callers, but published API; mark for a deliberate major-version cleanup instead of silently removing it. |
| `Config.cs`, `ServiceBinder.cs`, `JsonRpcProcessor.cs` | Keep current public overload families. | Concrete users include tests, hosts, old ASP.NET state adapter, Newtonsoft helpers, and samples. No proof that an overload is globally unnecessary. |
| `JsonRpcContext.cs:1`, `JsonRpcStateAsync.cs:1`, `Client/InProcessJsonRpcClient.cs:1` | Remove unused collections/LINQ/text/IO/threading imports as applicable. | No runtime or API behavior. |

Comments worth trimming: ServiceBinder:29/53/58 repeat dictionary/default/return assignments;
SMDService:328/336 narrate straightforward storage construction; Benchmark:353/358/367 merely label header/fields/footer.
Keep comments explaining ID scratch lifetime, output rollback, writer detachment, nested context restoration,
single-response batch arrays, notification suppression, and overload ambiguity: those document non-obvious constraints.
Correct RequestReader base comments saying method/name bytes keep escapes: the shipped reader returns decoded UTF-8 for escaped names.
Do not remove JsonRpcRequestReader merely because it has one shipped implementation; CreateReader is an explicit extension point.
Do not merge the boxed and streaming invocation bodies wholesale: their allocation and hook semantics intentionally differ.
Do not replace the two SMD lookup representations without preserving mutable IDictionary snapshots and immediate dispatch updates.

## 6. Things checked and found fine

- `RpcMethod.FromMethod` uses a compiled direct call; expression construction/reflection is registration work, not per-request MethodInfo.Invoke.
- Typed numeric and nullable binding avoids object[] and numeric result boxing on the normal streaming path.
- Positional parameters with exact count use IdentityMap; default constants are prepared at registration, not converted on each request.
- `JsmnRequestReader` binds built-in parameters from existing tokens; it does not tokenize each scalar again.
- Custom serializers receive raw value slices; using a custom reader/serializer remains meaningful even with a built-in fast path.
- Most performance-relevant concrete classes are already sealed, including the tokenizer, reader, serializer, writer, and invoker metadata.
- Tokenizer now maintains an explicit open-container stack, validates grammar/UTF-8, and enforces depth; the prior quadratic closing bug is not a new finding.
- Shared safe primitives already use spans, in token parameters, ref token access, Utf8Parser, and Utf8Formatter.
- Method lookup avoids string allocation on ordinary hits; misses/custom-reader fallback may decode Method and take the locked string table path.
- ID echo bypasses numeric formatting/object conversion for valid ordinary IDs; normalized lenient IDs have independent storage.
- InvocationState is one lazy object per thread with saved/restored fields, not one newly allocated frame per RPC.
- Scratch `_inUse` protects active byte buffers from reentrancy; P7 changes reuse economics, not the need for isolation.
- Output staging is intentional: the processor can rewind a partial result after serializer/service failure before copying to the caller.
- ReadOnlyMemory and single-segment sequence input avoid input copies; span and multi-segment inputs explicitly copy into pooled contiguous storage.
- Response staging adds one output copy; deleting it would lose rollback for arbitrary IBufferWriter destinations.
- Batch punctuation/rollback is correct for one response, mixed notifications, and all notifications; preserve the previous fixes.
- SMDServiceCollection updates UTF-8 dispatch on supported mutation; do not reinstate a stale successful-lookup cache.
- Newtonsoft pools char storage and retains writer state with failure handling; it is intentionally not a zero-allocation value adapter.
- STJ failed-write detachment, user converter precedence, and immutable effective options should survive all proposed optimizations.
- DateTime output already matches the documented fractional/Kind conventions; changing to default round-trip formatting is not equivalent.
- Raw connection processing flushes once per read-loop group, not once per RPC; do not move FlushAsync into the document loop.
- HTTP consumes BodyReader input after processing, and both transports enforce request-byte limits.
- Server GC and disabled concurrent GC are explicit harness settings; compare like-for-like when testing a change.
- The sync benchmark rotates five requests equally, uses ReadOnlyMemory and reused writers, and reports allocations outside its timed loop.
- Its hot loop has no per-request response validation; responses are printed during allocation profiling. Add correctness checks outside timing when measuring changes.
- Kestrel HTTP/TCP runs include loopback/client work; prefix validation is not full result/ID validation, especially for HTTP batches.
- The 16-thread README result is aggregate throughput; 488 ns is per-thread work time, not end-to-end p99 latency.
- The README's TCP 14.3–14.8 M already exceeds a 7–10 M process-wide target; a one-thread target is a separate engineering requirement.
- Native simdjson adoption remains unsupported by the repository's own evaluation; no new dependency is justified by this review.

Verification performed:

```text
git branch --show-current / git rev-parse --short HEAD
  finish-netstandard-upgrade / ccce23c
git log --oneline -40
  Read, including parser/dispatch/serializer hardening and subsequent benchmark changes.
dotnet build AustinHarris.JsonRpc.sln -c Release
  Exit 1; printed 0 warnings, 0 errors, no actionable diagnosis.
dotnet build AustinHarris.JsonRpc.sln -c Release --no-restore -m:1 -v:minimal
  Core/serializer package assets, AspNetCore, tests and TestServer_Console compiled.
  Solution failed in samples/WasmHost: MSB4216 ComputeWasmBuildAssets task host,
  followed by MSB4027 disposed MetadataLoadContext; 10 warnings, 2 errors.
dotnet test AustinHarris.JsonRpcTestN -c Release --no-build --no-restore -f net8.0 -v:minimal
  Passed 745; failed 0; skipped 0.
dotnet test AustinHarris.JsonRpcTestN -c Release --no-build --no-restore -f net10.0 -v:minimal
  Passed 745; failed 0; skipped 0.
dotnet run -c Release --no-build --project TestServer_Console -- --sync 2
  Stopped before timing: System.Management.ManagementException, Access denied,
  in Hardware.Info at Program.cs:19.
dnx dotnet-inspect -y -- member System.IO.Pipelines.PipeWriter --aspnetcore --oneline -30
  Could not load NuGet service index; used installed reference XML and official docs instead.
```

No code was changed for tests, benchmarks, instrumentation, or build workarounds.
The successful tests establish current regression coverage, not that the proposed changes work or meet their estimates.
I did not measure new throughput, emitted machine code, cache misses, p99 latency, or per-shape CPU attribution.
For implementation verification, use a dedicated machine/run window; warm each path, randomize A/B order,
report medians/spread, allocations and code size, and retain unmodified baseline output bytes as fixtures.
Do not repeatedly select the best `--sync 2` run or claim transport throughput scales linearly with core nanoseconds saved.

## 7. Needs decision

1. **Target definition:** 7–10 M per process is already demonstrated; 7–10 M per thread requires 143–100 ns/RPC.
   Recommendation: accept a first milestone of roughly 5–6 M/thread, then profile the remaining budget before attempting a scanner/dispatch fusion.
2. **Specialization versus code size:** one built-in fast path can reduce indirection; a cross-product of serializers, arities, and types will grow maintenance/JIT cost.
   Recommendation: P1 for existing primitive shapes, fallback for everything else; keep the public serializer/reader contracts.
3. **Public sealing/removal:** externally derivable classes and locally unused helpers can still have consumers.
   Recommendation: seal only private-constructor Handler/JsonRpcContext now; defer other public sealing, class-to-struct conversions, and helper removals.
4. **Token ABI/layout:** packing JsmnToken can improve density; changing a public sequential struct's layout is observable outside managed field access.
   Recommendation: benchmark a private representation or obtain an explicit layout-compatibility decision before reordering fields.
5. **Struct context versus wrapper compatibility:** readonly JsonRpcContext would remove per-access allocation but change reference/null semantics.
   Recommendation: preserve Current's API and document/use Handler.RpcContext for latency-sensitive service code.
6. **Scratch retention:** nested reuse and two recent serializer slots improve repeat workloads but retain more memory per worker.
   Recommendation: bounded depth/cache count and measured capacity trimming; no unbounded per-thread serializer dictionary.
7. **Notification result serialization:** Handler:355–379 currently serializes a result then rewinds it for a notification.
   Recommendation: consider an internal invoke-with-discard delegate for notification-heavy traffic only after deciding whether result getters/converters and serialization-error hooks must still execute.
   The wire remains empty either way, but observable application side effects differ; no gain is included in the sync estimate.
8. **HTTP output ownership:** P13 assumes the endpoint owns writes/flushes while processing a request.
   Recommendation: if services may manipulate HttpResponse directly, retain the wrapper or add an internal written-count bridge without changing public JsonRpcProcessor signatures.
9. **Task overload scheduling:** Task.FromResult(ProcessSync(...)) would remove a hop but run service code synchronously on the caller and change exception/scheduler behavior.
   Recommendation: keep existing scheduling; use the existing byte/sync APIs where the host already owns execution. Any public Processor/Config/ServiceBinder API change requires a separate decision.
10. **Public extension points versus conciseness:** removing JsonRpcRequestReader, legacy invoker delegates, or ServiceBinder/Config overloads would simplify code but break real integration contracts.
    Recommendation: remove the verified private/internal dead code first; do not trade public behavior for cosmetic shrinkage.
11. **Wire compatibility:** numeric suffixes, non-finite quoting, DateTime Kind/fractions, ID bytes, error data, hook mutations, and batch envelopes stay fixed.
    Recommendation: every fast path falls back when it cannot prove equivalence; none of the headline estimates assumes a wire-format change.

Only this review deliverable was authored by this reviewer. No commits, stash, branch changes,
Linear writes, knowledge-graph writes, messages to other agents, or production-source edits were performed.

## 8. Implementation record (2026-09-23, same branch)

What landed from this review, measured with `TestServer_Console --sync 2 1` (seven runs each, sorted; the machine was
about 15 % slower than when the README tables were taken, so only the A/B is meaningful):

| Step | 1-thread RPC/s, seven runs |
| --- | --- |
| baseline (ccce23c) | 3.20, 3.39, 3.42, 3.61, 3.87, 3.90, 4.14 M |
| + sealing/readonly/dead code, P1, P3, P4, P5, P6, P8, P9 | 3.66, 3.77, 3.89, 3.92, 3.94, 3.95, 4.16 M |
| + P2 (tokenizer with local scanner state) | 4.33, 4.42, 4.45, 4.52, 4.52, 4.61, 4.64 M |
| + hash over 8-byte words, bounded string reservation, nested-scratch return | 3.99, 4.19, 4.28, 4.30, 4.35, 4.42, 4.48, 4.68, 4.78 M (nine runs; noise, not a regression) |

Tests: 745/745 on net8.0 and net10.0 after every step; all four 2.0.0 packages pack.

- **P1 + P3 done.** `RpcMethod` compiles a third invoker (`JsmnInvoker`) used when the reader is the built-in
  `JsmnRequestReader` owned by `JsmnSerializer`: parameters are read through static helpers on the reader
  (`ReadInt32Param` and friends, nullable primitives as a null test plus the value read, everything else through
  `JsmnReader<T>`), and the result is written through new `Utf8Json` overloads that take the concrete
  `PooledByteBufferWriter`. The point is that expression-compiled delegates are dynamic methods, which get no tiered
  compilation and therefore no PGO devirtualization, so every virtual, delegate and interface call inside them was a
  real indirect call. The public `StreamingInvoker` path is unchanged and still serves custom serializers and
  readers. `WriteNull`'s reflection lookup now names its parameter type.
- **P2 done.** `JsmnTokenizer.Parse` keeps position, token count, parent, depth and the arrays in locals and publishes
  them once on exit; the string, primitive and bare-key scanners are static and return an end index or an error;
  token growth is a cold method; digit tests use `(uint)(b - '0') <= 9`. Grammar, UTF-8, escape, surrogate and depth
  checks are unchanged. This was the largest single gain.
- **P4 done.** Thread-local last-session-id/handler pair by reference, dropped whenever the local snapshot is rebuilt.
- **P5 done.** Envelope keys are selected by length (and first byte for the two six-byte names) before comparison.
- **P6 done.** `Utf8KeyTable` is one immutable snapshot: `int[]` bucket heads into a contiguous `Entry[]` of readonly
  structs; mutations rebuild and publish a new snapshot, no per-entry objects, no repeated `CountEntries`.
- **P8 done.** Dictionary `Add` is compiled once per plan; no `MethodInfo.Invoke` and no `object[2]` per entry.
- **P9 done.** Named parameters supplied in declaration order return the identity map, gated by a per-method
  `HasUniqueNames` flag set at registration; every other named case keeps the validated map.
- **Hash.** `Utf8Json.Hash` now consumes eight bytes per step (FNV-style multiply-xor over 64-bit words) with a byte
  tail; the method table is the only consumer.
- **P11 partly.** Strings longer than 512 chars are written in chunks (never splitting a surrogate pair), so the
  worst-case six-bytes-per-char reservation is bounded; short strings keep the single reservation.
- **P7 partly.** A nested (re-entrant) scratch returns its input array and pooled writer when released instead of
  leaving them to the GC. No per-thread stack of scratches.
- **Sealing and layout.** `Handler` and `JsonRpcContext` are sealed (private constructors, nothing derives). Other
  public classes stay open, per section 7 item 3. `JsmnToken` puts its three byte-sized fields first, 24 -> 20 bytes;
  this reorders a public struct's fields, which only matters to unsafe or interop code, of which there is none.
- **Readonly.** `Handler._sessionHandlersMaster` and `_defaultSessionId` are static readonly; `JsonRpcStateAsync`
  callback/state and `RpcParameter.NameUtf8` (now set by an internal constructor) are readonly; `TypePlan.Type` is gone.
- **Conciseness.** `Basic.cs` (commented-out class), `EmptyBatchError`, `Utf8KeyTable.ReplaceWith`/`Count`, the
  always-true `Where`, `MakeMember`'s owner argument, `PrintFinalIterationStats`, unused usings and the comments that
  restated the code are removed; `Where(...).Count() > 0` is `Any`; the reader base comments say names are decoded.
  Public members flagged as locally unused (`RemoveAt`, `ParamIsNull`, `FindDocumentEnd`, `InProcessClient`, the legacy
  `dele` delegate, the overload families) are kept, per section 7 item 10.
- **Not done, and why.** P10 (STJ one-entry type-info cache): a miss costs one small allocation only when two options
  instances alternate; left for a decision. P12 (incremental framer state): transport-level, small documents complete
  in one read, not worth the state machine yet. P13 (`PipeWriter.UnflushedBytes`): the wrapper costs two interface
  calls per HTTP document, below noise. P14 (date/char decode without temporary strings): not on the benchmark path.
  The reader's virtual members in `Handler.HandleRequest` are left virtual: that method is tiered code where dynamic
  PGO already guards and inlines the single implementation, and a concrete duplicate of the dispatch would cost more
  in maintenance than the type check it saves.
