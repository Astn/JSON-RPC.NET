using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;

namespace TestServer_Console;

public class BenchmarkRunner
{
    private static readonly StringBuilder sb = new StringBuilder(4096);

    // Serialises every render of the Task benchmark. The progress timer fires on pool threads every 250 ms
    // whether or not the previous render (a console clear-and-redraw) has finished, and the main thread
    // renders the final box itself; without this they interleave on `sb` and on the console cursor.
    private static readonly object renderLock = new object();

    private static int completed;            // RPCs finished in the current iteration (Interlocked / Volatile)
    private static int currentBatches;       // batches finished in the current iteration
    private static volatile bool benchmarkRunning;
    private static System.Threading.Timer updateTimer;

    private static int currentIteration;
    private static int currentBatchSize;
    private static Stopwatch currentStopwatch;
    private static long currentPerTaskBytesIn;
    private static long currentPerTaskBytesOut;
    private static Action<string> _print = Console.WriteLine;
    internal static readonly string[] taskInputs =
    [
        "{\"method\":\"add\",\"params\":[1,2],\"id\":1}",
        "{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}",
        "{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3}",
        "{\"method\":\"Test2\",\"params\":[3.456],\"id\":4}",
        "{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}"
    ];

    /// <summary>
    /// Direct synchronous throughput: N threads each call the byte-in/byte-out processor in a tight loop for
    /// <paramref name="seconds"/>. This measures the library itself (parse, bind, invoke, serialize) with no
    /// Task scheduling in the way, which is the number the 2.0 design is tuned for.
    /// </summary>
    internal static void BenchmarkSync(Action<string> print = null, JsonRpcSerializer serializer = null, int threads = 0, double seconds = 3)
    {
        _print = print ??= Console.WriteLine;
        serializer ??= Config.Serializer;
        if (threads <= 0) threads = Environment.ProcessorCount;

        var session = Handler.DefaultSessionId();
        var inputs = taskInputs.Select(t => (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(t)).ToArray();
        long bytesIn = inputs.Sum(i => (long)i.Length);
        long bytesOut = 0;
        foreach (var input in inputs)
        {
            using var w = new PooledByteBufferWriter(256);
            JsonRpcProcessor.Process(session, input, w, null, serializer);
            bytesOut += w.WrittenCount;
        }

        // warm up: JIT, type plans, compiled invokers
        RunSync(session, inputs, serializer, Math.Min(threads, 2), 0.5, out _);

        // allocation profile per request shape (a zero here means the request never touches the GC)
        {
            using var w = new PooledByteBufferWriter(1024);
            sb.Clear();
            sb.Append("Allocated bytes per RPC (").Append(serializer.Name).Append("):\n");
            for (int k = 0; k < inputs.Length; k++)
            {
                const int reps = 10000;
                for (int r = 0; r < 100; r++) { w.Clear(); JsonRpcProcessor.Process(session, inputs[k], w, null, serializer); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int r = 0; r < reps; r++) { w.Clear(); JsonRpcProcessor.Process(session, inputs[k], w, null, serializer); }
                long per = (GC.GetAllocatedBytesForCurrentThread() - before) / reps;
                w.Clear(); JsonRpcProcessor.Process(session, inputs[k], w, null, serializer);
                sb.Append("  ").Append(per.ToString().PadLeft(6)).Append(" B  ").Append(taskInputs[k]).Append("  ->  ").Append(w.ToString()).Append('\n');
            }
            _print(sb.ToString());
        }

        // Sweep 1, 2, 4, ... up to the requested thread count (the count itself is always included).
        var threadCounts = new List<int>();
        for (int t = 1; t < threads; t *= 2) threadCounts.Add(t);
        threadCounts.Add(threads);
        var rows = new List<ChartRow>(threadCounts.Count);

        foreach (var t in threadCounts)
        {
            var elapsed = RunSync(session, inputs, serializer, t, seconds, out long total);
            double rps = total / elapsed;
            double mbIn = rps * bytesIn / inputs.Length / (1024.0 * 1024.0);
            double mbOut = rps * bytesOut / inputs.Length / (1024.0 * 1024.0);
            rows.Add(new ChartRow($"{t} thread{(t == 1 ? "" : "s")}", rps, $"{1e9 / rps * t:N0} ns/RPC per thread"));

            // The detailed boxes are only printed for the two documented points: one thread and all threads.
            if (t != 1 && t != threads) continue;

            sb.Clear();
            const string Reset = "\u001b[0m";
            const string Cyan = "\u001b[36m";
            const string Blue = "\u001b[34m";
            const string Green = "\u001b[32m";
            int boxWidth = Console.IsOutputRedirected ? 100 : Math.Max(60, Console.WindowWidth);
            string header = $"Sync benchmark - {serializer.Name} - {t} thread{(t == 1 ? "" : "s")}";
            sb.Append(Cyan).Append("┌── ").Append(header).Append(" ─")
                .Append(new string('─', Math.Max(0, boxWidth - header.Length - 6))).Append(Reset).Append('\n');
            AppendField(sb, "Total RPCs", $"{total:N0}", Blue);
            AppendField(sb, "Elapsed", $"{elapsed:F3} s", Blue);
            AppendField(sb, "RPC/s", $"{rps:N0}", Green);
            AppendField(sb, "ns / RPC", $"{1e9 / rps * t:N0} (per thread)", Green);
            AppendField(sb, "In MBps", $"{mbIn:F2}", Green);
            AppendField(sb, "Out MBps", $"{mbOut:F2}", Green);
            sb.Append(Cyan).Append("└").Append(new string('─', boxWidth - 2)).Append(Reset).Append('\n');
            _print(sb.ToString());
        }

        PrintBarChart($"Sync benchmark - {serializer.Name} - RPC/s by thread count", "Threads", rows);
    }

    internal static double RunSync(string session, ReadOnlyMemory<byte>[] inputs, JsonRpcSerializer serializer, int threads, double seconds, out long total)
    {
        var counts = new long[threads * 16]; // padded to avoid false sharing
        var stop = false;
        var ready = new Barrier(threads + 1);
        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int slot = t * 16;
            workers[t] = new Thread(() =>
            {
                var output = new PooledByteBufferWriter(1024);
                ready.SignalAndWait();
                long n = 0;
                int i = 0;
                while (!Volatile.Read(ref stop))
                {
                    output.Clear();
                    JsonRpcProcessor.Process(session, inputs[i], output, null, serializer);
                    if (++i == inputs.Length) i = 0;
                    n++;
                }
                counts[slot] = n;
            }) { IsBackground = true };
            workers[t].Start();
        }
        ready.SignalAndWait();
        var sw = Stopwatch.StartNew();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        Volatile.Write(ref stop, true);
        foreach (var w in workers) w.Join();
        sw.Stop();
        total = 0;
        for (int t = 0; t < threads; t++) total += counts[t * 16];
        return sw.Elapsed.TotalSeconds;
    }

