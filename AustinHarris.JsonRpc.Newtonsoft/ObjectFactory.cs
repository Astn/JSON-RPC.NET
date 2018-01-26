using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using AustinHarris.JsonRpc.Jsmn;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    public class ObjectFactory : IObjectFactory
    {
        public IJsonRpcException CreateException(int code, string message, object data)
        {
            return new JsonRpcException(code, message, data);
        }

        public object DeserializeJson(string json, Type type)
        {
            return JsonConvert.DeserializeObject(json, type);
        }

        public IJsonRequest CreateRequest()
        {
            return new JsonRequest();
        }
//        // JSON.Net implementation
//        public string MethodName(string json)
//        {
//            JsonTextReader reader = new JsonTextReader(new StringReader(json));
//            using (reader)
//                while (reader.Read())
//                {
//                    if (reader.TokenType == JsonToken.PropertyName)
//                    {
//                        var name = (string)reader.Value;
//                        if (name == "method")
//                        {
//                            reader.Read();
//                            return (string)reader.Value;
//                        }
//                        continue;
//                    }
//                }
//            return String.Empty;
//        }
       
        // JSMN implementation
        public string MethodName(string json)
        {
            const string METHOD = "method";
            string value;
            if (jsmn.parseFirstField(json,METHOD,out value))
            {
                return value;
            }

            throw new KeyNotFoundException(METHOD);
        }
        
        const string envelopeResult1 = "{\"jsonrpc\":\"2.0\",\"result\":";
        const string envelopeError1 = "{\"jsonrpc\":\"2.0\",\"error\":";
        const string envelope2 = ",\"id\":";
        const string envelope3 = "}";
        static int LenEnvelopeResult = envelopeResult1.Length + envelope2.Length + envelope3.Length;
        static int LenEnvelopeError = envelopeError1.Length + envelope2.Length + envelope3.Length;
        [ThreadStatic]
        static char[] respBuffer = null;

        public string ToJsonRpcResponse(ref InvokeResult response)
        {
            if (respBuffer == null)
            {
                respBuffer = new char[200];
            }

            if (String.IsNullOrEmpty(response.SerializedError))
            {
                var respLen = response.SerializedResult.Length +
                                            response.SerializedId.Length
                                            + LenEnvelopeResult;
                if(respBuffer.Length < respLen)
                {
                    Array.Resize(ref respBuffer, respLen);
                }
                var preC = 0;
                var newC = 0;
                envelopeResult1             .CopyTo(0, respBuffer, 0, newC = envelopeResult1.Length);
                response.SerializedResult   .CopyTo(0, respBuffer, preC += newC, newC = response.SerializedResult.Length);
                envelope2                   .CopyTo(0, respBuffer, preC += newC, newC = envelope2.Length);
                response.SerializedId       .CopyTo(0, respBuffer, preC += newC, newC = response.SerializedId.Length);
                envelope3                   .CopyTo(0, respBuffer, preC += newC, newC = envelope3.Length);
                return new string(respBuffer, 0, preC += newC);
            }
            else
            {
                var respLen = response.SerializedError.Length +
                                            response.SerializedId.Length
                                            + LenEnvelopeError;
                if (respBuffer.Length < respLen)
                {
                    Array.Resize(ref respBuffer, respLen);
                }

                var preC = 0;
                var newC = 0;
                envelopeError1              .CopyTo(0, respBuffer, 0, newC = envelopeError1.Length);
                response.SerializedError    .CopyTo(0, respBuffer, preC += newC, newC = response.SerializedError.Length);
                envelope2                   .CopyTo(0, respBuffer, preC += newC, newC = envelope2.Length);
                response.SerializedId       .CopyTo(0, respBuffer, preC += newC, newC = response.SerializedId.Length);
                envelope3                   .CopyTo(0, respBuffer, preC += newC, newC = envelope3.Length);
                return new string(respBuffer, 0, preC += newC);
            }
        }
        static JsonSerializerSettings jsonSerializerSettings = new JsonSerializerSettings();
        public string Serialize<T>(T data)
        {
            return JsonConvert.SerializeObject(data, jsonSerializerSettings);
        }

        public IJsonRequest[] DeserializeRequests(string requests)
        {
            return JsonConvert.DeserializeObject<JsonRequest[]>(requests);
        }
        
        public void DeserializeJsonRef<T>(string json, ref ValueTuple<T> functionParameters, ref string rawId, KeyValuePair<string, Type>[] info)
        {
            jsmn.DeserializeJsonRef(json,ref functionParameters, ref rawId, info);
        }

        public void DeserializeJsonRef<T1, T2>(string json, ref (T1, T2) functionParameters, ref string rawId,
            KeyValuePair<string, Type>[] info)
        {
            jsmn.DeserializeJsonRef(json,ref functionParameters, ref rawId, info);
        }

//        public void DeserializeJsonRef<T>(string json, ref ValueTuple<T> functionParameters, ref string rawId, KeyValuePair<string, Type>[] info)
//        {
//            var prop = new Stack<String>();
//            var ptype = String.Empty;
//            JsonTextReader reader = new JsonTextReader(new StringReader(json));
//            var pidx = 0;
//            while (reader.Read())
//            {
//                if (reader.Value != null)
//                {
//                    if (reader.TokenType == JsonToken.PropertyName)
//                    {
//                        prop.Push((string)reader.Value);
//                        continue;
//                    }
//                    else if (prop.Peek() == "jsonrpc" && prop.Count == 1)
//                    {
//                        prop.Pop();
//                        continue;
//                    }
//                    else if (prop.Peek() == "method" && prop.Count == 1)
//                    {
//                        prop.Pop();
//                        continue;
//                    }
//                    else if (prop.Peek() == "id" && prop.Count == 1)
//                    {
//                        rawId = reader.Value.ToString();
//                        prop.Pop();
//                        continue;
//                    }
//                    else
//                    {
//                        if (ptype == "Array")
//                        {
//                            if (reader.TokenType == JsonToken.Null)
//                                functionParameters.Item1 = default(T);
//                            else
//                                functionParameters.Item1 = new JValue(reader.Value).ToObject<T>();
//                            pidx++;
//                        }
//                        else if (ptype == "Object")
//                        {
//                            var propName = prop.Pop();
//                            for (int i = 0; i < info.Length; i++)
//                            {
//                                if (info[i].Key == propName)
//                                {
//                                    if (reader.TokenType == JsonToken.Null)
//                                        functionParameters.Item1 = default(T);
//                                    else
//                                        functionParameters.Item1 = new JValue(reader.Value).ToObject<T>();
//                                }
//                            }
//                        }
//                    }
//                }
//                else
//                {
//                    if (prop.Count > 0 && prop.Peek() == "params")
//                    {
//                        if (reader.TokenType == JsonToken.StartArray)
//                        {
//                            ptype = "Array";
//                        }
//                        else if (reader.TokenType == JsonToken.StartObject)
//                        {
//                            ptype = "Object";
//                        }
//                        else if (reader.TokenType == JsonToken.EndArray
//                            || reader.TokenType == JsonToken.EndObject)
//                        {
//                            prop.Pop();
//                            continue;
//                        }
//                    }
//                }
//            }
//        }

//        public void DeserializeJsonRef<T1, T2>(string json, ref (T1, T2) functionParameters, ref string rawId, KeyValuePair<string, Type>[] info)
//        {
//            var prop = new Stack<String>();
//            var ptype = String.Empty;
//            JsonTextReader reader = new JsonTextReader(new StringReader(json));
//            var pidx = 0;
//            while (reader.Read())
//            {
//                if (reader.Value != null)
//                {
//                    if (reader.TokenType == JsonToken.PropertyName)
//                    {
//                        prop.Push((string)reader.Value);
//                        continue;
//                    }
//                    else if (prop.Peek() == "jsonrpc" && prop.Count == 1)
//                    {
//                        prop.Pop();
//                        continue;
//                    }
//                    else if (prop.Peek() == "method" && prop.Count == 1)
//                    {
//                        prop.Pop();
//                        continue;
//                    }
//                    else if (prop.Peek() == "id" && prop.Count == 1)
//                    {
//                        rawId = reader.Value.ToString();
//                        prop.Pop();
//                        continue;
//                    }
//                    else
//                    {
//                        if (ptype == "Array")
//                        {
//                            if (pidx == 0)
//                            {
//                                if (reader.TokenType == JsonToken.Null)
//                                    functionParameters.Item1 = default(T1);
//                                else
//                                    functionParameters.Item1 = new JValue(reader.Value).ToObject<T1>();
//                            }
//                            else
//                            {
//                                if (reader.TokenType == JsonToken.Null)
//                                    functionParameters.Item2 = default(T2);
//                                else
//                                    functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
//                            }
//                            pidx++;
//                        }
//                        else if (ptype == "Object")
//                        {
//                            var propName = prop.Pop();
//                            for (int i = 0; i < info.Length; i++)
//                            {
//                                if (info[i].Key == propName)
//                                {
//                                    if (i == 0)
//                                    {
//                                        if (reader.TokenType == JsonToken.Null)
//                                            functionParameters.Item1 = default(T1);
//                                        else
//                                            functionParameters.Item1 = new JValue(reader.Value).ToObject<T1>();
//                                    }
//                                    else
//                                    {
//                                        if (reader.TokenType == JsonToken.Null)
//                                            functionParameters.Item2 = default(T2);
//                                        else
//                                            functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
//                                    }
//                                }
//                            }
//                        }
//                    }
//                }
//                else
//                {
//                    if (prop.Count > 0 && prop.Peek() == "params")
//                    {
//                        if (reader.TokenType == JsonToken.StartArray)
//                        {
//                            ptype = "Array";
//                        }
//                        else if (reader.TokenType == JsonToken.StartObject)
//                        {
//                            ptype = "Object";
//                        }
//                        else if (reader.TokenType == JsonToken.EndArray
//                            || reader.TokenType == JsonToken.EndObject)
//                        {
//                            prop.Pop();
//                            continue;
//                        }
//                    }
//                }
//            }
//        }

        public void DeserializeJsonRef<T1, T2, T3>(string json, ref (T1, T2, T3) functionParameters, ref string rawId, KeyValuePair<string, Type>[] info)
        {
            var prop = new Stack<String>();
            var ptype = String.Empty;
            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            var pidx = 0;
            while (reader.Read())
            {
                if (reader.Value != null)
                {
                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        prop.Push((string)reader.Value);
                        continue;
                    }
                    else if (prop.Peek() == "jsonrpc" && prop.Count == 1)
                    {
                        prop.Pop();
                        continue;
                    }
                    else if (prop.Peek() == "method" && prop.Count == 1)
                    {
                        prop.Pop();
                        continue;
                    }
                    else if (prop.Peek() == "id" && prop.Count == 1)
                    {
                        rawId = reader.Value.ToString();
                        prop.Pop();
                        continue;
                    }
                    else
                    {
                        if (ptype == "Array")
                        {
                            if (pidx == 0)
                            {
                                if (reader.TokenType == JsonToken.Null)
                                    functionParameters.Item1 = default(T1);
                                else
                                    functionParameters.Item1 = new JValue(reader.Value).ToObject<T1>();
                            }
                            else if(pidx == 1)
                            {
                                if (reader.TokenType == JsonToken.Null)
                                    functionParameters.Item2 = default(T2);
                                else
                                    functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
                            }
                            else
                            {
                                if (reader.TokenType == JsonToken.Null)
                                    functionParameters.Item3 = default(T3);
                                else
                                    functionParameters.Item3 = new JValue(reader.Value).ToObject<T3>();
                            }
                            pidx++;
                        }
                        else if (ptype == "Object")
                        {
                            var propName = prop.Pop();
                            for (int i = 0; i < info.Length; i++)
                            {
                                if (info[i].Key == propName)
                                {
                                    if (i == 0)
                                    {
                                        if (reader.TokenType == JsonToken.Null)
                                            functionParameters.Item1 = default(T1);
                                        else
                                            functionParameters.Item1 = new JValue(reader.Value).ToObject<T1>();
                                    }
                                    else if(i == 1)
                                    {
                                        if (reader.TokenType == JsonToken.Null)
                                            functionParameters.Item2 = default(T2);
                                        else
                                            functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
                                    }
                                    else
                                    {
                                        if (reader.TokenType == JsonToken.Null)
                                            functionParameters.Item3 = default(T3);
                                        else
                                            functionParameters.Item3 = new JValue(reader.Value).ToObject<T3>();
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (prop.Count > 0 && prop.Peek() == "params")
                    {
                        if (reader.TokenType == JsonToken.StartArray)
                        {
                            ptype = "Array";
                        }
                        else if (reader.TokenType == JsonToken.StartObject)
                        {
                            ptype = "Object";
                        }
                        else if (reader.TokenType == JsonToken.EndArray
                            || reader.TokenType == JsonToken.EndObject)
                        {
                            prop.Pop();
                            continue;
                        }
                    }
                }
            }
        }

        public void DeserializeJsonRef<T1, T2, T3, T4>(string json, ref (T1, T2, T3, T4) functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5>(string json, ref (T1, T2, T3, T4, T5) functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5, T6>(string json, ref (T1, T2, T3, T4, T5, T6) functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5, T6, T7>(string json, ref (T1, T2, T3, T4, T5, T6, T7) functionParameters, ref string rawId, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

    }
}
