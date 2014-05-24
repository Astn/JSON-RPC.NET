using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AustinHarris.JsonRpc.Client;
using AustinHarris.JsonRpc;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        static object[] services ;

        static UnitTest1()
        {
            services = new object[] {
           new CalculatorService()};
        
        }

        [TestMethod]
        public void TestInProcessClient()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();

            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void NullableFloatToNullableFloat()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[1.2345],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.2345,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void NullableFloatToNullableFloat3()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[3.14159],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":3.14159,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void NullableFloatToNullableFloat2()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[null],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void DecimalToNullableDecimal()
        {
            string request = @"{method:'DecimalToNullableDecimal',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToListOfString()
        {
            string request = @"{method:'StringToListOfString',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void CustomStringToListOfString()
        {
            string request = @"{method:'CustomStringToListOfString',params:[{str:'some string'}],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToThrowingException()
        {
            string request = @"{method:'StringToThrowingException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal Error\",\"data\":";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));
        }

        [TestMethod]
        public void StringToRefException()
        {
            string request = @"{method:'StringToRefException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-1,\"message\":\"refException worked\",\"data\":null},\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToThrowJsonRpcException()
        {
            string request = @"{method:'StringToThrowJsonRpcException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27000,\"message\":\"Just some testing\"";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));

        }

        [TestMethod]
        public void ReturnsDateTime()
        {
            string request = @"{method:'ReturnsDateTime',params:[],id:1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void ReturnsCustomRecursiveClass()
        {
            string request = @"{method:'ReturnsCustomRecursiveClass',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"Nested1\":{\"Nested1\":null,\"Value1\":5},\"Value1\":10},\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void FloatToFloat()
        {
            string request = @"{method:'FloatToFloat',params:[0.123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.123,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void IntToInt()
        {
            string request = @"{method:'IntToInt',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void OptionalParamInt16()
        {            
            string request = @"{method:'TestOptionalParamInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void OptionalParamInt16NoParam()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int16ToInt16()
        {
            string request = @"{method:'Int16ToInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int32ToInt32()
        {
            string request = @"{method:'Int32ToInt32',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int64ToInt64()
        {
            string request = @"{method:'Int64ToInt64',params:[78915984515564],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":78915984515564,\"id\":1}";
            var result =  JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void TestOptionalParamByteMissing()
        {
            string request = @"{method:'TestOptionalParambyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyteMissing()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShortMissing()
        {
            string request = @"{method:'TestOptionalParamshort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintMissing()
        {
            string request = @"{method:'TestOptionalParamint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLongMissing()
        {
            string request = @"{method:'TestOptionalParamlong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshortMissing()
        {
            string request = @"{method:'TestOptionalParamushort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUintMissing()
        {
            string request = @"{method:'TestOptionalParamuint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlongMissing()
        {
            string request = @"{method:'TestOptionalParamulong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloatMissing()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDoubleMissing()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBoolMissing()
        {
            string request = @"{method:'TestOptionalParambool',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamCharMissing()
        {
            string request = @"{method:'TestOptionalParamchar',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimalMissing()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParamBytePresent()
        {
            string request = @"{method:'TestOptionalParambyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbytePresent()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShortPresent()
        {
            string request = @"{method:'TestOptionalParamshort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintPresent()
        {
            string request = @"{method:'TestOptionalParamint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLongPresent()
        {
            string request = @"{method:'TestOptionalParamlong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshortPresent()
        {
            string request = @"{method:'TestOptionalParamushort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUintPresent()
        {
            string request = @"{method:'TestOptionalParamuint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlongPresent()
        {
            string request = @"{method:'TestOptionalParamulong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloatPresent()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDoublePresent()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBoolPresent()
        {
            string request = @"{method:'TestOptionalParambool',params:[false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamCharPresent()
        {
            string request = @"{method:'TestOptionalParamchar',params:["+(int)'b'+"],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"b\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimalPresent()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParamBytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloatPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDoublePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBoolPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{'input':false},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamCharPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{'input':"+(int)'c'+"},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"c\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimalPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParamByteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloatMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDoubleMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBoolMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamCharMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimalMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParamByte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloat_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDouble_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBool_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamChar_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimal_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParamByte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123, input2: 67},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamByte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123, 67],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamByte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123, input2: 97},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":97,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123, 98],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamSbyte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamShort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamLong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUshort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamUlong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloat_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloat_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamFloat_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDouble_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDouble_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDouble_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBool_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBool_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[true, false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamBool_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamChar_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{'input1':" + (int)'c' + ", 'input2':" + (int)'d' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamChar_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + ", " + (int)'d' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamChar_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:["+(int)'c'+"],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimal_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimal_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamDecimal_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = JsonRpcProcessor.Process(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


    }
}
