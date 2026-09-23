# simdjson evaluation for JSON-RPC.NET 2.0 (fourth serializer?)

Decision rule from the owner: adopt simdjson **only** if it is measurably faster than the built-in jsmn
tokenizer on the library's own workload (single small JSON-RPC requests; the whole pipeline runs at
~273 ns per request single-threaded, so the tokenizer's share is well under 100 ns).

**Result: do not adopt.** On every input the simdjson binding is 3.4x to 6x slower than the full jsmn
envelope read, allocates 350 B to 14 KB per document where jsmn allocates nothing, and cannot hand back
slices of the request bytes in the shape `JsonRpcRequestReader` needs without extra copies. Details below.

## Environment

| item | value |
|---|---|
| machine | AMD Ryzen 7 7800X3D (8C/16T), 63 GB RAM, Windows 11 Pro 10.0.26200, x64 |
| SDK / runtime | .NET SDK 10.0.112 (global.json pins 10.0.100, rollForward latestFeature), runtime .NET 10.0.12, workstation GC, tiered PGO |
| repo | branch `finish-netstandard-upgrade` (worktree branch `worktree-agent-a5c308ba699f30667`), base commit `e598569` |
| core under test | `Json-Rpc/AustinHarris.JsonRpc.csproj` net10.0 target, Release |
| simdjson binding | NuGet `SimdJson.Net` 4.6.11.1 (published 2026-09-15, author Zoltan Csizmadia, MIT), wraps simdjson 4.6.11 On-Demand via a C ABI shim (`SimdJsonNative.dll`) |
| kernel selected by simdjson | `icelake` (AVX-512) |
| `SimdJsonParser.RequiredPadding` | 64 bytes (= SIMDJSON_PADDING) |
| harness | `benchmarks/SimdJsonEval` hand-rolled Stopwatch loop: 600 ms warm-up, trial length calibrated to ~200 ms, 9 trials, min and median reported; B/op from `GC.GetAllocatedBytesForCurrentThread` over a further n/10 iterations |

Run: `dotnet run -c Release --project benchmarks/SimdJsonEval` (add `--quick` for a 30-second pass).

## Candidate bindings on NuGet (2026-09-22)

| package | latest | published | TFMs | native runtimes | license | verdict |
|---|---|---|---|---|---|---|
| `SimdJson.Net` | 4.6.11.1 | 2026-09-15 | net8.0, net9.0, net10.0 | win-x64, win-arm64, linux-x64, linux-arm64, linux-musl-x64/arm64, osx-x64/arm64 | MIT (native part Apache-2.0) | the only maintained option; benchmarked here |
| `SimdJsonSharp.Bindings` (EgorBo) | 1.7.0 | 2019-05-26 | netstandard2.0 | win-x64, osx-x64 only | Apache-2.0 | 7 years stale, wraps a 2019 simdjson (DOM only, pre On-Demand); not evaluated |
| `SimdJsonSharp.Managed` (EgorBo) | 1.5.0 | 2019-03 | netcoreapp3.0 | pure C# port | Apache-2.0 | stale port of the 2019 DOM parser; not evaluated |
| `SimdJsonBindingsParser` / `SimdJsonBindings` | 1.8.1 / 1.7.0 | 2024-02 | - | none | Apache-2.0 | unlisted / deprecated by the owner |

`SimdJson.Net` is net8.0+ only, so a serializer built on it could never ship in the core package
(netstandard2.0/2.1 targets); it would be a companion package like the Json.NET / STJ ones.

## Inputs

| input | bytes |
|---|---:|
| `{"method":"add","params":[1,2],"id":1}` | 38 |
| `{"method":"NullableFloatToNullableFloat","params":[1.23],"id":3}` | 64 |
| `{"method":"StringMe","params":["Foo"],"id":5}` | 45 |
| batch x40: array of the three requests above, cycled | 1990 |
| object params x30: `{"method":"Configure","params":{ 30 members: int / double / string / bool / null },"id":7}` | 879 |
| notification (no id): `{"method":"add","params":[1,2]}` | 31 |

