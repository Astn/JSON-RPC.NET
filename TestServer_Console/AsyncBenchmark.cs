using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Newtonsoft;
using AustinHarris.JsonRpc.Serialization;
using AustinHarris.JsonRpc.SystemTextJson;

namespace TestServer_Console;

/// <summary>
/// <c>ProcessAsync</c> under load: the same five requests as <c>--sync</c> through the byte entry point, driven by
/// awaited workers on the thread pool. Each row registers the five methods with one return shape (synchronous,
/// completed <c>Task&lt;T&gt;</c>, inline <c>ValueTask&lt;T&gt;</c>) and one <see cref="RpcContextFlow"/>; the
/// <c>yield</c> row is one method that awaits <c>Task.Yield()</c>, a real suspension per request. The timing loop
/// has the shape of the synchronous harness (workers start behind a barrier, stop on a shared flag, walk the inputs
/// with an index) so that a difference between the two modes is a difference between the two entry points.
/// </summary>
internal static class AsyncBenchmark
{
    internal static readonly (string shape, RpcContextFlow flow)[] Rows =
    {
        ("sync", RpcContextFlow.None),
        ("Task", RpcContextFlow.Flow), ("Task", RpcContextFlow.None),
        ("ValueTask", RpcContextFlow.Flow), ("ValueTask", RpcContextFlow.None),
        ("yield", RpcContextFlow.Flow), ("yield", RpcContextFlow.None),
    };

    /// <summary>The inline rows a process-wide serialization point shows up in first; the scaling gate runs these.</summary>
    internal static readonly (string shape, RpcContextFlow flow)[] InlineRows = { ("sync", RpcContextFlow.None), ("Task", RpcContextFlow.None), ("ValueTask", RpcContextFlow.None) };

    /// <summary>The rows the per-serializer diagnostics after the gate measure: the inline None rows and the <c>yieldsOnce</c> None row.</summary>
    internal static readonly (string shape, RpcContextFlow flow)[] DiagnosticRows = InlineRows.Append(("yield", RpcContextFlow.None)).ToArray();

    internal readonly record struct Measurement(long Count, double Seconds, double StartupSeconds, long AllocatedBytes)
    {
        public double RpcPerSec => Count / Seconds;
        public double BytesPerRpc => Count == 0 ? 0 : AllocatedBytes / (double)Count;
    }

