using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            int cnt = 50;
            for (int iteration = 1; iteration <= 8; iteration++)
            {
                cnt *= iteration;
                int batches = 0;
                var sw = Stopwatch.StartNew();
                do { RunBatch(cnt); batches++; } while (sw.Elapsed < TimeSpan.FromMilliseconds(500));
                sw.Stop();
                double rps = (long)cnt * batches / sw.Elapsed.TotalSeconds;
                rows.Add((cnt, rps));
                Console.WriteLine($"#{iteration} batch {cnt,10:N0}: {rps,14:N0} RPC/s  ({(long)cnt * batches,12:N0} RPCs in {sw.Elapsed.TotalSeconds:F3} s)");
            }
            double peak = 0; int peakSize = 0;
            foreach (var (size, rps) in rows) if (rps > peak) { peak = rps; peakSize = size; }
            Console.WriteLine($"peak {peak:N0} RPC/s at batch {peakSize:N0}");
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
    }
}
