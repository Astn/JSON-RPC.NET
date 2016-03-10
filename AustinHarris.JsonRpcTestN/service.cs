using AustinHarris.JsonRpc;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace AustinHarris.JsonRpcTestN
{
    public class CalculatorService : JsonRpcService
    {
        [JsonRpcMethod]
        private double add(double l, double r)
        {
            return l + r;
        }

        [JsonRpcMethod]
        public DateTime? NullableDateTimeToNullableDateTime(DateTime? dt)
        {
            return dt;
        }

        [JsonRpcMethod]
        public float? NullableFloatToNullableFloat(float? a)
        {
            return a;
        }

        [JsonRpcMethod]
        public decimal? DecimalToNullableDecimal(decimal x)
        {
            return x;
        }

        [JsonRpcMethod]
        private List<string> StringToListOfString(string input)
        {
            return new List<string>() { "one", "two", "three", input };
        }

        [JsonRpcMethod]
        private List<string> StringToThrowingException(string input)
        {
            throw new Exception("Throwing Exception");
        }

        public class CustomString
        {
            public string str;
        }

        [JsonRpcMethod]
        private List<string> CustomStringToListOfString(CustomString input)
        {
            return new List<string>() { "one", "two", "three", input.str };
        }



        [JsonRpcMethod("internal.echo")]
        private string Handle_Echo(string s)
        {
            return s;
        }

        [JsonRpcMethod("error1")]
        private string devideByZero(string s)
        {
            var i = 0;
            var j = 15;
            return s + j / i;
        }

        [JsonRpcMethod]
        private bool TestCustomParameterName([JsonRpcParam("myCustomParameter")] string arg)
        {
            return true;
        }


        [JsonRpcMethod]
        private string StringToRefException(string s, ref JsonRpcException refException)
        {
            refException = new JsonRpcException(-1, "refException worked", null);
            return s;
        }
        [JsonRpcMethod]
        private string StringToThrowJsonRpcException(string s)
        {
            throw new JsonRpcException(-27000, "Just some testing", null);
            return s;
        }

        [JsonRpcMethod]
        private DateTime ReturnsDateTime()
        {
            return DateTime.Now;
        }

        [JsonRpcMethod]
        private recursiveClass ReturnsCustomRecursiveClass()
        {
            var obj = new recursiveClass() { Value1 = 10, Nested1 = new recursiveClass() { Value1 = 5 } };
            //obj.Nested1.Nested1 = obj;
            return obj;
        }

        [JsonRpcMethod]
        private float FloatToFloat(float input)
        {
            return input;
        }

        [JsonRpcMethod]
        private double DoubleToDouble(double input)
        {
            return input;
        }



        [JsonRpcMethod]
        private int IntToInt(int input)
        {
            return input;
        }

        [JsonRpcMethod]
        private Int32 Int32ToInt32(Int32 input)
        {
            return input;
        }

        [JsonRpcMethod]
        private Int16 Int16ToInt16(Int16 input)
        {
            return input;
        }

        [JsonRpcMethod]
        private Int16 TestOptionalParamInt16(Int16 input = 789)
        {
            return input;
        }

        [JsonRpcMethod]
        private Int64 Int64ToInt64(Int64 input)
        {
            return input;
        }

        private class recursiveClass
        {
            public recursiveClass Nested1 { get; set; }
            public int Value1 { get; set; }
        }

        #region OptionalParams Tests
        [JsonRpcMethod]
        private byte TestOptionalParambyte(byte input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private sbyte TestOptionalParamsbyte(sbyte input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private short TestOptionalParamshort(short input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private int TestOptionalParamint(int input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private long TestOptionalParamlong(long input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private ushort TestOptionalParamushort(ushort input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private uint TestOptionalParamuint(uint input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private ulong TestOptionalParamulong(ulong input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private float TestOptionalParamfloat(float input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private double TestOptionalParamdouble(double input = 1)
        {
            return input;
        }
        [JsonRpcMethod]
        private bool TestOptionalParambool(bool input = true)
        {
            return input;
        }
        [JsonRpcMethod]
        private char TestOptionalParamchar(char input = 'a')
        {
            return input;
        }
        [JsonRpcMethod]
        private decimal TestOptionalParamdecimal(decimal input = 1)
        {
            return input;
        }

        #endregion

        [JsonRpcMethod]
        public byte TestOptionalParambyte_2x(byte input1, byte input2 = 98)
        {
            return input2;
        }
        [JsonRpcMethod]
        public sbyte TestOptionalParamsbyte_2x(sbyte input1, sbyte input2 = 126)
        {
            return input2;
        }
        [JsonRpcMethod]
        public short TestOptionalParamshort_2x(short input1, short input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public int TestOptionalParamint_2x(int input1, int input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public long TestOptionalParamlong_2x(long input1, long input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public ushort TestOptionalParamushort_2x(ushort input1, ushort input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public uint TestOptionalParamuint_2x(uint input1, uint input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public ulong TestOptionalParamulong_2x(ulong input1, ulong input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public float TestOptionalParamfloat_2x(float input1, float input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public double TestOptionalParamdouble_2x(double input1, double input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        public bool TestOptionalParambool_2x(bool input1, bool input2 = true)
        {
            return input2;
        }
        [JsonRpcMethod]
        public char TestOptionalParamchar_2x(char input1, char input2 = 'd')
        {
            return input2;
        }
        [JsonRpcMethod]
        public decimal TestOptionalParamdecimal_2x(decimal input1, decimal input2 = 987)
        {
            return input2;
        }
        [JsonRpcMethod]
        private IList<string> TestOptionalParameters_Strings(string input1 = null, string input2 = null)
        {
            return new List<string>()
            {
                input1,
                input2
            };
        }

        [JsonRpcMethod]
        public bool TestOptionalParametersBoolsAndStrings(string input1, bool input2 = true, string input3 = "")
        {
            return input2;
        }

        [JsonRpcMethod]
        public void Notify(string message)
        {
            Trace.WriteLine(string.Format("Notified about: {0}", message));
        }

        [JsonRpcMethod]
        public string TestPreProcessor(string inputValue)
        {
            return "Success!";
        }

        [JsonRpcMethod]
        public string TestPreProcessorThrowsJsonRPCException(string inputValue)
        {
            throw new JsonRpcException(-27000, "Just some testing", null);
        }

        [JsonRpcMethod]
        public string TestPreProcessorThrowsException(string inputValue)
        {
            throw new Exception("TestException");
        }

        [JsonRpcMethod]
        public string TestPreProcessorSetsException(string inputValue)
        {
            JsonRpcContext.SetException(new JsonRpcException(-27000, "This exception was thrown using: JsonRpcContext.SetException()", null));
            return null;
        }

        [JsonRpcMethod]
        public string TestPostProcessor(string inputValue)
        {
            return "Success!";
        }

        [JsonRpcMethod]
        public string TestPostProcessorThrowsJsonRPCException(string inputValue)
        {
            throw new JsonRpcException(-27000, "Just some testing", null);
        }

        [JsonRpcMethod]
        public string TestPostProcessorThrowsException(string inputValue)
        {
            throw new Exception("TestException");
        }

        [JsonRpcMethod]
        public string TestPostProcessorSetsException(string inputValue)
        {
            JsonRpcContext.SetException(new JsonRpcException(-27001, "This exception was thrown using: JsonRpcContext.SetException()", null));
            return null;
        }
    }
}
