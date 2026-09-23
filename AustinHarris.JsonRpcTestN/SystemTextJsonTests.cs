using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using AustinHarris.JsonRpc.SystemTextJson;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Behaviour specific to the System.Text.Json serializer: option handling, the wire converters on their own,
    /// and the JsonElement object model. The shared wire-format suite lives in <see cref="Test"/> (fixture "stj").
    /// </summary>
    [TestFixture]
    public class SystemTextJsonTests
    {
        public enum Colour { Red, Green }

        public class Shape
        {
            public string ShapeName { get; set; }
            public Colour Tint { get; set; }
            public double Area { get; set; }
        }

        private class StjService
        {
            [JsonRpcMethod("shape")]
            public Shape MakeShape(string name) => new Shape { ShapeName = name, Tint = Colour.Green, Area = 4 };

            [JsonRpcMethod("echo")]
            public string Echo(string s) => s;

            [JsonRpcMethod("sum")]
            public int Sum(int a, int b) => a + b;
        }

        private const string SessionId = "stj-tests";

        [OneTimeSetUp]
        public void SelectSerializer()
        {
            Config.SetSerializer(new SystemTextJsonRpcSerializer());
            ServiceBinder.BindService(SessionId, new StjService());
        }

        [OneTimeTearDown]
        public void RestoreSerializer()
        {
            Handler.DestroySession(SessionId);
            Config.SetSerializer(null);
        }

        // ------------------------------------------------------------------ options

        [Test]
        public void DefaultOptionsAreSharedAndReadOnly()
        {
            var a = new SystemTextJsonRpcSerializer();
            var b = new SystemTextJsonRpcSerializer();
            Assert.IsNull(a.Options);
            Assert.AreSame(a.EffectiveOptions, b.EffectiveOptions);
            Assert.AreSame(SystemTextJsonRpcSerializer.DefaultOptions, a.EffectiveOptions);
            Assert.IsTrue(a.EffectiveOptions.IsReadOnly);
            Assert.IsTrue(a.EffectiveOptions.IncludeFields);
            Assert.IsTrue(a.EffectiveOptions.PropertyNameCaseInsensitive);
            Assert.AreEqual(JsonIgnoreCondition.Never, a.EffectiveOptions.DefaultIgnoreCondition);
            Assert.IsTrue(JsonRpcConverters.ContainsAll(a.EffectiveOptions));
        }

        [Test]
        public void UserOptionsAreHonoured_EnumAsStringAndCamelCase()
        {
            var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            var serializer = new SystemTextJsonRpcSerializer(options);

            // options built from CreateDefaultOptions already carry the converters: used as-is, not copied
            Assert.AreSame(options, serializer.Options);
            Assert.AreSame(options, serializer.EffectiveOptions);

            var response = JsonRpcProcessor.ProcessSync(SessionId, "{\"jsonrpc\":\"2.0\",\"method\":\"shape\",\"params\":[\"box\"],\"id\":7}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":{\"shapeName\":\"box\",\"tint\":\"Green\",\"area\":4.0},\"id\":7}", response);

            // the default serializer still produces the library conventions for the same call
            var plain = JsonRpcProcessor.ProcessSync(SessionId, "{\"jsonrpc\":\"2.0\",\"method\":\"shape\",\"params\":[\"box\"],\"id\":7}", null, null);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":{\"ShapeName\":\"box\",\"Tint\":1,\"Area\":4.0},\"id\":7}", plain);
        }

        [Test]
        public void UserOptionsWithoutConvertersAreCopiedNotMutated()
        {
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            options.Converters.Add(new JsonStringEnumConverter());
            // use the options elsewhere first so they are read-only, as they would be in a real app
            JsonSerializer.Serialize(new Shape(), options);
            Assert.IsTrue(options.IsReadOnly);
            int before = options.Converters.Count;

            var serializer = new SystemTextJsonRpcSerializer(options);

            Assert.AreSame(options, serializer.Options);
            Assert.AreNotSame(options, serializer.EffectiveOptions);
            Assert.AreEqual(before, options.Converters.Count, "the caller's options must not be mutated");
            Assert.IsTrue(JsonRpcConverters.ContainsAll(serializer.EffectiveOptions));
            Assert.AreSame(JsonNamingPolicy.CamelCase, serializer.EffectiveOptions.PropertyNamingPolicy);
            // the user's converter is still first, so it keeps precedence
            Assert.IsInstanceOf<JsonStringEnumConverter>(serializer.EffectiveOptions.Converters[0]);

            var response = JsonRpcProcessor.ProcessSync(SessionId, "{\"jsonrpc\":\"2.0\",\"method\":\"shape\",\"params\":[\"box\"],\"id\":1}", null, serializer);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":{\"shapeName\":\"box\",\"tint\":\"Green\",\"area\":4.0},\"id\":1}", response);
        }

        [Test]
        public void SessionSerializerIsUsed()
        {
            var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();
            options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
            var handler = Handler.GetSessionHandler(SessionId);
            handler.Serializer = new SystemTextJsonRpcSerializer(options);
            try
            {
                var response = JsonRpcProcessor.ProcessSync(SessionId, "{\"jsonrpc\":\"2.0\",\"method\":\"shape\",\"params\":[\"box\"],\"id\":1}", null, null);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":{\"shape_name\":\"box\",\"tint\":1,\"area\":4.0},\"id\":1}", response);
            }
            finally
            {
                handler.Serializer = null;
            }
        }

        // ------------------------------------------------------------------ converters on their own

        private static readonly JsonSerializerOptions Wire = SystemTextJsonRpcSerializer.DefaultOptions;

        [TestCase(3.0, "3.0")]
        [TestCase(71.0, "71.0")]
        [TestCase(0.0, "0.0")]
        [TestCase(-2.0, "-2.0")]
        [TestCase(3.14159, "3.14159")]
        [TestCase(0.123, "0.123")]
        [TestCase(1e21, "1E+21")]
        public void DoubleWritesWithDecimalPlace(double value, string expected)
        {
            Assert.AreEqual(expected, JsonSerializer.Serialize(value, Wire));
            Assert.AreEqual(expected, JsonSerializer.Serialize((double?)value, Wire));
        }

        [TestCase(1.2345f, "1.2345")]
        [TestCase(987f, "987.0")]
        [TestCase(0f, "0.0")]
        public void SingleWritesWithDecimalPlace(float value, string expected)
        {
            Assert.AreEqual(expected, JsonSerializer.Serialize(value, Wire));
            Assert.AreEqual(expected, JsonSerializer.Serialize((float?)value, Wire));
        }

        [Test]
        public void DecimalWritesWithDecimalPlace()
        {
            Assert.AreEqual("71.0", JsonSerializer.Serialize(71m, Wire));
            Assert.AreEqual("0.0", JsonSerializer.Serialize(0.0m, Wire));
            Assert.AreEqual("1.25", JsonSerializer.Serialize(1.25m, Wire));
            Assert.AreEqual("671.0", JsonSerializer.Serialize((decimal?)671m, Wire));
            Assert.AreEqual("null", JsonSerializer.Serialize((decimal?)null, Wire));
        }

        [Test]
        public void NumbersMatchTheBuiltInSerializer()
        {
            var jsmn = AustinHarris.JsonRpc.Jsmn.JsmnSerializer.Instance;
            var stj = new SystemTextJsonRpcSerializer();
            foreach (var d in new[] { 0.0, 1.0, -1.0, 0.1, 1.5, 123456789.0, 1e-7, 1e21, 0.30000000000000004, double.MaxValue, double.Epsilon })
            {
                Assert.AreEqual(jsmn.Serialize(d), stj.Serialize(d), "double " + d.ToString("R"));
            }
            foreach (var f in new[] { 0f, 1f, 1.2345f, 3.14159f, 987f, 1e-7f, float.MaxValue })
            {
                Assert.AreEqual(jsmn.Serialize(f), stj.Serialize(f), "float " + f.ToString("R"));
            }
            foreach (var m in new[] { 0m, 0.0m, 1m, 71m, 1.25m, 987m, decimal.MaxValue, -0.5m })
            {
                Assert.AreEqual(jsmn.Serialize(m), stj.Serialize(m), "decimal " + m);
            }
        }

        [Test]
        public void NumericReadsCoerce()
        {
            Assert.AreEqual(1, JsonSerializer.Deserialize<int>("true", Wire));
            Assert.AreEqual(0, JsonSerializer.Deserialize<long>("false", Wire));
            Assert.AreEqual(12, JsonSerializer.Deserialize<int>("\"12\"", Wire));
            Assert.AreEqual(2, JsonSerializer.Deserialize<int>("2.5", Wire), "round half to even");
            Assert.AreEqual(4, JsonSerializer.Deserialize<short>("3.5", Wire), "round half to even");
            Assert.AreEqual(71.0, JsonSerializer.Deserialize<double>("71", Wire));
            Assert.AreEqual(1.5, JsonSerializer.Deserialize<double>("\"1.5\"", Wire));
            Assert.AreEqual(1.0, JsonSerializer.Deserialize<double>("true", Wire));
            Assert.AreEqual(71m, JsonSerializer.Deserialize<decimal>("71", Wire));
            Assert.AreEqual(1.2345f, JsonSerializer.Deserialize<float>("1.2345", Wire));
            Assert.AreEqual(255, JsonSerializer.Deserialize<byte>("255", Wire));
            Assert.AreEqual(ulong.MaxValue, JsonSerializer.Deserialize<ulong>(ulong.MaxValue.ToString(), Wire));
            Assert.IsNull(JsonSerializer.Deserialize<int?>("null", Wire));
            Assert.AreEqual(5, JsonSerializer.Deserialize<int?>("5", Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<double>("\"mytext\"", Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>("\"mytext\"", Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<int>("null", Wire));
            Assert.Throws<OverflowException>(() => JsonSerializer.Deserialize<byte>("256", Wire));
        }

        [Test]
        public void BooleanReadsCoerce()
        {
            Assert.IsTrue(JsonSerializer.Deserialize<bool>("true", Wire));
            Assert.IsFalse(JsonSerializer.Deserialize<bool>("false", Wire));
            Assert.IsTrue(JsonSerializer.Deserialize<bool>("123", Wire));
            Assert.IsFalse(JsonSerializer.Deserialize<bool>("0", Wire));
            Assert.IsTrue(JsonSerializer.Deserialize<bool>("\"true\"", Wire));
            Assert.IsTrue(JsonSerializer.Deserialize<bool>("\"1\"", Wire));
            Assert.IsNull(JsonSerializer.Deserialize<bool?>("null", Wire));
            Assert.AreEqual("true", JsonSerializer.Serialize(true, Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<bool>("\"maybe\"", Wire));
        }

        [Test]
        public void CharConverter()
        {
            Assert.AreEqual("\"b\"", JsonSerializer.Serialize('b', Wire));
            Assert.AreEqual("\"\\\"\"", JsonSerializer.Serialize('"', Wire));
            Assert.AreEqual("\"b\"", JsonSerializer.Serialize((char?)'b', Wire));
            Assert.AreEqual('b', JsonSerializer.Deserialize<char>("98", Wire));
            Assert.AreEqual('b', JsonSerializer.Deserialize<char>("\"b\"", Wire));
            Assert.AreEqual('b', JsonSerializer.Deserialize<char?>("98", Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<char>("\"bc\"", Wire));
        }

        [Test]
        public void DateTimeConverter()
        {
            var text = "2014-06-30T14:50:38.5208399+09:00";
            var parsed = JsonSerializer.Deserialize<DateTime>("\"" + text + "\"", Wire);
            Assert.AreEqual(DateTime.Parse(text), parsed);
            Assert.AreEqual(DateTimeKind.Local, parsed.Kind);
            Assert.AreEqual(parsed, JsonSerializer.Deserialize<DateTime?>("\"" + text + "\"", Wire));
            Assert.IsNull(JsonSerializer.Deserialize<DateTime?>("null", Wire));

            // Json.NET's text: no fraction when it is zero, trailing zeros trimmed otherwise
            var whole = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            Assert.AreEqual("\"2020-01-02T03:04:05Z\"", JsonSerializer.Serialize(whole, Wire));
            Assert.AreEqual("\"2020-01-02T03:04:05\"", JsonSerializer.Serialize(DateTime.SpecifyKind(whole, DateTimeKind.Unspecified), Wire));
            Assert.AreEqual("\"2020-01-02T03:04:05.5Z\"", JsonSerializer.Serialize(whole.AddTicks(5_000_000), Wire));
            Assert.AreEqual("\"2020-01-02T03:04:05.5208399Z\"", JsonSerializer.Serialize(whole.AddTicks(5_208_399), Wire));
            Assert.AreEqual(Newtonsoft.Json.JsonConvert.SerializeObject(whole.AddTicks(5_208_399)), JsonSerializer.Serialize(whole.AddTicks(5_208_399), Wire));

            // the '+' in an offset survives whatever encoder the options use
            var local = new DateTime(2014, 6, 30, 14, 50, 38, DateTimeKind.Local).AddTicks(5208399);
            var strictEncoder = new JsonSerializerOptions();
            JsonRpcConverters.AddMissing(strictEncoder);
            Assert.AreEqual(JsonSerializer.Serialize(local, Wire), JsonSerializer.Serialize(local, strictEncoder));
            StringAssert.DoesNotContain("\\u002B", JsonSerializer.Serialize(local, strictEncoder));

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"not a date\"", Wire));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("12", Wire));
        }

        [Test]
        public void DateTimesMatchTheBuiltInSerializer()
        {
            var jsmn = AustinHarris.JsonRpc.Jsmn.JsmnSerializer.Instance;
            var stj = new SystemTextJsonRpcSerializer();
            var now = DateTime.Now;
            foreach (var dt in new[]
            {
                now, now.ToUniversalTime(), DateTime.SpecifyKind(now, DateTimeKind.Unspecified),
                new DateTime(2014, 6, 30, 14, 50, 38, DateTimeKind.Local).AddTicks(5208399),
                new DateTime(1, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Unspecified)
            })
            {
                Assert.AreEqual(jsmn.Serialize(dt), stj.Serialize(dt), dt.Kind.ToString());
                Assert.AreEqual(jsmn.Serialize((DateTime?)dt), stj.Serialize((DateTime?)dt), dt.Kind.ToString());
            }
            foreach (var dto in new[] { DateTimeOffset.Now, DateTimeOffset.UtcNow, new DateTimeOffset(2014, 6, 30, 14, 50, 38, TimeSpan.FromHours(9)).AddTicks(5208399), new DateTimeOffset(2014, 6, 30, 14, 50, 38, TimeSpan.Zero) })
            {
                Assert.AreEqual(jsmn.Serialize(dto), stj.Serialize(dto), dto.ToString());
            }
        }

        // ------------------------------------------------------------------ object model

        [Test]
        public void ReadAsObjectReturnsJsonElement()
        {
            var serializer = new SystemTextJsonRpcSerializer();
            var bytes = Encoding.UTF8.GetBytes("{\"str\":\"x\",\"n\":[1,2.5,null]}");

            var value = serializer.Read(bytes, typeof(object));
            Assert.IsInstanceOf<JsonElement>(value);
            var element = (JsonElement)value;
            Assert.AreEqual(JsonValueKind.Object, element.ValueKind);
            Assert.AreEqual("x", element.GetProperty("str").GetString());
            Assert.AreEqual(3, element.GetProperty("n").GetArrayLength());

            Assert.IsInstanceOf<JsonElement>(serializer.Read<object>(bytes));
            Assert.IsInstanceOf<JsonElement>(serializer.Read<object>(Encoding.UTF8.GetBytes("12")));
            Assert.IsNull(serializer.Read(Encoding.UTF8.GetBytes("null"), typeof(object)));

            // and it writes back out unchanged
            Assert.AreEqual("{\"str\":\"x\",\"n\":[1,2.5,null]}", serializer.Serialize(value, typeof(object)));
            Assert.AreEqual("{\"str\":\"x\",\"n\":[1,2.5,null]}", serializer.Serialize(element));
        }

        [Test]
        public void ReadWriteRoundTripsValues()
        {
            var serializer = new SystemTextJsonRpcSerializer();
            Assert.AreEqual("abc", serializer.Read<string>(Encoding.UTF8.GetBytes("\"abc\"")));
            Assert.AreEqual(12, serializer.Read<int>(Encoding.UTF8.GetBytes("12")));
            Assert.AreEqual(1.5, serializer.Read<double>(Encoding.UTF8.GetBytes("1.5")));
            CollectionAssert.AreEqual(new[] { 1, 2 }, serializer.Read<int[]>(Encoding.UTF8.GetBytes("[1,2]")));
            Assert.AreEqual("x", serializer.Read<CalculatorService.CustomString>(Encoding.UTF8.GetBytes("{\"STR\":\"x\"}")).str, "public field, case-insensitive");
            Assert.AreEqual("[1,2]", serializer.Serialize(new List<int> { 1, 2 }));
            Assert.AreEqual("null", serializer.Serialize((string)null));
            Assert.AreEqual("null", serializer.Serialize(null, typeof(Shape)));
            Assert.AreEqual("{\"ShapeName\":null,\"Tint\":0,\"Area\":0.0}", serializer.Serialize(new Shape()));
            Assert.AreEqual("{\"ClassName\":\"System.InvalidOperationException\",\"Message\":\"boom\",\"Source\":null,\"StackTraceString\":null,\"HResult\":-2146233079,\"InnerException\":null}",
                serializer.Serialize(ExceptionInfo.From(new InvalidOperationException("boom"))));
            Assert.Throws<JsonException>(() => serializer.Read<int>(Encoding.UTF8.GetBytes("\"x\"")));
            Assert.Throws<JsonException>(() => serializer.Read<int>(Encoding.UTF8.GetBytes("12 13")), "exactly one value");
        }

        [Test]
        public void WriteDoesNotLeaveTrailingBytesAndSupportsNestedWriters()
        {
            var serializer = new SystemTextJsonRpcSerializer();
            using (var w = new PooledByteBufferWriter(16))
            {
                serializer.Write(w, 1);
                serializer.Write(w, "a");
                serializer.Write(w, new[] { 1.0, 2.5 });
                Assert.AreEqual("1\"a\"[1.0,2.5]", w.ToString());
            }

            // a converter that re-enters the serializer on the same thread must not corrupt the cached writer
            var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();
            options.Converters.Add(new ReentrantConverter(serializer));
            var reentrant = new SystemTextJsonRpcSerializer(options);
            Assert.AreEqual("[{\"Inner\":\"{\\\"ShapeName\\\":\\\"s\\\",\\\"Tint\\\":0,\\\"Area\\\":1.0}\"},2]", reentrant.Serialize(new object[] { new Reentrant { Inner = new Shape { ShapeName = "s", Area = 1 } }, 2 }));
        }

        public class Reentrant
        {
            public Shape Inner { get; set; }
        }

        private sealed class ReentrantConverter : JsonConverter<Shape>
        {
            private readonly SystemTextJsonRpcSerializer _serializer;
            public ReentrantConverter(SystemTextJsonRpcSerializer serializer) { _serializer = serializer; }
            public override Shape Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Shape value, JsonSerializerOptions options)
            {
                // serialize the nested value with the outer serializer while the outer write is in progress
                writer.WriteStringValue(_serializer.Serialize(value));
            }
        }

        [Test]
        public void PreProcessHandlerSeesJsonElementParams()
        {
            object seen = null;
            var handler = Handler.GetSessionHandler(SessionId);
            handler.SetPreProcessHandler((rpc, ctx) => { seen = rpc.Params; return null; });
            try
            {
                var response = JsonRpcProcessor.ProcessSync(SessionId, "{\"jsonrpc\":\"2.0\",\"method\":\"sum\",\"params\":{\"a\":1,\"b\":2},\"id\":3}", null, null);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":3}", response);
                Assert.IsInstanceOf<JsonElement>(seen);
                Assert.AreEqual(2, ((JsonElement)seen).GetProperty("b").GetInt32());
            }
            finally
            {
                handler.SetPreProcessHandler(null);
            }
        }

        [Test]
        public void HandlerHandleRoundTripsJsonRequest()
        {
            var handler = Handler.GetSessionHandler(SessionId);
            var serializer = new SystemTextJsonRpcSerializer();

            var parameters = serializer.Read(Encoding.UTF8.GetBytes("[\"hello\"]"), typeof(object));
            var response = handler.Handle(new JsonRequest("echo", parameters, 5L));
            Assert.IsNull(response.Error);
            Assert.AreEqual("hello", response.Result);
            Assert.AreEqual(5L, response.Id);

            var named = serializer.Read(Encoding.UTF8.GetBytes("{\"b\":2,\"a\":40}"), typeof(object));
            response = handler.Handle(new JsonRequest("sum", named, "x"));
            Assert.IsNull(response.Error);
            Assert.AreEqual(42, response.Result);
            Assert.AreEqual("x", response.Id);

            response = handler.Handle(new JsonRequest("sum", parameters, 1L));
            Assert.IsNotNull(response.Error);
            Assert.AreEqual(-32602, response.Error.code);
        }
    }
}
