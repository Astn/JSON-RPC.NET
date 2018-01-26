using System;
using System.Collections.Generic;
using AustinHarris.JsonRpc.Jsmn;
using Xunit;

namespace AustinHarris.JsonRpcTestN.Jsmn
{
    public class Test
    {
        [Fact]
        public void SmallParseWorks()
        {
            var input = "{\"method\":\"Add\",\"params\":[1,2],\"id\":1}";
            var tokens = new jsmntok_t[100];
            var success = AustinHarris.JsonRpc.Jsmn.jsmn.parse(input,tokens) > 0;
            Assert.True(success);
        }
        
        [Fact]
        public void CanGetMethod()
        {
            var json = "{\"method\":\"Add\",\"params\":[1,2],\"id\":1}";
            string value;
            Assert.True((jsmn.parseFirstField(json, "method", out value)));
            Assert.Equal("Add",value);
        }

        [Fact]
        public void CanGetParamsArrayPrimitives1()
        {
            var json = "{\"method\":\"Add\",\"params\":[1],\"id\":1}";
            var functionParameters = new ValueTuple<int>();
            string rawId = string.Empty;
            var info = new KeyValuePair<string, Type>[] {new KeyValuePair<string, Type>("p1", typeof(int))};
            jsmn.DeserializeJsonRef(json,ref functionParameters, ref rawId, info);
            Assert.Equal(functionParameters.Item1, 1);
            Assert.Equal(rawId, "1");
        }
        
        [Fact]
        public void CanGetParamsArrayPrimitives2()
        {
            var json = "{\"method\":\"Add\",\"params\":[1,2],\"id\":3}";
            var functionParameters = new ValueTuple<int,int>();
            string rawId = string.Empty;
            var info = new KeyValuePair<string, Type>[] {new KeyValuePair<string, Type>("p1", typeof(int))};
            jsmn.DeserializeJsonRef(json,ref functionParameters, ref rawId, info);
            Assert.Equal(functionParameters.Item1, 1);
            Assert.Equal(functionParameters.Item2, 2);
            Assert.Equal(rawId, "3");
        }
    }
}