## What each row measures

| row | what it does |
|---|---|
| jsmn: JsmnTokenizer.Parse | (a) `JsmnTokenizer.Parse(span)` on one reused tokenizer instance (as `JsmnRequestReader` does): full token array, no walk |
| jsmn: JsmnRequestReader parse+locate | the envelope read the core actually performs: `TryParse`, then for every request `Select(i)`, `MethodUtf8`, `IdRaw`, `ParamCount`, `ParamRaw(i)`, `ParamNameUtf8(i)`, then `Release()` |
| STJ: Utf8JsonReader full walk | (b) `Utf8JsonReader` over the whole document, `ValueTextEquals` on property names, touching `ValueSpan` of scalars |
| simdjson: Parse(span) only, no walk | `SimdJsonParser.Parse(ReadOnlySpan<byte>)` + `Dispose` = 2 P/Invokes; the native side copies the bytes into its own padded buffer and runs stage 1 (structural index). No values are parsed (On-Demand is lazy) |
| simdjson: Parse(span) + GetField x3 | (c) parse, `ValueKind`, `GetObject`, `GetField("method")` -> `GetStringSpan`, `GetField("params")` -> iterate elements / members taking `GetRawJsonTokenSpan` (and `EscapedNameSpan`), `TryGetField("id")` -> `GetRawJsonTokenSpan`; dispose everything. For the batch: `GetArray`, iterate, `GetObject` per element, same walk |
| simdjson: Parse(span) + enumerate | same but the three keys are found by enumerating the object's members (`foreach JsonProperty`), which is how the jsmn reader locates them |
| simdjson: copy+ParseInPlace + GetField x3 | what the processor would do when the transport buffer has no slack: `Buffer.BlockCopy` into a padded pooled buffer, `ParseInPlace(padded, len)`, same walk |
| simdjson: ParseInPlace(prepadded) + GetField x3 | the buffer already has 64 bytes of slack (an over-allocated transport buffer): no copy, same walk |
| P/Invoke floor | one trivial call through the binding (`SimdJsonParser.RequiredPadding` -> `SimdJsonNative_GetPadding`) |

All walkers were cross-checked on every input (method, id, param count agree) before timing.

## Results (ns per document, single thread)

