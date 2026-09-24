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
            // When stdout is a pipe (CI, `dotnet run | tee`) there is no console buffer to clear or reposition.
            services = new object[] { new CalculatorService() };
            // `dotnet run -- --async [seconds] [workers]` exercises real asynchronous invocation.
            if (args.Length > 0 && args[0] == "--async")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                int workers = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 1;
                AsyncBenchmark.RunAsync(Console.WriteLine, seconds, workers).GetAwaiter().GetResult();
                return;
            }

            var interactive = !Console.IsOutputRedirected;
            if (interactive) Console.Clear();
            IHardwareInfo hardwareInfo = new HardwareInfo();
            hardwareInfo.RefreshAll();
            HardwarePrinter.PrintHardware(hardwareInfo);
            System.Threading.ThreadPool.SetMinThreads(Environment.ProcessorCount * 3, Environment.ProcessorCount * 3);
            Console.WriteLine("Setting task pool size to {0}", Environment.ProcessorCount * 4);

            // `dotnet run -- --sync [seconds] [threads]` runs the direct synchronous benchmark and exits (CI / scripted runs).
            if (args.Length > 0 && args[0] == "--sync")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                int threads = args.Length > 2 && int.TryParse(args[2], out var t) ? t : 0;
                BenchmarkRunner.BenchmarkSync(Console.WriteLine, null, threads, seconds);
                return;
            }

            // `dotnet run -- --kestrel [seconds]` hosts the AspNetCore package in-process and drives it over HTTP and TCP.
            if (args.Length > 0 && args[0] == "--kestrel")
            {
                double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 3;
                KestrelBenchmark.RunAsync(Console.WriteLine, seconds).GetAwaiter().GetResult();
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
                if (!interactive && string.IsNullOrWhiteSpace(line))
                {
                    BenchmarkRunner.Benchmark(Console.WriteLine);
                }
                else if (line.StartsWith("s", StringComparison.CurrentCultureIgnoreCase))
                {
                    BenchmarkRunner.BenchmarkSync(Console.WriteLine);
                }
                else if (line.StartsWith("k", StringComparison.CurrentCultureIgnoreCase))
                {
                    KestrelBenchmark.RunAsync(Console.WriteLine).GetAwaiter().GetResult();
                }
                else if (line.StartsWith("x", StringComparison.CurrentCultureIgnoreCase))
                {
                    CompareBenchmark.RunAsync(Console.WriteLine).GetAwaiter().GetResult();
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    Console.CursorVisible = false;
                    HardwarePrinter.PrintHardware(hardwareInfo);
                    var pos = Console.CursorTop;
                    BenchmarkRunner.Benchmark((update) =>
                    {
                        // Clear the console from pos to current position first
                        var currentline = Console.CursorTop;
                        var fullLine = new string(' ', Console.WindowWidth);
                        Console.SetCursorPosition(0, pos);
                        for (int i = 0; i < currentline-pos; i++)
                        {
                            Console.WriteLine( fullLine);
                        }
                        
                        Console.SetCursorPosition(0, pos);
                        Console.WriteLine(update);
                    });
                    Console.CursorVisible = true;
                }
                else if (line.StartsWith("c", StringComparison.CurrentCultureIgnoreCase))
                    ConsoleInput();
                PrintOptions();
            }
        }

        private static void PrintOptions()
        {
            Console.WriteLine("Hit Enter to run the Task-based benchmark");
            Console.WriteLine("'s' to run the direct synchronous benchmark (bytes in, bytes out)");
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
