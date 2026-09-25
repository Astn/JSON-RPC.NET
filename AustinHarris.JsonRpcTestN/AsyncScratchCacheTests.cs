using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// The async scratch lease behind <c>ProcessAsync</c>: a one-slot per-thread cache in front of the shared pool.
    /// The lease is exclusive and transferable, so these cases exercise every way a lease can leave the thread that
    /// rented it or overlap another lease on that thread, and check that every document still answers correctly.
    /// </summary>
    [TestFixture]
    public sealed class AsyncScratchCacheTests
    {
        private string _session;
        [SetUp] public void SetUp() => _session = "scratch-" + Guid.NewGuid().ToString("N");
        [TearDown] public void TearDown() => Handler.DestroySession(_session);
        private void Bind(string name, Delegate method, RpcContextFlow flow = RpcContextFlow.None) => ServiceBinder.BindMethod(_session, name, method, contextFlow: flow);
        private Task<string> Run(string json, JsonRpcSerializer serializer = null, CancellationToken token = default) => JsonRpcProcessor.ProcessAsync(_session, json, null, serializer, token);
        private static string Request(string method, string parameters = null, string id = "1") => "{\"method\":\"" + method + "\"" + (parameters == null ? "" : ",\"params\":" + parameters) + ",\"id\":" + id + "}";
        private static int Result(string json) => (int)JObject.Parse(json)["result"];
        private static TaskCompletionSource<int> Gate() => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        [Test]
        public async Task ReentrantDocument_InsideARunningMethod_OnOneThread()
        {
            // The outer document holds its lease while the inner one rents: the inner must not receive the
            // outer's scratch. Inline and suspended outer methods, many times over, so the slot is reused.
            Bind("inner", new Func<int, int>(n => n * 2));
            Bind("outer", new Func<int, Task<int>>(async n =>
            {
                var inner = await JsonRpcProcessor.ProcessAsync(_session, Request("inner", "[" + n + "]", "2")).ConfigureAwait(false);
                return Result(inner) + 1;
            }));
            Bind("outerYield", new Func<int, Task<int>>(async n =>
            {
                await Task.Yield();
                var inner = await JsonRpcProcessor.ProcessAsync(_session, Request("inner", "[" + n + "]", "3")).ConfigureAwait(false);
                return Result(inner) + 1;
            }));
            for (int i = 0; i < 300; i++)
            {
                Assert.AreEqual(2 * i + 1, Result(await Run(Request("outer", "[" + i + "]"))));
                Assert.AreEqual(2 * i + 1, Result(await Run(Request("outerYield", "[" + i + "]"))));
                Assert.AreEqual(2 * i, Result(await Run(Request("inner", "[" + i + "]"))));
            }
        }

        [Test]
        public async Task OverlappingDocuments_OnOneThread_SuspendedThenInline()
        {
            // Document A suspends and is completed elsewhere; document B runs inline on the same thread
            // meanwhile and returns its lease into the slot. Both answer, and the slot keeps working after.
            var gate = Gate();
            Bind("wait", new Func<Task<int>>(async () => { await gate.Task.ConfigureAwait(false); return 7; }));
            Bind("now", new Func<int, int>(n => n));
            var pendingA = Run(Request("wait"));
            Assert.IsFalse(pendingA.IsCompleted);
            for (int i = 0; i < 50; i++) Assert.AreEqual(i, Result(await Run(Request("now", "[" + i + "]", "2"))));
            gate.SetResult(0);
            Assert.AreEqual(7, Result(await pendingA));
            for (int i = 0; i < 50; i++) Assert.AreEqual(i, Result(await Run(Request("now", "[" + i + "]", "2"))));
        }

        [Test]
        public async Task CrossThreadCompletion_ManyWorkers_EveryDocumentAnswers()
        {
            Bind("yield", new Func<int, Task<int>>(async n => { await Task.Yield(); return n + 1; }));
            Bind("hop", new Func<int, Task<int>>(n => Task.Run(() => n + 2)));
            Bind("inline", new Func<int, int>(n => n + 3));
            var counts = await Task.WhenAll(Enumerable.Range(0, 16).Select(w => Task.Run(async () =>
            {
                int ok = 0;
                for (int i = 0; i < 400; i++)
                {
                    int n = w * 1000 + i;
                    var (name, expected) = (i % 3) switch { 0 => ("yield", n + 1), 1 => ("hop", n + 2), _ => ("inline", n + 3) };
                    if (Result(await Run(Request(name, "[" + n + "]")).ConfigureAwait(false)) == expected) ok++;
                }
                return ok;
            })));
            Assert.AreEqual(16 * 400, counts.Sum());
        }

        [Test]
        public async Task CancellationAfterTransfer_CommitsNothing_AndTheThreadKeepsWorking()
        {
            var gate = Gate();
            Bind("wait", new Func<Task<int>>(async () => { await gate.Task.ConfigureAwait(false); return 7; }));
            Bind("now", new Func<int>(() => 1));
            using var cts = new CancellationTokenSource();
            using var output = new PooledByteBufferWriter();
            var pending = JsonRpcProcessor.ProcessAsync(_session, new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(Request("wait"))), output, cancellationToken: cts.Token);
            Assert.IsFalse(pending.IsCompleted);
            cts.Cancel();
            gate.SetResult(0);
            Assert.CatchAsync<OperationCanceledException>(async () => await pending);
            Assert.AreEqual(0, output.WrittenCount, "cancellation commits no response bytes");
            for (int i = 0; i < 20; i++) Assert.AreEqual(1, Result(await Run(Request("now"))));
        }

        [Test]
        public async Task ThrowingReaderRelease_SurfacesOnce_AndTheNextDocumentGetsAFreshLease()
        {
            Bind("now", new Func<int>(() => 1));
            var serializer = new ReleaseThrowingSerializer(SerializerCatalog.Create("jsmn")) { ThrowOnRelease = true };
            // The damaged scratch is disposed, not cached, so the failure is not repeated by the next document.
            Assert.ThrowsAsync<InvalidOperationException>(async () => await Run(Request("now"), serializer));
            serializer.ThrowOnRelease = false;
            Assert.AreEqual(1, Result(await Run(Request("now"), serializer)));
            Assert.AreEqual(1, Result(await Run(Request("now"), serializer)));
            Assert.AreEqual(2, serializer.ReadersCreated, "the first reader went with its scratch; the second is cached and reused");
        }

        [Test]
        public async Task OversizedDocuments_AreTrimmedOnReturn_AndSmallOnesFollow()
        {
            Bind("echo", new Func<string, string>(s => s));
            Bind("now", new Func<int>(() => 1));
            var big = new string('x', 70 * 1024);
            for (int round = 0; round < 3; round++)
            {
                var json = await Run(Request("echo", "[\"" + big + "\"]"));
                Assert.AreEqual(big, (string)JObject.Parse(json)["result"]);
                Assert.AreEqual(1, Result(await Run(Request("now"))));
            }
            var sequence = new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(Request("echo", "[\"" + big + "\"]")));
            using var output = new PooledByteBufferWriter();
            await JsonRpcProcessor.ProcessAsync(_session, sequence, output);
            Assert.AreEqual(big, (string)JObject.Parse(output.ToString())["result"]);
            Assert.AreEqual(1, Result(await Run(Request("now"))));
        }

        private sealed class ReleaseThrowingSerializer : JsonRpcSerializer
        {
            private readonly JsonRpcSerializer _inner;
            internal bool ThrowOnRelease;
            internal int ReadersCreated;
            internal ReleaseThrowingSerializer(JsonRpcSerializer inner) { _inner = inner; }
            public override string Name => "release-throws";
            public override JsonRpcRequestReader CreateReader() { ReadersCreated++; return new Reader(this, _inner.CreateReader()); }
            public override T Read<T>(ReadOnlySpan<byte> bytes) => _inner.Read<T>(bytes);
            public override object Read(ReadOnlySpan<byte> bytes, Type type) => _inner.Read(bytes, type);
            public override void Write<T>(IBufferWriter<byte> output, T value) => _inner.Write(output, value);
            public override void Write(IBufferWriter<byte> output, object value, Type type) => _inner.Write(output, value, type);

            private sealed class Reader : JsonRpcRequestReader
            {
                private readonly ReleaseThrowingSerializer _owner;
                private readonly JsonRpcRequestReader _inner;
                internal Reader(ReleaseThrowingSerializer owner, JsonRpcRequestReader inner) { _owner = owner; _inner = inner; }
                public override bool TryParse(ReadOnlyMemory<byte> bytes, out string error) => _inner.TryParse(bytes, out error);
                public override ReadOnlyMemory<byte> Document => _inner.Document;
                public override bool IsBatch => _inner.IsBatch;
                public override int Count => _inner.Count;
                public override bool Select(int index) => _inner.Select(index);
                public override bool HasMethod => _inner.HasMethod;
                public override string Method => _inner.Method;
                public override ReadOnlySpan<byte> MethodUtf8 => _inner.MethodUtf8;
                public override JsonRpcIdKind IdKind => _inner.IdKind;
                public override ReadOnlySpan<byte> IdRaw => _inner.IdRaw;
                public override object IdValue => _inner.IdValue;
                public override JsonRpcParamsKind ParamsKind => _inner.ParamsKind;
                public override int ParamCount => _inner.ParamCount;
                public override ReadOnlySpan<byte> ParamNameUtf8(int i) => _inner.ParamNameUtf8(i);
                public override ReadOnlySpan<byte> ParamRaw(int i) => _inner.ParamRaw(i);
                public override bool ParamIsNull(int i) => _inner.ParamIsNull(i);
                public override T ReadParam<T>(int i) => _inner.ReadParam<T>(i);
                public override object ReadParam(int i, Type type) => _inner.ReadParam(i, type);
                public override object ParamsValue => _inner.ParamsValue;
                public override void Release()
                {
                    _inner.Release();
                    if (_owner.ThrowOnRelease) throw new InvalidOperationException("Release failed");
                }
            }
        }
    }
}
