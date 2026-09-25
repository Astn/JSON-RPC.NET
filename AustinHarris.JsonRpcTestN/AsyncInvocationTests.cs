using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Invocation;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    [TestFixture]
    public sealed class AsyncInvocationTests
    {
        private string _session;
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };
        [SetUp] public void SetUp() => _session = "async-" + Guid.NewGuid().ToString("N");
        [TearDown] public void TearDown() => Handler.DestroySession(_session);
        private void Bind(string name, Delegate method, RpcContextFlow flow = RpcContextFlow.Flow) => ServiceBinder.BindMethod(_session, name, method, contextFlow: flow);
        private Task<string> Run(string json, JsonRpcSerializer serializer = null, object context = null, CancellationToken token = default) => JsonRpcProcessor.ProcessAsync(_session, json, context, serializer, token);
        private string Sync(string json, object context = null)
        {
            var output = new ArrayBufferWriter<byte>();
            JsonRpcProcessor.Process(_session, Encoding.UTF8.GetBytes(json).AsSpan(), output, context);
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        private static TaskCompletionSource<int> Gate() => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static string Request(string method, string parameters = null, string id = "1") => "{\"method\":\"" + method + "\"" + (parameters == null ? "" : ",\"params\":" + parameters) + (id == null ? "" : ",\"id\":" + id) + "}";
        private static void Error(string json, int code) => Assert.AreEqual(code, (int)JObject.Parse(json)["error"]["code"], json);

        private static IEnumerable<TestCaseData> Shapes()
        {
            foreach (var serializer in Serializers)
            foreach (var shape in new[] { "task", "taskResult", "valueTask", "valueTaskResult" })
            for (int mode = 0; mode < 5; mode++)
            foreach (bool hooks in new[] { false, true })
                yield return new TestCaseData(serializer, shape, mode, hooks);
        }

        private static Task<int> TaskResult(int mode)
        {
            switch (mode)
            {
                case 1: throw new InvalidOperationException("synchronous throw");
                case 2: return Task.FromException<int>(new InvalidOperationException("completed fault"));
                case 3: return Task.FromCanceled<int>(new CancellationToken(true));
                case 4: return YieldResult();
                default: return Task.FromResult(7);
            }
        }
        private static async Task<int> YieldResult() { await Task.Yield(); return 7; }
        private static Task PlainTask(int mode) => TaskResult(mode);
        private static ValueTask<int> ValueTaskResult(int mode) => mode == 0 ? new ValueTask<int>(7) : new ValueTask<int>(TaskResult(mode));
        private static ValueTask PlainValueTask(int mode) => mode == 0 ? default : new ValueTask(TaskResult(mode));

        [TestCaseSource(nameof(Shapes))]
        public async Task AllAwaitableShapes(string serializerName, string shape, int mode, bool hooks)
        {
            Delegate method = shape == "task" ? (Delegate)new Func<int, Task>(PlainTask)
                : shape == "taskResult" ? new Func<int, Task<int>>(TaskResult)
                : shape == "valueTask" ? new Func<int, ValueTask>(PlainValueTask)
                : new Func<int, ValueTask<int>>(ValueTaskResult);
            Bind("run", method);
            int pre = 0, post = 0, errors = 0;
            if (hooks)
            {
                Handler.GetSessionHandler(_session).SetPreProcessHandler((r, c) => { pre++; return null; });
                Handler.GetSessionHandler(_session).SetPostProcessHandler((r, response, c) => { post++; return null; });
                Config.SetErrorHandler(_session, (r, e) => { errors++; return e; });
            }
            var json = await Run(Request("run", "[" + mode + "]"), SerializerCatalog.Create(serializerName));
            if (mode == 0 || mode == 4)
            {
                var result = JObject.Parse(json)["result"];
                if (shape.EndsWith("Result")) Assert.AreEqual(7, (int)result);
                else Assert.AreEqual(JTokenType.Null, result.Type);
            }
            else Error(json, -32603);
            if (hooks) { Assert.AreEqual(1, pre); Assert.AreEqual(1, post); Assert.AreEqual(mode > 0 && mode < 4 ? 1 : 0, errors); }
        }

        private sealed class OnceSource : IValueTaskSource<int>
        {
            private ManualResetValueTaskSourceCore<int> _core;
            internal int Consumed;
            internal OnceSource() { _core.RunContinuationsAsynchronously = true; }
            internal ValueTask<int> Value => new ValueTask<int>(this, _core.Version);
            internal void Complete() => _core.SetResult(7);
            public int GetResult(short token) { if (Interlocked.Increment(ref Consumed) != 1) throw new InvalidOperationException("consumed twice"); return _core.GetResult(token); }
            public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);
            public void OnCompleted(Action<object> continuation, object state, short token, ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
        }

        [TestCase("jsmn", false)] [TestCase("jsmn", true)]
        [TestCase("newtonsoft", false)] [TestCase("newtonsoft", true)]
        [TestCase("stj", false)] [TestCase("stj", true)]
        public async Task SourceBackedValueTask_IsConsumedOnce(string name, bool suspend)
        {
            var source = new OnceSource();
            Bind("run", new Func<ValueTask<int>>(() => source.Value));
            if (!suspend) source.Complete();
            var pending = Run(Request("run"), SerializerCatalog.Create(name));
            if (suspend) { Assert.IsFalse(pending.IsCompleted); await Task.Run(source.Complete); }
            Assert.AreEqual(7, (int)JObject.Parse(await pending)["result"]);
            Assert.AreEqual(1, source.Consumed);
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task NullTask_AndSynchronousRejection(string name)
        {
            int invoked = 0;
            Bind("null", new Func<Task<int>>(() => { invoked++; return null; }));
            var s = SerializerCatalog.Create(name);
            var sync = JsonRpcProcessor.ProcessSync(_session, Request("null"), null, s);
            Error(sync, -32603);
            StringAssert.Contains("is asynchronous; process the request with JsonRpcProcessor.ProcessAsync", sync);
            Assert.AreEqual(0, invoked);
            var json = await Run(Request("null"), s);
            Error(json, -32603);
            StringAssert.Contains("returned a null Task", json);
            Assert.AreEqual(1, invoked);
        }

        private sealed class AutoAsyncService : JsonRpcService
        {
            internal AutoAsyncService(string session) : base(session) { }
            [JsonRpcMethod("run")] public Task<int> Run() => Task.FromResult(7);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task CompatibilityRegistrationSurfaces_SupportAsyncMethods(int surface)
        {
            var types = new Dictionary<string, Type> { ["returns"] = typeof(Task<int>) };
            var method = new Func<Task<int>>(() => Task.FromResult(7));
            var handler = Handler.GetSessionHandler(_session);
            if (surface == 0) handler.MetaData.Services["run"] = new SMDService("POST", "JSON-RPC-2.0", types, new Dictionary<string, object>(), method);
#pragma warning disable CS0618
            else if (surface == 1) handler.RegisterFuction("run", types, null, method);
#pragma warning restore CS0618
            else _ = new AutoAsyncService(_session);
            Assert.AreEqual(7, (int)JObject.Parse(await Run(Request("run")))["result"]);
            Assert.AreEqual(typeof(int), handler.MetaData.Services["run"].Method.ResultType);
            var rejected = JsonRpcProcessor.ProcessSync(_session, Request("run"), null);
            Error(rejected, -32603);
            StringAssert.Contains("Method 'run' is asynchronous", rejected);
        }

        private sealed class InvalidService
        {
            [JsonRpcMethod] public async void Invalid() => await Task.Yield();
        }
        private sealed class InvalidAutoService : JsonRpcService
        {
            internal InvalidAutoService(string session) : base(session) { }
            [JsonRpcMethod] public async void Invalid() => await Task.Yield();
        }
        private static Task<int> MissingToken(CancellationToken token) => Task.FromResult(1);
        private static Task<int> RefParameter(ref int value) => Task.FromResult(value);
        private static Task<int> RefError(ref JsonRpcException error) => Task.FromResult(1);
        private delegate Task<int> RefDelegate(ref int value);
        private delegate Task<int> RefErrorDelegate(ref JsonRpcException error);
        private readonly struct CustomAwaitable { public TaskAwaiter GetAwaiter() => Task.CompletedTask.GetAwaiter(); }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)]
        public void AsyncVoid_IsRejectedOnEverySurface(int surface)
        {
            Action invalid = async () => await Task.Yield();
            var types = new Dictionary<string, Type> { ["returns"] = typeof(void) };
            TestDelegate registration = surface switch
            {
                0 => () => RpcMethod.FromMethodInfo("invalid", typeof(InvalidService).GetMethod("Invalid"), new InvalidService()),
                1 => () => RpcMethod.FromDelegate("invalid", invalid),
                2 => () => ServiceBinder.BindService(_session, new InvalidService()),
                3 => () => Bind("invalid", invalid),
                4 => () => new InvalidAutoService(_session),
                5 => () => new SMDService("POST", "JSON-RPC-2.0", types, new Dictionary<string, object>(), invalid),
#pragma warning disable CS0618
                _ => () => Handler.GetSessionHandler(_session).RegisterFuction("invalid", types, null, invalid)
#pragma warning restore CS0618
            };
            StringAssert.Contains("async void", Assert.Throws<NotSupportedException>(registration).Message);
        }

        [Test]
        public void UnsupportedSignatures_AreRejected()
        {
            StringAssert.Contains("[JsonRpcCancellation]", Assert.Throws<NotSupportedException>(() => Bind("token", new Func<CancellationToken, Task<int>>(MissingToken))).Message);
            StringAssert.Contains("by-ref", Assert.Throws<NotSupportedException>(() => Bind("ref", new RefDelegate(RefParameter))).Message);
            Assert.Throws<NotSupportedException>(() => Bind("refError", new RefErrorDelegate(RefError)));
            Assert.Throws<NotSupportedException>(() => Bind("custom", new Func<CustomAwaitable>(() => default)));
            Assert.Throws<NotSupportedException>(() => Bind("nested", new Func<Task<Task>>(() => Task.FromResult(Task.CompletedTask))));
        }

        private sealed class TokenService
        {
            internal CancellationToken Seen;
            internal TaskCompletionSource<int> Wait;
            [JsonRpcMethod("token")]
            public Task<int> Run(int value, [JsonRpcCancellation] CancellationToken token, int more = 2)
            {
                Seen = token;
                return Wait?.Task ?? Task.FromResult(value + more);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task CancellationParameter_IsInjectedAndExcludedFromSmd(string name)
        {
            var service = new TokenService();
            ServiceBinder.BindService(_session, service);
            using var cts = new CancellationTokenSource();
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual(5, (int)JObject.Parse(await Run(Request("token", "{\"value\":3}"), s, token: cts.Token))["result"]);
            Assert.AreEqual(cts.Token, service.Seen);
            var metadata = Handler.GetSessionHandler(_session).MetaData.Services["token"];
            CollectionAssert.AreEqual(new[] { "value", "more" }, metadata.parameters.Select(p => p.Name));
            Assert.AreEqual(typeof(int), metadata.Method.ResultType);
            Assert.AreEqual("int32", SMD.Types[metadata.returns.Type]["__name"]);
            Error(await Run(Request("token", "{\"value\":3,\"token\":null}"), s), -32602);
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task Flow_ContextIdAndErrorSurviveAwaits(string name)
        {
            var s = SerializerCatalog.Create(name);
            object context = new object();
            JsonRpcRequestId captured = default;
            Bind("flow", new Func<Task<int>>(async () =>
            {
                Assert.AreSame(context, Handler.RpcContext());
                Assert.AreSame(context, JsonRpcContext.Current().Value);
                captured = Handler.RpcRequestId();
                Handler.RpcSetException(new JsonRpcException(-32010, "before", null));
                await Task.Yield();
                await Task.Run(() => { Assert.AreSame(context, Handler.RpcContext()); Assert.AreEqual(captured, Handler.RpcRequestId()); });
                Assert.AreSame(context, JsonRpcContext.Current().Value);
                Assert.AreEqual(captured, Handler.RpcRequestId());
                Handler.RpcSetException(new JsonRpcException(-32011, "after", null));
                return 7;
            }));
            foreach (var id in new[] { "\"a\\\"b\\u00e9\\n\"", "123456789012345678901234567890" })
            {
                var json = await Run(Request("flow", id: id), s, context);
                Error(json, -32011);
                StringAssert.EndsWith("\"id\":" + id + "}", json);
                Assert.AreEqual(JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes(id)), captured);
            }
            Assert.IsNull(Handler.RpcContext());
            Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
        }

        [Test]
        public async Task None_SnapshotSurvives_AndAmbientDoesNotFlow()
        {
            var gate = Gate();
            object context = new object();
            JsonRpcRequestId snapshot = default;
            Bind("none", new Func<Task<int>>(async () =>
            {
                snapshot = Handler.RpcRequestId();
                var savedContext = Handler.RpcContext();
                Assert.AreSame(context, savedContext);
                await gate.Task.ConfigureAwait(false);
                Assert.IsNull(Handler.RpcContext());
                Assert.IsNull(JsonRpcContext.Current().Value);
                Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
                Assert.AreSame(context, savedContext);
                return (int)(long)snapshot.ToObject();
            }), RpcContextFlow.None);
            var pending = Run(Request("none", id: "7"), context: context);
            Assert.IsFalse(pending.IsCompleted);
            new Thread(() => gate.SetResult(0)).Start();
            Assert.AreEqual(7, (int)JObject.Parse(await pending)["result"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task NestedDispatch_RestoresParentsBeforeAndAfterAwait(string name)
        {
            var s = SerializerCatalog.Create(name);
            Bind("sync", new Func<string>(() => (string)Handler.RpcContext() + "/" + Handler.RpcRequestId()));
            Bind("child", new Func<Task<int>>(async () => { await Task.Yield(); Handler.RpcSetException(new JsonRpcException(-32022, "child", null)); return 0; }));
            Bind("parent", new Func<Task<int>>(async () =>
            {
                Handler.RpcSetException(new JsonRpcException(-32021, "parent", null));
                foreach (var phase in new[] { 0, 1 })
                {
                    if (phase == 1) await Task.Yield();
                    var child = JsonRpcProcessor.ProcessSync(_session, Request("sync", id: "2"), "child", s);
                    Assert.AreEqual("child/2", (string)JObject.Parse(child)["result"]);
                    Assert.AreEqual("parent", Handler.RpcContext());
                    Assert.AreEqual(JsonRpcRequestId.FromInt64(1), Handler.RpcRequestId());
                }
                Error(await Run(Request("child", id: "3"), s, "child"), -32022);
                Assert.AreEqual("parent", Handler.RpcContext());
                Assert.AreEqual(JsonRpcRequestId.FromInt64(1), Handler.RpcRequestId());
                return 7;
            }));
            Error(await Run(Request("parent"), s, "parent"), -32021);
        }

        [Test]
        public async Task FlowFrame_IsClearedForEscapedExecutionContext()
        {
            var gate = Gate();
            Task escaped = null;
            Bind("run", new Func<Task<int>>(() =>
            {
                escaped = Task.Run(async () => { await gate.Task; Assert.IsNull(Handler.RpcContext()); Assert.IsTrue(Handler.RpcRequestId().IsAbsent); });
                return Task.FromResult(7);
            }));
            await Run(Request("run"), context: new object());
            gate.SetResult(0);
            await escaped;
        }

        [Test]
        public async Task FlowScope_CompletedOnAnotherThread_LeavesThatThreadItsOwnFrame()
        {
            // A hooked Flow invocation suspends here and completes on a pool thread, where its scope is
            // disposed. That thread must keep its own frame afterwards: when it was handed this thread's
            // frame instead, a synchronous dispatch on each thread saved and restored the same frame, and
            // the interleaving below left this thread's method reading no id at all.
            var handler = Handler.GetSessionHandler(_session);
            handler.SetPreProcessHandler((request, context) => null);
            int completedOn = 0;
            handler.SetPostProcessHandler((request, response, context) => { if (request.Method == "suspend") completedOn = Environment.CurrentManagedThreadId; return null; });
            var suspended = new TaskCompletionSource<int>();
            Bind("suspend", new Func<Task<int>>(() => suspended.Task));
            var entered = new ManualResetEventSlim();
            var release = new ManualResetEventSlim();
            var left = new ManualResetEventSlim();
            Bind("hold", new Func<int>(() => { entered.Set(); release.Wait(); return 1; }));
            var mine = new object();
            Bind("peek", new Func<int>(() =>
            {
                release.Set();
                left.Wait();
                return (Handler.RpcRequestId().IsAbsent ? 0 : 1) + (ReferenceEquals(Handler.RpcContext(), mine) ? 2 : 0);
            }));
            Bind("warm", new Func<int>(() => 1));
            Sync(Request("warm"));   // this thread owns a frame before the suspension, as any thread that has dispatched does
            var pending = Run(Request("suspend"));
            int completingThread = 0;
            var other = Task.Run(() =>
            {
                completingThread = Environment.CurrentManagedThreadId;
                suspended.SetResult(7);
                Sync(Request("hold", id: "2"));
                left.Set();
            });
            // Block, do not await, until the other thread is inside "hold": an awaited continuation could be
            // run on that thread, ahead of "hold", and wait for itself.
            entered.Wait();
            var response = Sync(Request("peek", id: "\"mine\""), mine);
            await other;
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}", await pending);
            Assume.That(completedOn, Is.EqualTo(completingThread), "the completion did not run the scope's cleanup on the completing thread");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":\"mine\"}", response, "this thread's method sees its own id and context while the other thread dispatches");
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task Batch_IsSequential_AwaitsNotifications_AndIsolatesFaults(string name)
        {
            var order = new List<int>();
            var gate = Gate();
            Bind("first", new Func<int>(() => { order.Add(1); return 1; }));
            Bind("slow", new Func<Task<int>>(async () => { order.Add(2); await gate.Task; order.Add(3); return 2; }));
            Bind("fault", new Func<Task<int>>(() => { order.Add(4); return Task.FromException<int>(new Exception("fault")); }));
            Bind("last", new Func<ValueTask<int>>(() => { order.Add(5); return new ValueTask<int>(3); }));
            var pending = Run("[" + Request("first") + "," + Request("slow", id: null) + "," + Request("fault", id: "2") + "," + Request("last", id: "3") + "]", SerializerCatalog.Create(name));
            CollectionAssert.AreEqual(new[] { 1, 2 }, order);
            Assert.IsFalse(pending.IsCompleted);
            gate.SetResult(0);
            var response = JArray.Parse(await pending);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, order);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, response.Select(r => (int)r["id"]));
            Assert.AreEqual(-32603, (int)response[1]["error"]["code"]);
            Assert.AreEqual(3, (int)response[2]["result"]);
            Assert.AreEqual("", await Run("[" + Request("slow", id: null) + "," + Request("fault", id: null) + "]"));
        }

        [Test]
        public void CompletedDocuments_ReturnCachedTask_IncludingSyncBatches()
        {
            Bind("sync", new Func<int>(() => 7));
            Bind("async", new Func<Task<int>>(() => Task.FromResult(7)), RpcContextFlow.None);
            using var output = new PooledByteBufferWriter();
            foreach (var json in new[] { Request("sync"), Request("async"), "[" + Request("sync") + "," + Request("sync", id: null) + "]" })
            {
                output.Clear();
                var task = JsonRpcProcessor.ProcessAsync(_session, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(json), output);
                Assert.IsTrue(task.IsCompleted);
                Assert.AreSame(Task.CompletedTask, task);
                StringAssert.Contains("\"result\":7", output.ToString());
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task Hooks_ReplaceMethodParamsId_AndRunAfterCompletion(string name)
        {
            var gate = Gate();
            var calls = new List<string>();
            Bind("target", new Func<int, Task<int>>(async value =>
            {
                calls.Add("invoke");
                Assert.AreEqual("changed", Handler.RpcRequestId().GetString());
                await gate.Task;
                Assert.AreEqual("changed", Handler.RpcRequestId().GetString());
                return value;
            }));
            var handler = Handler.GetSessionHandler(_session);
            handler.SetPreProcessHandler((r, c) => { calls.Add("pre"); r.Method = "target"; r.Params = new object[] { 9 }; r.Id = "changed"; return null; });
            handler.SetPostProcessHandler((r, response, c) => { calls.Add("post"); Assert.AreEqual(9, response.Result); return null; });
            var pending = Run(Request("missing"), SerializerCatalog.Create(name));
            CollectionAssert.AreEqual(new[] { "pre", "invoke" }, calls);
            gate.SetResult(0);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":9,\"id\":\"changed\"}", await pending);
            CollectionAssert.AreEqual(new[] { "pre", "invoke", "post" }, calls);
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task ConversionFailures_AreAttributedAfterSuspension(string name)
        {
            Bind("convert", new Func<int, Task<int>>(async value => { await Task.Yield(); throw new FormatException("method failure"); }));
            var s = SerializerCatalog.Create(name);
            Error(await Run(Request("convert", "[1]"), s), -32603);
            var error = JObject.Parse(await Run(Request("convert", "[\"bad\"]"), s))["error"];
            Assert.AreEqual(-32602, (int)error["code"]);
            Assert.AreEqual("value", (string)error["data"]["parameter"]);
            Assert.AreEqual("conversion", (string)error["data"]["reason"]);
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task Cancellation_DiscardsDocument_AndWaitsForTerminalState(int phase)
        {
            using var cts = new CancellationTokenSource();
            var service = new TokenService { Wait = Gate() };
            ServiceBinder.BindService(_session, service);
            int last = 0;
            Bind("cancel", new Func<int>(() => { cts.Cancel(); return 1; }));
            Bind("last", new Func<int>(() => ++last));
            using var output = new PooledByteBufferWriter();
            output.Write((byte)'!');
            string json = phase == 1 ? "[" + Request("cancel") + "," + Request("last") + "]" : Request("token", "[1]");
            if (phase == 0) cts.Cancel();
            var pending = JsonRpcProcessor.ProcessAsync(_session, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(json), output, cancellationToken: cts.Token);
            if (phase == 2)
            {
                Assert.AreEqual(cts.Token, service.Seen);
                cts.Cancel();
                Assert.IsFalse(pending.IsCompleted, "the ignored cancellation cannot return borrowed storage early");
                service.Wait.SetResult(7);
            }
            try { await pending; Assert.Fail("expected canceled task"); }
            catch (OperationCanceledException) { }
            Assert.IsTrue(pending.IsCanceled);
            Assert.AreEqual("!", output.ToString());
            Assert.AreEqual(0, last);
        }

        [Test]
        public async Task MethodOwnedCancellation_IsAnOrdinaryError()
        {
            Bind("run", new Func<Task<int>>(async () => { await Task.Yield(); throw new OperationCanceledException("method-owned"); }));
            Exception seen = null;
            Handler.GetSessionHandler(_session).SetErrorHandler((request, error) => { seen = error.data as Exception; return error; });
            try
            {
                var result = await Run(Request("run"));
                Error(result, -32603);
                StringAssert.Contains("\"data\":null", result, "an unhandled exception is redacted on the wire");
                Assert.IsInstanceOf<OperationCanceledException>(seen, "but it is an ordinary error, not a cancellation of the processing");
                Assert.AreEqual("method-owned", seen.Message);
            }
            finally
            {
                Handler.GetSessionHandler(_session).SetErrorHandler(null);
            }
        }

        [TestCase(false)] [TestCase(true)]
        public async Task Aggregates_OnlySingleInnerIsUnwrapped(bool multiple)
        {
            var authored = new JsonRpcException(-32040, "authored", null);
            Bind("run", new Func<Task<int>>(async () => { await Task.Yield(); throw multiple ? new AggregateException(authored, new Exception("second")) : new AggregateException(authored); }));
            Error(await Run(Request("run")), multiple ? -32603 : -32040);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            internal Segment(ReadOnlyMemory<byte> memory) { Memory = memory; }
            internal Segment Append(ReadOnlyMemory<byte> memory) { var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length }; Next = next; return next; }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public async Task InputOwnership_AndCrossThreadCompletion(int overload)
        {
            var gate = Gate();
            JsonRpcRequestId snapshot = default;
            Bind("run", new Func<Task<int>>(async () => { snapshot = Handler.RpcRequestId(); await gate.Task; Assert.AreEqual(snapshot, Handler.RpcRequestId()); return 7; }));
            byte[] bytes = Encoding.UTF8.GetBytes(Request("run", id: "\"owned\""));
            using var output = new PooledByteBufferWriter();
            Task pending;
            if (overload == 0) pending = JsonRpcProcessor.ProcessAsync(_session, new ReadOnlyMemory<byte>(bytes), output);
            else if (overload == 1) pending = JsonRpcProcessor.ProcessAsync(_session, new ReadOnlySpan<byte>(bytes), output);
            else
            {
                var first = new Segment(bytes.AsMemory(0, 5));
                var last = first.Append(bytes.AsMemory(5));
                pending = JsonRpcProcessor.ProcessAsync(_session, new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), output);
            }
            Assert.IsFalse(pending.IsCompleted);
            if (overload != 0) Array.Fill(bytes, (byte)'?');
            new Thread(() => gate.SetResult(7)).Start();
            await pending;
            Array.Fill(bytes, (byte)'!');
            Assert.AreEqual("owned", snapshot.GetString());
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":\"owned\"}", output.ToString());
            await Run(Request("run", id: "2"));
            StringAssert.Contains("\"id\":\"owned\"", output.ToString());
        }

        private sealed class BrokenResult { public int Value => throw new InvalidOperationException("writer failed"); }
        [TestCaseSource(nameof(Serializers))]
        public async Task ResultWriteFailure_RewindsAfterAwait(string name)
        {
            var gate = Gate();
            Bind("broken", new Func<Task<BrokenResult>>(async () => { await gate.Task; return new BrokenResult(); }));
            Bind("good", new Func<int>(() => 7));
            var pending = Run("[" + Request("broken") + "," + Request("good", id: "2") + "]", SerializerCatalog.Create(name));
            Assert.IsFalse(pending.IsCompleted);
            gate.SetResult(0);
            var result = JArray.Parse(await pending);
            Assert.AreEqual(-32603, (int)result[0]["error"]["code"]);
            Assert.AreEqual(7, (int)result[1]["result"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task PreHookAuthoredException_IsConsumedByAsyncInvocation(string name)
        {
            var gate = Gate();
            Bind("run", new Func<Task<int>>(async () => { await gate.Task; return 7; }));
            var handler = Handler.GetSessionHandler(_session);
            handler.SetPreProcessHandler((r, c) => { Handler.RpcSetException(new JsonRpcException(-32070, "pre error", null)); return null; });
            handler.SetPostProcessHandler((r, response, c) =>
            {
                Assert.AreEqual(-32070, response.Error.code);
                Assert.IsNull(Handler.RpcGetAndRemoveRpcException());
                return null;
            });
            var pending = Run(Request("run"), SerializerCatalog.Create(name));
            gate.SetResult(0);
            Error(await pending, -32070);
        }

        private sealed class AttributeNoneService
        {
            [JsonRpcMethod("none", ContextFlow = RpcContextFlow.None)]
            public Task<int> Get() => Task.FromResult(7);
        }

        [Test]
        public void AttributeSelectsNoneAtRegistration()
        {
            ServiceBinder.BindService(_session, new AttributeNoneService());
            Assert.AreEqual(RpcContextFlow.None, Handler.GetSessionHandler(_session).MetaData.Services["none"].Method.ContextFlow);
        }

        [Test]
        public async Task DefaultSessionStringConvenience()
        {
            string name = _session + "-default";
            ServiceBinder.BindMethod(name, new Func<ValueTask<int>>(() => new ValueTask<int>(7)));
            try { Assert.AreEqual(7, (int)JObject.Parse(await JsonRpcProcessor.ProcessAsync(Request(name)))["result"]); }
            finally { ServiceBinder.UnbindMethod(name); }
        }

        private sealed class TrackingSerializer : JsonRpcSerializer
        {
            private readonly JsonRpcSerializer _inner;
            internal readonly List<TrackingReader> Readers = new List<TrackingReader>();
            internal Action OnWrite;
            internal TrackingSerializer(JsonRpcSerializer inner) { _inner = inner; }
            public override string Name => "tracking";
            public override JsonRpcRequestReader CreateReader() { var reader = new TrackingReader(_inner.CreateReader()); Readers.Add(reader); return reader; }
            public override T Read<T>(ReadOnlySpan<byte> bytes) => _inner.Read<T>(bytes);
            public override object Read(ReadOnlySpan<byte> bytes, Type type) => _inner.Read(bytes, type);
            public override void Write<T>(IBufferWriter<byte> output, T value) { OnWrite?.Invoke(); _inner.Write(output, value); }
            public override void Write(IBufferWriter<byte> output, object value, Type type) { OnWrite?.Invoke(); _inner.Write(output, value, type); }
        }

        private sealed class TrackingReader : JsonRpcRequestReader
        {
            private readonly JsonRpcRequestReader _inner;
            internal bool Active;
            internal int Reads;
            internal int Releases;
            private readonly byte[] _idBuffer = new byte[128];
            internal TrackingReader(JsonRpcRequestReader inner) { _inner = inner; }
            private JsonRpcRequestReader Current { get { Assert.IsTrue(Active, "reader lease must still be active"); return _inner; } }
            public override bool TryParse(ReadOnlyMemory<byte> bytes, out string error) { Assert.IsFalse(Active); Active = true; return _inner.TryParse(bytes, out error); }
            public override ReadOnlyMemory<byte> Document => Current.Document;
            public override bool IsBatch => Current.IsBatch;
            public override int Count => Current.Count;
            public override bool Select(int index) => Current.Select(index);
            public override bool HasMethod => Current.HasMethod;
            public override string Method => Current.Method;
            public override ReadOnlySpan<byte> MethodUtf8 { get { Array.Fill(_idBuffer, (byte)'?'); return Current.MethodUtf8; } }
            public override JsonRpcIdKind IdKind => Current.IdKind;
            public override ReadOnlySpan<byte> IdRaw { get { var id = Current.IdRaw; id.CopyTo(_idBuffer); return _idBuffer.AsSpan(0, id.Length); } }
            public override object IdValue => Current.IdValue;
            public override JsonRpcParamsKind ParamsKind => Current.ParamsKind;
            public override int ParamCount => Current.ParamCount;
            public override ReadOnlySpan<byte> ParamNameUtf8(int i) => Current.ParamNameUtf8(i);
            public override ReadOnlySpan<byte> ParamRaw(int i) => Current.ParamRaw(i);
            public override bool ParamIsNull(int i) => Current.ParamIsNull(i);
            public override T ReadParam<T>(int i) { Reads++; return Current.ReadParam<T>(i); }
            public override object ReadParam(int i, Type type) { Reads++; return Current.ReadParam(i, type); }
            public override object ParamsValue => Current.ParamsValue;
            public override void Release() { Assert.IsTrue(Active); Active = false; Releases++; _inner.Release(); }
        }

        [TestCaseSource(nameof(Serializers))]
        public async Task CustomReader_AndRentedMapRemainLeasedThroughFailure(string name)
        {
            var gate = Gate();
            Bind("read", new Func<int, int, Task<int>>(async (a, b) => { await gate.Task; throw new FormatException("after binding"); }));
            var serializer = new TrackingSerializer(SerializerCatalog.Create(name));
            var pending = Run(Request("read", "{\"b\":2,\"a\":1}", "\"escaped\\\"id\""), serializer);
            var reader = serializer.Readers.Single();
            Assert.IsTrue(reader.Active);
            Assert.AreEqual(0, reader.Releases);
            gate.SetResult(0);
            var json = await pending;
            Error(json, -32603);
            StringAssert.EndsWith("\"id\":\"escaped\\\"id\"}", json);
            Assert.GreaterOrEqual(reader.Reads, 4, "binding re-read after suspended conversion-shaped failure");
            Assert.IsFalse(reader.Active);
            Assert.AreEqual(1, reader.Releases);
            using var cts = new CancellationTokenSource(); cts.Cancel();
            try { await Run(Request("read"), serializer, token: cts.Token); } catch (OperationCanceledException) { }
            Assert.AreEqual(1, reader.Releases, "pre-cancellation must not release an idle cached reader again");
        }

        [Test]
        public async Task CancellationDuringResultWrite_DiscardsStaging()
        {
            using var cts = new CancellationTokenSource();
            var serializer = new TrackingSerializer(SerializerCatalog.Create("jsmn")) { OnWrite = cts.Cancel };
            Bind("run", new Func<Task<int>>(async () => { await Task.Yield(); return 7; }));
            using var output = new PooledByteBufferWriter();
            var pending = JsonRpcProcessor.ProcessAsync(_session, (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(Request("run")), output, serializer: serializer, cancellationToken: cts.Token);
            try { await pending; Assert.Fail("expected cancellation"); } catch (OperationCanceledException) { }
            Assert.IsTrue(pending.IsCanceled);
            Assert.AreEqual(0, output.WrittenCount);
        }

        [Test]
        public async Task NoneNestedInFlow_DoesNotInheritParentAmbient()
        {
            var gate = Gate();
            Bind("none", new Func<Task<int>>(async () =>
            {
                Assert.AreEqual("child", Handler.RpcContext());
                await gate.Task;
                Assert.IsNull(Handler.RpcContext());
                Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
                return 7;
            }), RpcContextFlow.None);
            Bind("flow", new Func<Task<int>>(async () =>
            {
                var child = Run(Request("none", id: "2"), context: "child");
                Assert.AreEqual("parent", Handler.RpcContext());
                gate.SetResult(1);
                Assert.AreEqual(7, (int)JObject.Parse(await child)["result"]);
                Assert.AreEqual("parent", Handler.RpcContext());
                return 7;
            }));
            Assert.AreEqual(7, (int)JObject.Parse(await Run(Request("flow"), context: "parent"))["result"]);
        }

        [Test]
        public async Task AsyncStartedInsideSync_DoesNotCaptureReusableParentFrame()
        {
            var gate = Gate();
            Task<string> child = null;
            Bind("child", new Func<Task<int>>(async () =>
            {
                Assert.AreEqual("child", Handler.RpcContext());
                await gate.Task;
                Assert.AreEqual("child", Handler.RpcContext());
                Assert.AreEqual(JsonRpcRequestId.FromInt64(2), Handler.RpcRequestId());
                return 7;
            }));
            Bind("parent", new Func<int>(() =>
            {
                child = Run(Request("child", id: "2"), context: "child");
                Assert.AreEqual("parent", Handler.RpcContext());
                Handler.RpcSetException(new JsonRpcException(-32050, "parent", null));
                return 1;
            }));
            Error(JsonRpcProcessor.ProcessSync(_session, Request("parent"), "parent"), -32050);
            gate.SetResult(0);
            Assert.AreEqual(7, (int)JObject.Parse(await child)["result"]);
        }

        [Test]
        public async Task ConcurrentDocuments_OwnIndependentFramesAndScratch()
        {
            var gate = Gate();
            Bind("run", new Func<Task<int>>(async () =>
            {
                int value = (int)Handler.RpcContext();
                var id = Handler.RpcRequestId();
                await gate.Task;
                Assert.AreEqual(value, Handler.RpcContext());
                Assert.AreEqual(id, Handler.RpcRequestId());
                return value;
            }));
            var tasks = Enumerable.Range(0, 80).Select(i => Run(Request("run", id: i.ToString()), context: i)).ToArray();
            Assert.IsTrue(tasks.All(t => !t.IsCompleted));
            gate.SetResult(0);
            var results = await Task.WhenAll(tasks);
            for (int i = 0; i < results.Length; i++)
            {
                var response = JObject.Parse(results[i]);
                Assert.AreEqual(i, (int)response["id"]);
                Assert.AreEqual(i, (int)response["result"]);
            }
        }

        private static int SyncToken([JsonRpcCancellation] CancellationToken token) => token.CanBeCanceled ? 7 : 0;
        private delegate int SyncRefTokenDelegate(CancellationToken token, ref JsonRpcException error);
        private static int SyncRefToken([JsonRpcCancellation] CancellationToken token, ref JsonRpcException error)
        {
            if (token.CanBeCanceled) error = new JsonRpcException(-32060, "token injected", null);
            return 0;
        }

        [Test]
        public async Task SynchronousMethod_CanRequestInjectedCancellation()
        {
            Bind("token", new Func<CancellationToken, int>(SyncToken));
            Bind("ref", new SyncRefTokenDelegate(SyncRefToken));
            using var cts = new CancellationTokenSource();
            Assert.AreEqual(7, (int)JObject.Parse(await Run(Request("token"), token: cts.Token))["result"]);
            Assert.AreEqual(0, (int)JObject.Parse(JsonRpcProcessor.ProcessSync(_session, Request("token"), null))["result"]);
            Error(await Run(Request("ref"), token: cts.Token), -32060);
            Assert.AreEqual(0, Handler.GetSessionHandler(_session).MetaData.Services["token"].parameters.Length);
        }

        private static IEnumerable<TestCaseData> PrimitiveResults()
        {
            foreach (bool suspend in new[] { false, true })
            foreach (var type in new[] { typeof(int), typeof(long), typeof(double), typeof(float), typeof(bool), typeof(decimal), typeof(string), typeof(int?), typeof(long?), typeof(double?), typeof(float?), typeof(bool?), typeof(decimal?), typeof(Guid) })
                yield return new TestCaseData(type, suspend);
        }
        private static Task<T> ResultTask<T>(T value, bool suspend) => suspend ? Delayed(value) : Task.FromResult(value);
        private static async Task<T> Delayed<T>(T value) { await Task.Yield(); return value; }

        [TestCaseSource(nameof(PrimitiveResults))]
        public async Task TypedWriters_HandlePrimitiveNullableAndFallbackResults(Type type, bool suspend)
        {
            object value = type == typeof(string) ? "text" : type == typeof(Guid) ? Guid.Parse("00112233-4455-6677-8899-aabbccddeeff") : Convert.ChangeType(1, Nullable.GetUnderlyingType(type) ?? type);
            var factory = GetType().GetMethod(nameof(MakeResultDelegate), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).MakeGenericMethod(type);
            Bind("run", (Delegate)factory.Invoke(null, new[] { value, (object)suspend }));
            var result = JObject.Parse(await Run(Request("run")))["result"];
            Assert.IsNotNull(result);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(SerializerCatalog.Create("jsmn").Serialize(value, type)), result));
        }
        private static Delegate MakeResultDelegate<T>(object value, bool suspend) => new Func<Task<T>>(() => ResultTask((T)value, suspend));

        [Test]
        public void CompletedNone_AllocatesZero_AndFlowIsMeasured()
        {
            var cached = Task.FromResult(7);
            Bind("none", new Func<Task<int>>(() => cached), RpcContextFlow.None);
            Bind("flow", new Func<Task<int>>(() => cached));
            long none = MeasureAllocations(Request("none"));
            long flow = MeasureAllocations(Request("flow"));
            Assert.AreEqual(0, none, "undivided total across 2000 requests");
            Assert.Greater(flow, 0);
            TestContext.WriteLine("Completed Task<int>: None = " + none + " B / 2000 requests; Flow = " + flow + " B / 2000 requests (" + flow / 2000 + " B/request).");
        }

        private long MeasureAllocations(string request)
        {
            var input = (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(request);
            using var output = new PooledByteBufferWriter(256);
            for (int i = 0; i < 500; i++) { output.Clear(); JsonRpcProcessor.ProcessAsync(_session, input, output).GetAwaiter().GetResult(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) { output.Clear(); JsonRpcProcessor.ProcessAsync(_session, input, output).GetAwaiter().GetResult(); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
