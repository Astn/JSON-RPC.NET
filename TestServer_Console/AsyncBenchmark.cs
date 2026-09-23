using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;

namespace TestServer_Console;

internal static class AsyncBenchmark
{
    internal static async Task RunAsync(Action<string> print, double seconds, int workers)
    {
        if (seconds <= 0 || workers <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var inputs = BenchmarkRunner.taskInputs.Select(t => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(t)).ToArray();
        var expected = BenchmarkRunner.taskInputs.Select(t => JsonRpcProcessor.ProcessSync(t)).ToArray();
        foreach (var shape in new[] { "sync", "Task", "ValueTask", "yield" })
        foreach (var flow in shape == "sync" ? new[] { RpcContextFlow.None } : new[] { RpcContextFlow.Flow, RpcContextFlow.None })
        {
            string session = "async-benchmark-" + shape + "-" + flow;
            try
            {
                Register(session, shape, flow);
                var rowInputs = shape == "yield" ? new[] { (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes("{\"method\":\"yieldsOnce\",\"id\":6}") } : inputs;
                var rowExpected = shape == "yield" ? new[] { "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":6}" } : expected;
                using (var output = new PooledByteBufferWriter())
                {
                    for (int i = 0; i < rowInputs.Length; i++)
                    {
                        output.Clear();
                        await JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output);
                        if (output.ToString() != rowExpected[i]) throw new InvalidOperationException("Unexpected response: " + output);
                        for (int warm = 0; warm < 500; warm++) { output.Clear(); await JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output); }
                        if (shape != "yield")
                        {
                            long before = GC.GetAllocatedBytesForCurrentThread();
                            for (int n = 0; n < 2000; n++)
                            {
                                output.Clear();
                                var task = JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output);
                                if (!task.IsCompleted) throw new InvalidOperationException("Expected inline completion.");
                                task.GetAwaiter().GetResult();
                            }
                            long totalBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                            print($"{shape} / {flow} / shape {i + 1}: {totalBytes} B total over 2000 requests ({totalBytes / 2000.0:F1} B/RPC), including method allocations.");
                        }
                    }
                }
                var start = Stopwatch.GetTimestamp();
                long beforeProcess = GC.GetTotalAllocatedBytes(true);
                var counts = await Task.WhenAll(Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
                {
                    long count = 0;
                    using var output = new PooledByteBufferWriter();
                    while ((Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency < seconds)
                    {
                        output.Clear();
                        await JsonRpcProcessor.ProcessAsync(session, rowInputs[count % rowInputs.Length], output).ConfigureAwait(false);
                        count++;
                    }
                    return count;
                })));
                double elapsed = (Stopwatch.GetTimestamp() - start) / (double)Stopwatch.Frequency;
                long allocations = GC.GetTotalAllocatedBytes(true) - beforeProcess;
                print($"{shape} / {flow}: {counts.Sum():N0} RPCs, {workers} workers, {elapsed:F3} s, {counts.Sum() / elapsed:N0} RPC/s; process allocation total {allocations:N0} B.");
            }
            finally { Handler.DestroySession(session); }
        }
    }

    private static void Register(string session, string shape, RpcContextFlow flow)
    {
        void Bind(string name, Delegate method) => ServiceBinder.BindMethod(session, name, method, contextFlow: flow);
        if (shape == "yield") { Bind("yieldsOnce", new Func<Task<int>>(YieldOnce)); return; }
        if (shape == "sync")
        {
            Bind("add", new Func<double, double, double>((l, r) => l + r));
            Bind("addInt", new Func<int, int, int>((l, r) => l + r));
            Bind("NullableFloatToNullableFloat", new Func<float?, float?>(a => a));
            Bind("Test2", new Func<decimal, decimal?>(x => x));
            Bind("StringMe", new Func<string, string>(x => x));
        }
        else if (shape == "Task")
        {
            Bind("add", new Func<double, double, Task<double>>((l, r) => Task.FromResult(l + r)));
            Bind("addInt", new Func<int, int, Task<int>>((l, r) => Task.FromResult(l + r)));
            Bind("NullableFloatToNullableFloat", new Func<float?, Task<float?>>(a => Task.FromResult(a)));
            Bind("Test2", new Func<decimal, Task<decimal?>>(x => Task.FromResult<decimal?>(x)));
            Bind("StringMe", new Func<string, Task<string>>(x => Task.FromResult(x)));
        }
        else
        {
            Bind("add", new Func<double, double, ValueTask<double>>((l, r) => new ValueTask<double>(l + r)));
            Bind("addInt", new Func<int, int, ValueTask<int>>((l, r) => new ValueTask<int>(l + r)));
            Bind("NullableFloatToNullableFloat", new Func<float?, ValueTask<float?>>(a => new ValueTask<float?>(a)));
            Bind("Test2", new Func<decimal, ValueTask<decimal?>>(x => new ValueTask<decimal?>(x)));
            Bind("StringMe", new Func<string, ValueTask<string>>(x => new ValueTask<string>(x)));
        }
    }

    private static async Task<int> YieldOnce() { await Task.Yield(); return 7; }
}