    internal static async Task RunAsync(Action<string> print, double seconds, int workers)
    {
        if (seconds <= 0 || workers <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var summary = new List<BenchmarkRunner.ChartRow>();
        foreach (var (shape, flow) in Rows)
        {
            string session = "async-benchmark-" + shape + "-" + flow;
            try
            {
                Register(session, shape, flow);
                var rowInputs = Inputs(shape);
                await ValidateAndReportAllocations(print, session, shape, flow, rowInputs);
                bool yield = shape == "yield";
                var m = await Measure(session, rowInputs, workers, seconds, processWide: yield);
                string label = yield ? "one request (yieldsOnce)" : "equal-weight mix of five requests";
                print($"{shape} / {flow}: {m.Count:N0} RPCs, {workers} workers, {m.Seconds:F3} s, {m.RpcPerSec:N0} RPC/s; worker startup {m.StartupSeconds * 1000:F1} ms; " +
                      $"{(yield ? "process-wide" : "worker-thread")} allocation total {m.AllocatedBytes:N0} B ({m.BytesPerRpc:F0} B/RPC, including method and harness allocations); {label}.");
                summary.Add(new BenchmarkRunner.ChartRow($"{shape} / {flow}", m.RpcPerSec, $"{m.Count,12:N0} RPCs  {m.BytesPerRpc,6:F0} B/RPC"));
            }
            finally { Handler.DestroySession(session); }
        }
        BenchmarkRunner.PrintBarChart($"ProcessAsync(bytes), awaited workers - {Config.Serializer.Name} - {workers} worker{(workers == 1 ? "" : "s")} - RPC/s by registration", "Registration", summary);
    }

    /// <summary>
    /// The scaling gate: the inline None rows at 1, 2 and <paramref name="workers"/> workers, <paramref name="runs"/>
    /// paired runs, medians, and the 2/1 and N/1 ratios. Returns false when any N/1 ratio is below
    /// <paramref name="threshold"/>. A process-wide serialization point on the async path holds the ratio near 1.3
    /// on any core count; the per-thread cache measured about 6.8 on 16 threads.
    /// </summary>
    internal static async Task<bool> ScaleAsync(Action<string> print, double seconds, int workers, double threshold, int runs = 3)
    {
        if (workers < 2) throw new ArgumentOutOfRangeException(nameof(workers));
        var counts = new[] { 1, 2, workers }.Distinct().ToArray();
        print($"Scaling check: ProcessAsync(bytes), {string.Join(", ", counts)} workers, {runs} paired runs of {seconds:0.#} s per cell, medians; {Environment.ProcessorCount} logical processors; gate {workers}/1 >= {threshold:0.0#}.\n");
        var results = new Dictionary<(string, RpcContextFlow, int), List<double>>();
        foreach (var (shape, flow) in InlineRows)
        {
            string session = "async-scale-" + shape + "-" + flow;
            try
            {
                Register(session, shape, flow);
                var rowInputs = Inputs(shape);
                await ValidateAndReportAllocations(print, session, shape, flow, rowInputs);
                await Measure(session, rowInputs, workers, Math.Min(seconds, 1));   // warm the pool threads and the JIT
                for (int run = 1; run <= runs; run++)
                foreach (int w in counts)
                {
                    var m = await Measure(session, rowInputs, w, seconds);
                    print($"  run {run}: {shape} / {flow}, {w,2} workers: {m.RpcPerSec,14:N0} RPC/s  ({m.Count:N0} RPCs in {m.Seconds:F3} s, startup {m.StartupSeconds * 1000:F1} ms)");
                    if (!results.TryGetValue((shape, flow, w), out var list)) results[(shape, flow, w)] = list = new List<double>();
                    list.Add(m.RpcPerSec);
                }
            }
            finally { Handler.DestroySession(session); }
        }

        bool pass = true;
        print("");
        print($"| Registration | 1 worker | 2 workers | {workers} workers | 2/1 | {workers}/1 | gate |");
        print("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var (shape, flow) in InlineRows)
        {
            double one = Median(results[(shape, flow, 1)]);
            double two = Median(results[(shape, flow, 2)]);
            double many = Median(results[(shape, flow, workers)]);
            bool ok = many / one >= threshold;
            pass &= ok;
            print($"| {shape} / {flow} | {one:N0} | {two:N0} | {many:N0} | {two / one:F2} | {many / one:F2} | {(ok ? "pass" : "FAIL")} |");
        }
        print("");
        print(pass ? $"Scaling check passed: every inline row scales at least {threshold:0.0#}x from 1 to {workers} workers."
                   : $"Scaling check FAILED: an inline row scales less than {threshold:0.0#}x from 1 to {workers} workers; look for a process-wide serialization point on the ProcessAsync path.");
        print("The 2/1 ratio is diagnostic (the lock was already visible at two workers, about 1.3); it is not gated.");
        return pass;
    }

    /// <summary>
    /// The diagnostics printed after the scaling gate, never part of its result: under each serializer (the built-in
    /// jsmn, System.Text.Json, Json.NET, in that order) the inline None rows and the <c>yieldsOnce</c> None row at 1 and
    /// <paramref name="workers"/> workers, one run of <paramref name="seconds"/> per cell, with the N/1 ratio and the bytes
    /// per request at one worker as <see cref="RunAsync"/> reports them, so a regression in one serializer's path or in the
    /// suspending path shows up on its own. The process-wide serializer is switched with
    /// <see cref="Config.SetSerializer(JsonRpcSerializer)"/> for each block and restored afterwards. A failure is printed
    /// after the rows measured so far and is not thrown, so it cannot change the gate's exit code.
    /// </summary>
    internal static async Task ScaleDiagnosticsAsync(Action<string> print, double seconds, int workers)
    {
        var serializers = new Func<JsonRpcSerializer>[] { () => JsmnSerializer.Instance, () => new SystemTextJsonRpcSerializer(), () => new NewtonsoftJsonRpcSerializer() };
        var rows = new List<string>();
        string where = "start";
        Exception failure = null;
        var previous = Config.Serializer;
        print("");
        print($"Diagnostics (not gated): ProcessAsync(bytes) under each serializer, 1 and {workers} workers, one run of {seconds:0.#} s per cell; " +
              "B per request at 1 worker as --async reports it (inline rows: worker-thread counters; yieldsOnce: process-wide).\n");
        try
        {
            for (int k = 0; k < serializers.Length; k++)
            {
                where = $"serializer {k + 1} of {serializers.Length}";
                Config.SetSerializer(serializers[k]());
                string name = Config.Serializer.Name;
                rows.Add($"| **{name}** | | | | |");
                foreach (var (shape, flow) in DiagnosticRows)
                {
                    where = $"{name} / {shape} / {flow}";
                    string session = "async-scale-diag-" + name + "-" + shape + "-" + flow;
                    try
                    {
                        Register(session, shape, flow);
                        var rowInputs = Inputs(shape);
                        await ValidateAndReportAllocations(print, session, shape, flow, rowInputs, report: false);
                        bool yield = shape == "yield";
                        await Measure(session, rowInputs, workers, Math.Min(seconds, 0.5), processWide: yield);   // warm-up
                        var one = await Measure(session, rowInputs, 1, seconds, processWide: yield);
                        print($"  {where}, {1,2} workers: {one.RpcPerSec,14:N0} RPC/s  ({one.Count:N0} RPCs in {one.Seconds:F3} s, {one.BytesPerRpc:F0} B/RPC)");
                        var many = await Measure(session, rowInputs, workers, seconds, processWide: yield);
                        print($"  {where}, {workers,2} workers: {many.RpcPerSec,14:N0} RPC/s  ({many.Count:N0} RPCs in {many.Seconds:F3} s)");
                        rows.Add($"| {shape} / {flow} | {one.RpcPerSec:N0} | {many.RpcPerSec:N0} | {many.RpcPerSec / one.RpcPerSec:F2} | {one.BytesPerRpc:F0} |");
                    }
                    finally { Handler.DestroySession(session); }
                }
            }
        }
        catch (Exception ex) { failure = ex; }
        finally { Config.SetSerializer(previous); }

        print("");
        print($"| Serializer / registration | 1 worker | {workers} workers | {workers}/1 | B per request |");
        print("| --- | ---: | ---: | ---: | ---: |");
        foreach (var row in rows) print(row);
        print("");
        if (failure != null)
            print($"Diagnostics stopped at {where}: {failure.GetType().Name}: {failure.Message}. The gate result above stands; the diagnostics are not part of it.");
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int n = sorted.Length;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    private static ReadOnlyMemory<byte>[] Inputs(string shape) => shape == "yield"
        ? new[] { (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes("{\"method\":\"yieldsOnce\",\"id\":6}") }
        : BenchmarkRunner.taskInputs.Select(t => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(t)).ToArray();

    /// <summary>Checks every response, warms the row, and prints the per-shape allocation of an inline document.</summary>
    /// <param name="report">False checks, warms and asserts inline completion without printing the per-shape lines.</param>
    private static async Task ValidateAndReportAllocations(Action<string> print, string session, string shape, RpcContextFlow flow, ReadOnlyMemory<byte>[] rowInputs, bool report = true)
    {
        var expected = shape == "yield"
            ? new[] { "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":6}" }
            : BenchmarkRunner.taskInputs.Select(t => JsonRpcProcessor.ProcessSync(t)).ToArray();
        using var output = new PooledByteBufferWriter();
        for (int i = 0; i < rowInputs.Length; i++)
        {
            output.Clear();
            await JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output);
            if (output.ToString() != expected[i]) throw new InvalidOperationException("Unexpected response: " + output);
            for (int warm = 0; warm < 500; warm++) { output.Clear(); await JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output); }
            if (shape == "yield") continue;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 2000; n++)
            {
                output.Clear();
                var task = JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output);
                if (!task.IsCompleted) throw new InvalidOperationException("Expected inline completion.");
                task.GetAwaiter().GetResult();
            }
            long totalBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (report) print($"{shape} / {flow} / shape {i + 1}: {totalBytes} B total over 2000 requests ({totalBytes / 2000.0:F1} B/RPC), including method allocations.");
        }
    }

