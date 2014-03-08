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


    }
}
