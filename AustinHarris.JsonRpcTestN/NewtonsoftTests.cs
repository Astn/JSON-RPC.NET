using System;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Behaviour that is specific to the Json.NET adapter: lenient request syntax, JsonSerializerSettings being
    /// honoured, the settings-based process helpers, the Json.NET object model reaching handlers, and the
    /// pooled read/write plumbing.
    /// </summary>
    [TestFixture]
    public class NewtonsoftTests
    {
        private const string Ok7 = "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}";

        [OneTimeSetUp]
        public void SelectSerializer()
        {
            // CalculatorService is registered once, by the main fixture's static constructor; make sure that has run
            RuntimeHelpers.RunClassConstructor(typeof(Test).TypeHandle);
            Config.SetSerializer(new NewtonsoftJsonRpcSerializer());
        }

        [OneTimeTearDown]
        public void RestoreSerializer()
        {
            Config.SetSerializer(null);
        }

        // ------------------------------------------------------------------ leniency

        [Test]
        public void LenientRequest_UnquotedKeysAndSingleQuotedStrings()
        {
            var result = JsonRpcProcessor.ProcessSync("{method:'IntToInt',params:[7],id:1}", null);
            Assert.AreEqual(Ok7, result);
        }

        [Test]
        public void LenientRequest_SingleQuotedStringParamAndId()
        {
            var result = JsonRpcProcessor.ProcessSync("{method:'internal.echo',params:['hi there'],id:'abc'}", null);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"hi there\",\"id\":\"abc\"}", result);
        }

        [Test]
        public void LenientRequest_SingleQuotedNamedParams()
        {
            var result = JsonRpcProcessor.ProcessSync("{method:'TestOptionalParamchar',params:{input:'z'},id:1}", null);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"z\",\"id\":1}", result);
        }

        [Test]
        public void LenientRequest_IsRejectedByTheStrictBuiltInSerializer()
        {
            var result = JsonRpcProcessor.ProcessSync(Handler.DefaultSessionId(), "{method:'IntToInt',params:[7],id:1}", null, JsmnSerializer.Instance);
            StringAssert.Contains("-32700", result);
        }

        // ------------------------------------------------------------------ settings

        [Test]
        public void Settings_DateFormatString_IsHonoured()
        {
            var settings = new JsonSerializerSettings { DateFormatString = "yyyy-MM-dd" };
            var serializer = new NewtonsoftJsonRpcSerializer(settings);
            var result = JsonRpcProcessor.ProcessSync(Handler.DefaultSessionId(), "{\"method\":\"ReturnsDateTime\",\"params\":[],\"id\":1}", null, serializer);
            Assert.IsTrue(Regex.IsMatch(result, "^\\{\"jsonrpc\":\"2.0\",\"result\":\"\\d{4}-\\d{2}-\\d{2}\",\"id\":1\\}$"), result);
        }

        [Test]
        public void Settings_Converter_IsHonouredOnReadAndWrite()
        {
            var settings = new JsonSerializerSettings { Converters = { new ShoutingStringConverter() } };
            var serializer = new NewtonsoftJsonRpcSerializer(settings);
            // read appends "!" then echo returns it, write upper-cases it
            var result = JsonRpcProcessor.ProcessSync(Handler.DefaultSessionId(), "{\"method\":\"internal.echo\",\"params\":[\"abc\"],\"id\":1}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"ABC!\",\"id\":1}", result);
        }

        [Test]
        public void Settings_PerSession_OverridesGlobal()
        {
            var sessionId = "newtonsoft per-session";
            try
            {
                var h = Handler.GetSessionHandler(sessionId);
#pragma warning disable CS0618, JSONRPC0002
                h.RegisterFuction("echo", new System.Collections.Generic.Dictionary<string, Type> { { "s", typeof(string) }, { "returns", typeof(string) } }, null, new Func<string, string>(s => s));
#pragma warning restore CS0618, JSONRPC0002
                h.Serializer = new NewtonsoftJsonRpcSerializer(new JsonSerializerSettings { Converters = { new ShoutingStringConverter() } });
                // four arguments on purpose: ProcessSync(sessionId, json, null) binds to the (jsonRpc, context, serializer) overload
                var result = JsonRpcProcessor.ProcessSync(sessionId, "{\"method\":\"echo\",\"params\":[\"abc\"],\"id\":1}", null, null);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"ABC!\",\"id\":1}", result);
            }
            finally
            {
                Handler.DestroySession(sessionId);
            }
        }

        [Test]
        public void Settings_DefaultsProduceTheSharedWireFormat()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            Assert.AreEqual("3.0", s.Serialize(3.0));
            Assert.AreEqual("71.0", s.Serialize(71f));
            Assert.AreEqual("71.0", s.Serialize(71m));
            Assert.AreEqual("1.2345", s.Serialize(1.2345f));
            Assert.AreEqual("\"x\"", s.Serialize('x'));
            Assert.AreEqual("null", s.Serialize<object>(null));
            // ISO-8601 with the offset; Json.NET drops trailing zeros of the fraction (the built-in serializer always writes seven digits)
            Assert.AreEqual("\"2020-01-02T03:04:05.1234567Z\"", s.Serialize(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567)));
            Assert.AreEqual("\"2020-01-02T03:04:05Z\"", s.Serialize(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
            Assert.AreEqual("{\"str\":null}", s.Serialize(new CalculatorService.CustomString()));
            Assert.AreEqual("{\"NodeId\":1,\"Leafs\":null}", s.Serialize(new TreeNode { NodeId = 1 }));
        }

        // ------------------------------------------------------------------ settings-based helpers

        [Test]
        public void ProcessSyncHelper_UsesSettings()
        {
            var settings = new JsonSerializerSettings { DateFormatString = "yyyy" };
            var result = NewtonsoftJsonRpc.ProcessSync(Handler.DefaultSessionId(), "{\"method\":\"ReturnsDateTime\",\"params\":[],\"id\":1}", null, settings);
            Assert.IsTrue(Regex.IsMatch(result, "^\\{\"jsonrpc\":\"2.0\",\"result\":\"\\d{4}\",\"id\":1\\}$"), result);
        }

        [Test]
        public void ProcessHelper_UsesSettings()
        {
            var settings = new JsonSerializerSettings { Converters = { new ShoutingStringConverter() } };
            var task = NewtonsoftJsonRpc.Process("{\"method\":\"internal.echo\",\"params\":[\"abc\"],\"id\":1}", null, settings);
            task.Wait();
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"ABC!\",\"id\":1}", task.Result);
        }

        [Test]
        public void Helper_NullSettings_UsesJsonNetDefaults()
        {
            Assert.AreEqual(Ok7, NewtonsoftJsonRpc.ProcessSync("{\"method\":\"IntToInt\",\"params\":[7],\"id\":1}", null, null));
        }

        [Test]
        public void Helper_CachesOneSerializerPerSettingsInstance()
        {
            var settings = new JsonSerializerSettings();
            Assert.AreSame(NewtonsoftJsonRpc.SerializerFor(settings), NewtonsoftJsonRpc.SerializerFor(settings));
            Assert.AreNotSame(NewtonsoftJsonRpc.SerializerFor(settings), NewtonsoftJsonRpc.SerializerFor(new JsonSerializerSettings()));
            Assert.AreSame(NewtonsoftJsonRpc.SerializerFor(null), NewtonsoftJsonRpc.SerializerFor(null));
        }

        // ------------------------------------------------------------------ object model

        [Test]
        public void PreProcessHandler_ReceivesJsonNetObjectModel()
        {
            object seenParams = null;
            Config.SetPreProcessHandler((rpc, ctx) => { seenParams = rpc.Params; return null; });
            try
            {
                Assert.AreEqual(Ok7, JsonRpcProcessor.ProcessSync("{\"method\":\"IntToInt\",\"params\":{\"input\":7},\"id\":1}", null));
                Assert.IsInstanceOf<JObject>(seenParams);
                Assert.AreEqual(7, (int)((JObject)seenParams)["input"]);

                Assert.AreEqual(Ok7, JsonRpcProcessor.ProcessSync("{\"method\":\"IntToInt\",\"params\":[7],\"id\":1}", null));
                Assert.IsInstanceOf<JArray>(seenParams);
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Test]
        public void HandlerHandle_RoundTripsJsonRequestThroughJsonNet()
        {
            var response = Handler.DefaultHandler.Handle(new JsonRequest("IntToInt", new JArray(5), 9L));
            Assert.IsNull(response.Error);
            Assert.AreEqual(5, response.Result);
            Assert.AreEqual(9L, response.Id);

            response = Handler.DefaultHandler.Handle(new JsonRequest("IntToInt", new JObject { ["input"] = 6 }, "x"));
            Assert.IsNull(response.Error);
            Assert.AreEqual(6, response.Result);
            Assert.AreEqual("x", response.Id);
        }

        [Test]
        public void ReadObject_MatchesJsonConvertDeserializeObject()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            Assert.AreEqual(12L, s.Deserialize("12", typeof(object)));
            Assert.AreEqual("abc", s.Deserialize("\"abc\"", typeof(object)));
            Assert.AreEqual("abc", s.Deserialize("'abc'", typeof(object)));
            Assert.IsNull(s.Deserialize("null", typeof(object)));
            Assert.IsInstanceOf<JObject>(s.Deserialize("{\"a\":1}", typeof(object)));
            Assert.IsInstanceOf<JArray>(s.Deserialize("[1,2]", typeof(object)));
            Assert.AreEqual(true, s.Deserialize("true", typeof(object)));
        }

        [Test]
        public void Read_ConversionFailure_Throws()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            Assert.Catch<Exception>(() => s.Deserialize<int>("\"abc\""));
            Assert.Catch<Exception>(() => s.Deserialize<int>("null"));
            Assert.Catch<Exception>(() => s.Deserialize<CalculatorService.CustomString>("[1]"));
        }

        // ------------------------------------------------------------------ plumbing

        [Test]
        public void Write_LongNonAsciiString_RoundTripsThroughTheChunkedEncoder()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            var sb = new StringBuilder();
            for (int i = 0; i < 20000; i++) sb.Append("aé€\U0001F600\"\\\n");
            var text = sb.ToString();
            var json = s.Serialize(text);
            Assert.AreEqual(text, JsonConvert.DeserializeObject<string>(json));
            Assert.AreEqual(text, s.Deserialize<string>(json));
        }

        [Test]
        public void Write_IsReusableAfterAFailedWrite()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            Assert.Catch<Exception>(() => s.Serialize(new Explodes()));
            Assert.AreEqual("[1,2]", s.Serialize(new[] { 1, 2 }));
            Assert.AreEqual("{\"str\":\"ok\"}", s.Serialize(new CalculatorService.CustomString { str = "ok" }));
        }

        [Test]
        public void Write_ReentrantFromAConverter_Works()
        {
            // the converter list is snapshotted when the serializer is built, so register the converter first and bind it afterwards
            var converter = new ReentrantConverter();
            var s = new NewtonsoftJsonRpcSerializer(new JsonSerializerSettings { Converters = { converter } });
            converter.Rpc = s;
            Assert.AreEqual("{\"inner\":{\"str\":\"in\"}}", s.Serialize(new Wrapped { Inner = new CalculatorService.CustomString { str = "in" } }));
            Assert.AreEqual("[1]", s.Serialize(new[] { 1 }));
        }

        [Test]
        public void Read_ManyValuesOnOneThread_ReusesTheDecoder()
        {
            var s = new NewtonsoftJsonRpcSerializer();
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(i, s.Deserialize<int>(i.ToString()));
                Assert.AreEqual("v" + i, s.Deserialize<string>("\"v" + i + "\""));
            }
        }

        // ------------------------------------------------------------------ helpers

        private sealed class ShoutingStringConverter : JsonConverter<string>
        {
            public override void WriteJson(JsonWriter writer, string value, JsonSerializer serializer)
            {
                writer.WriteValue(value?.ToUpperInvariant());
            }

            public override string ReadJson(JsonReader reader, Type objectType, string existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                return reader.Value == null ? null : Convert.ToString(reader.Value) + "!";
            }
        }

        private sealed class Explodes
        {
            public int Fine => 1;
            public int Boom => throw new InvalidOperationException("boom");
        }

        private sealed class Wrapped
        {
            public CalculatorService.CustomString Inner { get; set; }
        }

        /// <summary>Serializes the wrapped value by calling back into the RPC serializer from inside a write.</summary>
        private sealed class ReentrantConverter : JsonConverter<Wrapped>
        {
            public NewtonsoftJsonRpcSerializer Rpc { get; set; }

            public override void WriteJson(JsonWriter writer, Wrapped value, JsonSerializer serializer)
            {
                writer.WriteStartObject();
                writer.WritePropertyName("inner");
                writer.WriteRawValue(Rpc.Serialize(value.Inner));
                writer.WriteEndObject();
            }

            public override Wrapped ReadJson(JsonReader reader, Type objectType, Wrapped existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }
}
