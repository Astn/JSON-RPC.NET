using System;
using System.Text;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// The same five shapes as <see cref="DispatchBenchmarks"/>, bound through an interface tree with
    /// <c>ServiceBinder.BindInterface</c> under the same wire names in another session, plus the dotted names an
    /// interface tree produces (a long name costs hashing and comparison proportional to its length; that is the
    /// name, not the binding). Interface-bound rows must match the class-bound rows within noise.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class InterfaceBindingBenchmarks
    {
        private const string Flat = "micro-iface-flat";
        private const string Tree = "micro-iface-tree";

        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _add, _addInt, _nullableFloat, _decimal, _string, _treeAddInt, _treeDeep;

        public interface ICalculator
        {
            double add(double l, double r);
            int addInt(int l, int r);
            float? NullableFloatToNullableFloat(float? a);
            decimal? Test2(decimal x);
            string StringMe(string x);
        }

        public interface IRoot
        {
            ICalculator Calc { get; }
            IAdmin Admin { get; }
        }

        public interface IAdmin
        {
            ICalculator Calc { get; }
        }

        public sealed class Calculator : ICalculator
        {
            public double add(double l, double r) => l + r;
            public int addInt(int l, int r) => l + r;
            public float? NullableFloatToNullableFloat(float? a) => a;
            public decimal? Test2(decimal x) => x;
            public string StringMe(string x) => x;
        }

        public sealed class Root : IRoot, IAdmin
        {
            public ICalculator Calc { get; } = new Calculator();
            public IAdmin Admin => this;
        }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindInterface<ICalculator>(Flat, new Calculator());
            ServiceBinder.BindInterface<IRoot>(Tree, new Root());
            _out = new PooledByteBufferWriter(1024);
            _add = Utf8("{\"method\":\"add\",\"params\":[1,2],\"id\":1}");
            _addInt = Utf8("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            _nullableFloat = Utf8("{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3}");
            _decimal = Utf8("{\"method\":\"Test2\",\"params\":[3.456],\"id\":4}");
            _string = Utf8("{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}");
            _treeAddInt = Utf8("{\"method\":\"Calc.addInt\",\"params\":[1,7],\"id\":2}");
            _treeDeep = Utf8("{\"method\":\"Admin.Calc.addInt\",\"params\":[1,7],\"id\":2}");

            Expect(Flat, _addInt, "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}");
            Expect(Tree, _treeAddInt, "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}");
            Expect(Tree, _treeDeep, "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}");
        }

        [GlobalCleanup]
        public void Cleanup() => _out.Dispose();

        [Benchmark(Baseline = true)] public int Add() => Run(Flat, _add);
        [Benchmark] public int AddInt() => Run(Flat, _addInt);
        [Benchmark] public int NullableFloat() => Run(Flat, _nullableFloat);
        [Benchmark] public int Decimal() => Run(Flat, _decimal);
        [Benchmark] public int String() => Run(Flat, _string);
        [Benchmark] public int TreeAddInt() => Run(Tree, _treeAddInt);
        [Benchmark] public int TreeDeepAddInt() => Run(Tree, _treeDeep);

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