| input | parser | ns/op (min) | ns/op (median) | B/op |
|---|---|---:|---:|---:|
| (any) | P/Invoke floor: SimdJsonParser.RequiredPadding | 6.0 | 6.3 | 0 |
| add [1,2] | jsmn: JsmnTokenizer.Parse | 75.5 | 77.0 | 0 |
| add [1,2] | jsmn: JsmnRequestReader parse+locate | 112.8 | 122.5 | 0 |
| add [1,2] | STJ: Utf8JsonReader full walk | 100.9 | 106.6 | 0 |
| add [1,2] | simdjson: Parse(span) only, no walk | 154.0 | 156.2 | 64 |
| add [1,2] | simdjson: Parse(span) + GetField x3 | 679.3 | 690.1 | 392 |
| add [1,2] | simdjson: Parse(span) + enumerate | 734.3 | 749.1 | 576 |
| add [1,2] | simdjson: copy+ParseInPlace + GetField x3 | 711.3 | 740.8 | 392 |
| add [1,2] | simdjson: ParseInPlace(prepadded) + GetField x3 | 742.6 | 752.4 | 392 |
| NullableFloat [1.23] | jsmn: JsmnTokenizer.Parse | 87.4 | 90.3 | 0 |
| NullableFloat [1.23] | jsmn: JsmnRequestReader parse+locate | 115.9 | 127.4 | 0 |
| NullableFloat [1.23] | STJ: Utf8JsonReader full walk | 90.0 | 91.7 | 0 |
| NullableFloat [1.23] | simdjson: Parse(span) only, no walk | 145.7 | 146.3 | 64 |
| NullableFloat [1.23] | simdjson: Parse(span) + GetField x3 | 633.6 | 644.7 | 352 |
| NullableFloat [1.23] | simdjson: Parse(span) + enumerate | 686.1 | 723.6 | 536 |
| NullableFloat [1.23] | simdjson: copy+ParseInPlace + GetField x3 | 660.8 | 668.3 | 352 |
| NullableFloat [1.23] | simdjson: ParseInPlace(prepadded) + GetField x3 | 626.5 | 645.8 | 352 |
| StringMe ["Foo"] | jsmn: JsmnTokenizer.Parse | 86.8 | 88.7 | 0 |
| StringMe ["Foo"] | jsmn: JsmnRequestReader parse+locate | 110.9 | 111.7 | 0 |
| StringMe ["Foo"] | STJ: Utf8JsonReader full walk | 86.3 | 89.8 | 0 |
| StringMe ["Foo"] | simdjson: Parse(span) only, no walk | 145.9 | 161.6 | 64 |
| StringMe ["Foo"] | simdjson: Parse(span) + GetField x3 | 625.3 | 635.2 | 352 |
| StringMe ["Foo"] | simdjson: Parse(span) + enumerate | 671.9 | 677.3 | 536 |
| StringMe ["Foo"] | simdjson: copy+ParseInPlace + GetField x3 | 623.1 | 638.6 | 352 |
| StringMe ["Foo"] | simdjson: ParseInPlace(prepadded) + GetField x3 | 627.7 | 656.5 | 352 |
| batch x40 (~2 KB) | jsmn: JsmnTokenizer.Parse | 3016.5 | 3060.5 | 0 |
| batch x40 (~2 KB) | jsmn: JsmnRequestReader parse+locate | 4121.8 | 4227.1 | 0 |
| batch x40 (~2 KB) | STJ: Utf8JsonReader full walk | 3307.2 | 3398.2 | 0 |
| batch x40 (~2 KB) | simdjson: Parse(span) only, no walk | 459.0 | 462.6 | 64 |
| batch x40 (~2 KB) | simdjson: Parse(span) + GetField x3 | 22539.6 | 23146.3 | 13832 |
| batch x40 (~2 KB) | simdjson: Parse(span) + enumerate | 24371.6 | 27758.4 | 21192 |
| batch x40 (~2 KB) | simdjson: copy+ParseInPlace + GetField x3 | 22020.0 | 24049.3 | 13832 |
| batch x40 (~2 KB) | simdjson: ParseInPlace(prepadded) + GetField x3 | 21869.6 | 22460.2 | 13832 |
| object params x30 (~2 KB) | jsmn: JsmnTokenizer.Parse | 913.4 | 928.6 | 0 |
| object params x30 (~2 KB) | jsmn: JsmnRequestReader parse+locate | 1145.1 | 1160.5 | 0 |
| object params x30 (~2 KB) | STJ: Utf8JsonReader full walk | 742.6 | 753.3 | 0 |
| object params x30 (~2 KB) | simdjson: Parse(span) only, no walk | 278.7 | 288.9 | 64 |
| object params x30 (~2 KB) | simdjson: Parse(span) + GetField x3 | 3819.5 | 3905.8 | 2736 |
| object params x30 (~2 KB) | simdjson: Parse(span) + enumerate | 3820.8 | 3930.4 | 2920 |
| object params x30 (~2 KB) | simdjson: copy+ParseInPlace + GetField x3 | 3757.1 | 3808.4 | 2736 |
| object params x30 (~2 KB) | simdjson: ParseInPlace(prepadded) + GetField x3 | 3732.8 | 3755.1 | 2736 |
| notification (no id) | jsmn: JsmnTokenizer.Parse | 62.8 | 65.8 | 0 |
| notification (no id) | jsmn: JsmnRequestReader parse+locate | 90.2 | 91.1 | 0 |
| notification (no id) | STJ: Utf8JsonReader full walk | 82.6 | 85.4 | 0 |
| notification (no id) | simdjson: Parse(span) only, no walk | 146.2 | 154.1 | 64 |
| notification (no id) | simdjson: Parse(span) + GetField x3 | 2705.0 | 2767.2 | 824 |
| notification (no id) | simdjson: Parse(span) + enumerate | 634.7 | 639.9 | 504 |
| notification (no id) | simdjson: copy+ParseInPlace + GetField x3 | 2669.7 | 2701.7 | 824 |
| notification (no id) | simdjson: ParseInPlace(prepadded) + GetField x3 | 2704.0 | 2833.1 | 824 |

