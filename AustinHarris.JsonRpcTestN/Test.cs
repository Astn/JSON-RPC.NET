using NUnit.Framework;
using System;
using System.Linq;
using AustinHarris.JsonRpc;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AustinHarris.JsonRpcTestN
{
    public class Poco
    {
        public static Poco WithOffset(int offset)
        {
            return new Poco(offset);
        }

        readonly int _offset;
        public Poco(int offset)
        {
            _offset = offset;
        }

        [JsonRpcMethod("add")]
        public int Add(int input) { return input + _offset; } 
    }

    [TestFixture()]
    public class Test
    {
        [Test()]
        public void TestCase()
        {
        }
        static object[] services;

        static Test()
        {
            services = new object[] {
                new CalculatorService()};
        }

        [Test()]
        public void TestCanCreateMultipleServicesOfSameTypeInTheirOwnSessions()
        {
            Func<int, string> request = (int param) => String.Format("{{method:'add',params:[{0}],id:1}}", param);
            Func<int, string> expectedResult = (int param) => String.Format("{{\"jsonrpc\":\"2.0\",\"result\":{0},\"id\":1}}", param);

            for (int i = 0; i < 100; i++)
            {
                ServiceBinder.BindService(i.ToString(), Poco.WithOffset(i));
            }

            for (int i = 0; i < 100; i++)
            {
                var result = JsonRpcProcessor.Process(i.ToString(), request(10));
                result.Wait();
                var actual1 = JObject.Parse(result.Result);
                var expected1 = JObject.Parse(expectedResult(10 + i));
                Assert.AreEqual(expected1, actual1);
            }
        }

        [Test()]
        public void TestCanCreateAndRemoveSession()
        {
            var h = JsonRpc.Handler.GetSessionHandler("this one");
            var metadata = new System.Collections.Generic.List<Tuple<string, Type>> {
                Tuple.Create ("sooper", typeof(string)),
                Tuple.Create ("returns", typeof(string))
            }.ToDictionary(x => x.Item1, x => x.Item2);
            h.RegisterFuction("workie", metadata, new System.Collections.Generic.Dictionary<string, object>(),new Func<string, string>(x => "workie ... " + x));

            string request = @"{method:'workie',params:{'sooper':'good'},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"workie ... good\",\"id\":1}";
            string expectedResultAfterDestroy = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"Method not found\",\"code\":-32601,\"data\":\"The method does not exist / is not available.\"},\"id\":1}";
            var result = JsonRpcProcessor.Process("this one", request);
            result.Wait();


            var actual1 = JObject.Parse(result.Result);
            var expected1 = JObject.Parse(expectedResult);
            Assert.AreEqual(expected1, actual1);

            h.Destroy();

            var result2 = JsonRpcProcessor.Process("this one", request);
            result2.Wait();

            Assert.AreEqual(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result));
        }

        [Test()]
        public void TestInProcessClient()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void NullableDateTimeToNullableDateTime()
        {
            string request = @"{method:'NullableDateTimeToNullableDateTime',params:['2014-06-30T14:50:38.5208399+09:00'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"2014-06-30T14:50:38.5208399+09:00\",\"id\":1}";
            var expectedDate = DateTime.Parse("2014-06-30T14:50:38.5208399+09:00");
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            var acutalDate = DateTime.Parse(result.Result.Substring(27, 33));
            Assert.AreEqual(expectedDate, acutalDate);
        }

        [Test()]
        public void NullableFloatToNullableFloat()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[1.2345],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.2345,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void NullableFloatToNullableFloat3()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[3.14159],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":3.14159,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }


        [Test()]
        public void NullableFloatToNullableFloat2()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[null],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":null,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void DecimalToNullableDecimal()
        {
            string request = @"{method:'DecimalToNullableDecimal',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void StringToListOfString()
        {
            string request = @"{method:'StringToListOfString',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void CustomStringToListOfString()
        {
            string request = @"{method:'CustomStringToListOfString',params:[{str:'some string'}],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void StringToThrowingException()
        {
            string request = @"{method:'StringToThrowingException',params:['some string'],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            StringAssert.Contains("-32603", result.Result);
        }

        [Test()]
        public void StringToRefException()
        {
            string request = @"{method:'StringToRefException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"refException worked\",\"code\":-1,\"data\":null},\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(JObject.Parse(expectedResult), JObject.Parse(result.Result));
        }

        [Test()]
        public void StringToThrowJsonRpcException()
        {
            string request = @"{method:'StringToThrowJsonRpcException',params:['some string'],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            StringAssert.Contains("-2700", result.Result);
        }

        [Test()]
        public void ReturnsDateTime()
        {
            string request = @"{method:'ReturnsDateTime',params:[],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [Test()]
        public void ReturnsCustomRecursiveClass()
        {
            string request = @"{method:'ReturnsCustomRecursiveClass',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"Nested1\":{\"Nested1\":null,\"Value1\":5},\"Value1\":10},\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [Test()]
        public void FloatToFloat()
        {
            string request = @"{method:'FloatToFloat',params:[0.123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.123,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [Test()]
        public void IntToInt()
        {
            string request = @"{method:'IntToInt',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void OptionalParamInt16()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void OptionalParamInt16NoParam()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void Int16ToInt16()
        {
            string request = @"{method:'Int16ToInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void Int32ToInt32()
        {
            string request = @"{method:'Int32ToInt32',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void Int64ToInt64()
        {
            string request = @"{method:'Int64ToInt64',params:[78915984515564],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":78915984515564,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [Test()]
        public void TestOptionalParamByteMissing()
        {
            string request = @"{method:'TestOptionalParambyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyteMissing()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShortMissing()
        {
            string request = @"{method:'TestOptionalParamshort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamintMissing()
        {
            string request = @"{method:'TestOptionalParamint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLongMissing()
        {
            string request = @"{method:'TestOptionalParamlong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshortMissing()
        {
            string request = @"{method:'TestOptionalParamushort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUintMissing()
        {
            string request = @"{method:'TestOptionalParamuint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlongMissing()
        {
            string request = @"{method:'TestOptionalParamulong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloatMissing()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDoubleMissing()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBoolMissing()
        {
            string request = @"{method:'TestOptionalParambool',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamCharMissing()
        {
            string request = @"{method:'TestOptionalParamchar',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimalMissing()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParamBytePresent()
        {
            string request = @"{method:'TestOptionalParambyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbytePresent()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShortPresent()
        {
            string request = @"{method:'TestOptionalParamshort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamintPresent()
        {
            string request = @"{method:'TestOptionalParamint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLongPresent()
        {
            string request = @"{method:'TestOptionalParamlong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshortPresent()
        {
            string request = @"{method:'TestOptionalParamushort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUintPresent()
        {
            string request = @"{method:'TestOptionalParamuint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlongPresent()
        {
            string request = @"{method:'TestOptionalParamulong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloatPresent()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDoublePresent()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBoolPresent()
        {
            string request = @"{method:'TestOptionalParambool',params:[false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamCharPresent()
        {
            string request = @"{method:'TestOptionalParamchar',params:[" + (int)'b' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"b\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimalPresent()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParamBytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloatPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDoublePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBoolPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{'input':false},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamCharPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{'input':" + (int)'c' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"c\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimalPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParamByteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloatMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDoubleMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBoolMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamCharMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimalMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParamByte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloat_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDouble_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBool_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamChar_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimal_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParamByte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123, input2: 67},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamByte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123, 67],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamByte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123, input2: 97},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":97,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123, 98],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamSbyte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamShort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamLong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUshort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamUlong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloat_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloat_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamFloat_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDouble_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDouble_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDouble_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBool_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBool_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[true, false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamBool_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamChar_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{'input1':" + (int)'c' + ", 'input2':" + (int)'d' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamChar_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + ", " + (int)'d' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamChar_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimal_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimal_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [Test()]
        public void TestOptionalParamDecimal_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParametersStrings_BothMissing()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[null,null],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParametersStrings_SecondMissing()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:['first'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"first\",null],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParametersStrings_BothExists()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:['first','second'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"first\",\"second\"],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestOptionalParametersBoolsAndStrings()
        {
            string request =
                "{\"jsonrpc\":\"2.0\",\"method\":\"TestOptionalParametersBoolsAndStrings\",\"params\":{\"input1\":\"murkel\"},\"Id\":1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [Test()]
        public void TestBatchResultWrongRequests()
        {
            string request = @"[{},{""jsonrpc"":""2.0"",""id"":4}]";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.IsTrue(Regex.IsMatch(result.Result, @"\[(\{.*""error"":.*?,""id"":.*?\}),(\{.*""error"":.*?,""id"":.*?\})\]"), "Should have two errors.");
        }

        [Test()]
        public void TestBatchResultMultipleMethodCallsNotificationAtLast()
        {
            string request =
                @"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1},{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""]}]";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.IsFalse(result.Result.EndsWith(@",]"), "result.Result.EndsWith(@',]')");

        }

        [Test()]
        public void TestEmptyBatchResult()
        {
            var secondRequest = @"{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""]}";
            var result = JsonRpcProcessor.Process(secondRequest);
            result.Wait();

            Assert.IsTrue(string.IsNullOrEmpty(result.Result));
        }


        [Test()]
        public void TestNotificationVoidResult()
        {
            var secondRequest = @"{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""], ""id"":73}";
            var result = JsonRpcProcessor.Process(secondRequest);
            result.Wait();
            Console.WriteLine(result.Result);
            Assert.IsTrue(result.Result.Contains("result"), "Json Rpc 2.0 Spec - 'result' - This member is REQUIRED on success. A function that returns void should have the result property included even though the value may be null.");
        }

        [Test()]
        public void TestLeftOutParams()
        {
            var request =
                @"{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""id"":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.IsFalse(result.Result.Contains(@"error"":{""code"":-32602"), @"According to JSON-RPC 2.0 the ""params"" member MAY be omitted.");
        }

        [Test()]
        public void TestMultipleResults()
        {
            var result =
                JsonRpcProcessor.Process(
                    @"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1},{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1}]");
            result.Wait();

            Assert.IsTrue(result.Result.EndsWith("]"));
        }

        [Test()]
        public void TestSingleResultBatch()
        {
            var result =
                JsonRpcProcessor.Process(@"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1}]");
            result.Wait();
            Assert.IsFalse(result.Result.EndsWith("]"));
        }

        class PreProcessHandlerLocal
        {
            public JsonRequest rpc = null;
            public object context = null;
            public int run = 0;

            public JsonRpcException PreProcess(JsonRequest rpc, object context)
            {
                run++;

                this.rpc = rpc;
                this.context = context;

                return null;
            }
        }

        [Test()]
        public void TestPreProcessor()
        {
            try {
                PreProcessHandlerLocal handler = new PreProcessHandlerLocal();
                Config.SetPreProcessHandler(new PreProcessHandler(handler.PreProcess));
                string request = @"{method:'TestPreProcessor',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"Success!\",\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.Null(handler.context, "Context should be null");
            } finally {
                Config.SetPreProcessHandler(null);
            }

        }

        [Test()]
        public void TestPreProcessorThrowsJsonRPCException()
        {
            try
            {
                PreProcessHandlerLocal handler = new PreProcessHandlerLocal();
                Config.SetPreProcessHandler(new PreProcessHandler(handler.PreProcess));
                string request = @"{method:'TestPreProcessorThrowsJsonRPCException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27000,\"message\":\"Just some testing\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Test()]
        public void TestPreProcessorThrowsException()
        {
            try
            {
                PreProcessHandlerLocal handler = new PreProcessHandlerLocal();
                Config.SetPreProcessHandler(new PreProcessHandler(handler.PreProcess));
                string request = @"{method:'TestPreProcessorThrowsException',params:{inputValue:'some string'},id:1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                StringAssert.Contains("-32603", result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Test()]
        public void TestPreProcessorSetsException()
        {
            try
            {
                PreProcessHandlerLocal handler = new PreProcessHandlerLocal();
                Config.SetPreProcessHandler(new PreProcessHandler(handler.PreProcess));
                string request = @"{method:'TestPreProcessorSetsException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27000,\"message\":\"This exception was thrown using: JsonRpcContext.SetException()\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Test()]
        public void TestPreProcessOnSession()
        {
            var sessionId = "my session";
            var h = JsonRpc.Handler.GetSessionHandler(sessionId);
            PreProcessHandlerLocal preHandler = new PreProcessHandlerLocal();
            h.SetPreProcessHandler(new PreProcessHandler(preHandler.PreProcess));

            var metadata = new System.Collections.Generic.List<Tuple<string, Type>> {
                Tuple.Create ("sooper", typeof(string)),
                Tuple.Create ("returns", typeof(string))
            }.ToDictionary(x => x.Item1, x => x.Item2);
            h.RegisterFuction("workie", metadata, new System.Collections.Generic.Dictionary<string, object>(),new Func<string, string>(x => "workie ... " + x));

            string request = @"{method:'workie',params:{'sooper':'good'},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"workie ... good\",\"id\":1}";
            string expectedResultAfterDestroy = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"Method not found\",\"code\":-32601,\"data\":\"The method does not exist / is not available.\"},\"id\":1}";
            var result = JsonRpcProcessor.Process(sessionId, request);
            result.Wait();

            var actual1 = JObject.Parse(result.Result);
            var expected1 = JObject.Parse(expectedResult);
            Assert.AreEqual(expected1, actual1);
            Assert.AreEqual(1, preHandler.run);

            h.Destroy();

            var result2 = JsonRpcProcessor.Process(sessionId, request);
            result2.Wait();

            Assert.AreEqual(1, preHandler.run);
            Assert.AreEqual(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result));
        }

        class PostProcessHandlerLocal
        {
            public JsonRequest rpc = null;
            public JsonResponse response = null;
            public object context = null;
            public int run = 0;
            private bool changeResponse_;

            public PostProcessHandlerLocal(bool changeResponse)
            {
                changeResponse_ = changeResponse;
            }

            public JsonRpcException PostProcess(JsonRequest rpc, JsonResponse response, object context)
            {
                run++;

                this.rpc = rpc;
                this.response = response;
                this.context = context;

                if (changeResponse_)
                {
                    return new JsonRpcException(-123, "Test error", null);
                }
                return null;
            }
        }

        [Test()]
        public void TestPostProcessor()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(false);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessor',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"Success!\",\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run, "Expect number of times run 1");
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.AreEqual("Success!", (string)handler.response.Result);
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessorThrowsJsonRPCException()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(false);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorThrowsJsonRPCException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27000,\"message\":\"Just some testing\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.Null(handler.response.Result, "Result should be null");
                Assert.NotNull(handler.response.Error, "Error should not be null");
                Assert.AreEqual(-27000, handler.response.Error.code, "Error code mismatch");
                Assert.AreEqual("Just some testing", handler.response.Error.message, "Error message mismatch");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessorThrowsException()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(false);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorThrowsException',params:{inputValue:'some string'},id:1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                StringAssert.Contains("-32603", result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.Null(handler.response.Result, "Result should be null");
                Assert.NotNull(handler.response.Error, "Error should not be null");
                Assert.AreEqual(-32603, handler.response.Error.code, "Error code mismatch");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessorSetsException()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(false);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorSetsException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27001,\"message\":\"This exception was thrown using: JsonRpcContext.SetException()\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }


        [Test()]
        public void TestPostProcessorChangesReturn()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(true);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessor',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-123,\"message\":\"Test error\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.AreEqual(1, handler.run, "Expect number of times run 1");
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.AreEqual("Success!", (string)handler.response.Result);
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessorThrowsJsonRPCExceptionChangesReturn()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(true);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorThrowsJsonRPCException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-123,\"message\":\"Test error\",\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.Null(handler.response.Result, "Result should be null");
                Assert.NotNull(handler.response.Error, "Error should not be null");
                Assert.AreEqual(-27000, handler.response.Error.code, "Error code mismatch");
                Assert.AreEqual("Just some testing", handler.response.Error.message, "Error message mismatch");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessorThrowsExceptionChangesReturn()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(true);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorThrowsException',params:{inputValue:'some string'},id:1}";
                string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"Test error\",\"code\":-123,\"data\":null},\"id\":1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                AssertJsonAreEqual(expectedResult, result.Result);
                Assert.AreEqual(1, handler.run);
                Assert.NotNull(handler.rpc, "RPC should not be null");
                Assert.NotNull(handler.response, "response should not be null");
                Assert.Null(handler.response.Result, "Result should be null");
                Assert.NotNull(handler.response.Error, "Error should not be null");
                Assert.AreEqual(-32603, handler.response.Error.code, "Error code mismatch");
                Assert.Null(handler.context, "Context should be null");
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Test()]
        public void TestPostProcessOnSession()
        {
            var sessionId = "my first session";
            var h = JsonRpc.Handler.GetSessionHandler(sessionId);
            PostProcessHandlerLocal postHandler = new PostProcessHandlerLocal(false);
            h.SetPostProcessHandler(new PostProcessHandler(postHandler.PostProcess));

            var metadata = new System.Collections.Generic.List<Tuple<string, Type>> {
                Tuple.Create ("sooper", typeof(string)),
                Tuple.Create ("returns", typeof(string))
            }.ToDictionary(x => x.Item1, x => x.Item2);
            h.RegisterFuction("workie", metadata, new System.Collections.Generic.Dictionary<string, object>(), new Func<string, string>(x => "workie ... " + x));

            string request = @"{method:'workie',params:{'sooper':'good'},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"workie ... good\",\"id\":1}";
            string expectedResultAfterDestroy = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"Method not found\",\"code\":-32601,\"data\":\"The method does not exist / is not available.\"},\"id\":1}";
            var result = JsonRpcProcessor.Process(sessionId, request);
            result.Wait();
            
            var actual1 = JObject.Parse(result.Result);
            var expected1 = JObject.Parse(expectedResult);
            Assert.AreEqual(expected1, actual1);
            Assert.AreEqual(1, postHandler.run);

            h.Destroy();

            var result2 = JsonRpcProcessor.Process(sessionId, request);
            result2.Wait();

            Assert.AreEqual(1, postHandler.run);
            Assert.AreEqual(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result));
        }

        [Test()]
        public void TestExtraParameters()
        {
            string request = @"{method:'ReturnsDateTime',params:{extra:'mytext'},id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsTrue(result.Result.Contains("error"));
            Assert.IsTrue(result.Result.Contains("\"code\":-32602"));
        }

        [Test()]
        public void TestCustomParameterName()
        {
            Func<string, string> request = (string paramName) => String.Format("{{method:'TestCustomParameterName',params:{{ {0}:'some string'}},id:1}}", paramName);
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            // Check custom param name specified in attribute works 
            var result = JsonRpcProcessor.Process(request("myCustomParameter"));
            result.Wait();
            Assert.AreEqual(JObject.Parse(expectedResult), JObject.Parse(result.Result));
            // Check method can't be used with its actual parameter name 
            result = JsonRpcProcessor.Process(request("arg"));
            result.Wait();
            StringAssert.Contains("-32602", result.Result); // check for 'invalid params' error code 
        }

        [Test()]
        public void TestCustomParameterWithNoSpecificName()
        {
            Func<string, string> request = (string paramName) => String.Format("{{method:'TestCustomParameterWithNoSpecificName',params:{{ {0}:'some string'}},id:1}}", paramName);
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            // Check method can be used with its parameter name 
            var result = JsonRpcProcessor.Process(request("arg"));
            result.Wait();
            Assert.AreEqual(JObject.Parse(expectedResult), JObject.Parse(result.Result));
        } 

        [Test]
        public void TestNestedReturnType()
        {
            var request = @"{""jsonrpc"":""2.0"",""method"":""TestNestedReturnType"",""id"":1}";
            var expected = @"{""jsonrpc"":""2.0"",""result"":{""NodeId"":1,""Leafs"":[{""NodeId"":2,""Leafs"":[]},{""NodeId"":3,""Leafs"":[]}]},""id"":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(expected, result.Result);
        }

        private static void AssertJsonAreEqual(string expectedJson, string actualJson)
        {
            Newtonsoft.Json.Linq.JObject expected = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(expectedJson);
            Newtonsoft.Json.Linq.JObject actual = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(actualJson);
            AssertJsonAreEqual(expected, actual, "root");
        }

        private static void AssertJsonAreEqual(JToken expectedJson, JToken actualJson, string path)
        {
            Assert.AreEqual(expectedJson.GetType(), actualJson.GetType(), "Type mismatch at " + path);
            if (expectedJson is JObject)
            {
                AssertJsonAreEqual((JObject)expectedJson, (JObject)actualJson, path);
            } else if (expectedJson is JObject)
            {
                AssertJsonAreEqual((JArray)expectedJson, (JArray)actualJson, path);
            } else if (expectedJson is JValue)
            {
                AssertJsonAreEqual((JValue)expectedJson, (JValue)actualJson, path);
            } else
            {
                throw new Exception("I don't know how to handle " + expectedJson.GetType().ToString());
            }
        }

        private static void AssertJsonAreEqual(JObject expectedJson, JObject actualJson, string path)
        {
            Console.WriteLine("expected: {0}", expectedJson);
            Console.WriteLine("actual  : {0}", actualJson);
            Assert.AreEqual(expectedJson.Count, actualJson.Count, "Count of json object at " + path);
            for (var expectedElementsEnumerator = expectedJson.GetEnumerator(); expectedElementsEnumerator.MoveNext(); )
            {
                JToken actualElement = null;
                Assert.IsTrue(actualJson.TryGetValue(expectedElementsEnumerator.Current.Key, out actualElement), "Couldn't find " + path + "[" + expectedElementsEnumerator.Current.Key + "]");
                AssertJsonAreEqual(expectedElementsEnumerator.Current.Value, actualElement, path + "[" + expectedElementsEnumerator.Current.Key + "]");
            }
        }

        private static void AssertJsonAreEqual(JArray expectedJson, JArray actualJson, string path)
        {
            Assert.AreEqual(expectedJson.Count, actualJson.Count, "Count of json array at " + path);
            for (int jsonIndex = 0; jsonIndex < expectedJson.Count; jsonIndex++)
            {
                AssertJsonAreEqual(expectedJson[jsonIndex], actualJson[jsonIndex], path + "[" + jsonIndex.ToString() + "]");
            }
        }

        private static void AssertJsonAreEqual(JValue expectedJson, JValue actualJson, string path)
        {
            Assert.AreEqual(expectedJson.Type, actualJson.Type, path);
            switch (expectedJson.Type)
            {
                case JTokenType.Boolean:
                    Assert.AreEqual((bool)expectedJson.Value, (bool)actualJson.Value, path);
                    break;
                case JTokenType.Integer:
                    Assert.AreEqual((System.Int64)expectedJson.Value, (System.Int64)actualJson.Value, path);
                    break;
                case JTokenType.String:
                    Assert.AreEqual((string)expectedJson.Value, (string)actualJson.Value, path);
                    break;
                case JTokenType.Null:
                    //Not used
                    break;
                default:
                    throw new Exception("I don't know how to handle type " + expectedJson.Type.ToString());
            }
        }
    }
}

