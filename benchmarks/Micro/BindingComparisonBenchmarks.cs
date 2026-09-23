using System;
using System.Text;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// The same method registered two ways in one process, class-bound with <c>[JsonRpcMethod]</c> and
    /// interface-bound with <c>ServiceBinder.BindInterface</c>, measured back to back so a run-to-run drift of the
    /// machine cannot show up as a binding difference. Each interface row must match its class row within noise.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class BindingComparisonBenchmarks
    {
        private const string ClassSession = "micro-cmp-class";
        private const string InterfaceSession = "micro-cmp-iface";

        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _addInt, _decimal, _string;

        public interface ICalculator
        {
            int addInt(int l, int r);
            decimal? Test2(decimal x);
            string StringMe(string x);
        }

        public sealed class Calculator : ICalculator
        {
            [JsonRpcMethod] public int addInt(int l, int r) => l + r;
            [JsonRpcMethod] public decimal? Test2(decimal x) => x;
            [JsonRpcMethod] public string StringMe(string x) => x;
        }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindService(ClassSession, new Calculator());
            ServiceBinder.BindInterface<ICalculator>(InterfaceSession, new Calculator());
            _out = new PooledByteBufferWriter(1024);
            _addInt = Utf8("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            _decimal = Utf8("{\"method\":\"Test2\",\"params\":[3.456],\"id\":4}");
            _string = Utf8("{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}");
            foreach (var session in new[] { ClassSession, InterfaceSession })
            {
                Expect(session, _addInt, "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}");
                Expect(session, _decimal, "{\"jsonrpc\":\"2.0\",\"result\":3.456,\"id\":4}");
                Expect(session, _string, "{\"jsonrpc\":\"2.0\",\"result\":\"Foo\",\"id\":5}");
            }
        }

        [GlobalCleanup]
        public void Cleanup() => _out.Dispose();

        [Benchmark(Baseline = true)] public int ClassAddInt() => Run(ClassSession, _addInt);
        [Benchmark] public int InterfaceAddInt() => Run(InterfaceSession, _addInt);
        [Benchmark] public int ClassDecimal() => Run(ClassSession, _decimal);
        [Benchmark] public int InterfaceDecimal() => Run(InterfaceSession, _decimal);
        [Benchmark] public int ClassString() => Run(ClassSession, _string);
        [Benchmark] public int InterfaceString() => Run(InterfaceSession, _string);

        private int Run(string session, ReadOnlyMemory<byte> input)
        {
            var w = _out;
            w.Clear();
            JsonRpcProcessor.Process(session, input, w);
            return w.WrittenCount;
        }

        private void Expect(string session, ReadOnlyMemory<byte> input, string expected)
        {
            _out.Clear();
            JsonRpcProcessor.Process(session, input, _out);
            var actual = _out.ToString();
            if (actual != expected) throw new InvalidOperationException("unexpected response: " + actual);
        }

        private static ReadOnlyMemory<byte> Utf8(string s) => Encoding.UTF8.GetBytes(s);
    }
}
