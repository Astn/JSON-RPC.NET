using System;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// The asynchronous entry point: a synchronous method through <c>ProcessAsync</c> (the cost of the async
    /// dispatcher itself), a <c>Task&lt;int&gt;</c> and a <c>ValueTask&lt;int&gt;</c> method that complete inline,
    /// each with the ambient frame flowing (default) and without, and a method that yields once (the price of a
    /// real suspension: continuation objects and a thread hop). Compare the inline rows with
    /// <see cref="DispatchBenchmarks.AddInt"/>.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class AsyncDispatchBenchmarks
    {
        private const string Session = "micro-async";

        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _sync, _taskFlow, _task, _valueTaskFlow, _valueTask, _yieldFlow, _yield;

        public sealed class Service
        {
            [JsonRpcMethod("addInt")] public int AddInt(int l, int r) => l + r;
            [JsonRpcMethod("addTaskFlow", ContextFlow = RpcContextFlow.Flow)] public Task<int> AddTaskFlow(int l, int r) => Task.FromResult(l + r);
            [JsonRpcMethod("addTask")] public Task<int> AddTask(int l, int r) => Task.FromResult(l + r);
            [JsonRpcMethod("addValueTaskFlow", ContextFlow = RpcContextFlow.Flow)] public ValueTask<int> AddValueTaskFlow(int l, int r) => new ValueTask<int>(l + r);
            [JsonRpcMethod("addValueTask")] public ValueTask<int> AddValueTask(int l, int r) => new ValueTask<int>(l + r);
            [JsonRpcMethod("addYieldFlow", ContextFlow = RpcContextFlow.Flow)] public async Task<int> AddYieldFlow(int l, int r) { await Task.Yield(); return l + r; }
            [JsonRpcMethod("addYield")] public async Task<int> AddYield(int l, int r) { await Task.Yield(); return l + r; }
        }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindService(Session, new Service());
            _out = new PooledByteBufferWriter(1024);
            _sync = Utf8("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            _taskFlow = Utf8("{\"method\":\"addTaskFlow\",\"params\":[1,7],\"id\":2}");
            _task = Utf8("{\"method\":\"addTask\",\"params\":[1,7],\"id\":2}");
            _valueTaskFlow = Utf8("{\"method\":\"addValueTaskFlow\",\"params\":[1,7],\"id\":2}");
            _valueTask = Utf8("{\"method\":\"addValueTask\",\"params\":[1,7],\"id\":2}");
            _yieldFlow = Utf8("{\"method\":\"addYieldFlow\",\"params\":[1,7],\"id\":2}");
            _yield = Utf8("{\"method\":\"addYield\",\"params\":[1,7],\"id\":2}");

            const string expected = "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}";
            Expect(_sync, expected);
            Expect(_task, expected);
            Expect(_taskFlow, expected);
            Expect(_valueTask, expected);
            Expect(_valueTaskFlow, expected);
            Expect(_yield, expected);
            Expect(_yieldFlow, expected);
        }

        [GlobalCleanup]
        public void Cleanup() => _out.Dispose();

        [Benchmark(Baseline = true)] public int SyncViaProcess() { var w = _out; w.Clear(); JsonRpcProcessor.Process(Session, _sync, w); return w.WrittenCount; }
        [Benchmark] public ValueTask<int> SyncViaProcessAsync() => Run(_sync);
        [Benchmark] public ValueTask<int> TaskInlineFlow() => Run(_taskFlow);
        [Benchmark] public ValueTask<int> TaskInline() => Run(_task);
        [Benchmark] public ValueTask<int> ValueTaskInlineFlow() => Run(_valueTaskFlow);
        [Benchmark] public ValueTask<int> ValueTaskInline() => Run(_valueTask);
        [Benchmark] public ValueTask<int> YieldOnceFlow() => Run(_yieldFlow);
        [Benchmark] public ValueTask<int> YieldOnce() => Run(_yield);

        // ValueTask so the driver itself allocates nothing when the document completes inline; an async Task<int>
        // driver would charge every row a Task<int> for the (uncached) byte count.
        private async ValueTask<int> Run(ReadOnlyMemory<byte> input)
        {
            var w = _out;
            w.Clear();
            var t = JsonRpcProcessor.ProcessAsync(Session, input, w);
            if (!t.IsCompleted) await t.ConfigureAwait(false);
            return w.WrittenCount;
        }

        private void Expect(ReadOnlyMemory<byte> input, string expected)
        {
            _out.Clear();
            JsonRpcProcessor.ProcessAsync(Session, input, _out).GetAwaiter().GetResult();
            var actual = _out.ToString();
            if (actual != expected) throw new InvalidOperationException("unexpected response: " + actual);
        }

        private static ReadOnlyMemory<byte> Utf8(string s) => Encoding.UTF8.GetBytes(s);
    }
}
