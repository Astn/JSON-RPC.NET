using System;
using System.Collections.Generic;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Invocation;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Structured error data (issues #123 and #145): an argument the serializer cannot convert is the client's
    /// fault (-32602, naming the parameter) instead of an internal error, and "Method not found" says which method.
    /// Every scenario runs against the three serializers.
    /// </summary>
    [TestFixture]
    public class ErrorDataTests
    {
        private const string Session = "error-data";
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };

        public class Order
        {
            public int Quantity { get; set; }
            public string Sku { get; set; }
        }

        private class Service
        {
            [JsonRpcMethod("ed.int")]
            public int Int(int value) => value;

            [JsonRpcMethod("ed.two")]
            public string Two(string first, Guid second) => first + second;

            [JsonRpcMethod("ed.optional")]
            public int Optional(int a, int b = 2) => a + b;

            [JsonRpcMethod("ed.order")]
            public int OrderQuantity(Order order) => order.Quantity;

            [JsonRpcMethod("ed.nullable")]
            public int? Nullable(int? value) => value;

            [JsonRpcMethod("ed.list")]
            public int List(List<int> values) => values.Count;

            [JsonRpcMethod("ed.dateTime")]
            public string DateTimeArg(DateTime when) => when.Year.ToString();

            /// <summary>The same exception type a converter would throw, but from inside the method: not a binding failure.</summary>
            [JsonRpcMethod("ed.parses")]
            public int Parses(string text) => int.Parse(text);

            [JsonRpcMethod("ed.throwsFormat")]
            public int ThrowsFormat() => throw new FormatException("from the method");
        }

        [OneTimeSetUp]
        public void Bind()
        {
            ServiceBinder.BindService(Session, new Service());
        }

        [OneTimeTearDown]
        public void Destroy()
        {
            Handler.DestroySession(Session);
        }

        private static JObject Run(string json, JsonRpcSerializer serializer)
        {
            var response = JsonRpcProcessor.ProcessSync(Session, json, null, serializer);
            Assert.IsFalse(string.IsNullOrEmpty(response), "expected a response");
            return JObject.Parse(response);
        }

        private static JObject InvalidParams(string json, JsonRpcSerializer serializer, string parameter, int index, string expectedType)
        {
            var response = Run(json, serializer);
            Assert.IsNotNull(response["error"], json + " -> " + response.ToString(Newtonsoft.Json.Formatting.None));
            Assert.AreEqual(-32602, (int)response["error"]["code"], json + " -> " + response.ToString(Newtonsoft.Json.Formatting.None));
            Assert.AreEqual("Invalid params", (string)response["error"]["message"]);
            var data = (JObject)response["error"]["data"];
            Assert.AreEqual("conversion", (string)data["reason"], response.ToString());
            Assert.AreEqual(parameter, (string)data["parameter"], response.ToString());
            Assert.AreEqual(index, (int)data["index"], response.ToString());
            Assert.AreEqual(expectedType, (string)data["expectedType"], response.ToString());
            return response;
        }

        // ------------------------------------------------------------------ #123: conversion failures are -32602

        [TestCaseSource(nameof(Serializers))]
        public void WrongType_ForAPrimitive(string name)
        {
            var s = SerializerCatalog.Create(name);
            var response = InvalidParams("{\"method\":\"ed.int\",\"params\":[\"abc\"],\"id\":1}", s, "value", 0, "int32");
            Assert.AreEqual(1, (int)response["id"]);
            Assert.IsNull(response["error"]["data"]["message"], "the serializer's message (which may echo the value) is not sent by default");
            StringAssert.DoesNotContain("abc", response["error"]["data"].ToString(), "the offending value is not echoed");

            InvalidParams("{\"method\":\"ed.int\",\"params\":{\"value\":\"abc\"},\"id\":1}", s, "value", 0, "int32");
            InvalidParams("{\"method\":\"ed.int\",\"params\":[{\"a\":1}],\"id\":1}", s, "value", 0, "int32");
            InvalidParams("{\"method\":\"ed.int\",\"params\":[null],\"id\":1}", s, "value", 0, "int32");
            InvalidParams("{\"method\":\"ed.int\",\"params\":[99999999999],\"id\":1}", s, "value", 0, "int32");
        }

        [TestCaseSource(nameof(Serializers))]
        public void TheFailingParameter_IsTheOneNamed(string name)
        {
            var s = SerializerCatalog.Create(name);
            InvalidParams("{\"method\":\"ed.two\",\"params\":[\"ok\",\"not-a-guid\"],\"id\":1}", s, "second", 1, "guid");
            InvalidParams("{\"method\":\"ed.two\",\"params\":{\"second\":\"not-a-guid\",\"first\":\"ok\"},\"id\":1}", s, "second", 1, "guid");
            InvalidParams("{\"method\":\"ed.two\",\"params\":[{\"a\":1},\"not-a-guid\"],\"id\":1}", s, "first", 0, "string");
            InvalidParams("{\"method\":\"ed.optional\",\"params\":{\"a\":1,\"b\":\"x\"},\"id\":1}", s, "b", 1, "int32");
            InvalidParams("{\"method\":\"ed.optional\",\"params\":[\"x\"],\"id\":1}", s, "a", 0, "int32");
            Assert.AreEqual(3, (int)Run("{\"method\":\"ed.optional\",\"params\":[1],\"id\":1}", s)["result"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void TypeSpellings(string name)
        {
            var s = SerializerCatalog.Create(name);
            InvalidParams("{\"method\":\"ed.nullable\",\"params\":[\"x\"],\"id\":1}", s, "value", 0, "int32?");
            InvalidParams("{\"method\":\"ed.list\",\"params\":[[1,\"x\"]],\"id\":1}", s, "values", 0, "List<int32>");
            InvalidParams("{\"method\":\"ed.list\",\"params\":[5],\"id\":1}", s, "values", 0, "List<int32>");
            InvalidParams("{\"method\":\"ed.dateTime\",\"params\":[\"yesterday\"],\"id\":1}", s, "when", 0, "datetime");
            InvalidParams("{\"method\":\"ed.order\",\"params\":[{\"Quantity\":\"many\"}],\"id\":1}", s, "order", 0, "Order");
            InvalidParams("{\"method\":\"ed.order\",\"params\":[[1,2]],\"id\":1}", s, "order", 0, "Order");
            Assert.AreEqual(3, (int)Run("{\"method\":\"ed.order\",\"params\":[{\"Quantity\":3}],\"id\":1}", s)["result"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Message_IsSentOnlyWithExceptionDetails(string name)
        {
            var s = SerializerCatalog.Create(name);
            try
            {
                Config.IncludeExceptionDetails = true;
                var response = InvalidParams("{\"method\":\"ed.int\",\"params\":[\"abc\"],\"id\":1}", s, "value", 0, "int32");
                var message = (string)response["error"]["data"]["message"];
                Assert.IsFalse(string.IsNullOrEmpty(message), response.ToString());
            }
            finally
            {
                Config.IncludeExceptionDetails = false;
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void TheSameExceptionFromInsideTheMethod_StaysInternal(string name)
        {
            var s = SerializerCatalog.Create(name);
            var response = Run("{\"method\":\"ed.parses\",\"params\":[\"abc\"],\"id\":1}", s);
            Assert.AreEqual(-32603, (int)response["error"]["code"], "a FormatException thrown by the method is not a binding failure");
            Assert.AreEqual("System.FormatException", (string)response["error"]["data"]["ClassName"]);

            response = Run("{\"method\":\"ed.throwsFormat\",\"id\":1}", s);
            Assert.AreEqual(-32603, (int)response["error"]["code"]);
            Assert.AreEqual("from the method", (string)response["error"]["data"]["Message"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ErrorHandler_SeesTheStructuredData(string name)
        {
            var s = SerializerCatalog.Create(name);
            object seen = null;
            try
            {
                Config.SetErrorHandler(Session, (request, ex) => { seen = ex.data; return ex; });
                InvalidParams("{\"method\":\"ed.two\",\"params\":[\"ok\",\"bad\"],\"id\":1}", s, "second", 1, "guid");
                var info = seen as ParameterErrorInfo;
                Assert.IsNotNull(info, "the handler gets the ParameterErrorInfo object");
                Assert.AreEqual("second", info.Parameter);
                Assert.AreEqual(1, info.Index);
                Assert.AreEqual("guid", info.ExpectedType);
                Assert.AreEqual("conversion", info.Reason);
                Assert.IsNotNull(info.Cause);
                Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(info.Cause), info.Cause.GetType().FullName);

                // the handler can replace it with a plain string, as before
                Config.SetErrorHandler(Session, (request, ex) => new JsonRpcException(ex.code, ex.message, "bad " + ((ParameterErrorInfo)ex.data).Parameter));
                var response = Run("{\"method\":\"ed.two\",\"params\":[\"ok\",\"bad\"],\"id\":1}", s);
                Assert.AreEqual("bad second", (string)response["error"]["data"]);
            }
            finally
            {
                Config.SetErrorHandler(Session, null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void WithHooks_TheSameCodesAndData(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            try
            {
                handler.SetPreProcessHandler((request, context) => null);
                handler.SetPostProcessHandler((request, response, context) => null);
                InvalidParams("{\"method\":\"ed.two\",\"params\":[\"ok\",\"not-a-guid\"],\"id\":1}", s, "second", 1, "guid");
                InvalidParams("{\"method\":\"ed.int\",\"params\":{\"value\":\"abc\"},\"id\":1}", s, "value", 0, "int32");

                // a hook that replaces params is dispatched from the re-parsed request: same reporting
                handler.SetPreProcessHandler((request, context) => { request.Params = new object[] { "x" }; return null; });
                InvalidParams("{\"method\":\"ed.int\",\"params\":[1],\"id\":1}", s, "value", 0, "int32");
            }
            finally
            {
                handler.SetPreProcessHandler(null);
                handler.SetPostProcessHandler(null);
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void HandleJsonRequest_ReportsTheSame(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            var saved = handler.Serializer;
            try
            {
                handler.Serializer = s;
                var response = handler.Handle(new JsonRequest("ed.int", new object[] { "abc" }, 1L));
                Assert.IsNotNull(response.Error);
                Assert.AreEqual(-32602, response.Error.code);
                Assert.AreEqual("value", ((ParameterErrorInfo)response.Error.data).Parameter);
            }
            finally
            {
                handler.Serializer = saved;
            }
        }

        [Test]
        public void ConversionFailureFamily()
        {
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new JsonRpcBindException("x")));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new FormatException()));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new OverflowException()));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new InvalidCastException()));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new System.Text.Json.JsonException("x")));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new Newtonsoft.Json.JsonSerializationException("x")));
            Assert.IsTrue(ParameterErrorInfo.IsConversionFailure(new Newtonsoft.Json.JsonReaderException("x")));
            Assert.IsFalse(ParameterErrorInfo.IsConversionFailure(new NotSupportedException()));
            Assert.IsFalse(ParameterErrorInfo.IsConversionFailure(new InvalidOperationException()));
            Assert.IsFalse(ParameterErrorInfo.IsConversionFailure(new ArgumentException()));
            Assert.IsFalse(ParameterErrorInfo.IsConversionFailure(null));
        }

        [Test]
        public void ExpectedTypeSpelling()
        {
            Assert.AreEqual("int32", ParameterErrorInfo.Describe(typeof(int)));
            Assert.AreEqual("int64?", ParameterErrorInfo.Describe(typeof(long?)));
            Assert.AreEqual("string[]", ParameterErrorInfo.Describe(typeof(string[])));
            Assert.AreEqual("guid", ParameterErrorInfo.Describe(typeof(Guid)));
            Assert.AreEqual("boolean", ParameterErrorInfo.Describe(typeof(bool)));
            Assert.AreEqual("uint8[]", ParameterErrorInfo.Describe(typeof(byte[])));
            Assert.AreEqual("Dictionary<string,Order>", ParameterErrorInfo.Describe(typeof(Dictionary<string, Order>)));
            Assert.AreEqual("object", ParameterErrorInfo.Describe(typeof(object)));
            Assert.AreEqual("datetimeoffset", ParameterErrorInfo.Describe(typeof(DateTimeOffset)));
        }

        /// <summary>A type the built-in serializer cannot read at all is a server-side limitation, not the client's fault.</summary>
        [Test]
        public void UnsupportedType_StaysInternal()
        {
            const string session = "error-data-unsupported";
            try
            {
                ServiceBinder.BindMethod(session, "takesDelegate", new Func<Action, int>(a => 1));
                var response = JObject.Parse(JsonRpcProcessor.ProcessSync(session, "{\"method\":\"takesDelegate\",\"params\":[{}],\"id\":1}", null, SerializerCatalog.Create("jsmn")));
                Assert.AreEqual(-32603, (int)response["error"]["code"], response.ToString());
                Assert.AreEqual("System.NotSupportedException", (string)response["error"]["data"]["ClassName"]);
            }
            finally
            {
                Handler.DestroySession(session);
            }
        }

        // ------------------------------------------------------------------ #145: method not found names the method

        [TestCaseSource(nameof(Serializers))]
        public void MethodNotFound_NamesTheMethod(string name)
        {
            var s = SerializerCatalog.Create(name);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":{\"method\":\"no.such\"}},\"id\":1}",
                JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"no.such\",\"id\":1}", null, s));
            // the decoded name, re-escaped as any string
            var escaped = JObject.Parse(JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"n\\u00f6/\\\"x\\\"\",\"id\":\"q\"}", null, s));
            Assert.AreEqual(-32601, (int)escaped["error"]["code"]);
            Assert.AreEqual("nö/\"x\"", (string)escaped["error"]["data"]["method"]);
            // in a batch, per request
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":{\"method\":\"a\"}},\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":2}]",
                JsonRpcProcessor.ProcessSync(Session, "[{\"method\":\"a\",\"id\":1},{\"method\":\"b\"},{\"method\":\"ed.int\",\"params\":[3],\"id\":2}]", null, s));
        }

        [TestCaseSource(nameof(Serializers))]
        public void MethodNotFound_ReachesTheErrorHandler_OnBothPaths(string name)
        {
            var s = SerializerCatalog.Create(name);
            var handler = Handler.GetSessionHandler(Session);
            var seen = new List<string>();
            try
            {
                Config.SetErrorHandler(Session, (request, ex) =>
                {
                    if (ex.data is MethodNotFoundInfo info) seen.Add(request.Method + "=" + info.Method + "/" + ex.code);
                    return new JsonRpcException(ex.code, ex.message, "try " + ((MethodNotFoundInfo)ex.data).Method + " later");
                });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":\"try nope later\"},\"id\":1}",
                    JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"nope\",\"id\":1}", null, s));
                Assert.AreEqual("", JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"nope2\"}", null, s), "a notification still answers nothing");

                handler.SetPreProcessHandler((request, context) => null);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":\"try nope3 later\"},\"id\":1}",
                    JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"nope3\",\"id\":1}", null, s));

                // a hook that redirects to a missing method: the effective name is reported
                handler.SetPreProcessHandler((request, context) => { request.Method = "renamed"; return null; });
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":\"try renamed later\"},\"id\":1}",
                    JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"ed.int\",\"params\":[1],\"id\":1}", null, s));

                Assert.AreEqual(new[] { "nope=nope/-32601", "nope2=nope2/-32601", "nope3=nope3/-32601", "renamed=renamed/-32601" }, seen);
            }
            finally
            {
                handler.SetPreProcessHandler(null);
                Config.SetErrorHandler(Session, null);
            }
        }
    }
}
