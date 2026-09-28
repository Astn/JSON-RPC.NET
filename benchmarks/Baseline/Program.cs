using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;

namespace Baseline
{
    // The five benchmark methods, as TestServer_Console/service.cs declares them.
    public class CalculatorService : JsonRpcService
    {
        [JsonRpcMethod] private double add(double l, double r) => l + r;
        [JsonRpcMethod] private int addInt(int l, int r) => l + r;
        [JsonRpcMethod] public float? NullableFloatToNullableFloat(float? a) => a;
        [JsonRpcMethod] public decimal? Test2(decimal x) => x;
        [JsonRpcMethod] public string StringMe(string x) => x;
    }

    public static class Program
    {
        private static readonly string[] Inputs =
        {
            "{\"method\":\"add\",\"params\":[1,2],\"id\":1}",
            "{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}",
            "{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3}",
            "{\"method\":\"Test2\",\"params\":[3.456],\"id\":4}",
            "{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}"
        };

        public static int Main()
        {
            var service = new CalculatorService();
            var version = typeof(JsonRpcProcessor).Assembly.GetName().Version;
            Console.WriteLine($"AustinHarris.JsonRpc {version} from NuGet, {Environment.ProcessorCount} logical cores, server GC {System.Runtime.GCSettings.IsServerGC}");
            foreach (var input in Inputs)
            {
                var response = JsonRpcProcessor.Process(input).Result;
                Console.WriteLine($"  {input}  ->  {response}");
                if (response.Contains("\"error\"")) { Console.WriteLine("a request answered with an error; the service is not bound"); return 1; }
            }

            // The same shape as TestServer_Console's legacy mode: a one-second warm-up on the measured path, then
            // eight batch sizes (50, 100, 300, 1,200, 6,000, 36,000, 252,000, 2,016,000), each repeated until the
            // iteration has run for at least half a second; RPC/s is total requests over total time.
            var warm = Stopwatch.StartNew();
            while (warm.Elapsed < TimeSpan.FromSeconds(1)) RunBatch(5000);

            var rows = new List<(int size, double rps)>();
            var resultRows = new JsonArray();
            int cnt = 50;
            for (int iteration = 1; iteration <= 8; iteration++)
            {
                cnt *= iteration;
                int batches = 0;
                var cpu = CpuWindow.Start();
                var sw = Stopwatch.StartNew();
                do { RunBatch(cnt); batches++; } while (sw.Elapsed < TimeSpan.FromMilliseconds(500));
                sw.Stop();
                var usage = cpu.Stop();
                double rps = (long)cnt * batches / sw.Elapsed.TotalSeconds;
                rows.Add((cnt, rps));
                Console.WriteLine($"#{iteration} batch {cnt,10:N0}: {rps,14:N0} RPC/s  ({(long)cnt * batches,12:N0} RPCs in {sw.Elapsed.TotalSeconds:F3} s)");
                var row = new JsonObject { ["id"] = "headline-123-string", ["batch"] = cnt, ["rpcPerSec"] = Math.Round(rps), ["rpcs"] = (long)cnt * batches, ["seconds"] = Math.Round(sw.Elapsed.TotalSeconds, 3) };
                if (usage.Count > 0) row["cpu"] = usage;
                resultRows.Add(row);
            }
            double peak = 0; int peakSize = 0;
            foreach (var (size, rps) in rows) if (rps > peak) { peak = rps; peakSize = size; }
            Console.WriteLine($"peak {peak:N0} RPC/s at batch {peakSize:N0}");
            WriteResults(version, resultRows);
            GC.KeepAlive(service);
            return 0;
        }

        private static void RunBatch(int count)
        {
            int pending = count;
            using (var done = new ManualResetEventSlim(false))
            {
                Action<Task<string>> onDone = _ => { if (Interlocked.Decrement(ref pending) == 0) done.Set(); };
                Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, i =>
                {
                    JsonRpcProcessor.Process(Inputs[i % 5]).ContinueWith(onDone, TaskContinuationOptions.ExecuteSynchronously);
                });
                done.Wait();
            }
        }

        /// <summary>
        /// With JSONRPC_BENCH_RESULTS set, writes baseline-&lt;JSONRPC_BENCH_RUN&gt;.json there in the shape TestServer_Console's
        /// results use (benchmarks/charts/ingest.py reads it): one row per batch size, id headline-123-string.
        /// </summary>
        private static void WriteResults(Version version, JsonArray rows)
        {
            var dir = Environment.GetEnvironmentVariable("JSONRPC_BENCH_RESULTS");
            if (string.IsNullOrWhiteSpace(dir)) return;
            int run = int.TryParse(Environment.GetEnvironmentVariable("JSONRPC_BENCH_RUN"), out int r) && r > 0 ? r : 1;
            var commit = Environment.GetEnvironmentVariable("JSONRPC_BENCH_COMMIT");
            var doc = new JsonObject
            {
                ["mode"] = "baseline",
                ["machine"] = Machine(),
                ["runtime"] = $"{RuntimeInformation.FrameworkDescription}, Release, {(GCSettings.IsServerGC ? "Server GC" : "Workstation GC")}",
                ["serializer"] = "Json.NET (1.2.3)",
                ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["seconds"] = 0.5,
                ["commit"] = string.IsNullOrEmpty(commit) ? null : commit,
                ["run"] = run,
                ["cores"] = Environment.ProcessorCount,
                ["version"] = version.ToString(),
                ["rows"] = rows,
            };
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"baseline-{run}.json");
            File.WriteAllText(path, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            Console.WriteLine($"wrote {path}");
        }

