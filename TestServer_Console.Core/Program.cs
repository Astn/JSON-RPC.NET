using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AustinHarris.JsonRpc;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;

namespace TestServer_Console
{
    class Program
    {
        static object[] services = new object[] {
           new CalculatorService()
        };

        static void Main(string[] args)
        {
            PrintOptions();
            for (string line = Console.ReadLine(); line != "q"; line = Console.ReadLine())
            {
                if (string.IsNullOrWhiteSpace(line))
                    Benchmark();
                else if (line.StartsWith("c", StringComparison.CurrentCultureIgnoreCase))
                    ConsoleInput();
                PrintOptions();
            }
        }

        private static void PrintOptions()
        {
            Console.WriteLine("Hit Enter to run benchmark");
            Console.WriteLine("'c' to start reading console input");
            Console.WriteLine("'q' to quit");
        }

        private static void ConsoleInput()
        {
            for (string line = Console.ReadLine(); !string.IsNullOrEmpty(line); line = Console.ReadLine())
            {
                JsonRpcProcessor.ProcessAsync(line).ContinueWith(response => Console.WriteLine( response.Result ));
            }
        }

        private static volatile int ctr;
        private static void Benchmark()
        {
            Console.WriteLine("Starting benchmark");
            AustinHarris.JsonRpc.Config.ConfigureFactory(new AustinHarris.JsonRpc.Newtonsoft.ObjectFactory());
            var cnt = 50;
            var iterations = 8;
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                cnt *= iteration;
                ctr = 0;
                var handler = Handler.GetSessionHandler();
                Task<string>[] tasks = new Task<string>[cnt];
                string[] results = new string[cnt];
                var sessionid = Handler.DefaultSessionId();

                GC.Collect();
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < cnt; i += 5)
                {
                    tasks[i] = JsonRpcProcessor.ProcessAsync(handler, "{'method':'Add','params':[1,2],'id':1}");
                    tasks[i + 1] = JsonRpcProcessor.ProcessAsync(handler, "{'method':'AddInt','params':[1,7],'id':2}");
                    tasks[i + 2] = JsonRpcProcessor.ProcessAsync(handler, "{'method':'NullableFloatToNullableFloat','params':[1.23],'id':3}");
                    tasks[i + 3] = JsonRpcProcessor.ProcessAsync(handler, "{'method':'Test2','params':[3.456],'id':4}");
                    tasks[i + 4] = JsonRpcProcessor.ProcessAsync(handler, "{'method':'StringMe','params':['Foo'],'id':5}");
                }
                Task.WaitAll(tasks);
                sw.Stop();
                Console.WriteLine("Async  processed {0} rpc in {1}ms for {2} rpc/sec", cnt, sw.ElapsedMilliseconds, (double)cnt * 1000d / sw.ElapsedMilliseconds);

                for (int i = 0; i < tasks.Length; i++)
                {
                    tasks[i] = null;
                }
                GC.Collect();

                sw = Stopwatch.StartNew();
                for (int i = 0; i < cnt; i+=5)
                {
                    tasks[i] = JsonRpcProcessor.ProcessAsync2(handler, "{'method':'Add','params':[1,2],'id':1}");
                    tasks[i + 1] = JsonRpcProcessor.ProcessAsync2(handler, "{'method':'AddInt','params':[1,7],'id':2}");
                    tasks[i + 2] = JsonRpcProcessor.ProcessAsync2(handler, "{'method':'NullableFloatToNullableFloat','params':[1.23],'id':3}");
                    tasks[i + 3] = JsonRpcProcessor.ProcessAsync2(handler, "{'method':'Test2','params':[3.456],'id':4}");
                    tasks[i + 4] = JsonRpcProcessor.ProcessAsync2(handler, "{'method':'StringMe','params':['Foo'],'id':5}");
                }
                Task.WaitAll(tasks);
                sw.Stop();
                Console.WriteLine("Async2 processed {0} rpc in {1}ms for {2} rpc/sec", cnt, sw.ElapsedMilliseconds, (double)cnt * 1000d / sw.ElapsedMilliseconds);

                GC.Collect();
                sw = Stopwatch.StartNew();
                for (int i = 0; i < cnt; i += 5)
                {
                    results[i] = JsonRpcProcessor.Process(handler, "{'method':'Add','params':[1,2],'id':1}", null);
                    results[i + 1] = JsonRpcProcessor.Process(handler, "{'method':'AddInt','params':[1,7],'id':2}", null);
                    results[i + 2] = JsonRpcProcessor.Process(handler, "{'method':'NullableFloatToNullableFloat','params':[1.23],'id':3}", null);
                    results[i + 3] = JsonRpcProcessor.Process(handler, "{'method':'Test2','params':[3.456],'id':4}", null);
                    results[i + 4] = JsonRpcProcessor.Process(handler, "{'method':'StringMe','params':['Foo'],'id':5}", null);
                }
                sw.Stop();
                Console.WriteLine("Direct processed {0} rpc in {1}ms for {2} rpc/sec", cnt, sw.ElapsedMilliseconds, (double)cnt * 1000d / sw.ElapsedMilliseconds);
            }


            Console.WriteLine("Finished benchmark...");
        }

    }

    
}
