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
        }

        [TestMethod]
        public void NullableFloatToNullableFloat()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[1.2345],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":1.2345,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
        }
        [TestMethod]
        public void NullableFloatToNullableFloat2()
        {
            string request = @"{method:'NullableFloatToNullableFloat',params:[null],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
        }

        [TestMethod]
        public void DecimalToNullableDecimal()
        {
            string request = @"{method:'DecimalToNullableDecimal',params:[0.0],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.0,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
        }

        [TestMethod]
        public void StringToListOfString()
        {
            string request = @"{method:'StringToListOfString',params:['some string'],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
        }

        [TestMethod]
        public void CustomStringToListOfString()
        {
            string request = @"{method:'CustomStringToListOfString',params:[{str:'some string'}],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[\"one\",\"two\",\"three\",\"some string\"],\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.AreEqual(result.Result, expectedResult);
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
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-1,\"message\":\"refException worked\"";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsTrue(result.Result.StartsWith(expectedResult));
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
        }


        [TestMethod]
        public void FloatToFloat()
        {
            string request = @"{method:'FloatToFloat',params:[0.123],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":0.123,\"Value1\":10},\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }


        [TestMethod]
        public void IntToInt()
        {
            string request = @"{method:'IntToInt',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void OptionalParamInt16()
        {            
            string request = @"{method:'TestOptionalParamInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void OptionalParamInt16NoParam()
        {
            string request = @"{method:'TestOptionalParamInt16',params:[],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result = InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void Int16ToInt16()
        {
            string request = @"{method:'Int16ToInt16',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void Int32ToInt32()
        {
            string request = @"{method:'Int32ToInt32',params:[789],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":789,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

        [TestMethod]
        public void Int64ToInt64()
        {
            string request = @"{method:'Int64ToInt64',params:[78915984515564],id:1}";
            string expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":78915984515564,\"id\":1}";
            var result =  InProcessClient.Invoke(request);
            result.Wait();
            Assert.IsFalse(result.Result.Contains("error"));
        }

    }
}