    internal static void Benchmark(Action<string> print = null)
    {
        // get current console position
        _print = print ??= Console.WriteLine;

        long batchInputSize = taskInputs.Sum(t => Encoding.Default.GetByteCount(t));
        long batchResultSize =
            taskInputs.Sum(input => Encoding.Default.GetByteCount(JsonRpcProcessor.Process(input).Result));

        long perTaskBytesIn = batchInputSize / 5;
        long perTaskBytesOut = batchResultSize / 5;

        var iterations = 8;
        var cnt = 50;
        var results = new List<ChartRow>(iterations);
        var minIterationTime = TimeSpan.FromMilliseconds(500);

        // Warm up on the exact path the iterations use, for long enough that tiered compilation has replaced
        // the tier-0 code with optimised (PGO-instrumented) code. Without this the small early iterations
        // finish in microseconds and measure JIT and tier-0 code, so their RPC/s jump around by 2x per run.
        var warm = Stopwatch.StartNew();
        while (warm.Elapsed < TimeSpan.FromSeconds(1)) RunBatch(5000);

        for (int iteration = 1; iteration <= iterations; iteration++)
        {
            cnt *= iteration;
            completed = 0;
            currentBatches = 0;
            benchmarkRunning = true;

            currentIteration = iteration;
            currentBatchSize = cnt;
            currentPerTaskBytesIn = perTaskBytesIn;
            currentPerTaskBytesOut = perTaskBytesOut;
            currentStopwatch = Stopwatch.StartNew();
            // Live updates every 250 ms. StopProgressTimer() below guarantees no tick is still running here.
            updateTimer = new System.Threading.Timer(OnProgressTick, null, 250, 250);

            // A batch of 50 completes in microseconds, well under the stopwatch's and the thread pool's noise
            // floor. Repeat the batch until the iteration has run for at least minIterationTime so every row
            // of the chart is a real measurement; RPC/s is total requests over total time.
            do
            {
                RunBatch(cnt);
                Interlocked.Increment(ref currentBatches);
            } while (currentStopwatch.Elapsed < minIterationTime);
            currentStopwatch.Stop();

            // Wait for any in-flight tick before printing the final box, then close the iteration under the
            // lock so a tick that was queued but not yet started sees benchmarkRunning == false and skips.
            StopProgressTimer();
            lock (renderLock)
            {
                PrintBenchmarkProgress();
                benchmarkRunning = false;
            }

            double seconds = currentStopwatch.Elapsed.TotalSeconds;
            long total = (long)cnt * currentBatches;
            results.Add(new ChartRow($"#{iteration} {cnt,11:N0}", total / seconds, $"{total,11:N0} RPCs in {seconds:F3} s"));
        }

        lock (renderLock)
        {
            PrintBarChart("Benchmark Summary - RPC/s by batch size", "  #  Batch size", results);
        }
    }

