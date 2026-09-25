using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Dispatch and handler hardening from the 2.0 review: pre-process mutations are dispatched (4), nested
    /// calls keep their invocation frame (5), exception details are redacted by default (6), hook
    /// materialisation stays inside the error boundary (7), SMD service edits are honoured (12), notifications
    /// never answer (13), batches always answer with an array (14), async methods require async processing (16),
    /// unknown named parameters are rejected (19) and the Process overloads bind to the intended session (20).
    /// Every scenario runs against the three serializers, passed explicitly to the processor.
    /// </summary>
    [TestFixture]
    public class DispatchHardeningTests
    {
        private const string Session = "dispatch-hardening";

        /// <summary>The serializer of the scenario in flight; the nested-call method needs it.</summary>
        private static JsonRpcSerializer _current;

        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };

        private class HardeningService
        {
            public static object ContextAfterNested;
            public static string InnerResponse;

            [JsonRpcMethod("ping")]
            public int Ping() => 7;

            [JsonRpcMethod("echo")]
            public string Echo(string s) => s;

            [JsonRpcMethod("unserializable")]
            public Unserializable Unserializable() => new Unserializable();

            [JsonRpcMethod("accept")]
            public int Accept(object o) => 7;

            [JsonRpcMethod("optional")]
            public int Optional(int a = 9) => a;

            [JsonRpcMethod("sum")]
            public int Sum(int a, int b) => a + b;

            [JsonRpcMethod("frac")]
            public double Frac(double d) => d;

            [JsonRpcMethod("ctx")]
            public string Ctx() => (string)Handler.RpcContext();

            [JsonRpcMethod("nested")]
            public string Nested()
            {
                Handler.RpcSetException(new JsonRpcException(-32001, "outer failure", null));
                InnerResponse = JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"ctx\",\"id\":2}", "inner", _current);
                ContextAfterNested = Handler.RpcContext();
                return "outer result";
            }

            [JsonRpcMethod("throws")]
            public int Throws() => throw new InvalidOperationException("private database /server/secret");

            /// <summary>
            /// Three levels deep. The dispatcher reports the inner exception of a wrapped one (legacy behaviour),
            /// so the client sees the ArgumentException; its own InnerException is the redaction test's chain.
            /// </summary>
            [JsonRpcMethod("throwsInner")]
            public int ThrowsInner()
            {
                try
                {
                    try
                    {
                        throw new Exception("innermost message");
                    }
                    catch (Exception innermost)
                    {
                        throw new ArgumentException("inner message", innermost);
                    }
                }
                catch (Exception inner)
                {
                    throw new InvalidOperationException("outer message", inner);
                }
            }

            [JsonRpcMethod("rpcex")]
            public int RpcEx() => throw new JsonRpcException(-32005, "application error", "authored data");
        }

        private class SessionProbe
        {
            private readonly string _name;
            public SessionProbe(string name) { _name = name; }

            [JsonRpcMethod("dh.whichSession")]
            public string WhichSession() => _name;
        }

        /// <summary>A result every serializer fails to write: the getter throws.</summary>
        public class Unserializable
        {
            public string Secret => throw new InvalidOperationException("private database /server/secret");
        }

        private class TaskReturningService
        {
            [JsonRpcMethod("asyncTask")]
            public Task<int> AsyncTask() => Task.FromResult(7);
        }

        private class ValueTaskReturningService
        {
            [JsonRpcMethod("asyncValueTask")]
            public ValueTask<int> AsyncValueTask() => new ValueTask<int>(7);
        }

        private class PlainTaskReturningService
        {
            [JsonRpcMethod("asyncPlain")]
            public Task AsyncPlain() => Task.CompletedTask;
        }

        private class PlainValueTaskReturningService
        {
            [JsonRpcMethod("asyncPlainValue")]
            public ValueTask AsyncPlainValue() => default;
        }

        private class AsyncVoidService
        {
            [JsonRpcMethod("asyncVoid")]
            public async void AsyncVoid() => await Task.Yield();
        }

        [OneTimeSetUp]
        public void BindServices()
        {
            ServiceBinder.BindService(Session, new HardeningService());
            ServiceBinder.BindService(Session, new SessionProbe(Session));
            ServiceBinder.BindService(Handler.DefaultSessionId(), new SessionProbe("default"));
        }

        [OneTimeTearDown]
        public void DestroySessions()
        {
            Handler.DestroySession(Session);
#pragma warning disable CS0618
            Handler.DefaultHandler.UnRegisterFunction("dh.whichSession");
#pragma warning restore CS0618
        }

        private static string Run(string json, object context = null, JsonRpcSerializer serializer = null, string session = Session)
        {
            return JsonRpcProcessor.ProcessSync(session, json, context, serializer);
        }

        private static JObject Parse(string response)
        {
            Assert.IsFalse(string.IsNullOrEmpty(response), "expected a response");
            return JObject.Parse(response);
        }

        // ------------------------------------------------------------------ 4: pre-process mutations

        [TestCaseSource(nameof(Serializers))]
        public void PreProcessHandler_ReplacingMethodAndParams_ChangesWhatRuns(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) =>
                {
                    request.Method = "ping";
                    request.Params = null;
                    return null;
                });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}", Run("{\"method\":\"echo\",\"params\":[\"original\"],\"id\":1}", null, serializer));

                handler.SetPreProcessHandler((request, context) =>
                {
                    request.Params = new object[] { "replaced" };
                    return null;
                });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"replaced\",\"id\":1}", Run("{\"method\":\"echo\",\"params\":[\"original\"],\"id\":1}", null, serializer));

                handler.SetPreProcessHandler((request, context) =>
                {
                    request.Params = new Dictionary<string, object> { ["b"] = 40, ["a"] = 2 };
                    return null;
                });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":42,\"id\":1}", Run("{\"method\":\"sum\",\"params\":[1,1],\"id\":1}", null, serializer));
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void PreProcessHandler_ReplacingId_IsEchoed(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => { request.Id = "changed"; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":\"changed\"}", Run("{\"method\":\"ping\",\"id\":1}", null, serializer));
                handler.SetPreProcessHandler((request, context) => { request.Id = 99L; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":99}", Run("{\"method\":\"ping\",\"id\":\"x\"}", null, serializer));
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void PreProcessHandler_LeavingRequestAlone_IsByteIdenticalToFastPath(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var requests = new[]
            {
                "{\"jsonrpc\":\"2.0\",\"method\":\"echo\",\"params\":[\"a\\\"b\\u00e9\\n\"],\"id\":1}",
                "{\"method\":\"sum\",\"params\":{\"b\":2,\"a\":40},\"id\":\"s\"}",
                "{\"method\":\"optional\",\"params\":{},\"id\":3}",
                "{\"method\":\"frac\",\"params\":[1.5],\"id\":4}",
                "{\"method\":\"frac\",\"params\":[2],\"id\":5}",
                "{\"method\":\"missing\",\"id\":6}",
                "{\"method\":\"sum\",\"params\":[1],\"id\":7}",
                "{\"method\":\"throws\",\"id\":8}",
                "{\"method\":\"rpcex\",\"id\":null}",
                "[{\"method\":\"ping\",\"id\":1},{\"method\":\"ping\"},{\"method\":\"echo\",\"params\":[\"x\"],\"id\":2}]",
            };
            var expected = new string[requests.Length];
            for (int i = 0; i < requests.Length; i++) expected[i] = Run(requests[i], null, serializer);

            var handler = Handler.GetSessionHandler(Session);
            try
            {
                int calls = 0;
                handler.SetPreProcessHandler((request, context) => { calls++; return null; });
                for (int i = 0; i < requests.Length; i++)
                {
                    Assert.AreEqual(expected[i], Run(requests[i], null, serializer), requests[i]);
                }
                Assert.AreEqual(requests.Length + 2, calls, "the pre-handler ran once per request");
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void PreProcessHandler_Throwing_IsAnInternalError(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => throw new InvalidOperationException("hook failed"));
                var response = Parse(Run("{\"method\":\"ping\",\"id\":1}", null, serializer));
                Assert.AreEqual(-32603, (int)response["error"]["code"]);
                Assert.AreEqual(JTokenType.Null, response["error"]["data"].Type, "a hook failure is an unhandled exception: redacted like any other");
                Assert.AreEqual(1, (int)response["id"]);
                Config.IncludeExceptionDetails = true;
                try
                {
                    response = Parse(Run("{\"method\":\"ping\",\"id\":1}", null, serializer));
                    Assert.AreEqual("hook failed", (string)response["error"]["data"]["Message"]);
                }
                finally
                {
                    Config.IncludeExceptionDetails = false;
                }
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        // ------------------------------------------------------------------ 5: nested calls

        [TestCaseSource(nameof(Serializers))]
        public void NestedProcessing_RestoresContextAndException(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            _current = serializer;
            HardeningService.ContextAfterNested = null;
            HardeningService.InnerResponse = null;

            var outer = Run("{\"jsonrpc\":\"2.0\",\"method\":\"nested\",\"id\":1}", "outer", serializer);

            Assert.AreEqual("outer", HardeningService.ContextAfterNested, "the outer context must survive the nested call");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"inner\",\"id\":2}", HardeningService.InnerResponse, "the inner call sees its own context and no inherited error");
            var response = Parse(outer);
            Assert.AreEqual(-32001, (int)response["error"]["code"], "the outer call keeps the exception it set: " + outer);
            Assert.AreEqual(1, (int)response["id"]);
            Assert.IsNull(Handler.RpcContext(), "no context outside an invocation");
            Assert.IsNull(Handler.RpcGetAndRemoveRpcException(), "no exception leaks out of the invocation");
        }

        [TestCaseSource(nameof(Serializers))]
        public void NestedProcessing_RestoresContextAndException_WithHooks(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPostProcessHandler((request, response, context) => null);
                NestedProcessing_RestoresContextAndException(name);
            }
            finally
            {
                handler.SetPostProcessHandler(null);
            }
        }

        // ------------------------------------------------------------------ 6: exception disclosure

        [TestCaseSource(nameof(Serializers))]
        public void ExceptionDetails_AreRedactedByDefault(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            Assert.IsFalse(Config.IncludeExceptionDetails, "the default is off");

            var raw = Run("{\"method\":\"throws\",\"id\":1}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal Error\",\"data\":null},\"id\":1}", raw,
                "nothing about the exception leaves the process: no type name, no message");

            raw = Run("{\"method\":\"throwsInner\",\"id\":1}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal Error\",\"data\":null},\"id\":1}", raw);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ExceptionDetails_ErrorHandlerStillSeesTheException(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            Exception seen = null;
            try
            {
                handler.SetErrorHandler((request, error) => { seen = error.data as Exception; return error; });
                var response = Parse(Run("{\"method\":\"throws\",\"id\":1}", null, serializer));
                Assert.IsInstanceOf<InvalidOperationException>(seen, "the handler gets the exception itself, redaction happens when the response is written");
                Assert.AreEqual(JTokenType.Null, response["error"]["data"].Type);

                // a handler that replaces the error with its own data is not redacted: that data is authored
                handler.SetErrorHandler((request, error) => new JsonRpcException(-32000, "Server error", "ticket 42"));
                response = Parse(Run("{\"method\":\"throws\",\"id\":1}", null, serializer));
                Assert.AreEqual("ticket 42", (string)response["error"]["data"]);
            }
            finally
            {
                handler.SetErrorHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void ExceptionDetails_ResultSerializationFailure_IsRedactedToo(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var raw = Run("{\"method\":\"unserializable\",\"id\":1}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal Error\",\"data\":null},\"id\":1}", raw,
                "an exception thrown while writing the result is an unhandled exception as well");
        }

        [TestCaseSource(nameof(Serializers))]
        public void ExceptionDetails_AreSentWhenEnabled(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            try
            {
                Config.IncludeExceptionDetails = true;
                var response = Parse(Run("{\"method\":\"throwsInner\",\"id\":1}", null, serializer));
                var data = (JObject)response["error"]["data"];
                Assert.AreEqual("System.ArgumentException", (string)data["ClassName"]);
                Assert.AreEqual("inner message", (string)data["Message"]);
                StringAssert.Contains("ThrowsInner", (string)data["StackTraceString"]);
                Assert.AreEqual(new ArgumentException().HResult, (int)data["HResult"]);
                Assert.AreEqual("innermost message", (string)data["InnerException"]["Message"]);
                Assert.AreEqual("System.Exception", (string)data["InnerException"]["ClassName"]);
                StringAssert.Contains("ThrowsInner", (string)data["InnerException"]["StackTraceString"]);

                response = Parse(Run("{\"method\":\"throws\",\"id\":1}", null, serializer));
                data = (JObject)response["error"]["data"];
                Assert.AreEqual("System.InvalidOperationException", (string)data["ClassName"]);
                Assert.AreEqual(-2146233079, (int)data["HResult"]);
                StringAssert.Contains("Throws", (string)data["StackTraceString"]);
                Assert.IsNotNull((string)data["Source"]);
            }
            finally
            {
                Config.IncludeExceptionDetails = false;
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void ApplicationJsonRpcException_KeepsItsData(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32005,\"message\":\"application error\",\"data\":\"authored data\"},\"id\":1}",
                Run("{\"method\":\"rpcex\",\"id\":1}", null, serializer));
        }

        // ------------------------------------------------------------------ 7: hook materialisation error boundary

        /// <summary>
        /// System.Text.Json for everything, except that converting a value to <see cref="object"/> (what the
        /// hook path needs for <c>accept(object)</c>) fails: a stand-in for any parameter conversion that throws.
        /// </summary>
        private sealed class ObjectConversionFailsSerializer : JsonRpcSerializer
        {
            private readonly JsonRpcSerializer _inner = new AustinHarris.JsonRpc.SystemTextJson.SystemTextJsonRpcSerializer();
            public override string Name => "stj-failing-object";
            public override int MaxDepth => _inner.MaxDepth;
            public override T Read<T>(ReadOnlySpan<byte> utf8Json) => (T)Read(utf8Json, typeof(T));
            public override object Read(ReadOnlySpan<byte> utf8Json, Type type)
            {
                if (type == typeof(object)) throw new InvalidCastException("simulated parameter conversion failure");
                return _inner.Read(utf8Json, type);
            }
            public override void Write<T>(IBufferWriter<byte> output, T value) => _inner.Write(output, value);
            public override void Write(IBufferWriter<byte> output, object value, Type type) => _inner.Write(output, value, type);
        }

        [Test]
        public void HookMaterialisation_ConversionFailure_IsAnInternalError()
        {
            var serializer = new ObjectConversionFailsSerializer();
            var handler = Handler.GetSessionHandler(Session);
            string deep = "{\"jsonrpc\":\"2.0\",\"method\":\"accept\",\"params\":[[1]],\"id\":2}";

            // without hooks the failure happens while binding the argument: the client's fault, -32602 naming the parameter
            var plain = Parse(Run(deep, null, serializer));
            Assert.AreEqual(-32602, (int)plain["error"]["code"], plain.ToString());
            Assert.AreEqual("o", (string)plain["error"]["data"]["parameter"]);
            Assert.AreEqual("object", (string)plain["error"]["data"]["expectedType"]);

            // with a hook the same failure happens while materialising params for the hook: that is not an argument, -32603

            JsonRequest seenByErrorHandler = null;
            try
            {
                handler.SetPreProcessHandler((request, context) => null);
                Config.SetErrorHandler(Session,(request, ex) => { seenByErrorHandler = request; return ex; });

                string single = null;
                Assert.DoesNotThrow(() => single = Run(deep, null, serializer));
                var response = Parse(single);
                Assert.AreEqual(-32603, (int)response["error"]["code"], single);
                Assert.AreEqual(2, (int)response["id"]);
                Assert.IsNotNull(seenByErrorHandler, "the error handler still runs");
                Assert.AreEqual("accept", seenByErrorHandler.Method);
                Assert.IsNull(seenByErrorHandler.Params, "params are unavailable when their conversion is the failure");

                string batchJson = "[{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}," + deep + ",{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":3}]";
                string batch = null;
                Assert.DoesNotThrow(() => batch = Run(batchJson, null, serializer));
                var responses = JArray.Parse(batch);
                Assert.AreEqual(3, responses.Count, batch);
                Assert.AreEqual(7, (int)responses[0]["result"]);
                Assert.AreEqual(-32603, (int)responses[1]["error"]["code"]);
                Assert.AreEqual(2, (int)responses[1]["id"]);
                Assert.AreEqual(7, (int)responses[2]["result"]);
                Assert.AreEqual(3, (int)responses[2]["id"]);
            }
            finally
            {
                handler.SetPreProcessHandler(null);
                Config.SetErrorHandler(Session,null);
            }
        }

        // ------------------------------------------------------------------ 12: SMD service edits

        [TestCaseSource(nameof(Serializers))]
        public void ServicesDictionary_AddRemoveReplace_AreHonoured(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var services = Handler.GetSessionHandler(Session).MetaData.Services;
            const string ok7 = "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}";
            const string ping = "{\"method\":\"ping\",\"id\":1}";

            Assert.AreEqual(ok7, Run(ping, null, serializer));
            Assert.IsTrue(services.ContainsKey("ping"));
            var original = services["ping"];
            int count = services.Count;

            Assert.IsTrue(services.Remove("ping"));
            Assert.AreEqual(-32601, (int)Parse(Run(ping, null, serializer))["error"]["code"], "removed from the dictionary means unreachable");
            Assert.AreEqual(count - 1, services.Count);

            services.Add("ping", original);
            Assert.AreEqual(ok7, Run(ping, null, serializer), "added back");
            Assert.AreEqual(count, services.Count);

            var replacement = new SMDService("POST", "JSON-RPC-2.0",
                new Dictionary<string, Type> { ["returns"] = typeof(int) }, new Dictionary<string, object>(), new Func<int>(() => 8));
            services["ping"] = replacement;
            Assert.AreEqual(count, services.Count, "same count");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":1}", Run(ping, null, serializer), "same-count replacement is dispatched");

            services["ping"] = original;
            Assert.AreEqual(ok7, Run(ping, null, serializer));

            Assert.IsFalse(services.Remove("never-registered"));
            Assert.Throws<ArgumentException>(() => services.Add("ping", original), "Add rejects a duplicate like a dictionary");
        }

        // ------------------------------------------------------------------ 13: notifications never answer

        [TestCaseSource(nameof(Serializers))]
        public void Notifications_NeverGetAResponse(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            int errorsSeen = 0;
            try
            {
                Config.SetErrorHandler(Session,(request, ex) => { errorsSeen++; return ex; });

                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"missing\"}", null, serializer), "method not found");
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"sum\",\"params\":[1]}", null, serializer), "binding failure");
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"sum\",\"params\":[\"x\",\"y\"]}", null, serializer), "conversion failure");
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"throws\"}", null, serializer), "method exception");
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"rpcex\"}", null, serializer), "JsonRpcException");
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"ping\"}", null, serializer), "success");
                Assert.GreaterOrEqual(errorsSeen, 4, "the error handler still runs for notifications");

                // an invalid request object is not a notification
                var invalid = Parse(Run("{\"jsonrpc\":\"2.0\",\"params\":[1]}", null, serializer));
                Assert.AreEqual(-32600, (int)invalid["error"]["code"]);
                Assert.AreEqual(JTokenType.Null, invalid["id"].Type);
                invalid = Parse(Run("{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"params\":1}", null, serializer));
                Assert.AreEqual(-32600, (int)invalid["error"]["code"]);

                // in a batch an errored notification contributes nothing
                Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}]",
                    Run("[{\"method\":\"missing\"},{\"method\":\"ping\",\"id\":1},{\"method\":\"throws\"}]", null, serializer));
                Assert.AreEqual("", Run("[{\"method\":\"missing\"},{\"method\":\"throws\"},{\"method\":\"ping\"}]", null, serializer), "a batch of notifications only");
            }
            finally
            {
                Config.SetErrorHandler(Session,null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void Notifications_NeverGetAResponse_WithHooks(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            int postSeen = 0;
            try
            {
                handler.SetPreProcessHandler((request, context) => null);
                handler.SetPostProcessHandler((request, response, context) => { postSeen++; return null; });
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"missing\"}", null, serializer));
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"throws\"}", null, serializer));
                Assert.AreEqual("", Run("{\"jsonrpc\":\"2.0\",\"method\":\"ping\"}", null, serializer));
                Assert.AreEqual(3, postSeen, "the post-process handler still runs");
                Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}]",
                    Run("[{\"method\":\"missing\"},{\"method\":\"ping\",\"id\":1}]", null, serializer));
            }
            finally
            {
                handler.SetPreProcessHandler(null);
                handler.SetPostProcessHandler(null);
            }
        }

        // ------------------------------------------------------------------ 14: batch shape

        [TestCaseSource(nameof(Serializers))]
        public void Batch_AlwaysAnswersWithAnArray(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}]", Run("[{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}]", null, serializer));
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}]", Run("[{\"method\":\"ping\",\"id\":1},{\"method\":\"ping\"}]", null, serializer));
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":2}]",
                Run("[{\"method\":\"ping\",\"id\":1},{\"method\":\"ping\",\"id\":2}]", null, serializer));
            Assert.AreEqual("", Run("[{\"method\":\"ping\"}]", null, serializer));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}", Run("{\"method\":\"ping\",\"id\":1}", null, serializer), "a single request is not a batch");
            var empty = Parse(Run("[]", null, serializer));
            Assert.AreEqual(-32600, (int)empty["error"]["code"]);
        }

        // ------------------------------------------------------------------ 16: async return types

        [Test]
        public void AsyncReturnTypes_RequireAsyncProcessing_AsyncVoidIsRejected()
        {
            const string session = "dispatch-hardening-async";
            try
            {
                ServiceBinder.BindService(session, new TaskReturningService());
                ServiceBinder.BindService(session, new ValueTaskReturningService());
                ServiceBinder.BindService(session, new PlainTaskReturningService());
                ServiceBinder.BindService(session, new PlainValueTaskReturningService());
                foreach (var method in new[] { "asyncTask", "asyncValueTask", "asyncPlain", "asyncPlainValue" })
                {
                    var response = Parse(Run("{\"method\":\"" + method + "\",\"id\":1}", session: session));
                    Assert.AreEqual(-32603, (int)response["error"]["code"]);
                    StringAssert.Contains("is asynchronous", (string)response["error"]["message"]);
                }
                var ex = Assert.Throws<NotSupportedException>(() => ServiceBinder.BindService(session, new AsyncVoidService()));
                StringAssert.Contains("async void", ex.Message);
                Assert.Throws<NotSupportedException>(() => ServiceBinder.BindMethod(session, "boundAsyncVoid", new Action(async () => await Task.Yield())));
            }
            finally { Handler.DestroySession(session); }
        }

        // ------------------------------------------------------------------ 19: named parameters

        [TestCaseSource(nameof(Serializers))]
        public void NamedParameters_UnknownNamesAreRejected(string name)
        {
            var serializer = SerializerCatalog.Create(name);

            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":9,\"id\":1}", Run("{\"method\":\"optional\",\"params\":{},\"id\":1}", null, serializer), "absent optional takes its default");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":4,\"id\":1}", Run("{\"method\":\"optional\",\"params\":{\"a\":4},\"id\":1}", null, serializer));

            var response = Parse(Run("{\"method\":\"optional\",\"params\":{\"typo\":4},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"], "an unknown name is not silently replaced by the default");
            StringAssert.Contains("typo", (string)response["error"]["data"]);

            response = Parse(Run("{\"method\":\"sum\",\"params\":{\"a\":1,\"b\":2,\"c\":3},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"]);
            StringAssert.Contains("'c'", (string)response["error"]["data"]);

            response = Parse(Run("{\"method\":\"sum\",\"params\":{\"a\":1,\"c\":3},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"], "unknown names are reported even when the count matches");
            StringAssert.Contains("'c'", (string)response["error"]["data"]);

            response = Parse(Run("{\"method\":\"sum\",\"params\":{\"a\":1},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"]);
            StringAssert.Contains("'b'", (string)response["error"]["data"]);

            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":42,\"id\":1}", Run("{\"method\":\"sum\",\"params\":{\"b\":40,\"a\":2},\"id\":1}", null, serializer));
        }

        [TestCaseSource(nameof(Serializers))]
        public void NamedParameters_DuplicateNamesAreRejected(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var response = Parse(Run("{\"method\":\"sum\",\"params\":{\"a\":1,\"a\":2,\"b\":3},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"]);
            StringAssert.Contains("'a'", (string)response["error"]["data"]);
            StringAssert.Contains("more than once", (string)response["error"]["data"]);

            response = Parse(Run("{\"method\":\"optional\",\"params\":{\"a\":1,\"a\":2},\"id\":1}", null, serializer));
            Assert.AreEqual(-32602, (int)response["error"]["code"]);
        }

        // ------------------------------------------------------------------ 20: Process overloads bind to the intended session

        [Test]
        public void ProcessOverloads_BindToTheIntendedSession()
        {
            const string json = "{\"jsonrpc\":\"2.0\",\"method\":\"dh.whichSession\",\"id\":1}";
            const string ctx = "{\"jsonrpc\":\"2.0\",\"method\":\"ctx\",\"id\":1}";
            var serializer = AustinHarris.JsonRpc.Jsmn.JsmnSerializer.Instance;
            const string onDefault = "{\"jsonrpc\":\"2.0\",\"result\":\"default\",\"id\":1}";
            const string onSession = "{\"jsonrpc\":\"2.0\",\"result\":\"dispatch-hardening\",\"id\":1}";

            Assert.AreEqual(onDefault, JsonRpcProcessor.Process(json).Result, "Process(json)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.Process(json, (object)"ctx").Result, "Process(json, (object)ctx)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.ProcessSync(json, null), "ProcessSync(json, null)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.ProcessSync(json, (object)"ctx"), "ProcessSync(json, (object)ctx)");
            Assert.AreEqual(onSession, JsonRpcProcessor.Process(Session, json, null).Result, "Process(sessionId, json, null)");
            Assert.AreEqual(onSession, JsonRpcProcessor.ProcessSync(Session, json, null), "ProcessSync(sessionId, json, null)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.Process(serializer, json).Result, "Process(serializer, json)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.Process(serializer, json, null).Result, "Process(serializer, json, null)");
            Assert.AreEqual(onDefault, JsonRpcProcessor.ProcessSync(serializer, json), "ProcessSync(serializer, json)");
            Assert.AreEqual(onSession, JsonRpcProcessor.ProcessSync(Session, json, null, serializer), "ProcessSync(sessionId, json, null, serializer)");

            // the context reaches the method through every shape
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"c1\",\"id\":1}", JsonRpcProcessor.Process(Session, ctx, "c1").Result);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"c2\",\"id\":1}", JsonRpcProcessor.ProcessSync(Session, ctx, "c2"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"c3\",\"id\":1}", JsonRpcProcessor.ProcessSync(Session, ctx, "c3", serializer));
        }
    }
}
