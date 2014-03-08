using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AustinHarris.JsonRpc.Client;

namespace UnitTests
{
    [TestClass]
    public class UnitTest1
    {
        static object[] services = new object[] {
           new CalculatorService()
        };

        [TestMethod]
        public void TestInProcessClient()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();

            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void NullableFloatToNullableFloat()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[1.2345],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.2345,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void NullableFloatToNullableFloat2()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[null],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void DecimalToNullableDecimal()
        {
            string request = @"{method:'DecimalToNullableDecimal',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToListOfString()
        {
            string request = @"{method:'StringToListOfString',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void CustomStringToListOfString()
        {
            string request = @"{method:'CustomStringToListOfString',params:[{str:'some string'}],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToThrowingException()
        {
            string request = @"{method:'StringToThrowingException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32603,\"message\":\"Internal Error\",\"data\":";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));
        }

        [TestMethod]
        public void StringToRefException()
        {
            string request = @"{method:'StringToRefException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-1,\"message\":\"refException worked\",\"data\":null},\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void StringToThrowJsonRpcException()
        {
            string request = @"{method:'StringToThrowJsonRpcException',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-27000,\"message\":\"Just some testing\"";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));

        }

        [TestMethod]
        public void ReturnsDateTime()
        {
            string request = @"{method:'ReturnsDateTime',params:[],id:1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void ReturnsCustomRecursiveClass()
        {
            string request = @"{method:'ReturnsCustomRecursiveClass',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"Nested1\":{\"Nested1\":null,\"Value1\":5},\"Value1\":10},\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void FloatToFloat()
        {
            string request = @"{method:'FloatToFloat',params:[0.123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.123,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void IntToInt()
        {
            string request = @"{method:'IntToInt',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void OptionalParamInt16()
        {            
            string request = @"{method:'TestOptionalParamInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void OptionalParamInt16NoParam()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int16ToInt16()
        {
            string request = @"{method:'Int16ToInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int32ToInt32()
        {
            string request = @"{method:'Int32ToInt32',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void Int64ToInt64()
        {
            string request = @"{method:'Int64ToInt64',params:[78915984515564],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":78915984515564,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


        [TestMethod]
        public void TestOptionalParambyteMissing()
        {
            string request = @"{method:'TestOptionalParambyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyteMissing()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshortMissing()
        {
            string request = @"{method:'TestOptionalParamshort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintMissing()
        {
            string request = @"{method:'TestOptionalParamint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlongMissing()
        {
            string request = @"{method:'TestOptionalParamlong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushortMissing()
        {
            string request = @"{method:'TestOptionalParamushort',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuintMissing()
        {
            string request = @"{method:'TestOptionalParamuint',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulongMissing()
        {
            string request = @"{method:'TestOptionalParamulong',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloatMissing()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdoubleMissing()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamboolMissing()
        {
            string request = @"{method:'TestOptionalParambool',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamcharMissing()
        {
            string request = @"{method:'TestOptionalParamchar',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimalMissing()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParambytePresent()
        {
            string request = @"{method:'TestOptionalParambyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbytePresent()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshortPresent()
        {
            string request = @"{method:'TestOptionalParamshort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintPresent()
        {
            string request = @"{method:'TestOptionalParamint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlongPresent()
        {
            string request = @"{method:'TestOptionalParamlong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushortPresent()
        {
            string request = @"{method:'TestOptionalParamushort',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuintPresent()
        {
            string request = @"{method:'TestOptionalParamuint',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulongPresent()
        {
            string request = @"{method:'TestOptionalParamulong',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloatPresent()
        {
            string request = @"{method:'TestOptionalParamfloat',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdoublePresent()
        {
            string request = @"{method:'TestOptionalParamdouble',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamboolPresent()
        {
            string request = @"{method:'TestOptionalParambool',params:[false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamcharPresent()
        {
            string request = @"{method:'TestOptionalParamchar',params:["+(int)'b'+"],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"b\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimalPresent()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:[71],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParambytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbytePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushortPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuintPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulongPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloatPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdoublePresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamboolPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{'input':false},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamcharPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{'input':"+(int)'c'+"},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"c\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimalPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{'input':71},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":71.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParambyteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyteMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushortMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuintMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulongMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloatMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdoubleMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamboolMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamcharMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"a\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimalMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal',params:{},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParambyte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyte_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushort_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuint_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulong_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloat_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdouble_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambool_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamchar_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimal_2ndMissingObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }

        [TestMethod]
        public void TestOptionalParambyte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:{input1:123, input2: 67},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambyte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123, 67],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":67,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambyte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyte_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:{input1:123, input2: 97},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":97,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyte_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123, 98],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":98,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamsbyte_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamsbyte_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":126,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamshort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamshort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamlong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamlong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushort_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushort_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamushort_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamushort_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuint_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuint_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamuint_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamuint_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulong_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulong_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamulong_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamulong_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloat_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloat_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamfloat_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamfloat_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdouble_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdouble_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123,  671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdouble_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdouble_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambool_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambool_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[true, false],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParambool_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParambool_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamchar_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:{'input1':" + (int)'c' + ", 'input2':" + (int)'d' + "},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamchar_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:[" + (int)'c' + ", " + (int)'d' + "],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamchar_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamchar_2x',params:["+(int)'c'+"],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"d\",\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimal_2ndPresentObjectSyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:{input1:123, input2: 671},id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimal_2ndPresentArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123, 671],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":671.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }
        [TestMethod]
        public void TestOptionalParamdecimal_2ndMissingArraySyntax()
        {
            string request = @"{method:'TestOptionalParamdecimal_2x',params:[123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":987.0,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
            Assert.AreEqual(expectedResult, result.Result);
        }


    }
}
