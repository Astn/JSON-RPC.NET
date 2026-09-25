using System;
using AustinHarris.JsonRpc;
using Hardware.Info;

namespace TestServer_Console
{
    class Program
    {
        // Bound explicitly in Main: a static field initializer (beforefieldinit) is not guaranteed to run,
        // and without it every benchmark request answered "Method not found".
        static object[] services;

        static void Main(string[] args)
        {
            services = new object[] { new CalculatorService() };
            // Before any mode runs: the awaited-worker modes (--async, --scale) start their workers on the pool.
            System.Threading.ThreadPool.SetMinThreads(Environment.ProcessorCount * 3, Environment.ProcessorCount * 3);

            // `dotnet run -- --async [seconds] [workers]` drives ProcessAsync from awaited workers (the Async table).
            if (args.Length > 0 && args[0] == "--async")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                int workers = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 1;
                AsyncBenchmark.RunAsync(Console.WriteLine, seconds, workers).GetAwaiter().GetResult();
                return;
            }

            // `dotnet run -- --scale [seconds] [workers] [threshold]` is the release gate for the ProcessAsync path:
            // the inline rows at 1, 2 and N workers, three paired runs, medians; exit code 1 when N/1 is below the threshold.
            if (args.Length > 0 && args[0] == "--scale")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                int workers = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 16;
                double threshold = args.Length > 3 && double.TryParse(args[3], out var r) ? r : 4.0;
                bool pass = AsyncBenchmark.ScaleAsync(Console.WriteLine, seconds, workers, threshold).GetAwaiter().GetResult();
                Environment.ExitCode = pass ? 0 : 1;
                return;
            }

            var interactive = !Console.IsOutputRedirected;
            if (interactive) Console.Clear();
            IHardwareInfo hardwareInfo = new HardwareInfo();
            hardwareInfo.RefreshAll();
            HardwarePrinter.PrintHardware(hardwareInfo);
            Console.WriteLine("Thread pool minimum set to {0}", Environment.ProcessorCount * 3);

            // `dotnet run -- --sync [seconds] [threads]` runs the direct synchronous benchmark and exits (CI / scripted runs).
            if (args.Length > 0 && args[0] == "--sync")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                int threads = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 0;
                BenchmarkRunner.BenchmarkSync(Console.WriteLine, null, threads, seconds);
                return;
            }

            // `dotnet run -- --kestrel [seconds] [async]` hosts the AspNetCore package in-process and drives it over HTTP and TCP;
            // `async` turns EnableAsyncMethods on and adds a TCP row with yielding methods.
            if (args.Length > 0 && args[0] == "--kestrel")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                bool asyncMethods = args.Length > 2 && args[2] == "async";
                KestrelBenchmark.RunAsync(Console.WriteLine, seconds, asyncMethods: asyncMethods).GetAwaiter().GetResult();
                return;
            }

            // `dotnet run -- --sweep [seconds] [output.json]` measures every library and transport at 1, 2, 4, 8 and 16 connections.
            if (args.Length > 0 && args[0] == "--sweep")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 2;
                CompareBenchmark.SweepAsync(Console.WriteLine, seconds, args.Length > 2 ? args[2] : null).GetAwaiter().GetResult();
                return;
            }

            // `dotnet run -- --compare [seconds]` benchmarks the same requests through JSON-RPC.Net, StreamJsonRpc and gRPC for .NET.
            if (args.Length > 0 && args[0] == "--compare")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                CompareBenchmark.RunAsync(Console.WriteLine, seconds).GetAwaiter().GetResult();
                return;
            }

            PrintOptions();
            for (string line = Console.ReadLine(); line != null && line != "q"; line = Console.ReadLine())
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("s", StringComparison.CurrentCultureIgnoreCase))
                {
                    BenchmarkRunner.BenchmarkSync(Console.WriteLine);
                }
                else if (line.StartsWith("a", StringComparison.CurrentCultureIgnoreCase))
                {
                    AsyncBenchmark.RunAsync(Console.WriteLine, 3, 1).GetAwaiter().GetResult();
                    AsyncBenchmark.RunAsync(Console.WriteLine, 3, Environment.ProcessorCount).GetAwaiter().GetResult();
                }
                else if (line.StartsWith("t", StringComparison.CurrentCultureIgnoreCase))
                {
                    LegacyStringBenchmark(interactive, hardwareInfo);
                }
                else if (line.StartsWith("k", StringComparison.CurrentCultureIgnoreCase))
                {
                    KestrelBenchmark.RunAsync(Console.WriteLine).GetAwaiter().GetResult();
                }
                else if (line.StartsWith("x", StringComparison.CurrentCultureIgnoreCase))
                {
                    CompareBenchmark.RunAsync(Console.WriteLine).GetAwaiter().GetResult();
                }
                else if (line.StartsWith("c", StringComparison.CurrentCultureIgnoreCase))
                    ConsoleInput();
                PrintOptions();
            }
        }

        /// <summary>The 1.x string overloads through the thread pool, with live progress when there is a console to reposition.</summary>
        private static void LegacyStringBenchmark(bool interactive, IHardwareInfo hardwareInfo)
        {
            if (!interactive)
            {
                BenchmarkRunner.Benchmark(Console.WriteLine);
                return;
            }
            Console.CursorVisible = false;
            HardwarePrinter.PrintHardware(hardwareInfo);
            var pos = Console.CursorTop;
            BenchmarkRunner.Benchmark((update) =>
            {
                // Clear the console from pos to current position first
                var currentline = Console.CursorTop;
                var fullLine = new string(' ', Console.WindowWidth);
                Console.SetCursorPosition(0, pos);
                for (int i = 0; i < currentline - pos; i++)
                {
                    Console.WriteLine(fullLine);
                }

                Console.SetCursorPosition(0, pos);
                Console.WriteLine(update);
            });
            Console.CursorVisible = true;
        }

        private static void PrintOptions()
        {
            Console.WriteLine("Hit Enter (or 's') for Process(bytes), dedicated threads: the library alone, the Sync table (--sync)");
            Console.WriteLine("'a' for ProcessAsync(bytes), awaited workers, at 1 and " + Environment.ProcessorCount + " workers: the Async table (--async)");
            Console.WriteLine("'t' for Legacy Process(string), scheduled synchronous work: the 1.x string overloads through the thread pool, not the byte path");
            Console.WriteLine("'k' to run the Kestrel benchmark (AspNetCore package over HTTP and TCP)");
            Console.WriteLine("'x' to compare against StreamJsonRpc and gRPC for .NET (same calls, same Kestrel)");
            Console.WriteLine("'c' to start reading console input");
            Console.WriteLine("'q' to quit");
        }

        private static void ConsoleInput()
        {
            for (string line = Console.ReadLine(); !string.IsNullOrEmpty(line); line = Console.ReadLine())
            {
                JsonRpcProcessor.Process(line).ContinueWith(response => Console.WriteLine( response.Result ));
            }
        }
    }
}
