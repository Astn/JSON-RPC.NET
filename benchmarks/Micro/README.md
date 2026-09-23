# Micro-benchmarks

BenchmarkDotNet timings of one request on one core, per request shape, with an allocation column. Use these to judge a change to the dispatch path; use `TestServer_Console --sync` for throughput under load and the README tables.

```bash
dotnet run -c Release --project benchmarks/Micro -- --filter '*'
```

Useful arguments: `--filter '*Dispatch*'` for one class, `--job short` for a quick look (3 warm-up and 3 measured iterations), `--disasm` to print the JIT disassembly of each benchmark.

Rows:

- `DispatchBenchmarks`: the five shapes of the console harness (`add`, `addInt`, nullable float, decimal, string), a batch of the five, and a notification, through `JsonRpcProcessor.Process` with the built-in serializer and a class registered with `[JsonRpcMethod]`.
- `InterfaceBindingBenchmarks`: the same five shapes through a contract registered with `ServiceBinder.BindInterface`, plus one and two levels of interface-typed properties (`Calc.addInt`, `Admin.Calc.addInt`). Interface rows should match the class rows of `DispatchBenchmarks`; the tree rows pay only for the longer method name.
- `BindingComparisonBenchmarks`: `addInt`, decimal and string through the same class registered with `[JsonRpcMethod]` and through `BindInterface`, in one process, so a drift of the machine between runs cannot masquerade as a binding cost.
- `AsyncDispatchBenchmarks`: a synchronous method through `Process` and `ProcessAsync`, `Task<int>` and `ValueTask<int>` methods that complete inline, and a method that yields once, each with the default `RpcContextFlow.None` and with `RpcContextFlow.Flow`. The inline default rows should allocate nothing; the `Flow` rows pay for the execution-context bridge; the yielding rows show the cost of a real suspension.

Read the `Allocated` column first: a non-zero value on a numeric shape means the request touched the GC, which the fast path must not do. Then compare `Mean` between two builds run under the same load; on a busy machine the ratio is meaningful, the absolute number is not.