An earlier full run (before the parse-only and P/Invoke-floor rows were added) produced the same numbers
within noise; the min column is the stable one, medians on the first input group are occasionally
perturbed by the OS.

## Reading the numbers

- **Small requests (the workload):** the whole jsmn envelope read is 111-128 ns and allocation-free.
  simdjson's parse *alone* (stage 1 + native copy + document handle, no values read) is 146-162 ns, i.e.
  already slower than everything jsmn does, and the minimum envelope walk brings it to 625-750 ns with
  352-392 B of managed garbage per request. That is 5x-6x the jsmn reader and 2.3x the *entire* current
  pipeline (~273 ns/request).
- **The 64-byte padding copy is not the problem.** `Parse(span)` (native-side copy), `copy+ParseInPlace`
  (managed-side copy into a padded pooled buffer) and `ParseInPlace(prepadded)` (no copy) are all within
  noise of each other (about 620-750 ns). Removing the copy by over-allocating transport buffers would
  save at most ~10-30 ns; the cost is in the handle-per-node interop model, not in the memcpy.
- **~2 KB documents:** simdjson's structural index really is fast (459 ns for the 1990-byte batch vs
  3.0 us for jsmn's full tokenization), but the walk needed to get method/params/id out of it costs
  ~550 ns per request (22 us for 40 requests vs 4.1 us for the jsmn reader). For the 30-member object
  params: 3.7 us vs 1.15 us (jsmn) / 0.74 us (Utf8JsonReader).
- **Notifications:** the binding reports a missing field by throwing `SimdJsonException`
  (`TryGetField` is `try { GetField } catch`), so every notification costs a thrown-and-caught exception:
  2.7 us and 824 B. Locating keys by enumeration avoids the throw but allocates a `string` per member
  name (the enumerator always materialises the key).
- **Utf8JsonReader** (for reference) is on par with the jsmn tokenizer on small requests and faster on
  the 30-member object; both are far ahead of the binding.

## Interop and slicing findings (SimdJson.Net 4.6.11.1)

- **Object model:** every `JsonDocument`, `JsonValue`, `JsonArray`, `JsonObject` and each iterator is a
  managed class wrapping a native handle allocated by the shim (`SimdJsonNative_Create*`/`Destroy*`).
  One P/Invoke to obtain each node, one to destroy it; no struct/ref-struct API and no way to reuse a
  handle. `[LibraryImport]` with `Cdecl`, no `SuppressGCTransition`; the measured floor per call is
  ~6 ns, the real calls cost more because each performs a native heap allocation or a lazy parse step.
- **P/Invoke count for one parse + minimum walk of `{"method":"add","params":[1,2],"id":1}`: 25**
  (`Parse`, `DocumentGetType`, `DocumentGetObject`, 3x `ObjectGetFieldByKey`, `ValueGetString`,
  `ValueGetType`, `ValueGetArray`, `ArrayBegin`, 3x `ArrayIterNext`, 2x `ValueRawJsonToken` for the
  elements, `ValueRawJsonToken` for the id, `DestroyArrayIter`, `DestroyArray`, 6x `DestroyValue`,
  `DestroyObject`, `DestroyDocument`). General formula for a single request with n array params:
  19 + 3n; for a batch element add `ValueGetObject` + `DestroyValue` + the per-element `ArrayIterNext`.
  The 40-request batch is ~890 P/Invokes and ~330 managed objects (13.8 KB).
