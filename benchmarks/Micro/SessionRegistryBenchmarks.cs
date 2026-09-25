using System;
using System.Text;
using System.Threading;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// The session registry on the request path: the per-thread last hit, the per-thread snapshot, a miss that
    /// falls through to the master registry, and lookups while another thread registers and destroys sessions
    /// (every change makes each thread copy the master registry on its next lookup). The rows with "Churn" run
    /// with that background thread active; compare them with the quiet rows to see what the master dictionary
    /// costs the request path, which is what the choice of dictionary type is judged by.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class SessionRegistryBenchmarks
    {
        private const int Registered = 64;
        private static readonly string Stable = "registry-stable";
        private static readonly string Unknown = "registry-unknown-" + Guid.NewGuid().ToString("N");

        private string[] _ids;
        private int _next;
        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _request;
        private Thread _churn;
        private volatile bool _stop;

        public sealed class Service
        {
            [JsonRpcMethod] private int addInt(int l, int r) => l + r;
        }

        [Params(false, true)]
        public bool Churn { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindService(Stable, new Service());
            _ids = new string[Registered];
            for (int i = 0; i < Registered; i++)
            {
                _ids[i] = "registry-" + i;
                ServiceBinder.BindService(_ids[i], new Service());
            }
            _out = new PooledByteBufferWriter(256);
            _request = Encoding.UTF8.GetBytes("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            if (Churn)
            {
                _stop = false;
                _churn = new Thread(() =>
                {
                    int n = 0;
                    while (!_stop)
                    {
                        string id = "registry-churn-" + (n++ & 1023);
                        ServiceBinder.BindService(id, new Service());
                        Handler.DestroySession(id);
                        Thread.SpinWait(200);
                    }
                }) { IsBackground = true };
                _churn.Start();
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _stop = true;
            _churn?.Join();
            Handler.DestroySession(Stable);
            foreach (var id in _ids) Handler.DestroySession(id);
        }

        /// <summary>The same string instance every time: the reference-equality last-hit path.</summary>
        [Benchmark(Baseline = true)]
        public Handler Lookup_LastHit() => Handler.GetSessionHandler(Stable);

        /// <summary>A different registered id every call: the per-thread snapshot dictionary.</summary>
        [Benchmark]
        public Handler Lookup_Rotating()
        {
            int i = _next++;
            return Handler.GetSessionHandler(_ids[i & (Registered - 1)]);
        }

        /// <summary>An id that is not registered: snapshot miss, then master miss, no session created.</summary>
        [Benchmark]
        public bool Lookup_Unknown() => Handler.TryGetSessionHandler(Unknown, out _);

        /// <summary>Register, look up from the request path and destroy: the master registry mutated per call.</summary>
        [Benchmark]
        public bool RegisterLookupDestroy()
        {
            string id = "registry-transient-" + (_next++ & 255);
            Handler.GetSessionHandler(id);
            bool found = Handler.TryGetSessionHandler(id, out _);
            Handler.DestroySession(id);
            return found;
        }

        /// <summary>One request end to end on the stable session, through the byte-first processor.</summary>
        [Benchmark]
        public int Process_Stable()
        {
            _out.Clear();
            JsonRpcProcessor.Process(Stable, _request, _out);
            return _out.WrittenCount;
        }
    }
}
