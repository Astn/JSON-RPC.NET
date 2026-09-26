using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    [TestFixture]
    [NonParallelizable]
    public class LimitsTests
    {
        private const string Call = "{\"jsonrpc\":\"2.0\",\"method\":\"limits.hit\",\"id\":1}";
        private const string Notification = "{\"jsonrpc\":\"2.0\",\"method\":\"limits.hit\"}";
        private const string Result = "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}";
        private string _session;
        private Service _service;

        public sealed class Service
        {
            public int Calls;
            [JsonRpcMethod("limits.hit")]
            public int Hit() { Interlocked.Increment(ref Calls); return 7; }
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            internal Segment(ReadOnlyMemory<byte> memory) { Memory = memory; }
            internal Segment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }

            internal Segment AppendAt(long index, ReadOnlyMemory<byte> memory)
            {
                var next = new Segment(memory) { RunningIndex = index };
                Next = next;
                return next;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _session = "limits-" + Guid.NewGuid().ToString("N");
            _service = new Service();
            ServiceBinder.BindService(_session, _service);
            Config.SetLimits(JsonRpcLimits.Default);
        }

        [TearDown]
        public void TearDown()
        {
            Config.SetLimits(JsonRpcLimits.Default);
            Handler.DestroySession(_session);
        }

        private static ReadOnlySequence<byte> Split(byte[] bytes)
        {
            int middle = bytes.Length / 2;
            var first = new Segment(bytes.AsMemory(0, middle));
            var last = first.Append(bytes.AsMemory(middle));
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private static string Text(ArrayBufferWriter<byte> output) => Encoding.UTF8.GetString(output.WrittenSpan);

        private static string LimitError(string name, long maximum) =>
            "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32600,\"message\":\"Invalid Request\",\"data\":{\"limit\":\"" +
            name + "\",\"maximum\":" + maximum + "}},\"id\":null}";

        [TestCase("jsmn")]
        [TestCase("newtonsoft")]
        [TestCase("stj")]
        public async Task DocumentBytes_AllPublicEntriesRejectBeforeDispatch(string serializerName)
        {
            var serializer = SerializerCatalog.Create(serializerName);
            byte[] bytes = Encoding.UTF8.GetBytes(Call);
            long maximum = bytes.Length - 1;
            string expected = LimitError("maxDocumentBytes", maximum);
            Config.SetLimits(new JsonRpcLimits(maximum, 0));

            var output = new ArrayBufferWriter<byte>();
            var single = new ReadOnlySequence<byte>(bytes);
            JsonRpcProcessor.Process(_session, in single, output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "sync single-segment sequence");
            output.Clear();
            var multi = Split(bytes);
            JsonRpcProcessor.Process(_session, in multi, output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "sync multi-segment sequence");
            output.Clear();
            JsonRpcProcessor.Process(_session, bytes.AsMemory(), output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "sync memory");
            output.Clear();
            JsonRpcProcessor.Process(_session, bytes.AsSpan(), output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "sync span");
            Assert.AreEqual(expected, Encoding.UTF8.GetString(JsonRpcProcessor.ProcessBytes(_session, bytes.AsSpan(), serializer: serializer)));
            Assert.AreEqual(expected, JsonRpcProcessor.ProcessSync(_session, Call, null, serializer));
            Assert.AreEqual(expected, await JsonRpcProcessor.Process(_session, Call, null, serializer));

            output.Clear();
            await JsonRpcProcessor.ProcessAsync(_session, single, output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "async single-segment sequence");
            output.Clear();
            await JsonRpcProcessor.ProcessAsync(_session, multi, output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "async multi-segment sequence");
            output.Clear();
            await JsonRpcProcessor.ProcessAsync(_session, bytes.AsMemory(), output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "async memory");
            output.Clear();
            await JsonRpcProcessor.ProcessAsync(_session, bytes.AsSpan(), output, serializer: serializer);
            Assert.AreEqual(expected, Text(output), "async span");
            Assert.AreEqual(expected, await JsonRpcProcessor.ProcessAsync(_session, Call, serializer: serializer));
            Assert.AreEqual(0, _service.Calls);
        }

        [Test]
        public async Task DefaultSessionAndStateWrappersInheritByteCheck()
        {
            Config.SetLimits(new JsonRpcLimits(1, 0));
            string expected = LimitError("maxDocumentBytes", 1);
            Assert.AreEqual(expected, JsonRpcProcessor.ProcessSync(Call));
            Assert.AreEqual(expected, JsonRpcProcessor.ProcessSync(SerializerCatalog.Create("jsmn"), Call));
            Assert.AreEqual(expected, await JsonRpcProcessor.Process(Call));
            Assert.AreEqual(expected, await JsonRpcProcessor.Process(SerializerCatalog.Create("jsmn"), Call));
            Assert.AreEqual(expected, await JsonRpcProcessor.ProcessAsync(Call));

            async Task<string> StateResult(bool defaultSession)
            {
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var state = new JsonRpcStateAsync(ar => completion.SetResult(((JsonRpcStateAsync)ar).Result), null) { JsonRpc = Call };
                if (defaultSession) JsonRpcProcessor.Process(state);
                else JsonRpcProcessor.Process(_session, state);
                return await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }

            Assert.AreEqual(expected, await StateResult(false));
            Assert.AreEqual(expected, await StateResult(true));
            Assert.AreEqual(0, _service.Calls);
        }

        [Test]
        public async Task SequenceLengthIsCheckedBeforeIntConversionOrFlattening()
        {
            Config.SetLimits(_session, new JsonRpcLimits(1, 0));
            var first = new Segment(ReadOnlyMemory<byte>.Empty);
            var last = first.AppendAt((long)int.MaxValue + 1, ReadOnlyMemory<byte>.Empty);
            var sequence = new ReadOnlySequence<byte>(first, 0, last, 0);
            var output = new ArrayBufferWriter<byte>();
            JsonRpcProcessor.Process(_session, in sequence, output);
            Assert.AreEqual(LimitError("maxDocumentBytes", 1), Text(output));
            output.Clear();
            await JsonRpcProcessor.ProcessAsync(_session, sequence, output);
            Assert.AreEqual(LimitError("maxDocumentBytes", 1), Text(output));
        }

        [Test]
        public void AsyncPreCancellationWinsOverDocumentLimit()
        {
            Config.SetLimits(_session, new JsonRpcLimits(1, 0));
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var bytes = Encoding.UTF8.GetBytes(Call);
            var output = new ArrayBufferWriter<byte>();
            var sequence = new ReadOnlySequence<byte>(bytes);
            Assert.IsTrue(JsonRpcProcessor.ProcessAsync(_session, sequence, output, cancellationToken: cts.Token).IsCanceled);
            Assert.IsTrue(JsonRpcProcessor.ProcessAsync(_session, bytes.AsMemory(), output, cancellationToken: cts.Token).IsCanceled);
            Assert.IsTrue(JsonRpcProcessor.ProcessAsync(_session, bytes.AsSpan(), output, cancellationToken: cts.Token).IsCanceled);
            Assert.IsTrue(JsonRpcProcessor.ProcessAsync(_session, Call, cancellationToken: cts.Token).IsCanceled);
            Assert.AreEqual(0, output.WrittenCount);
        }

        [TestCase("jsmn")]
        [TestCase("newtonsoft")]
        [TestCase("stj")]
        public async Task BatchCountRejectsWholeMixedAndNotificationOnlyBatches(string serializerName)
        {
            var serializer = SerializerCatalog.Create(serializerName);
            Config.SetLimits(_session, new JsonRpcLimits(0, 2));
            string expected = LimitError("maxBatchCount", 2);
            string mixed = "[" + Call + "," + Notification + ",42]";
            string notifications = "[" + Notification + "," + Notification + "," + Notification + "]";
            foreach (string batch in new[] { mixed, notifications })
            {
                Assert.AreEqual(expected, JsonRpcProcessor.ProcessSync(_session, batch, null, serializer));
                Assert.AreEqual(expected, await JsonRpcProcessor.ProcessAsync(_session, batch, serializer: serializer));
            }
            Assert.AreEqual(0, _service.Calls);
        }

        [TestCase("jsmn")]
        [TestCase("newtonsoft")]
        [TestCase("stj")]
        public async Task StringLimitUsesUtf8ByteCount(string serializerName)
        {
            var serializer = SerializerCatalog.Create(serializerName);
            string ascii = "{\"method\":\"limits.hit\",\"id\":1,\"note\":\"aa\"}";
            string multibyte = "{\"method\":\"limits.hit\",\"id\":1,\"note\":\"€€\"}";
            int maximum = Encoding.UTF8.GetByteCount(ascii);
            Config.SetLimits(_session, new JsonRpcLimits(maximum, 0));
            Assert.AreEqual(Result, JsonRpcProcessor.ProcessSync(_session, ascii, null, serializer));
            Assert.AreEqual(LimitError("maxDocumentBytes", maximum), JsonRpcProcessor.ProcessSync(_session, multibyte, null, serializer));
            Assert.AreEqual(LimitError("maxDocumentBytes", maximum), await JsonRpcProcessor.ProcessAsync(_session, multibyte, serializer: serializer));
            Assert.AreEqual(1, _service.Calls);
        }

        [Test]
        public void ConfigurationRejectsNegativesAndNullGlobal_AndSessionCanInherit()
        {
            Assert.AreEqual(4 * 1024 * 1024, JsonRpcLimits.Default.MaxDocumentBytes);
            Assert.AreEqual(1024, JsonRpcLimits.Default.MaxBatchCount);
            Assert.AreEqual(0, JsonRpcLimits.Unlimited.MaxDocumentBytes);
            Assert.AreEqual(0, JsonRpcLimits.Unlimited.MaxBatchCount);
            Assert.AreEqual("maxDocumentBytes", Assert.Throws<ArgumentOutOfRangeException>(() => new JsonRpcLimits(-1, 1)).ParamName);
            Assert.AreEqual("maxBatchCount", Assert.Throws<ArgumentOutOfRangeException>(() => new JsonRpcLimits(1, -1)).ParamName);
            Assert.Throws<ArgumentNullException>(() => Config.SetLimits(null));

            var global = new JsonRpcLimits(1, 0);
            Config.SetLimits(global);
            Config.SetLimits(_session, null);
            Assert.AreSame(global, Config.Limits);
            Assert.AreEqual(LimitError("maxDocumentBytes", 1), JsonRpcProcessor.ProcessSync(_session, Call, null));
            Config.SetLimits(_session, new JsonRpcLimits(0, 0));
            Assert.AreEqual(Result, JsonRpcProcessor.ProcessSync(_session, Call, null));
            Assert.AreEqual(1, _service.Calls);
            Config.SetLimits(_session, null);
            Assert.AreEqual(LimitError("maxDocumentBytes", 1), JsonRpcProcessor.ProcessSync(_session, Call, null));

            string created = "limits-created-" + Guid.NewGuid().ToString("N");
            try
            {
                Config.SetLimits(created, null);
                Assert.IsTrue(Handler.TryGetSessionHandler(created, out var handler));
                Assert.IsNull(handler.Limits);
            }
            finally { Handler.DestroySession(created); }
        }

        [Test]
        public void ZeroDisablesEachField_AndUnlimitedAcceptsFiveMiB()
        {
            Config.SetLimits(_session, new JsonRpcLimits(0, 1));
            string large = "{\"method\":\"limits.hit\",\"id\":1,\"note\":\"" + new string('x', 5 * 1024 * 1024) + "\"}";
            Assert.AreEqual(Result, JsonRpcProcessor.ProcessSync(_session, large, null));
            Config.SetLimits(_session, new JsonRpcLimits(0, 0));
            Assert.AreEqual("[" + Result + "," + Result + "]", JsonRpcProcessor.ProcessSync(_session, "[" + Call + "," + Call + "]", null));
            Config.SetLimits(_session, JsonRpcLimits.Unlimited);
            Assert.AreEqual(Result, JsonRpcProcessor.ProcessSync(_session, large, null));
            Assert.AreEqual(4, _service.Calls);
        }

        [Test]
        public async Task LimitErrorsReachParseHandler_ButNotPreProcessHandler()
        {
            int parsed = 0, pre = 0;
            Config.SetLimits(_session, new JsonRpcLimits(1, 1));
            Config.SetParseErrorHandler(_session, (raw, error) =>
            {
                Assert.IsNotNull(raw);
                Assert.AreEqual(-32600, error.code);
                Assert.IsInstanceOf<LimitExceededInfo>(error.data);
                parsed++;
                return error;
            });
            Config.SetPreProcessHandler(_session, (request, context) => { pre++; return null; });
            try
            {
                Assert.AreEqual(LimitError("maxDocumentBytes", 1), JsonRpcProcessor.ProcessSync(_session, Call, null));
                Config.SetLimits(_session, new JsonRpcLimits(0, 1));
                Assert.AreEqual(LimitError("maxBatchCount", 1), await JsonRpcProcessor.ProcessAsync(_session, "[" + Call + "," + Call + "]"));
                Assert.AreEqual(2, parsed);
                Assert.AreEqual(0, pre);
                Assert.AreEqual(0, _service.Calls);
            }
            finally
            {
                Config.SetParseErrorHandler(_session, null);
                Config.SetPreProcessHandler(_session, null);
            }
        }
    }
}