        /// <summary>CPU model, capacity, OS and runtime: the string TestServer_Console's machine line gives on Linux.</summary>
        private static string Machine()
        {
            var overridden = Environment.GetEnvironmentVariable("JSONRPC_BENCH_MACHINE");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
            string model = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? RuntimeInformation.ProcessArchitecture.ToString();
            int hostCpus = 0;
            bool named = false;
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (!named && line.StartsWith("model name", StringComparison.Ordinal)) { model = line.Split(':', 2)[1].Trim(); named = true; }
                        if (line.StartsWith("processor", StringComparison.Ordinal) && line.Contains(':')) hostCpus++;
                    }
                }
                catch (IOException) { }
            }
            string capacity = Environment.ProcessorCount + " cores";
            if (OperatingSystem.IsLinux() && CpuWindow.Quota() > 0 && hostCpus > 0) capacity += $" of {hostCpus} host CPUs, cgroup quota";
            return $"{model}, {capacity}, {RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}, Release, {(GCSettings.IsServerGC ? "Server GC" : "Workstation GC")}";
        }
    }

    /// <summary>
    /// CPU over one timed window, with TestServer_Console's CpuMeter arithmetic: the process as a share of wall time times
    /// the cores, the machine from cgroup cpu.stat under a quota, else /proc/stat, else GetSystemTimes. There are no client
    /// threads here, so there is no client figure.
    /// </summary>
    internal sealed class CpuWindow
    {
        private readonly Stopwatch _wall = Stopwatch.StartNew();
        private readonly double _process = ProcessSeconds();
        private readonly double _quota = Quota();
        private readonly ulong _busy, _total;
        private readonly bool _system;

        private CpuWindow() { _system = SystemTimes(out _busy, out _total); }

        internal static CpuWindow Start() => new CpuWindow();

        internal JsonObject Stop()
        {
            double wall = _wall.Elapsed.TotalSeconds;
            var o = new JsonObject();
            if (_system && SystemTimes(out ulong busy, out ulong total))
            {
                // Under a quota the counters are cgroup microseconds of CPU; otherwise busy and total ticks.
                double system = _quota > 0 ? Percent((busy - _busy) / 1_000_000.0, wall * _quota) : Percent(busy - _busy, total - _total);
                o["system"] = Math.Round(system, 1);
            }
            o["process"] = Math.Round(Percent(ProcessSeconds() - _process, wall * Environment.ProcessorCount), 1);
            return o;
        }

        private static double Percent(double amount, double capacity) => capacity > 0 ? Math.Clamp(100 * amount / capacity, 0, 100) : 0;

        private static double ProcessSeconds()
        {
            using var p = Process.GetCurrentProcess();
            return p.TotalProcessorTime.TotalSeconds;
        }

        /// <summary>The cgroup v2 CPU quota in cores, or 0 when there is none.</summary>
        internal static double Quota()
        {
            try
            {
                if (!OperatingSystem.IsLinux() || !File.Exists("/sys/fs/cgroup/cpu.max")) return 0;
                var parts = File.ReadAllText("/sys/fs/cgroup/cpu.max").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || parts[0] == "max") return 0;
                double quota = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                double period = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                return quota > 0 && period > 0 ? quota / period : 0;
            }
            catch (Exception) { return 0; }
        }

        private bool SystemTimes(out ulong busy, out ulong total)
        {
            busy = total = 0;
            try
            {
                if (OperatingSystem.IsLinux() && _quota > 0)
                {
                    var line = File.ReadLines("/sys/fs/cgroup/cpu.stat").FirstOrDefault(s => s.StartsWith("usage_usec ", StringComparison.Ordinal));
                    return line != null && ulong.TryParse(line.AsSpan("usage_usec ".Length), out busy);
                }
                if (OperatingSystem.IsLinux())
                {
                    var parts = File.ReadLines("/proc/stat").First().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 6 || parts[0] != "cpu") return false;
                    for (int i = 1; i < parts.Length; i++) total += ulong.Parse(parts[i]);
                    busy = total - ulong.Parse(parts[4]) - ulong.Parse(parts[5]);
                    return true;
                }
                if (OperatingSystem.IsWindows() && GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    busy = kernel.Value - idle.Value + user.Value;
                    total = kernel.Value + user.Value;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime { internal uint Low; internal uint High; internal readonly ulong Value => ((ulong)High << 32) | Low; }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    }
}
