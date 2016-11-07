using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AustinHarris.JsonRpc;
using System.Threading;
using System.Diagnostics;

namespace TestServer_Console
{
    class Program
    {
        static object[] services = new object[] {
           new CalculatorService()
        };

        static void Main(string[] args)
        {
            string input = "";
            do
            {
                input = PrintOptions();
                if (string.IsNullOrWhiteSpace(input))
                    Benchmark();
                else if (input.StartsWith("C", StringComparison.CurrentCultureIgnoreCase))
                    ConsoleInput();
                else
                    PrintOptions();
            } while (input != "x");
        }

        private static string PrintOptions()
        {
            Console.WriteLine("Hit Enter to run benchmark");
            Console.WriteLine("'c' to start reading console input");
            Console.WriteLine("'x' to exit");
            return Console.ReadLine();
        }

        private static void ConsoleInput()
        {
            var rpcResultHandler = new AsyncCallback(_ => Console.WriteLine(((JsonRpcStateAsync)_).Result));

            for (string line = Console.ReadLine(); !string.IsNullOrEmpty(line); line = Console.ReadLine())
            {
                var async = new JsonRpcStateAsync(rpcResultHandler, null);
                async.JsonRpc = line;
                JsonRpcProcessor.Process(async);
            }
        }

        private static volatile int ctr;
        private static void Benchmark()
        {
            Console.WriteLine("Starting benchmark");
           
            var cnt = 20;
            var iterations = 8;
            for (int iteration = 1; iteration <= iterations; iteration++)
            {
                cnt *= iteration;
                ctr = 0;
                var sw = Stopwatch.StartNew();
                AutoResetEvent are = new AutoResetEvent(false);

                var fn = new Action<System.Threading.Tasks.Task<String>>(_ => {
                    if(Interlocked.Increment(ref ctr) == cnt)
                        {
                            sw.Stop();
                            Console.WriteLine("processed {0} rpc in {1}ms for {2} rpc/sec",cnt,sw.ElapsedMilliseconds, (double)cnt * 1000d / sw.ElapsedMilliseconds);
                            are.Set();
                        }
                });

                var sessionid = Handler.DefaultSessionId();
                for (int i = 0; i < cnt; i+=5)
                {
                    JsonRpcProcessor.Process(sessionid, "{'method':'add','params':[1,2],'id':1}").ContinueWith(fn);

                    JsonRpcProcessor.Process(sessionid, "{'method':'addInt','params':[1,7],'id':2}").ContinueWith(fn);

                    JsonRpcProcessor.Process(sessionid, "{'method':'NullableFloatToNullableFloat','params':[1.23],'id':3}").ContinueWith(fn);

                    JsonRpcProcessor.Process(sessionid, "{'method':'Test2','params':[3.456],'id':4}").ContinueWith(fn);

                    JsonRpcProcessor.Process(sessionid, "{'method':'StringMe','params':['Foo'],'id':5}").ContinueWith(fn);
                }
                are.WaitOne();
            }


            Console.WriteLine("Finished benchmark...");
        }

    }

    
}
