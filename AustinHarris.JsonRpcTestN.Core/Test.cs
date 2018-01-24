using Xunit;
using System;
using System.Linq;
using AustinHarris.JsonRpc;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace AustinHarris.JsonRpcTestN.Core
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

    public class Test
    {
        [Fact]
        public void TestCase()
        {
        }
        static object[] services;

        static Test()
        {
            Config.ConfigureFactory(new AustinHarris.JsonRpc.Newtonsoft.ObjectFactory());
            services = new object[] {
                new CalculatorService()};
        }

        [Fact]
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
                Assert.Equal(expected1, actual1);
            }
        }

        [Fact]
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
            Assert.True(JToken.DeepEquals(expected1, actual1));
            h.Destroy();

            var result2 = JsonRpcProcessor.Process("this one", request);
            result2.Wait();

            Assert.True(JToken.DeepEquals(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result)));
        }

        [Fact]
        public void TestInProcessClient()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.Equal(result.Result, expectedResult);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestStringToString()
        {
            string request = @"{method:'internal.echo',params:['hi'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":'hi',\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.Equal(result.Result, expectedResult);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void NullableDateTimeToNullableDateTime()
        {
            string request = @"{method:'NullableDateTimeToNullableDateTime',params:['2014-06-30T14:50:38.5208399+09:00'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"2014-06-30T14:50:38.5208399+09:00\",\"id\":1}";
            var expectedDate = DateTime.Parse("2014-06-30T14:50:38.5208399+09:00");
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            var acutalDate = DateTime.Parse(result.Result.Substring(27, 33));
            Assert.Equal(expectedDate, acutalDate);
        }

        [Theory]
        [InlineData(@"{method:'NullableFloatToNullableFloat',params:[1.2345],id:1}", "{\"jsonrpc\":\"2.0\",\"result\":1.2345,\"id\":1}")]
        [InlineData(@"{method:'NullableFloatToNullableFloat',params:[3.14159],id:1}", "{\"jsonrpc\":\"2.0\",\"result\":3.14159,\"id\":1}")]
        [InlineData(@"{method:'NullableFloatToNullableFloat',params:[null],id:1}", "{\"jsonrpc\":\"2.0\",\"result\":null,\"id\":1}")]
        public void NullableFloatToNullableFloat(string request, string response)
        {
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Equal(response, result.Result);
        }
        
      
        [Fact]
        public void DecimalToNullableDecimal()
        {
            string request = @"{method:'DecimalToNullableDecimal',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Equal(result.Result, expectedResult);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void StringToListOfString()
        {
            string request = @"{method:'StringToListOfString',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Equal(result.Result, expectedResult);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void CustomStringToListOfString()
        {
            string request = @"{method:'CustomStringToListOfString',params:[{str:'some string'}],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Equal(result.Result, expectedResult);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void StringToThrowingException()
        {
            string request = @"{method:'StringToThrowingException',params:['some string'],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Contains("-32603", result.Result);
        }

        [Fact]
        public void StringToRefException()
        {
            string request = @"{method:'StringToRefException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"message\":\"refException worked\",\"code\":-1,\"data\":null},\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.True(JToken.DeepEquals(JObject.Parse(expectedResult), JObject.Parse(result.Result)));
        }

        [Fact]
        public void StringToThrowJsonRpcException()
        {
            string request = @"{method:'StringToThrowJsonRpcException',params:['some string'],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Contains("-2700", result.Result);
        }

        [Fact]
        public void ReturnsDateTime()
        {
            string request = @"{method:'ReturnsDateTime',params:[],id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
        }

        [Fact]
        public void ReturnsCustomRecursiveClass()
        {
            string request = @"{method:'ReturnsCustomRecursiveClass',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"Nested1\":{\"Nested1\":null,\"Value1\":5},\"Value1\":10},\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }


        [Fact]
        public void FloatToFloat()
        {
            string request = @"{method:'FloatToFloat',params:[0.123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.123,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }


        [Fact]
        public void IntToInt()
        {
            string request = @"{method:'IntToInt',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void OptionalParamInt16()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void OptionalParamInt16NoParam()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void Int16ToInt16()
        {
            string request = @"{method:'Int16ToInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void Int32ToInt32()
        {
            string request = @"{method:'Int32ToInt32',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void Int64ToInt64()
        {
            string request = @"{method:'Int64ToInt64',params:[78915984515564],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":78915984515564,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }


        [Fact]
        public void TestOptionalParamByteMissing()
        {
            string request = @"{method:'TestOptionalParambyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyteMissing()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShortMissing()
        {
            string request = @"{method:'TestOptionalParamshort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamintMissing()
        {
            string request = @"{method:'TestOptionalParamint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLongMissing()
        {
            string request = @"{method:'TestOptionalParamlong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshortMissing()
        {
            string request = @"{method:'TestOptionalParamushort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUintMissing()
        {
            string request = @"{method:'TestOptionalParamuint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlongMissing()
        {
            string request = @"{method:'TestOptionalParamulong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloatMissing()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDoubleMissing()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBoolMissing()
        {
            string request = @"{method:'TestOptionalParambool',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamCharMissing()
        {
            string request = @"{method:'TestOptionalParamchar',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimalMissing()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParamBytePresent()
        {
            string request = @"{method:'TestOptionalParambyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbytePresent()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShortPresent()
        {
            string request = @"{method:'TestOptionalParamshort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamintPresent()
        {
            string request = @"{method:'TestOptionalParamint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLongPresent()
        {
            string request = @"{method:'TestOptionalParamlong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshortPresent()
        {
            string request = @"{method:'TestOptionalParamushort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUintPresent()
        {
            string request = @"{method:'TestOptionalParamuint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlongPresent()
        {
            string request = @"{method:'TestOptionalParamulong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloatPresent()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDoublePresent()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBoolPresent()
        {
            string request = @"{method:'TestOptionalParambool',params:[false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamCharPresent()
        {
            string request = @"{method:'TestOptionalParamchar',params:[" + (int)'b' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"b\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimalPresent()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParamBytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloatPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDoublePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBoolPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{'input':false},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamCharPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{'input':" + (int)'c' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"c\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimalPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParamByteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloatMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDoubleMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBoolMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamCharMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimalMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParamByte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloat_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDouble_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBool_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamChar_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimal_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParamByte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123, input2: 67},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamByte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123, 67],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamByte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123, input2: 97},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":97,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123, 98],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamSbyte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamShort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamLong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUshort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamUlong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloat_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloat_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamFloat_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDouble_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDouble_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDouble_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBool_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBool_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[true, false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamBool_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamChar_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{'input1':" + (int)'c' + ", 'input2':" + (int)'d' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamChar_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + ", " + (int)'d' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamChar_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimal_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimal_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }
        [Fact]
        public void TestOptionalParamDecimal_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParametersStrings_BothMissing()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[null,null],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParametersStrings_SecondMissing()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:['first'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"first\",null],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParametersStrings_BothExists()
        {
            string request = @"{method:'TestOptionalParameters_Strings',params:['first','second'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"first\",\"second\"],\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestOptionalParametersBoolsAndStrings()
        {
            string request =
                "{\"jsonrpc\":\"2.0\",\"method\":\"TestOptionalParametersBoolsAndStrings\",\"params\":{\"input1\":\"murkel\"},\"id\":1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.DoesNotContain("error", result.Result);
            Assert.Equal(expectedResult, result.Result);
        }

        [Fact]
        public void TestBatchResultWrongRequests()
        {
            string request = @"[{},{""jsonrpc"":""2.0"",""id"":4}]";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.True(Regex.IsMatch(result.Result, @"\[(\{.*""error"":.*?,""id"":.*?\}),(\{.*""error"":.*?,""id"":.*?\})\]"), "Should have two errors.");
        }

        [Fact]
        public void TestBatchResultMultipleMethodCallsNotificationAtLast()
        {
            string request =
                @"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1},{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""]}]";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.False(result.Result.EndsWith(@",]"), "result.Result.EndsWith(@',]')");

        }

        [Fact]
        public void TestEmptyBatchResult()
        {
            var secondRequest = @"{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""]}";
            var result = JsonRpcProcessor.Process(secondRequest);
            result.Wait();

            Assert.True(string.IsNullOrEmpty(result.Result));
        }


        [Fact]
        public void TestNotificationVoidResult()
        {
            var secondRequest = @"{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""Hello World!""], ""id"":73}";
            var result = JsonRpcProcessor.Process(secondRequest);
            result.Wait();
            Console.WriteLine(result.Result);
            Assert.True(result.Result.Contains("result"), "Json Rpc 2.0 Spec - 'result' - This member is REQUIRED on success. A function that returns void should have the result property included even though the value may be null.");
        }

        [Fact]
        public void TestLeftOutParams()
        {
            var request =
                @"{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""id"":1}";

            var result = JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.False(result.Result.Contains(@"error"":{""code"":-32602"), @"According to JSON-RPC 2.0 the ""params"" member MAY be omitted.");
        }

        [Fact]
        public void TestMultipleResults()
        {
            var result =
                JsonRpcProcessor.Process(
                    @"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1},{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1}]");
            result.Wait();

            Assert.EndsWith("]", result.Result);
        }

        [Fact]
        public void TestSingleResultBatch()
        {
            var result =
                JsonRpcProcessor.Process(@"[{""jsonrpc"":""2.0"",""method"":""ReturnsDateTime"",""params"":{},""id"":1}]");
            result.Wait();
            Assert.False(result.Result.EndsWith("]"));
        }

        class PreProcessHandlerLocal
        {
            public IJsonRequest rpc = null;
            public object context = null;
            public int run = 0;

            public IJsonRpcException PreProcess(IJsonRequest rpc, object context)
            {
                run++;

                this.rpc = rpc;
                this.context = context;

                return null;
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.Null(handler.context);
            } finally {
                Config.SetPreProcessHandler(null);
            }

        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Fact]
        public void TestPreProcessorThrowsException()
        {
            try
            {
                PreProcessHandlerLocal handler = new PreProcessHandlerLocal();
                Config.SetPreProcessHandler(new PreProcessHandler(handler.PreProcess));
                string request = @"{method:'TestPreProcessorThrowsException',params:{inputValue:'some string'},id:1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                Assert.Contains("-32603", result.Result);
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }

        [Fact]
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
            Assert.True(JToken.DeepEquals(expected1, actual1));
            Assert.Equal(1, preHandler.run);

            h.Destroy();

            var result2 = JsonRpcProcessor.Process(sessionId, request);
            result2.Wait();

            Assert.Equal(1, preHandler.run);
            Assert.True(JToken.DeepEquals(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result)));
        }

        class PostProcessHandlerLocal
        {
            public IJsonRequest rpc = null;
            public IJsonResponse response = null;
            public object context = null;
            public int run = 0;
            private bool changeResponse_;

            public PostProcessHandlerLocal(bool changeResponse)
            {
                changeResponse_ = changeResponse;
            }

            public IJsonRpcException PostProcess(IJsonRequest rpc, IJsonResponse response, object context)
            {
                run++;

                this.rpc = rpc;
                this.response = response;
                this.context = context;

                if (changeResponse_)
                {
                    return new JsonRpc.Newtonsoft.JsonRpcException(-123, "Test error", null);
                }
                return null;
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Equal("Success!", (string)handler.response.Result);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Null(handler.response.Result);
                Assert.NotNull(handler.response.Error);
                Assert.Equal(-27000, handler.response.Error.code);
                Assert.Equal("Just some testing", handler.response.Error.message);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
        public void TestPostProcessorThrowsException()
        {
            try
            {
                PostProcessHandlerLocal handler = new PostProcessHandlerLocal(false);
                Config.SetPostProcessHandler(new PostProcessHandler(handler.PostProcess));
                string request = @"{method:'TestPostProcessorThrowsException',params:{inputValue:'some string'},id:1}";
                var result = JsonRpcProcessor.Process(request);
                result.Wait();
                Assert.Contains("-32603", result.Result);
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Null(handler.response.Result);
                Assert.NotNull(handler.response.Error);
                Assert.Equal(-32603, handler.response.Error.code);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPreProcessHandler(null);
            }
        }


        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Equal("Success!", (string)handler.response.Result);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Null(handler.response.Result);
                Assert.NotNull(handler.response.Error);
                Assert.Equal(-27000, handler.response.Error.code);
                Assert.Equal("Just some testing", handler.response.Error.message);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
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
                Assert.Equal(1, handler.run);
                Assert.NotNull(handler.rpc);
                Assert.NotNull(handler.response);
                Assert.Null(handler.response.Result);
                Assert.NotNull(handler.response.Error);
                Assert.Equal(-32603, handler.response.Error.code);
                Assert.Null(handler.context);
            }
            finally
            {
                Config.SetPostProcessHandler(null);
            }
        }

        [Fact]
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
            Assert.True(JToken.DeepEquals(expected1, actual1));
            Assert.Equal(1, postHandler.run);

            h.Destroy();

            var result2 = JsonRpcProcessor.Process(sessionId, request);
            result2.Wait();

            Assert.Equal(1, postHandler.run);
            Assert.True(JToken.DeepEquals(JObject.Parse(expectedResultAfterDestroy), JObject.Parse(result2.Result)));
        }

        [Fact]
        public void TestExtraParameters()
        {
            string request = @"{method:'ReturnsDateTime',params:{extra:'mytext'},id:1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Contains("error", result.Result);
            Assert.Contains("\"code\":-32602", result.Result);
        }

        [Fact]
        public void TestCustomParameterName()
        {
            Func<string, string> request = (string paramName) => String.Format("{{method:'TestCustomParameterName',params:{{ {0}:'some string'}},id:1}}", paramName);
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            // Check custom param name specified in attribute works 
            var result = JsonRpcProcessor.Process(request("myCustomParameter"));
            result.Wait();
            Assert.Equal(JObject.Parse(expectedResult), JObject.Parse(result.Result));
            // Check method can't be used with its actual parameter name 
            result = JsonRpcProcessor.Process(request("arg"));
            result.Wait();
            Assert.Contains("-32602", result.Result); // check for 'invalid params' error code 
        }

        [Fact]
        public void TestCustomParameterWithNoSpecificName()
        {
            Func<string, string> request = (string paramName) => String.Format("{{method:'TestCustomParameterWithNoSpecificName',params:{{ {0}:'some string'}},id:1}}", paramName);
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            // Check method can be used with its parameter name 
            var result = JsonRpcProcessor.Process(request("arg"));
            result.Wait();
            Assert.Equal(JObject.Parse(expectedResult), JObject.Parse(result.Result));
        } 

        [Fact]
        public void TestNestedReturnType()
        {
            var request = @"{""jsonrpc"":""2.0"",""method"":""TestNestedReturnType"",""id"":1}";
            var expected = @"{""jsonrpc"":""2.0"",""result"":{""NodeId"":1,""Leafs"":[{""NodeId"":2,""Leafs"":[]},{""NodeId"":3,""Leafs"":[]}]},""id"":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.Equal(expected, result.Result);
        }

        private static void AssertJsonAreEqual(string expectedJson, string actualJson)
        {
            Newtonsoft.Json.Linq.JObject expected = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(expectedJson);
            Newtonsoft.Json.Linq.JObject actual = (Newtonsoft.Json.Linq.JObject)Newtonsoft.Json.JsonConvert.DeserializeObject(actualJson);
            AssertJsonAreEqual(expected, actual, "root");
        }

        private static void AssertJsonAreEqual(JToken expectedJson, JToken actualJson, string path)
        {
            Assert.Equal(expectedJson.GetType(), actualJson.GetType());
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
            Assert.Equal(expectedJson.Count, actualJson.Count);
            for (var expectedElementsEnumerator = expectedJson.GetEnumerator(); expectedElementsEnumerator.MoveNext(); )
            {
                JToken actualElement = null;
                Assert.True(actualJson.TryGetValue(expectedElementsEnumerator.Current.Key, out actualElement), "Couldn't find " + path + "[" + expectedElementsEnumerator.Current.Key + "]");
                AssertJsonAreEqual(expectedElementsEnumerator.Current.Value, actualElement, path + "[" + expectedElementsEnumerator.Current.Key + "]");
            }
        }

        private static void AssertJsonAreEqual(JArray expectedJson, JArray actualJson, string path)
        {
            Assert.Equal(expectedJson.Count, actualJson.Count);
            for (int jsonIndex = 0; jsonIndex < expectedJson.Count; jsonIndex++)
            {
                AssertJsonAreEqual(expectedJson[jsonIndex], actualJson[jsonIndex], path + "[" + jsonIndex.ToString() + "]");
            }
        }

        private static void AssertJsonAreEqual(JValue expectedJson, JValue actualJson, string path)
        {
            Assert.Equal(expectedJson.Type, actualJson.Type);
            switch (expectedJson.Type)
            {
                case JTokenType.Boolean:
                    Assert.Equal((bool)expectedJson.Value, (bool)actualJson.Value);
                    break;
                case JTokenType.Integer:
                    Assert.Equal((System.Int64)expectedJson.Value, (System.Int64)actualJson.Value);
                    break;
                case JTokenType.String:
                    Assert.Equal((string)expectedJson.Value, (string)actualJson.Value);
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

