using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Newtonsoft;
using AustinHarris.JsonRpc.Serialization;
using AustinHarris.JsonRpc.SystemTextJson;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Regression tests for the serializer findings of the 2.0 review: write-only members (built-in writer),
    /// non-finite floats (all three serializers, byte parity with Json.NET), mutable structs (built-in mapper)
    /// and the System.Text.Json cached writer after a failed write.
    /// </summary>
    [TestFixture]
    public class SerializerHardeningTests
    {
        private static JsonRpcSerializer[] All() => new JsonRpcSerializer[]
        {
            JsmnSerializer.Instance,
            new NewtonsoftJsonRpcSerializer(),
            new SystemTextJsonRpcSerializer()
        };

        private static T Read<T>(JsonRpcSerializer s, string json) => s.Read<T>(Encoding.UTF8.GetBytes(json));

        // ------------------------------------------------------------------ finding 9: write-only members

        public class WriteOnlyFirst
        {
            public int Hidden { set { } }
            public int Visible => 3;
        }

        public class WriteOnlyMiddle
        {
            public int A => 1;
            public int Hidden { set { } }
            public int B => 2;
        }

        public class WriteOnlyLast
        {
            public int A => 1;
            public int B => 2;
            public int Hidden { set { } }
        }

        public class WriteOnlyOnly
        {
            public int Hidden { set { } }
        }

        public class WriteOnlyAroundFields
        {
            public int Hidden1 { set { } }
            public int F = 5;
            public int Hidden2 { set { } }
            public string G = "g";
            public int Hidden3 { set { } }
        }

        [Test]
        public void WriteOnlyMember_First_IsSkippedWithoutLeadingComma()
        {
            Assert.AreEqual("{\"Visible\":3}", JsmnSerializer.Instance.Serialize(new WriteOnlyFirst()));
        }

        [Test]
        public void WriteOnlyMember_Middle_IsSkippedWithoutDoubleComma()
        {
            Assert.AreEqual("{\"A\":1,\"B\":2}", JsmnSerializer.Instance.Serialize(new WriteOnlyMiddle()));
        }

        [Test]
        public void WriteOnlyMember_Last_IsSkippedWithoutTrailingComma()
        {
            Assert.AreEqual("{\"A\":1,\"B\":2}", JsmnSerializer.Instance.Serialize(new WriteOnlyLast()));
        }

        [Test]
        public void WriteOnlyMember_Only_WritesEmptyObject()
        {
            Assert.AreEqual("{}", JsmnSerializer.Instance.Serialize(new WriteOnlyOnly()));
        }

        [Test]
        public void WriteOnlyMember_BetweenFields_IsSkipped()
        {
            Assert.AreEqual("{\"F\":5,\"G\":\"g\"}", JsmnSerializer.Instance.Serialize(new WriteOnlyAroundFields()));
        }

        [Test]
        public void WriteOnlyMember_AllSerializersAgree()
        {
            foreach (var s in All())
            {
                Assert.AreEqual("{\"Visible\":3}", s.Serialize(new WriteOnlyFirst()), s.Name);
                Assert.AreEqual("{\"A\":1,\"B\":2}", s.Serialize(new WriteOnlyMiddle()), s.Name);
                Assert.AreEqual("{\"A\":1,\"B\":2}", s.Serialize(new WriteOnlyLast()), s.Name);
                Assert.AreEqual("{}", s.Serialize(new WriteOnlyOnly()), s.Name);
            }
        }

        [Test]
        public void WriteOnlyMember_StillBindsOnRead()
        {
            // the setter is still honoured when reading (the plan keeps the member, only the writer skips it)
            var s = JsmnSerializer.Instance;
            Assert.AreEqual(3, Read<WriteOnlyFirst>(s, "{\"Hidden\":9,\"Visible\":1}").Visible);
        }

        private sealed class WriteOnlyService
        {
            [JsonRpcMethod("hardening.shape")]
            public WriteOnlyFirst Shape() => new WriteOnlyFirst();
        }

        [Test]
        public void WriteOnlyMember_RpcResultIsValidJson()
        {
            const string session = "hardening-writeonly";
            ServiceBinder.BindService(session, new WriteOnlyService());
            try
            {
                foreach (var s in All())
                {
                    var response = JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.shape\",\"id\":1}", null, s);
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":{\"Visible\":3},\"id\":1}", response, s.Name);
                }
            }
            finally
            {
                Handler.DestroySession(session);
            }
        }

        // ------------------------------------------------------------------ finding 10: non-finite floats

        public class Floats
        {
            public double D { get; set; }
            public float F { get; set; }
            public double? N { get; set; }
        }

        [TestCase(double.NaN, "\"NaN\"")]
        [TestCase(double.PositiveInfinity, "\"Infinity\"")]
        [TestCase(double.NegativeInfinity, "\"-Infinity\"")]
        public void NonFiniteDouble_WritesJsonNetString(double value, string expected)
        {
            // Json.NET's default FloatFormatHandling.String is the reference
            Assert.AreEqual(expected, global::Newtonsoft.Json.JsonConvert.SerializeObject(value));
            foreach (var s in All())
            {
                Assert.AreEqual(expected, s.Serialize(value), s.Name + " double");
                Assert.AreEqual(expected, s.Serialize((double?)value), s.Name + " double?");
                Assert.AreEqual(expected, s.Serialize((object)value, typeof(object)), s.Name + " boxed double");
            }
        }

        [TestCase(float.NaN, "\"NaN\"")]
        [TestCase(float.PositiveInfinity, "\"Infinity\"")]
        [TestCase(float.NegativeInfinity, "\"-Infinity\"")]
        public void NonFiniteSingle_WritesJsonNetString(float value, string expected)
        {
            Assert.AreEqual(expected, global::Newtonsoft.Json.JsonConvert.SerializeObject(value));
            foreach (var s in All())
            {
                Assert.AreEqual(expected, s.Serialize(value), s.Name + " float");
                Assert.AreEqual(expected, s.Serialize((float?)value), s.Name + " float?");
            }
        }

        [Test]
        public void NonFinite_InsideObjectsAndArrays_AllSerializersProduceTheSameBytes()
        {
            var poco = new Floats { D = double.NaN, F = float.NegativeInfinity, N = double.PositiveInfinity };
            var list = new List<double> { 1.5, double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            const string expectedPoco = "{\"D\":\"NaN\",\"F\":\"-Infinity\",\"N\":\"Infinity\"}";
            const string expectedList = "[1.5,\"NaN\",\"Infinity\",\"-Infinity\"]";
            Assert.AreEqual(expectedPoco, global::Newtonsoft.Json.JsonConvert.SerializeObject(poco));
            Assert.AreEqual(expectedList, global::Newtonsoft.Json.JsonConvert.SerializeObject(list));
            foreach (var s in All())
            {
                Assert.AreEqual(expectedPoco, s.Serialize(poco), s.Name);
                Assert.AreEqual(expectedList, s.Serialize(list), s.Name);
            }
        }

        [Test]
        public void NonFinite_StringsReadBackToTheValues()
        {
            foreach (var s in All())
            {
                Assert.IsNaN(Read<double>(s, "\"NaN\""), s.Name);
                Assert.AreEqual(double.PositiveInfinity, Read<double>(s, "\"Infinity\""), s.Name);
                Assert.AreEqual(double.NegativeInfinity, Read<double>(s, "\"-Infinity\""), s.Name);
                Assert.IsNaN(Read<float>(s, "\"NaN\""), s.Name);
                Assert.AreEqual(float.PositiveInfinity, Read<float>(s, "\"Infinity\""), s.Name);
                Assert.AreEqual(float.NegativeInfinity, Read<float>(s, "\"-Infinity\""), s.Name);
                Assert.IsNaN(Read<double?>(s, "\"NaN\"").Value, s.Name);
                Assert.AreEqual(double.NegativeInfinity, Read<double?>(s, "\"-Infinity\"").Value, s.Name);

                var poco = Read<Floats>(s, "{\"D\":\"NaN\",\"F\":\"-Infinity\",\"N\":\"Infinity\"}");
                Assert.IsNaN(poco.D, s.Name);
                Assert.AreEqual(float.NegativeInfinity, poco.F, s.Name);
                Assert.AreEqual(double.PositiveInfinity, poco.N, s.Name);

                // and a value round-trips through the serializer
                Assert.IsNaN(Read<double>(s, s.Serialize(double.NaN)), s.Name);
                Assert.AreEqual(double.NegativeInfinity, Read<double>(s, s.Serialize(double.NegativeInfinity)), s.Name);
            }
        }

        private sealed class NonFiniteService
        {
            [JsonRpcMethod("hardening.nonfinite")]
            public double Echo(double d) => d;
        }

        [Test]
        public void NonFinite_RpcRoundTripIsValidJsonOnAllSerializers()
        {
            const string session = "hardening-nonfinite";
            ServiceBinder.BindService(session, new NonFiniteService());
            try
            {
                foreach (var s in All())
                {
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"NaN\",\"id\":1}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.nonfinite\",\"params\":[\"NaN\"],\"id\":1}", null, s), s.Name);
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"-Infinity\",\"id\":2}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.nonfinite\",\"params\":[\"-Infinity\"],\"id\":2}", null, s), s.Name);
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1.5,\"id\":3}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.nonfinite\",\"params\":[1.5],\"id\":3}", null, s), s.Name);
                }
            }
            finally
            {
                Handler.DestroySession(session);
            }
        }

        [Test]
        public void FiniteNumbersStillWriteWithDecimalPlace()
        {
            foreach (var s in All())
            {
                Assert.AreEqual("3.0", s.Serialize(3.0), s.Name);
                Assert.AreEqual("-2.5", s.Serialize(-2.5), s.Name);
                Assert.AreEqual("987.0", s.Serialize(987f), s.Name);
            }
        }

        // ------------------------------------------------------------------ finding 18: mutable structs

        public struct Pair
        {
            public int X { get; set; }
            public int Y { get; set; }
        }

        public struct FieldPair
        {
            public int X;
            public string Name;
        }

        public class PairHolder
        {
            public Pair P { get; set; }
            public Pair? Q { get; set; }
            public FieldPair F;
        }

        public struct Outer
        {
            public Pair Inner { get; set; }
            public List<Pair> Items { get; set; }
        }

        [Test]
        public void MutableStruct_Properties_Bind()
        {
            foreach (var s in All())
            {
                var p = Read<Pair>(s, "{\"X\":7,\"Y\":8}");
                Assert.AreEqual(7, p.X, s.Name);
                Assert.AreEqual(8, p.Y, s.Name);
                Assert.AreEqual(7, Read<Pair>(s, "{\"x\":7}").X, s.Name + " case-insensitive");
                Assert.AreEqual(0, Read<Pair>(s, "{}").X, s.Name + " default");
            }
        }

        [Test]
        public void MutableStruct_Fields_Bind()
        {
            foreach (var s in All())
            {
                var f = Read<FieldPair>(s, "{\"X\":3,\"Name\":\"n\"}");
                Assert.AreEqual(3, f.X, s.Name);
                Assert.AreEqual("n", f.Name, s.Name);
            }
        }

        [Test]
        public void MutableStruct_Nullable_Binds()
        {
            foreach (var s in All())
            {
                var p = Read<Pair?>(s, "{\"X\":7}");
                Assert.IsTrue(p.HasValue, s.Name);
                Assert.AreEqual(7, p.Value.X, s.Name);
                Assert.IsFalse(Read<Pair?>(s, "null").HasValue, s.Name);
            }
        }

        [Test]
        public void MutableStruct_InListAndArray_Binds()
        {
            foreach (var s in All())
            {
                var list = Read<List<Pair>>(s, "[{\"X\":1},{\"X\":2,\"Y\":3}]");
                Assert.AreEqual(2, list.Count, s.Name);
                Assert.AreEqual(1, list[0].X, s.Name);
                Assert.AreEqual(2, list[1].X, s.Name);
                Assert.AreEqual(3, list[1].Y, s.Name);
                var array = Read<Pair[]>(s, "[{\"Y\":9}]");
                Assert.AreEqual(9, array[0].Y, s.Name);
            }
        }

        [Test]
        public void MutableStruct_AsPocoProperty_Binds()
        {
            foreach (var s in All())
            {
                var h = Read<PairHolder>(s, "{\"P\":{\"X\":3},\"Q\":{\"X\":4,\"Y\":5},\"F\":{\"X\":6,\"Name\":\"f\"}}");
                Assert.AreEqual(3, h.P.X, s.Name);
                Assert.AreEqual(4, h.Q.Value.X, s.Name);
                Assert.AreEqual(5, h.Q.Value.Y, s.Name);
                Assert.AreEqual(6, h.F.X, s.Name);
                Assert.AreEqual("f", h.F.Name, s.Name);
                Assert.IsNull(Read<PairHolder>(s, "{\"P\":{\"X\":3},\"Q\":null}").Q, s.Name);
            }
        }

        [Test]
        public void MutableStruct_NestedInStruct_Binds()
        {
            foreach (var s in All())
            {
                var o = Read<Outer>(s, "{\"Inner\":{\"X\":1,\"Y\":2},\"Items\":[{\"X\":3}]}");
                Assert.AreEqual(1, o.Inner.X, s.Name);
                Assert.AreEqual(2, o.Inner.Y, s.Name);
                Assert.AreEqual(3, o.Items[0].X, s.Name);
            }
        }

        [Test]
        public void MutableStruct_WritesLikeAClass()
        {
            foreach (var s in All())
            {
                Assert.AreEqual("{\"X\":7,\"Y\":8}", s.Serialize(new Pair { X = 7, Y = 8 }), s.Name);
                Assert.AreEqual("{\"X\":7,\"Y\":0}", s.Serialize((Pair?)new Pair { X = 7 }), s.Name);
                Assert.AreEqual("null", s.Serialize((Pair?)null), s.Name);
                Assert.AreEqual("[{\"X\":1,\"Y\":0}]", s.Serialize(new List<Pair> { new Pair { X = 1 } }), s.Name);
            }
        }

        private sealed class StructService
        {
            [JsonRpcMethod("hardening.pair")]
            public int Sum(Pair p) => p.X + p.Y;

            [JsonRpcMethod("hardening.pairs")]
            public int SumAll(List<Pair> items)
            {
                int total = 0;
                foreach (var p in items) total += p.X + p.Y;
                return total;
            }
        }

        [Test]
        public void MutableStruct_AsRpcParameter_BindsOnAllSerializers()
        {
            const string session = "hardening-struct";
            ServiceBinder.BindService(session, new StructService());
            try
            {
                foreach (var s in All())
                {
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":15,\"id\":1}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.pair\",\"params\":[{\"X\":7,\"Y\":8}],\"id\":1}", null, s), s.Name);
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":15,\"id\":2}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.pair\",\"params\":{\"p\":{\"X\":7,\"Y\":8}},\"id\":2}", null, s), s.Name);
                    Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":10,\"id\":3}",
                        JsonRpcProcessor.ProcessSync(session, "{\"jsonrpc\":\"2.0\",\"method\":\"hardening.pairs\",\"params\":[[{\"X\":1,\"Y\":2},{\"X\":3,\"Y\":4}]],\"id\":3}", null, s), s.Name);
                }
            }
            finally
            {
                Handler.DestroySession(session);
            }
        }

        public class NoDefaultCtor
        {
            public NoDefaultCtor(int x) { X = x; }
            public int X { get; }
        }

        [Test]
        public void ClassWithoutParameterlessConstructor_StillRejectedByBuiltIn()
        {
            // a NotSupportedException, not a JsonRpcBindException: the type is the server's limitation, not the client's value
            Assert.Throws<NotSupportedException>(() => Read<NoDefaultCtor>(JsmnSerializer.Instance, "{\"X\":1}"));
        }

        // ------------------------------------------------------------------ finding 3: System.Text.Json writer after a throw

        public class Explodes
        {
            public int Good => 1;
            public int Bad => throw new InvalidOperationException("boom");
        }

        public class Wrapper
        {
            public int Before { get; set; } = 1;
            public Nested Value { get; set; } = new Nested();
            public int After { get; set; } = 2;
        }

        public class Nested { }

        [Test]
        public void StjFailedWrite_ThenReuseWithSameOptions()
        {
            var a = new SystemTextJsonRpcSerializer();
            Assert.Catch(() => a.Serialize(new Explodes()));
            Assert.AreEqual("7", a.Serialize(7));
            Assert.AreEqual("[1,\"a\"]", a.Serialize(new object[] { 1, "a" }));
            Assert.AreEqual("{\"Good\":1}", a.Serialize(new { Good = 1 }));
        }

        [Test]
        public void StjFailedWrite_ThenReuseWithDifferentOptions()
        {
            // the review's repro: A fails part-way through a value, then B (other options) evicts A's cached writer
            var a = new SystemTextJsonRpcSerializer();
            var b = new SystemTextJsonRpcSerializer(new JsonSerializerOptions());
            Assert.Catch(() => a.Serialize(new Explodes()));
            Assert.AreEqual("7", b.Serialize(7));
            Assert.AreEqual("7", a.Serialize(7));
            // and the other way round
            Assert.Catch(() => b.Serialize(new Explodes()));
            Assert.AreEqual("\"x\"", a.Serialize("x"));
            Assert.AreEqual("\"x\"", b.Serialize("x"));
        }

        [Test]
        public void StjFailedWrite_LeavesNothingInTheCallersBuffer()
        {
            var a = new SystemTextJsonRpcSerializer();
            using (var w = new PooledByteBufferWriter(64))
            {
                Assert.Catch(() => a.Write(w, new Explodes()));
                Assert.AreEqual(0, w.WrittenCount, "nothing may be flushed into the output after the failure");
                a.Write(w, 7);
                Assert.AreEqual("7", w.ToString());
            }
        }

        [Test]
        public void StjFailedWrite_OnTheOutputItself_ThenReuse()
        {
            var a = new SystemTextJsonRpcSerializer();
            Assert.Catch(() => a.Write(new ThrowingBufferWriter(), new int[256]));
            Assert.AreEqual("[1,2]", a.Serialize(new[] { 1, 2 }));
            var b = new SystemTextJsonRpcSerializer(new JsonSerializerOptions());
            Assert.AreEqual("[1,2]", b.Serialize(new[] { 1, 2 }));
        }

        [Test]
        public void StjFailedWrite_Nested_CaughtInsideConverter()
        {
            var outer = new SystemTextJsonRpcSerializer();
            var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();
            options.Converters.Add(new NestedConverter(outer, swallow: true));
            var reentrant = new SystemTextJsonRpcSerializer(options);

            // the nested write fails on a throwaway writer while the outer (cached) writer is in use
            Assert.AreEqual("{\"Before\":1,\"Value\":\"7\",\"After\":2}", reentrant.Serialize(new Wrapper()));
            Assert.AreEqual("7", outer.Serialize(7));
            Assert.AreEqual("7", reentrant.Serialize(7));
        }

        [Test]
        public void StjFailedWrite_Nested_PropagatingToTheOuterWrite()
        {
            var outer = new SystemTextJsonRpcSerializer();
            var options = SystemTextJsonRpcSerializer.CreateDefaultOptions();
            options.Converters.Add(new NestedConverter(outer, swallow: false));
            var reentrant = new SystemTextJsonRpcSerializer(options);

            using (var w = new PooledByteBufferWriter(64))
            {
                Assert.Catch(() => reentrant.Write(w, new Wrapper()));
                Assert.AreEqual(0, w.WrittenCount);
            }
            Assert.AreEqual("7", outer.Serialize(7));
            Assert.AreEqual("7", reentrant.Serialize(7));
            Assert.AreEqual("7", new SystemTextJsonRpcSerializer(new JsonSerializerOptions()).Serialize(7));
        }

        private sealed class NestedConverter : JsonConverter<Nested>
        {
            private readonly SystemTextJsonRpcSerializer _serializer;
            private readonly bool _swallow;
            public NestedConverter(SystemTextJsonRpcSerializer serializer, bool swallow) { _serializer = serializer; _swallow = swallow; }
            public override Nested Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, Nested value, JsonSerializerOptions options)
            {
                if (_swallow)
                {
                    try { _serializer.Serialize(new Explodes()); } catch (InvalidOperationException) { }
                    writer.WriteStringValue(_serializer.Serialize(7));
                }
                else
                {
                    _serializer.Serialize(new Explodes());
                }
            }
        }

        private sealed class ThrowingBufferWriter : System.Buffers.IBufferWriter<byte>
        {
            private readonly byte[] _buffer = new byte[16];
            public void Advance(int count) => throw new InvalidOperationException("output is closed");
            public Memory<byte> GetMemory(int sizeHint = 0) => _buffer;
            public Span<byte> GetSpan(int sizeHint = 0) => _buffer;
        }
    }
}
