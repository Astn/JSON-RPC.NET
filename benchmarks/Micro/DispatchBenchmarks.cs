using System;
using System.Text;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// The five request shapes of TestServer_Console's sync benchmark, one at a time, through the byte-first
    /// processor with the built-in serializer. Same wire names as the console harness so the rows compare.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class DispatchBenchmarks
    {
        private const string Session = "micro-sync";

        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _add, _addInt, _nullableFloat, _decimal, _string, _batch, _notification;

        public sealed class Service
        {
            [JsonRpcMethod] private double add(double l, double r) => l + r;
            [JsonRpcMethod] private int addInt(int l, int r) => l + r;
            [JsonRpcMethod] public float? NullableFloatToNullableFloat(float? a) => a;
            [JsonRpcMethod] public decimal? Test2(decimal x) => x;
            [JsonRpcMethod] public string StringMe(string x) => x;
        }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindService(Session, new Service());
            _out = new PooledByteBufferWriter(1024);
            _add = Utf8("{\"method\":\"add\",\"params\":[1,2],\"id\":1}");
            _addInt = Utf8("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            _nullableFloat = Utf8("{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3}");
            _decimal = Utf8("{\"method\":\"Test2\",\"params\":[3.456],\"id\":4}");
            _string = Utf8("{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}");
            _batch = Utf8("[{\"method\":\"add\",\"params\":[1,2],\"id\":1},{\"method\":\"addInt\",\"params\":[1,7],\"id\":2},{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3},{\"method\":\"Test2\",\"params\":[3.456],\"id\":4},{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}]");
            _notification = Utf8("{\"method\":\"addInt\",\"params\":[1,7]}");

            // fail loudly if a shape does not answer what the console harness expects
            Expect(_addInt, "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}");
            Expect(_string, "{\"jsonrpc\":\"2.0\",\"result\":\"Foo\",\"id\":5}");
        }

        [GlobalCleanup]
        public void Cleanup() => _out.Dispose();

        [Benchmark(Baseline = true)] public int Add() => Run(_add);
        [Benchmark] public int AddInt() => Run(_addInt);
        [Benchmark] public int NullableFloat() => Run(_nullableFloat);
        [Benchmark] public int Decimal() => Run(_decimal);
        [Benchmark] public int String() => Run(_string);
        [Benchmark] public int Batch5() => Run(_batch);
        [Benchmark] public int Notification() => Run(_notification);

        private int Run(ReadOnlyMemory<byte> input)
        {
            var w = _out;
            w.Clear();
            JsonRpcProcessor.Process(Session, input, w);
            return w.WrittenCount;
        }

        private void Expect(ReadOnlyMemory<byte> input, string expected)
        {
            _out.Clear();
            JsonRpcProcessor.Process(Session, input, _out);
            var actual = _out.ToString();
            if (actual != expected) throw new InvalidOperationException("unexpected response: " + actual);
        }

        private static ReadOnlyMemory<byte> Utf8(string s) => Encoding.UTF8.GetBytes(s);
    }
}