    /// <summary>
    /// Submits <paramref name="count"/> requests through the Task-returning overload from all cores and waits
    /// for the last one to finish. Nothing is retained: the old harness kept every Task and its result string
    /// in an array until the batch ended, which for the two-million batch meant hundreds of megabytes of live
    /// objects being promoted through the GC generations, and the big batches came out slower than the small
    /// ones for that reason alone.
    /// </summary>
    private static void RunBatch(int count)
    {
        int pending = count;
        using var done = new ManualResetEventSlim(false);
        Action<Task<string>> onDone = _ =>
        {
            Interlocked.Increment(ref completed);
            if (Interlocked.Decrement(ref pending) == 0) done.Set();
        };

        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
        {
            JsonRpcProcessor.Process(Handler.DefaultSessionId(), taskInputs[i % 5])
                .ContinueWith(onDone, TaskContinuationOptions.ExecuteSynchronously);
        });

        done.Wait();
    }

    /// <summary>One bar of a chart: a left label, the RPC/s the bar is proportional to, and trailing text.</summary>
    internal readonly record struct ChartRow(string Label, double RpcPerSec, string Trailing);

    /// <summary>
    /// A horizontal bar per row, scaled to the fastest row, with the RPC/s figure and the row's trailing text
    /// (elapsed time, ns per request, ...) on the right. Fits the console width; plain text when redirected.
    /// </summary>
    internal static void PrintBarChart(string header, string labelHeader, List<ChartRow> rows)
    {
        if (rows.Count == 0) return;

        const string Reset = "\u001b[0m";
        const string Cyan = "\u001b[36m";
        const string Blue = "\u001b[34m";
        const string Green = "\u001b[32m";
        const string Dim = "\u001b[2m";

        int boxWidth = Console.IsOutputRedirected ? 100 : Math.Max(60, Console.WindowWidth);
        double max = rows.Max(r => r.RpcPerSec);
        int labelWidth = Math.Max(labelHeader.Length, rows.Max(r => r.Label.Length));
        int trailingWidth = rows.Max(r => r.Trailing.Length);

        // "│ " + label + " │" + bar + "│ " + rps(10) + "  " + trailing
        int fixedWidth = 2 + labelWidth + 2 + 2 + 10 + 2 + trailingWidth;
        int barWidth = Math.Max(10, boxWidth - fixedWidth);

        sb.Clear();
        sb.Append(Cyan).Append("┌── ").Append(header).Append(" ─")
            .Append(new string('─', Math.Max(0, boxWidth - header.Length - 6))).Append(Reset).Append('\n');
        sb.Append("│ ").Append(Blue).Append(labelHeader.PadRight(labelWidth)).Append(Reset).Append(" │")
            .Append(Dim).Append(Truncate("RPC/s, scaled to the fastest row", barWidth).PadRight(barWidth)).Append(Reset)
            .Append("│ ").Append(Blue).Append("RPC/s".PadLeft(10)).Append(Reset).Append('\n');

        foreach (var r in rows)
        {
            int filled = max > 0 ? (int)Math.Round(barWidth * r.RpcPerSec / max) : 0;
            sb.Append("│ ").Append(Blue).Append(r.Label.PadRight(labelWidth)).Append(Reset).Append(" │")
                .Append(Green).Append(new string('█', filled)).Append(Reset)
                .Append(Dim).Append(new string('░', barWidth - filled)).Append(Reset)
                .Append("│ ").Append(r.RpcPerSec.ToString("N0").PadLeft(10))
                .Append(Dim).Append("  ").Append(r.Trailing).Append(Reset).Append('\n');
        }

        sb.Append(Cyan).Append("└").Append(new string('─', boxWidth - 2)).Append(Reset).Append('\n');
        _print(sb.ToString());
    }

    private static string Truncate(string s, int width) => s.Length <= width ? s : s.Substring(0, width);

    private static void OnProgressTick(object _)
    {
        // If the previous render is still on screen, drop this tick instead of queueing behind it: the
        // console redraw can take longer than the timer period, and stacked ticks would only garble output.
        if (!Monitor.TryEnter(renderLock)) return;
        try
        {
            if (benchmarkRunning) PrintBenchmarkProgress();
        }
        finally
        {
            Monitor.Exit(renderLock);
        }
    }

    private static void StopProgressTimer()
    {
        var timer = updateTimer;
        updateTimer = null;
        if (timer == null) return;
        using var drained = new ManualResetEvent(false);
        // Dispose(WaitHandle) signals once every callback that has started has completed.
        if (timer.Dispose(drained)) drained.WaitOne();
    }

    private static void PrintBenchmarkProgress()
    {
        int comp = Volatile.Read(ref completed);
        int batches = Volatile.Read(ref currentBatches);

        double elapsedSec = currentStopwatch.Elapsed.TotalSeconds;
        double rpcPerSec = elapsedSec > 0 ? comp * 1000.0 / currentStopwatch.ElapsedMilliseconds : 0;

        long bytesInProcessed = (long)comp * currentPerTaskBytesIn;
        long bytesOutProcessed = (long)comp * currentPerTaskBytesOut;

        double mbpsIn = elapsedSec > 0 ? bytesInProcessed / (1024.0 * 1024.0) / elapsedSec : 0;
        double mbpsOut = elapsedSec > 0 ? bytesOutProcessed / (1024.0 * 1024.0) / elapsedSec : 0;
        double mbpsTotal = mbpsIn + mbpsOut;

        sb.Clear();

        const string Reset = "\u001b[0m";
        const string Cyan = "\u001b[36m";
        const string Blue = "\u001b[34m";
        const string Green = "\u001b[32m";

        int boxWidth = Console.IsOutputRedirected ? 100 : Math.Max(60, Console.WindowWidth);

        string header = $"Benchmark Progress - Iteration {currentIteration}";
        sb.Append(Cyan).Append("┌── ").Append(header).Append(" ─")
            .Append(new string('─', boxWidth - header.Length - 6)).Append(Reset).Append('\n');

        AppendField(sb, "Batch size", $"{currentBatchSize:N0}", Blue);
        AppendField(sb, "Total RPCs", $"{comp:N0}  ({batches:N0} batches done)", Blue);
        AppendField(sb, "Elapsed", $"{elapsedSec:F3} s", Blue);
        AppendField(sb, "RPC/s", $"{rpcPerSec:N0}", Green);
        AppendField(sb, "In MBps", $"{mbpsIn:F2}", Green);
        AppendField(sb, "Out MBps", $"{mbpsOut:F2}", Green);
        AppendField(sb, "Total MBps", $"{mbpsTotal:F2}", Green);

        sb.Append(Cyan).Append("└").Append(new string('─', boxWidth - 2)).Append(Reset).Append('\n');

        _print(sb.ToString());
    }

    private static void AppendField(StringBuilder sb, string label, string value, string color)
    {
        const string Reset = "\u001b[0m";
        sb.Append("│ ").Append(color).Append(label.PadRight(12)).Append(Reset).Append(": ").Append(value)
            .Append('\n');
    }

}