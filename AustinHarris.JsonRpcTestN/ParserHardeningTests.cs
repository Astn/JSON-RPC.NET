using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Newtonsoft;
using AustinHarris.JsonRpc.Serialization;
using AustinHarris.JsonRpc.SystemTextJson;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Parser hardening from the 2026-09-23 review: findings 1 (nesting depth, O(1) container close),
    /// 2 (strict grammar and UTF-8 validation), 8 (lenient ids aliasing the name-decode buffer) and
    /// 15 (escaped envelope member names). The envelope reader is shared by every serializer, so each case
    /// runs against strict jsmn, lenient jsmn, Json.NET (lenient) and System.Text.Json (strict).
    /// </summary>
    [TestFixture]
    public class ParserHardeningTests
    {
        private const string Session = "parser-hardening";
        private const string Ok7 = "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}";

        public static int CountedCalls;

        /// <summary>A plain class: JsonRpcService's constructor would bind it to the default session.</summary>
        public class HardeningService
        {
            [JsonRpcMethod] public int ping() => 7;
            [JsonRpcMethod] public int counted() { Interlocked.Increment(ref CountedCalls); return 7; }
            [JsonRpcMethod] public string echo(string s) => s;
            [JsonRpcMethod] public int accept(object o) => 7;
            [JsonRpcMethod] public string named(string a, string b) => a + "|" + b;
        }

        private static readonly object BindLock = new object();
        private static bool _bound;

        [OneTimeSetUp]
        public void Bind()
        {
            lock (BindLock)
            {
                if (_bound) return;
                ServiceBinder.BindService(Session, new HardeningService());
                _bound = true;
            }
        }

        public static IEnumerable<TestCaseData> Serializers()
        {
            yield return new TestCaseData(JsmnSerializer.Instance).SetArgDisplayNames("jsmn");
            yield return new TestCaseData(new JsmnSerializer(lenient: true)).SetArgDisplayNames("jsmn-lenient");
            yield return new TestCaseData(new NewtonsoftJsonRpcSerializer()).SetArgDisplayNames("newtonsoft");
            yield return new TestCaseData(new SystemTextJsonRpcSerializer()).SetArgDisplayNames("stj");
        }

        public static IEnumerable<TestCaseData> LenientSerializers()
        {
            yield return new TestCaseData(new JsmnSerializer(lenient: true)).SetArgDisplayNames("jsmn-lenient");
            yield return new TestCaseData(new NewtonsoftJsonRpcSerializer()).SetArgDisplayNames("newtonsoft");
        }

        // ------------------------------------------------------------------ helpers

        private static string Run(JsonRpcSerializer s, string json) => JsonRpcProcessor.ProcessSync(Session, json, null, s);

        private static string Run(JsonRpcSerializer s, byte[] utf8)
        {
            var w = new ArrayBufferWriter<byte>();
            JsonRpcProcessor.Process(Session, new ReadOnlyMemory<byte>(utf8), w, null, s);
            return Encoding.UTF8.GetString(w.WrittenSpan);
        }

        private static byte[] Bytes(params object[] parts)
        {
            var list = new List<byte>();
            foreach (var p in parts)
            {
                if (p is string str) list.AddRange(Encoding.UTF8.GetBytes(str));
                else if (p is byte[] arr) list.AddRange(arr);
                else if (p is byte b) list.Add(b);
                else if (p is int i) list.Add(checked((byte)i));
                else throw new ArgumentException(p.GetType().Name);
            }
            return list.ToArray();
        }

        private static void AssertParseError(string response, string detail = null, string because = null)
        {
            StringAssert.Contains("\"code\":-32700", response, because);
            StringAssert.EndsWith("\"id\":null}", response, because);
            if (detail != null) StringAssert.Contains(detail, response, because);
        }

        private static string ResultOf(string response)
        {
            StringAssert.DoesNotContain("\"error\"", response);
            return JObject.Parse(response)["result"].Value<string>();
        }

        /// <summary>{"method":…,"unused":[[[…0…]]],"id":1} with <paramref name="depth"/> open containers including the root object.</summary>
        private static string Nested(int depth, string method = "ping")
        {
            return "{\"method\":\"" + method + "\",\"unused\":" + new string('[', depth - 1) + "0" + new string(']', depth - 1) + ",\"id\":1}";
        }

        // ------------------------------------------------------------------ finding 1: nesting depth

        [Test]
        public void Depth_DefaultIs64()
        {
            Assert.AreEqual(64, JsmnTokenizer.DefaultMaxDepth);
            Assert.AreEqual(64, JsmnSerializer.Instance.MaxDepth);
            Assert.AreEqual(64, new JsmnSerializer(lenient: true).MaxDepth);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Depth_AtDefaultLimit_Dispatches(JsonRpcSerializer s)
        {
            Assert.AreEqual(Ok7, Run(s, Nested(64)));
        }

        [TestCaseSource(nameof(Serializers))]
        public void Depth_OverDefaultLimit_IsParseErrorAndDoesNotDispatch(JsonRpcSerializer s)
        {
            int before = CountedCalls;
            var r = Run(s, Nested(65, "counted"));
            AssertParseError(r, "depth");
            StringAssert.Contains("64", r);
            Assert.AreEqual(before, CountedCalls, "the method must not run");
        }

        [Test]
        public void Depth_ConfigurableOnJsmnSerializer()
        {
            var s = new JsmnSerializer(lenient: false, maxDepth: 8);
            Assert.AreEqual(8, s.MaxDepth);
            Assert.AreEqual(Ok7, Run(s, Nested(8)));
            AssertParseError(Run(s, Nested(9)), "depth of 8");
            Assert.Throws<ArgumentOutOfRangeException>(() => new JsmnSerializer(false, 0));
        }

        [Test]
        public void Depth_FollowsTheSerializerOptions()
        {
            // JsonRpcSerializer.MaxDepth is virtual: the envelope reader enforces whatever limit the JSON library
            // itself was configured with, so an envelope is never accepted that the value converter would reject
            var stj = new SystemTextJsonRpcSerializer(new System.Text.Json.JsonSerializerOptions { MaxDepth = 128 });
            Assert.AreEqual(128, stj.MaxDepth);
            Assert.AreEqual(Ok7, Run(stj, Nested(100)));
            AssertParseError(Run(stj, Nested(129)), "depth of 128");

            var nsj = new NewtonsoftJsonRpcSerializer(new Newtonsoft.Json.JsonSerializerSettings { MaxDepth = 8 });
            Assert.AreEqual(8, nsj.MaxDepth);
            Assert.AreEqual(Ok7, Run(nsj, Nested(8)));
            AssertParseError(Run(nsj, Nested(9)), "depth of 8");

            Assert.AreEqual(64, new SystemTextJsonRpcSerializer().MaxDepth);
            Assert.AreEqual(64, new NewtonsoftJsonRpcSerializer().MaxDepth);
        }

        [Test]
        public void Depth_ValueReaderHonoursLimit()
        {
            // Read<T>/Deserialize<T> tokenize with the same limit, so a bare value is bounded as well
            var s = new JsmnSerializer(lenient: false, maxDepth: 4);
            Assert.IsNotNull(s.Deserialize<object>("[[[[1]]]]"));
            var ex = Assert.Throws<JsonRpcBindException>(() => s.Deserialize<object>("[[[[[1]]]]]"));
            StringAssert.Contains("depth", ex.Message);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Depth_BoundsRecursiveParameterBinding(JsonRpcSerializer s)
        {
            // accept(object) descends the value recursively (JsmnMapper.ReadDynamic for jsmn); the tokenizer's
            // limit runs before any binding, so the recursion can never exceed MaxDepth
            string Deep(int arrays) => "{\"method\":\"accept\",\"params\":[" + new string('[', arrays) + "1" + new string(']', arrays) + "],\"id\":1}";
            Assert.AreEqual(Ok7, Run(s, Deep(62)));          // root + params + 62 = 64
            AssertParseError(Run(s, Deep(63)), "depth");
        }

        [Test]
        public void Depth_1000To8000_RejectedQuickly()
        {
            foreach (int depth in new[] { 1000, 2000, 4000, 8000 })
            {
                var json = Nested(depth);
                var sw = Stopwatch.StartNew();
                var r = Run(JsmnSerializer.Instance, json);
                sw.Stop();
                AssertParseError(r, "depth", "depth " + depth);
                Assert.Less(sw.ElapsedMilliseconds, 100, "depth " + depth);
            }
        }

        [Test]
        public void Depth_Raised_ParseTimeGrowsLinearly()
        {
            var s = new JsmnSerializer(lenient: false, maxDepth: 10000);
            foreach (int depth in new[] { 1000, 2000, 4000, 8000 })
                Assert.AreEqual(Ok7, Run(s, Nested(depth)), "depth " + depth);

            var tok = new JsmnTokenizer { MaxDepth = 10000 };
            double t1000 = MinTicks(tok, Encoding.UTF8.GetBytes(Nested(1000)));
            double t8000 = MinTicks(tok, Encoding.UTF8.GetBytes(Nested(8000)));
            // 8x the input: linear ~8x; the old parent-chain walk was quadratic (~64x, measured 18-40x)
            Assert.Less(t8000 / t1000, 32.0, "1000 deep: " + t1000 + " ticks, 8000 deep: " + t8000 + " ticks");
        }

        private static double MinTicks(JsmnTokenizer tok, byte[] doc)
        {
            const int Inner = 20;
            long best = long.MaxValue;
            for (int rep = 0; rep < 15; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < Inner; i++)
                {
                    if (tok.Parse(doc) <= 0) Assert.Fail("parse failed");
                }
                sw.Stop();
                if (sw.ElapsedTicks < best) best = sw.ElapsedTicks;
            }
            return best;
        }

        // ------------------------------------------------------------------ finding 2: grammar

        private static readonly string[] ValidInAllModes =
        {
            "{\"method\":\"ping\",\"id\":1}",
            " \r\n\t{\"method\":\"ping\",\"id\":1}\r\n ",
            "{\"Method\":\"ping\",\"ID\":1}",
            "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"params\":[],\"id\":1}",
            "{\"method\":\"ping\",\"params\":null,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":[true,false,null,-0,0,1.5,-1.5e+3,2E-2,1e10,0.5e-3,\"\\u00e9\\n\\\"\\\\\\/\\b\\f\\r\\t\"],\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":{\"a\":{},\"b\":[],\"c\":[{}],\"d\":\"\",\"e\":[[],[[]]]},\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":\"\\uD83D\\uDE00 \u00e9 \u20ac \U0001F600 \u007f\",\"id\":1}",
            "{ \"method\" : \"ping\" , \"ignored\" : [ 1 , 2 ] , \"id\" : 1 }",
        };

        [TestCaseSource(nameof(Serializers))]
        public void Grammar_ValidDocumentsStillDispatch(JsonRpcSerializer s)
        {
            foreach (var json in ValidInAllModes)
                Assert.AreEqual(Ok7, Run(s, json), json);
        }

        private static readonly string[] InvalidInAllModes =
        {
            // trailing content after the single root
            "{\"method\":\"ping\",\"id\":1}{}",
            "{\"method\":\"ping\",\"id\":1}x",
            "{\"method\":\"ping\",\"id\":1} 2",
            "{\"method\":\"ping\",\"id\":1}\"\"",
            "[{\"method\":\"ping\",\"id\":1}][]",
            // literals must be exactly true/false/null
            "{\"method\":\"ping\",\"ignored\":truX,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":tru,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":True,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":truee,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":nul,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":fals,\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":falsey,\"id\":1}",
            "{\"method\":\"ping\",\"id\":nxxx}",
            // number grammar
            "{\"method\":\"ping\",\"id\":01}",
            "{\"method\":\"ping\",\"id\":-}",
            "{\"method\":\"ping\",\"id\":1.}",
            "{\"method\":\"ping\",\"id\":.5}",
            "{\"method\":\"ping\",\"id\":+1}",
            "{\"method\":\"ping\",\"id\":1e}",
            "{\"method\":\"ping\",\"id\":1e+}",
            "{\"method\":\"ping\",\"id\":1.5.5}",
            "{\"method\":\"ping\",\"id\":0x10}",
            "{\"method\":\"ping\",\"id\":1x}",
            "{\"method\":\"ping\",\"id\":--1}",
            // separators and structure
            "{\"method\":\"ping\",\"id\":1 2}",
            "{\"method\":\"ping\",\"id\":1,,}",
            "{\"method\":\"ping\",,\"id\":1}",
            "{,\"method\":\"ping\",\"id\":1}",
            "{\"method\" \"ping\",\"id\":1}",
            "{\"method\"::\"ping\",\"id\":1}",
            "{\"method\":,\"id\":1}",
            "{\"method\":\"ping\",\"id\":}",
            "{\"method\":\"ping\" \"id\":1}",
            "{:\"ping\",\"id\":1}",
            "{\"method\":\"ping\",\"params\":[1 2],\"id\":1}",
            "{\"method\":\"ping\",\"params\":[\"a\":1],\"id\":1}",
            "{\"method\":\"ping\",\"params\":[1},\"id\":1}",
            "{\"method\":\"ping\",\"params\":{\"a\":1],\"id\":1}",
            "{\"method\":\"ping\",\"params\":{\"a\"},\"id\":1}",
            "{\"method\":\"ping\",\"params\":[],\"id\":1",
            "{\"method\":\"ping\",\"params\":[,],\"id\":1}",
            "[,]",
            "]",
            "}",
            // strings: unescaped control characters and bad escapes
            "{\"method\":\"echo\",\"params\":[\"a\tb\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"a\nb\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"a\u0001b\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\x\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\u12\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\uZZZZ\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\uD800\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\uDC00\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\uD800\\u0041\"],\"id\":1}",
            "{\"method\":\"echo\",\"params\":[\"\\'\"],\"id\":1}",
        };

        [TestCaseSource(nameof(Serializers))]
        public void Grammar_InvalidDocumentsAreParseErrors(JsonRpcSerializer s)
        {
            foreach (var json in InvalidInAllModes)
            {
                // the lenient configurations accept \' inside strings; every other case is invalid for everyone
                if (s.Lenient && json.Contains("\\'")) continue;
                AssertParseError(Run(s, json), null, json);
            }
        }

        private static readonly string[] LenientOnlySyntax =
        {
            "{\"method\":\"ping\",\"id\":1,}",
            "{\"method\":\"ping\",\"ignored\":[1,],\"id\":1}",
            "{\"method\":\"ping\",\"ignored\":{\"a\":1,},\"id\":1}",
            "{method:\"ping\",id:1}",
            "{'method':'ping','id':1}",
            "{method:'ping',ignored:'it\\'s',id:1}",
            "{\"method\":\"ping\",\"ignored\":{1:2},\"id\":1}",     // a bare-word member name
        };

        [TestCaseSource(nameof(Serializers))]
        public void Grammar_LenientSyntaxIsRejectedInStrictModeOnly(JsonRpcSerializer s)
        {
            foreach (var json in LenientOnlySyntax)
            {
                var r = Run(s, json);
                if (s.Lenient) Assert.AreEqual(Ok7, r, json);
                else AssertParseError(r, null, json);
            }
        }

        private static readonly string[] InvalidEvenWhenLenient =
        {
            "{method:'ping',id:1}{}",
            "{method:'ping',id:1}x",
            "{method:'ping',ignored:truX,id:1}",
            "{method:'ping',ignored:[abc],id:1}",       // a bare word is only a member name, never a value
            "{method:'ping',ignored:undefined,id:1}",
            "{method:'ping',id:01}",
            "{method:'ping',id:nxxx}",
            "{method:'ping',id:1.}",
            "{method:'echo',params:['a\tb'],id:1}",
            "{method:'echo',params:['\\uD800'],id:1}",
            "{method:'echo',params:['\\q'],id:1}",
            "{method:'ping',,id:1}",
            "{,method:'ping',id:1}",
            "{method:'ping',id:1,,}",
            "{method 'ping',id:1}",
            "{method:'ping',id}",
            "[,]",
        };

        [TestCaseSource(nameof(LenientSerializers))]
        public void Grammar_LenientModeStillValidatesLiteralsNumbersAndStrings(JsonRpcSerializer s)
        {
            foreach (var json in InvalidEvenWhenLenient)
                AssertParseError(Run(s, json), null, json);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Grammar_TrailingCommaInBatch(JsonRpcSerializer s)
        {
            var r = Run(s, "[{\"method\":\"ping\",\"id\":1},]");
            if (s.Lenient) StringAssert.Contains("\"result\":7", r);
            else AssertParseError(r);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Grammar_NoStateLeaksBetweenDocuments(JsonRpcSerializer s)
        {
            // the pooled reader is reused per thread: a rejected document must not affect the next one
            AssertParseError(Run(s, "{\"method\":\"ping\",\"id\":1}{}"));
            Assert.AreEqual(Ok7, Run(s, "{\"method\":\"ping\",\"id\":1}"));
            AssertParseError(Run(s, Nested(65)));
            Assert.AreEqual(Ok7, Run(s, Nested(64)));
            AssertParseError(Run(s, "{\"method\":\"ping\",\"params\":[[[[1"));
            Assert.AreEqual(Ok7, Run(s, "{\"method\":\"ping\",\"id\":1}"));
        }

        // ------------------------------------------------------------------ finding 2: UTF-8

        private static IEnumerable<byte[]> InvalidUtf8InString()
        {
            byte[] Doc(params byte[] id) => Bytes("{\"method\":\"echo\",\"params\":[\"x\"],\"id\":\"", id, "\"}");
            yield return Doc(0xFF);
            yield return Doc(0x61, 0xFF, 0x62);
            yield return Doc(0xFE);
            yield return Doc(0x80);                    // stray continuation byte
            yield return Doc(0xBF);
            yield return Doc(0xC0, 0x80);              // overlong 2-byte
            yield return Doc(0xC1, 0xBF);
            yield return Doc(0xC2, 0x41);              // bad continuation
            yield return Doc(0xC2);                    // truncated by the closing quote
            yield return Doc(0xE0, 0x80, 0x80);        // overlong 3-byte
            yield return Doc(0xE0, 0x9F, 0xBF);
            yield return Doc(0xE2, 0x82);              // truncated 3-byte
            yield return Doc(0xE2, 0x82, 0x41);
            yield return Doc(0xED, 0xA0, 0x80);        // UTF-16 surrogate U+D800
            yield return Doc(0xED, 0xBF, 0xBF);
            yield return Doc(0xF0, 0x80, 0x80, 0x80);  // overlong 4-byte
            yield return Doc(0xF0, 0x8F, 0xBF, 0xBF);
            yield return Doc(0xF0, 0x9F, 0x98);        // truncated 4-byte
            yield return Doc(0xF4, 0x90, 0x80, 0x80);  // above U+10FFFF
            yield return Doc(0xF5, 0x80, 0x80, 0x80);
            yield return Doc(0xF8, 0x80, 0x80, 0x80, 0x80);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Utf8_InvalidSequencesInsideStringsAreParseErrors(JsonRpcSerializer s)
        {
            foreach (var doc in InvalidUtf8InString())
                AssertParseError(Run(s, doc), null, BitConverter.ToString(doc));
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void Utf8_InvalidSequencesInsideSingleQuotedStringsAreParseErrors(JsonRpcSerializer s)
        {
            AssertParseError(Run(s, Bytes("{method:'echo',params:['x'],id:'a", 0xFF, "b'}")));
            AssertParseError(Run(s, Bytes("{method:'echo',params:['a", 0xC0, 0x80, "'],id:1}")));
            AssertParseError(Run(s, Bytes("{method:'echo',params:['x'],id:'", 0xED, 0xA0, 0x80, "'}")));
        }

        [TestCaseSource(nameof(Serializers))]
        public void Utf8_InvalidBytesOutsideStringsAreParseErrors(JsonRpcSerializer s)
        {
            AssertParseError(Run(s, Bytes("{\"method\":\"ping\",\"id\":1}", 0xC2, 0xA0)));   // NBSP after the root
            AssertParseError(Run(s, Bytes("{\"method\":\"ping\",", 0xC2, 0xA0, "\"id\":1}")));
            AssertParseError(Run(s, Bytes("{\"method\":\"ping\",\"id\":", 0xFF, "}")));
        }

        [TestCaseSource(nameof(Serializers))]
        public void Utf8_WellFormedSequencesRoundTrip(JsonRpcSerializer s)
        {
            const string text = "h\u00e9llo \u20ac \U0001F600 \u0800 \uFFFD \uD7FF \uE000 \U0010FFFF";
            var r = Run(s, Bytes("{\"method\":\"echo\",\"params\":[\"", text, "\"],\"id\":1}"));
            Assert.AreEqual(text, ResultOf(r));
            // the same text as a string id is echoed byte for byte
            var raw = Encoding.UTF8.GetBytes(text);
            var r2 = Run(s, Bytes("{\"method\":\"ping\",\"id\":\"", raw, "\"}"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":\"" + text + "\"}", r2);
        }

        // ------------------------------------------------------------------ finding 8: lenient ids and the decode buffer

        [TestCaseSource(nameof(LenientSerializers))]
        public void LenientId_SurvivesEscapedMethodName(JsonRpcSerializer s)
        {
            var r = Run(s, "{method:'\\u0065cho',params:['x'],id:'abc'}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"abc\"}", r);
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void LenientId_SurvivesEscapedMethodAndParameterNames(JsonRpcSerializer s)
        {
            var r = Run(s, "{method:'n\\u0061med',params:{'\\u0061':'1',b:'2'},id:'abc'}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"1|2\",\"id\":\"abc\"}", r);
            // decoded parameter names that are long enough to outgrow the initial decode buffer
            var longName = new string('p', 100);
            var r2 = Run(s, "{method:'\\u0065cho',params:{'\\u0073':'v'},id:'" + longName + "'}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"v\",\"id\":\"" + longName + "\"}", r2);
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void LenientId_ShortAndLongIds(JsonRpcSerializer s)
        {
            foreach (var id in new[] { "a", "ab", "abc", new string('k', 63), new string('k', 64), new string('k', 65), new string('k', 500) })
            {
                var r = Run(s, "{method:'\\u0065cho',params:{'\\u0073':'v'},id:'" + id + "'}");
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"v\",\"id\":\"" + id + "\"}", r, "id length " + id.Length);
            }
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void LenientId_EscapesInsideSingleQuotesAreNormalised(JsonRpcSerializer s)
        {
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"abc\"}", Run(s, "{method:'\\u0065cho',params:['x'],id:'a\\u0062c'}"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"say \\\"hi\\\"\"}", Run(s, "{method:'\\u0065cho',params:['x'],id:'say \"hi\"'}"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"a\\\\b\\nc\"}", Run(s, "{method:'\\u0065cho',params:['x'],id:'a\\\\b\\nc'}"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"it's\"}", Run(s, "{method:'\\u0065cho',params:['x'],id:'it\\'s'}"));
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void LenientId_InBatchWithEscapedMethods(JsonRpcSerializer s)
        {
            var r = Run(s, "[{method:'\\u0065cho',params:['x'],id:'abc'},{method:'\\u0065cho',params:['y'],id:'de'},{method:'n\\u0061med',params:{'\\u0061':'1',b:'2'},id:'f'}]");
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"abc\"},{\"jsonrpc\":\"2.0\",\"result\":\"y\",\"id\":\"de\"},{\"jsonrpc\":\"2.0\",\"result\":\"1|2\",\"id\":\"f\"}]", r);
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedDoubleQuotedIdIsEchoedByteForByte(JsonRpcSerializer s)
        {
            var r = Run(s, "{\"method\":\"\\u0065cho\",\"params\":{\"\\u0073\":\"x\"},\"id\":\"a\\u0062c\"}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"a\\u0062c\"}", r);
        }

        // ------------------------------------------------------------------ finding 15: escaped envelope keys

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_Method(JsonRpcSerializer s)
        {
            Assert.AreEqual(Ok7, Run(s, "{\"m\\u0065thod\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok7, Run(s, "{\"\\u006D\\u0065\\u0074\\u0068\\u006F\\u0064\":\"ping\",\"id\":1}"));
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_ParamsAndId(JsonRpcSerializer s)
        {
            const string expected = "{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":1}";
            Assert.AreEqual(expected, Run(s, "{\"method\":\"echo\",\"p\\u0061rams\":[\"x\"],\"\\u0069d\":1}"));
            Assert.AreEqual(expected, Run(s, "{\"\\u006Aso\\u006Erpc\":\"2.0\",\"m\\u0065thod\":\"echo\",\"\\u0070\\u0061\\u0072\\u0061\\u006D\\u0073\":{\"\\u0073\":\"x\"},\"\\u0069\\u0064\":1}"));
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_CaseInsensitiveAfterDecoding(JsonRpcSerializer s)
        {
            Assert.AreEqual(Ok7, Run(s, "{\"M\\u0045thod\":\"ping\",\"\\u0049D\":1}"));
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_InBatch(JsonRpcSerializer s)
        {
            var r = Run(s, "[{\"m\\u0065thod\":\"ping\",\"\\u0069d\":1},{\"method\":\"echo\",\"p\\u0061rams\":[\"x\"],\"id\":2},{\"method\":\"ping\",\"id\":3}]");
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":2},{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":3}]", r);
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_Notification(JsonRpcSerializer s)
        {
            Assert.IsTrue(string.IsNullOrEmpty(Run(s, "{\"m\\u0065thod\":\"ping\"}")));
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedKeys_OnlyTheDecodedNameMatches(JsonRpcSerializer s)
        {
            // decodes to "mfthod": not the method member
            var r = Run(s, "{\"m\\u0066thod\":\"ping\",\"id\":1}");
            StringAssert.Contains("\"code\":-32600", r);
            StringAssert.Contains("Missing property 'method'", r);
            // an escaped name that decodes to something else is ignored like any other extra member
            Assert.AreEqual(Ok7, Run(s, "{\"method\":\"ping\",\"m\\u0065thodx\":\"nope\",\"id\":1}"));
        }

        [TestCaseSource(nameof(LenientSerializers))]
        public void EscapedKeys_MixedWithBareKeys(JsonRpcSerializer s)
        {
            Assert.AreEqual(Ok7, Run(s, "{\"m\\u0065thod\":'ping',id:1}"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"x\",\"id\":\"q\"}", Run(s, "{method:'echo',\"p\\u0061rams\":['x'],\"\\u0069d\":'q'}"));
        }
    }
}
