using System;
using System.Text;
using System.Threading;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Request id access from inside a method (issue #56): the raw slice, the kind and the owned snapshot, read
    /// from the invocation frame on demand. Nothing is decoded or allocated unless a method asks; the numeric
    /// request stays allocation-free whether or not it reads the id. Every scenario runs against the three serializers.
    /// </summary>
    [TestFixture]
    public class RequestIdTests
    {
        private const string Session = "request-id";
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };
        private static JsonRpcSerializer _current;

        private class IdService
        {
            public static JsonRpcRequestId Captured;
            public static string CapturedRaw;
            public static JsonRpcIdKind CapturedKind;

            [JsonRpcMethod("rid.kind")]
            public string Kind() => Handler.RpcRequestIdKind().ToString();

            [JsonRpcMethod("rid.raw")]
            public string Raw() => Encoding.UTF8.GetString(Handler.RpcRequestIdRaw().ToArray());

            [JsonRpcMethod("rid.describe")]
            public string Describe() => DescribeId(JsonRpcContext.CurrentRequestId());

            [JsonRpcMethod("rid.int64")]
            public long Int64() => Handler.RpcRequestId().TryGetInt64(out var v) ? v : -1;

            /// <summary>Reads everything that must be free for an integer id: raw, kind and the snapshot.</summary>
            [JsonRpcMethod("rid.touch")]
            public int Touch() => Handler.RpcRequestIdRaw().Length + (int)Handler.RpcRequestIdKind() * 100 + (Handler.RpcRequestId().IsAbsent ? 0 : 1000);

            /// <summary>Reads what must be free for any id: raw and kind (a string snapshot allocates its string).</summary>
            [JsonRpcMethod("rid.rawLength")]
            public int RawLength() => Handler.RpcRequestIdRaw().Length + (int)Handler.RpcRequestIdKind() * 100;

            [JsonRpcMethod("rid.capture")]
            public int Capture()
            {
                Captured = Handler.RpcRequestId();
                CapturedRaw = Encoding.UTF8.GetString(Handler.RpcRequestIdRaw().ToArray());
                CapturedKind = Handler.RpcRequestIdKind();
                return 1;
            }

            /// <summary>A parameter named id binds from params only; the envelope id is not injected.</summary>
            [JsonRpcMethod("rid.echoParam")]
            public string EchoParam(int id) => id + "/" + Handler.RpcRequestId();

            /// <summary>Snapshot before, dispatch another request synchronously, compare after.</summary>
            [JsonRpcMethod("rid.nested")]
            public string Nested()
            {
                var before = Handler.RpcRequestId();
                var inner = JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"rid.describe\",\"id\":\"inner-id\"}", null, _current);
                var after = Handler.RpcRequestId();
                return DescribeId(before) + " | " + inner + " | " + (before == after ? "same" : "changed:" + DescribeId(after));
            }

            /// <summary>The frame is per thread: another thread sees no request.</summary>
            [JsonRpcMethod("rid.otherThread")]
            public string OtherThread()
            {
                var snapshot = Handler.RpcRequestId();
                string seen = null;
                var t = new Thread(() => seen = Handler.RpcRequestIdKind() + "/" + Handler.RpcRequestIdRaw().Length + "/" + DescribeId(snapshot));
                t.Start();
                t.Join();
                return seen;
            }

            [JsonRpcMethod("rid.plain")]
            public int Plain(int n) => n * 2;

            [JsonRpcMethod("rid.throws")]
            public int Throws() => throw new InvalidOperationException("boom");
        }

        private static string DescribeId(JsonRpcRequestId id)
        {
            switch (id.Kind)
            {
                case JsonRpcIdKind.Integer:
                    return id.TryGetInt64(out var v) ? "int64:" + v : "bigint:" + id.GetIntegerText();
                case JsonRpcIdKind.String:
                    return "string:" + id.GetString();
                default:
                    return id.Kind.ToString();
            }
        }

        [OneTimeSetUp]
        public void Bind()
        {
            ServiceBinder.BindService(Session, new IdService());
        }

        [OneTimeTearDown]
        public void Destroy()
        {
            Handler.DestroySession(Session);
        }

        private static string Run(string json, JsonRpcSerializer serializer, object context = null)
        {
            return JsonRpcProcessor.ProcessSync(Session, json, context, serializer);
        }

        private static string Result(string json, JsonRpcSerializer serializer)
        {
            var response = Run(json, serializer);
            var parsed = JObject.Parse(response);
            Assert.IsNull(parsed["error"], response);
            return (string)parsed["result"];
        }

        // ------------------------------------------------------------------ the semantics table

        [TestCaseSource(nameof(Serializers))]
        public void IntegerId(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("Integer", Result("{\"method\":\"rid.kind\",\"id\":42}", s));
            Assert.AreEqual("42", Result("{\"method\":\"rid.raw\",\"id\":42}", s));
            Assert.AreEqual("int64:42", Result("{\"method\":\"rid.describe\",\"id\":42}", s));
            Assert.AreEqual("int64:-7", Result("{\"method\":\"rid.describe\",\"id\":-7}", s));
            Assert.AreEqual("int64:0", Result("{\"method\":\"rid.describe\",\"id\":0}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":42,\"id\":42}", Run("{\"method\":\"rid.int64\",\"id\":42}", s));
        }

        [TestCaseSource(nameof(Serializers))]
        public void Int64Boundaries(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("int64:9223372036854775807", Result("{\"method\":\"rid.describe\",\"id\":9223372036854775807}", s));
            Assert.AreEqual("int64:-9223372036854775808", Result("{\"method\":\"rid.describe\",\"id\":-9223372036854775808}", s));
        }

        [TestCaseSource(nameof(Serializers))]
        public void OversizedInteger_KeepsItsDigits(string name)
        {
            var s = SerializerCatalog.Create(name);
            const string big = "123456789012345678901234567890";
            Assert.AreEqual("Integer", Result("{\"method\":\"rid.kind\",\"id\":" + big + "}", s));
            Assert.AreEqual(big, Result("{\"method\":\"rid.raw\",\"id\":" + big + "}", s));
            Assert.AreEqual("bigint:" + big, Result("{\"method\":\"rid.describe\",\"id\":" + big + "}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":-1,\"id\":" + big + "}", Run("{\"method\":\"rid.int64\",\"id\":" + big + "}", s), "TryGetInt64 is false, the id is still echoed byte for byte");
        }

        [TestCaseSource(nameof(Serializers))]
        public void StringId(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("String", Result("{\"method\":\"rid.kind\",\"id\":\"abc\"}", s));
            Assert.AreEqual("\"abc\"", Result("{\"method\":\"rid.raw\",\"id\":\"abc\"}", s), "raw keeps the quotes");
            Assert.AreEqual("string:abc", Result("{\"method\":\"rid.describe\",\"id\":\"abc\"}", s));
            Assert.AreEqual("string:", Result("{\"method\":\"rid.describe\",\"id\":\"\"}", s));
            Assert.AreEqual("string:123", Result("{\"method\":\"rid.describe\",\"id\":\"123\"}", s), "a string of digits is a string");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":-1,\"id\":\"123\"}", Run("{\"method\":\"rid.int64\",\"id\":\"123\"}", s));
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedStringId_IsDecoded_RawIsNot(string name)
        {
            var s = SerializerCatalog.Create(name);
            const string request = "{\"method\":\"rid.describe\",\"id\":\"a\\\"b\\u00e9\\n\"}";
            Assert.AreEqual("string:a\"bé\n", Result(request, s));
            Assert.AreEqual("\"a\\\"b\\u00e9\\n\"", Result("{\"method\":\"rid.raw\",\"id\":\"a\\\"b\\u00e9\\n\"}", s), "raw is the request's own JSON");
        }

        [TestCaseSource(nameof(Serializers))]
        public void NullId_IsItsOwnKind(string name)
        {
            var s = SerializerCatalog.Create(name);
            IdService.Captured = JsonRpcRequestId.FromInt64(-1);
            var response = Run("{\"method\":\"rid.capture\",\"id\":null}", s);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":null}", response);
            Assert.AreEqual(JsonRpcIdKind.Null, IdService.Captured.Kind);
            Assert.IsTrue(IdService.Captured.IsNull);
            Assert.IsFalse(IdService.Captured.IsAbsent);
            Assert.AreEqual("null", IdService.CapturedRaw);
            Assert.AreEqual(JsonRpcIdKind.Null, IdService.CapturedKind);
            Assert.AreEqual(JsonRpcRequestId.Null, IdService.Captured);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Notification_HasAbsentId(string name)
        {
            var s = SerializerCatalog.Create(name);
            IdService.Captured = JsonRpcRequestId.FromInt64(-1);
            IdService.CapturedRaw = "?";
            Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"rid.capture\"}", s), "no response for a notification");
            Assert.AreEqual(JsonRpcIdKind.Absent, IdService.Captured.Kind);
            Assert.IsTrue(IdService.Captured.IsAbsent);
            Assert.AreEqual("", IdService.CapturedRaw);
            Assert.AreEqual(JsonRpcIdKind.Absent, IdService.CapturedKind);
            Assert.AreEqual(default(JsonRpcRequestId), IdService.Captured);
        }

        [TestCaseSource(nameof(Serializers))]
        public void FractionalId_IsRejectedBeforeTheMethodRuns(string name)
        {
            var s = SerializerCatalog.Create(name);
            IdService.Captured = JsonRpcRequestId.FromInt64(-1);
            var response = JObject.Parse(Run("{\"method\":\"rid.capture\",\"id\":1.5}", s));
            Assert.AreEqual(-32600, (int)response["error"]["code"]);
            Assert.AreEqual(-1, IdService.Captured.ToObject(), "the method did not run");
        }

        [Test]
        public void LenientSingleQuotedId_IsNormalisedToJson()
        {
            var s = SerializerCatalog.Create("newtonsoft");
            Assert.AreEqual("\"x\"", Result("{\"method\":\"rid.raw\",\"id\":'x'}", s), "raw is the normalised JSON string, not the single-quoted source");
            Assert.AreEqual("string:x", Result("{\"method\":\"rid.describe\",\"id\":'x'}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"String\",\"id\":\"x\"}", Run("{\"method\":\"rid.kind\",\"id\":'x'}", s));
        }

        [Test]
        public void OutsideAnInvocation_NothingIsReported()
        {
            Assert.AreEqual(JsonRpcIdKind.Absent, Handler.RpcRequestIdKind());
            Assert.AreEqual(0, Handler.RpcRequestIdRaw().Length);
            Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
            Assert.IsTrue(JsonRpcContext.CurrentRequestId().IsAbsent);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ParameterNamedId_BindsFromParamsOnly(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("5/77", Result("{\"method\":\"rid.echoParam\",\"params\":{\"id\":5},\"id\":77}", s));
            Assert.AreEqual("5/77", Result("{\"method\":\"rid.echoParam\",\"params\":[5],\"id\":77}", s));
            var response = JObject.Parse(Run("{\"method\":\"rid.echoParam\",\"params\":{},\"id\":77}", s));
            Assert.AreEqual(-32602, (int)response["error"]["code"], "the envelope id is never injected into a parameter");
        }

        // ------------------------------------------------------------------ frames, hooks, threads

        [TestCaseSource(nameof(Serializers))]
        public void NestedDispatch_RestoresTheOuterId(string name)
        {
            var s = SerializerCatalog.Create(name);
            _current = s;
            Assert.AreEqual("int64:9 | {\"jsonrpc\":\"2.0\",\"result\":\"string:inner-id\",\"id\":\"inner-id\"} | same",
                Result("{\"method\":\"rid.nested\",\"id\":9}", s));
            Assert.IsTrue(Handler.RpcRequestId().IsAbsent, "nothing leaks out of the invocation");
        }

        [TestCaseSource(nameof(Serializers))]
        public void NestedDispatch_RestoresTheOuterId_WithHooks(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => null);
                handler.SetPostProcessHandler((request, response, context) => null);
                NestedDispatch_RestoresTheOuterId(name);
            }
            finally
            {
                handler.SetPreProcessHandler(null);
                handler.SetPostProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void MethodException_RestoresTheFrame(string name)
        {
            var s = SerializerCatalog.Create(name);
            var response = JObject.Parse(Run("{\"method\":\"rid.throws\",\"id\":3}", s));
            Assert.AreEqual(-32603, (int)response["error"]["code"]);
            Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
            Assert.AreEqual(0, Handler.RpcRequestIdRaw().Length);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Hooks_LeavingTheIdAlone_SeeTheSameId(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => null);
                Assert.AreEqual("int64:42", Result("{\"method\":\"rid.describe\",\"id\":42}", s));
                Assert.AreEqual("string:abc", Result("{\"method\":\"rid.describe\",\"id\":\"abc\"}", s));
                Assert.AreEqual("\"abc\"", Result("{\"method\":\"rid.raw\",\"id\":\"abc\"}", s));
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void Hooks_ReplacingTheId_TheMethodSeesTheReplacement(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => { request.Id = "changed"; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"string:changed\",\"id\":\"changed\"}", Run("{\"method\":\"rid.describe\",\"id\":1}", s));
                Assert.AreEqual("\"changed\"", Result("{\"method\":\"rid.raw\",\"id\":1}", s));

                handler.SetPreProcessHandler((request, context) => { request.Id = 99L; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"int64:99\",\"id\":99}", Run("{\"method\":\"rid.describe\",\"id\":\"x\"}", s));

                handler.SetPreProcessHandler((request, context) => { request.Id = null; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"Null\",\"id\":null}", Run("{\"method\":\"rid.describe\",\"id\":5}", s));

                // a replacement JSON-RPC does not allow is rejected before the method runs
                handler.SetPreProcessHandler((request, context) => { request.Id = 1.5; return null; });
                IdService.Captured = JsonRpcRequestId.FromInt64(-1);
                var response = JObject.Parse(Run("{\"method\":\"rid.capture\",\"id\":5}", s));
                Assert.AreEqual(-32600, (int)response["error"]["code"], response.ToString());
                Assert.AreEqual(-1, IdService.Captured.ToObject(), "the method did not run");
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void Snapshot_SurvivesLaterRequestsAndOtherThreads(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":\"first\"}", Run("{\"method\":\"rid.capture\",\"id\":\"first\"}", s));
            var snapshot = IdService.Captured;
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":2}", Run("{\"method\":\"rid.capture\",\"id\":2}", s));
            Assert.AreEqual("string:first", DescribeId(snapshot), "the snapshot owns its value; the reader has moved on");
            Assert.AreEqual("int64:2", DescribeId(IdService.Captured));

            Assert.AreEqual("Absent/0/int64:11", Result("{\"method\":\"rid.otherThread\",\"id\":11}", s), "the frame is per thread; the snapshot travels");
        }

        [TestCaseSource(nameof(Serializers))]
        public void Batch_EachRequestSeesItsOwnId(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":\"int64:1\",\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":\"string:two\",\"id\":\"two\"},{\"jsonrpc\":\"2.0\",\"result\":\"Null\",\"id\":null}]",
                Run("[{\"method\":\"rid.describe\",\"id\":1},{\"method\":\"rid.describe\"},{\"method\":\"rid.describe\",\"id\":\"two\"},{\"method\":\"rid.describe\",\"id\":null}]", s));
        }

        [Test]
        public void HandleJsonRequest_SeesTheId()
        {
            var handler = Handler.GetSessionHandler(Session);
            var response = handler.Handle(new JsonRequest("rid.describe", null, "boxed"));
            Assert.IsNull(response.Error);
            Assert.AreEqual("string:boxed", response.Result);
            Assert.AreEqual("boxed", response.Id);
            response = handler.Handle(new JsonRequest("rid.describe", null, 12L));
            Assert.AreEqual("int64:12", response.Result);
        }

        // ------------------------------------------------------------------ the struct itself

        [Test]
        public void Struct_EqualityAndConversions()
        {
            Assert.AreEqual(JsonRpcRequestId.FromInt64(5), JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("5")));
            Assert.AreEqual(JsonRpcRequestId.FromString("5"), JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("\"5\"")));
            Assert.AreNotEqual(JsonRpcRequestId.FromInt64(5), JsonRpcRequestId.FromString("5"));
            Assert.AreEqual(JsonRpcRequestId.Null, JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("null")));
            Assert.AreEqual(JsonRpcRequestId.Absent, JsonRpcRequestId.FromRaw(ReadOnlySpan<byte>.Empty));
            Assert.AreEqual(JsonRpcIdKind.Invalid, JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("1.5")).Kind);
            Assert.AreEqual(JsonRpcRequestId.FromInt64(5).GetHashCode(), JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("5")).GetHashCode());

            var big = JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("99999999999999999999"));
            Assert.IsTrue(big.IsInteger);
            Assert.IsFalse(big.TryGetInt64(out _));
            Assert.AreEqual("99999999999999999999", big.GetIntegerText());
            Assert.AreEqual("99999999999999999999", big.ToObject());
            Assert.AreEqual(big, JsonRpcRequestId.FromRaw(Encoding.UTF8.GetBytes("99999999999999999999")));

            Assert.AreEqual(5L, JsonRpcRequestId.FromInt64(5).ToObject());
            Assert.AreEqual("5", JsonRpcRequestId.FromInt64(5).GetIntegerText());
            Assert.IsNull(JsonRpcRequestId.FromInt64(5).GetString());
            Assert.AreEqual("x", JsonRpcRequestId.FromString("x").ToObject());
            Assert.IsNull(JsonRpcRequestId.FromString("x").GetIntegerText());
            Assert.IsNull(JsonRpcRequestId.Null.ToObject());
            Assert.AreEqual("5", JsonRpcRequestId.FromInt64(5).ToString());
            Assert.AreEqual("x", JsonRpcRequestId.FromString("x").ToString());
            Assert.AreEqual("null", JsonRpcRequestId.Null.ToString());
            Assert.AreEqual("", JsonRpcRequestId.Absent.ToString());

            Assert.AreEqual(JsonRpcRequestId.FromInt64(7), JsonRpcRequestId.FromObject(7));
            Assert.AreEqual(JsonRpcRequestId.FromInt64(7), JsonRpcRequestId.FromObject(7L));
            Assert.AreEqual(JsonRpcRequestId.FromString("s"), JsonRpcRequestId.FromObject("s"));
            Assert.AreEqual(JsonRpcRequestId.Null, JsonRpcRequestId.FromObject(null));
            Assert.AreEqual(JsonRpcIdKind.Invalid, JsonRpcRequestId.FromObject(1.5).Kind);

            var w = new AustinHarris.JsonRpc.Serialization.PooledByteBufferWriter(64);
            JsonRpcRequestId.FromString("a\"b").WriteTo(w);
            Assert.AreEqual("\"a\\\"b\"", w.ToString());
            w.Clear(); big.WriteTo(w);
            Assert.AreEqual("99999999999999999999", w.ToString());
            w.Clear(); JsonRpcRequestId.FromInt64(-3).WriteTo(w);
            Assert.AreEqual("-3", w.ToString());
            w.Clear(); JsonRpcRequestId.Absent.WriteTo(w);
            Assert.AreEqual("null", w.ToString());
            w.Dispose();
        }

        // ------------------------------------------------------------------ zero cost

        /// <summary>
        /// The numeric request on the built-in serializer allocates nothing today; reading the raw id, the kind or the
        /// integer snapshot must keep it that way, and so must not reading it. A string snapshot allocates exactly its string.
        /// </summary>
        [Test]
        public void BuiltInSerializer_ReadingTheId_AllocatesNothing()
        {
            var s = SerializerCatalog.Create("jsmn");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1202,\"id\":42}", Run("{\"jsonrpc\":\"2.0\",\"method\":\"rid.touch\",\"id\":42}", s), "2 raw bytes, Integer, present");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.int64\",\"id\":42}", s), "integer snapshot");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.touch\",\"id\":42}", s), "raw + kind + snapshot");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.touch\",\"id\":-9223372036854775808}", s), "the widest integer");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.touch\",\"id\":null}", s), "null id");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.touch\"}", s), "notification");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.rawLength\",\"id\":\"abc\"}", s), "a string id that is not decoded");
            Assert.AreEqual(0, MeasureAllocations("{\"jsonrpc\":\"2.0\",\"method\":\"rid.plain\",\"params\":[5],\"id\":\"s\"}", s), "a method that never reads the id: no cost");
        }

        private static long MeasureAllocations(string request, JsonRpcSerializer serializer)
        {
            var input = Encoding.UTF8.GetBytes(request);
            using (var output = new AustinHarris.JsonRpc.Serialization.PooledByteBufferWriter(256))
            {
                var memory = new ReadOnlyMemory<byte>(input);
                for (int i = 0; i < 500; i++) { output.Clear(); JsonRpcProcessor.Process(Session, memory, output, null, serializer); }
                const int reps = 2000;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < reps; i++) { output.Clear(); JsonRpcProcessor.Process(Session, memory, output, null, serializer); }
                // an undivided total: one allocation per request would show as reps times its size
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }
    }
}