- **Slices of the request bytes (the `JsonRpcRequestReader` contract: `MethodUtf8`, `IdRaw`,
  `ParamRaw(i)` are spans of the document):**
  - With `Parse(span)` the shim copies the input into a native padded buffer, so `GetRawJsonTokenSpan`
    / `GetRawJsonStringSpan` / `EscapedNameSpan` return spans over **native memory**, not the request.
    They are valid only until the document is disposed and cannot be turned into a `ReadOnlyMemory<byte>`
    of the document without another copy.
  - With `ParseInPlace(ReadOnlyMemory<byte>, len)` the shim reads the caller's pinned buffer directly, so
    raw-token spans do point into the request bytes (pointer arithmetic against the pinned base recovers
    the offset). This requires `RequiredPadding` = 64 readable bytes after the JSON in *every* input
    buffer, which the `Process(sessionId, ReadOnlyMemory<byte>, ...)` entry point cannot guarantee; the
    processor would have to copy into an over-allocated pooled buffer on that path (the `ReadOnlySequence`
    / span / string entry points already copy into `Scratch.Input`, which could simply be rented 64 bytes
    larger).
  - `GetStringSpan` (unescaped) points into the parser's own string buffer (native, overwritten on the next
    parse), never into the input. `MethodUtf8` wants the escaped bytes, which `GetRawJsonStringSpan`
    provides.
  - Raw JSON of a **structured** param (`ParamRaw(i)` for an object/array parameter) needs
    `GetArray()`/`GetObject()` + `GetRawJsonSpan()` + dispose (3 extra P/Invokes and one more object), and
    it consumes the On-Demand iterator (forward-only), so params must be walked strictly in order.
- **Lifetime model:** one parser per thread (`SimdJsonParser.Shared` is `[ThreadStatic]`), one live
  document per parser, every node must be disposed (or the native handle leaks). This maps onto the
  per-thread `Scratch`/reader in `JsonRpcProcessor`, but the reader's `Release()` would have to dispose
  a tree of handles rather than reset an int.
- **Lenient mode:** simdjson is strict RFC 8259 only; the Json.NET-style lenient envelope reading
  (`JsonRpcSerializer.Lenient`) could not be served by it.
- **Packaging:** net8.0+ only; native binaries for win/linux/linux-musl/osx x64+arm64 are included and
  load via `NativeLibrary` from `runtimes/<rid>/native` (works out of the box with `dotnet run` on
  win-x64). No x86, no netstandard.

## Recommendation

**Do not adopt** SimdJson.Net (or any current simdjson binding) as a fourth serializer.

- On the library's workload it is 5x-6x slower than the built-in jsmn envelope read and would more than
  double the whole per-request pipeline cost (~273 ns -> ~800 ns), while turning an allocation-free
  path into 350-400 B of garbage per request (2.7 us and 824 B for notifications).
- The 64-byte padding is a real integration cost (a copy on the `ReadOnlyMemory<byte>` fast path unless
  callers over-allocate), but it is not what makes it slow: the three parse variants are within noise.
  The cost is structural in the binding: one native handle and one P/Invoke per JSON node, exceptions
  for missing fields, and string materialisation of member names during enumeration.
- There is no document size at which this binding wins on this workload: at ~2 KB it is still 3.3x-5.3x
  slower than the jsmn reader. The only thing simdjson wins is the structural-index pass on multi-KB
  input (459 ns vs 3.0 us for the 2 KB batch). Capturing that would require writing a custom native
  shim that performs the whole envelope walk in one call and returns offsets (not this package), and
  even then the parse-only floor (~150 ns) exceeds the full jsmn read for the single small requests the
  server is tuned for. If very large batches (tens of KB) ever become the dominant traffic that could be
  revisited as a custom-native-shim project, not as a NuGet dependency.