    /// <summary>
    /// One timed cell. Workers are <c>Task.Run</c> tasks that await each call; they start together behind a barrier,
    /// the clock starts once every worker is ready, and they stop on a shared flag. Startup is reported separately.
    /// </summary>
    /// <param name="processWide">Count allocations process-wide (the yield row: its continuations run on other pool threads, so
    /// a worker's thread-local counter is meaningless) instead of summing the worker threads' own counters (exact for inline rows).</param>
    internal static async Task<Measurement> Measure(string session, ReadOnlyMemory<byte>[] rowInputs, int workers, double seconds, bool processWide = false)
    {
        int stop = 0;
        var counts = new long[workers * 16]; // padded to avoid false sharing
        var allocs = new long[workers * 16];
        using var ready = new Barrier(workers + 1);
        var startup = Stopwatch.StartNew();
        var tasks = new Task[workers];
        for (int w = 0; w < workers; w++)
        {
            int slot = w * 16;
            tasks[w] = Task.Run(async () =>
            {
                using var output = new PooledByteBufferWriter();
                ready.SignalAndWait();
                // Per worker thread, exact for inline rows (the process-wide counter under-reports at this rate).
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long n = 0;
                int i = 0;
                while (Volatile.Read(ref stop) == 0)
                {
                    output.Clear();
                    await JsonRpcProcessor.ProcessAsync(session, rowInputs[i], output).ConfigureAwait(false);
                    if (++i == rowInputs.Length) i = 0;
                    n++;
                }
                counts[slot] = n;
                allocs[slot] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            });
        }
        ready.SignalAndWait();
        double startupSeconds = startup.Elapsed.TotalSeconds;
        long before = GC.GetTotalAllocatedBytes(false);
        var sw = Stopwatch.StartNew();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        Volatile.Write(ref stop, 1);
        await Task.WhenAll(tasks);
        sw.Stop();
        long processAllocated = GC.GetTotalAllocatedBytes(false) - before;
        long total = 0, allocatedTotal = 0;
        for (int w = 0; w < workers; w++) { total += counts[w * 16]; allocatedTotal += allocs[w * 16]; }
        return new Measurement(total, sw.Elapsed.TotalSeconds, startupSeconds, processWide ? processAllocated : allocatedTotal);
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
