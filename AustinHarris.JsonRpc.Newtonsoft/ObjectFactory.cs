using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
namespace AustinHarris.JsonRpc.Newtonsoft
{
    public class ObjectFactory : IObjectFactory
    {
        public IJsonRpcException CreateException(int code, string message, object data)
        {
            return new JsonRpcException(code, message, data);
        }

        public IJsonResponse CreateJsonErrorResponse(IJsonRpcException Error)
        {
            return new JsonResponse()
            {
                Error = Error
            };
        }

        public IJsonResponse CreateJsonResponse()
        {
            return new JsonResponse();
        }

        public IJsonResponse CreateJsonSuccessResponse(object result)
        {
            return new JsonResponse()
            {
                Result = result
            };
        }

        public object DeserializeJson(string json, Type type)
        {
            return JsonConvert.DeserializeObject(json, type);
        }

        public IJsonRequest CreateRequest()
        {
            return new JsonRequest();
        }

        public string MethodName(string json)
        {
            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            using (reader)
                while (reader.Read())
                {
                    if (reader.Value != null)
                    {
                        if (reader.TokenType == JsonToken.PropertyName)
                        {
                            var name = (string)reader.Value;
                            if (name == "method")
                            {
                                reader.Read();
                                return (string)reader.Value;
                            }
                            continue;
                        }
                    }
                }
            return String.Empty;
        }

        public IJsonRequest DeserializeRequest(string request)
        {
            var req = new JsonRequest();
            var prop = new Stack<String>();
            var ptype = String.Empty;
            JsonTextReader reader = new JsonTextReader(new StringReader(request));
            while (reader.Read())
            {
                if (reader.Value != null)
                {
                    if (reader.TokenType == JsonToken.PropertyName)
                    {
                        prop.Push( (string)reader.Value );
                        continue;
                    }
                    else if (prop.Peek() == "jsonrpc" && prop.Count == 1)
                    {
                        //req.JsonRpc = (string)reader.Value;
                        prop.Pop();
                        continue;
                    }
                    else if (prop.Peek() == "method" && prop.Count == 1)
                    {
                        req.Method = (string)reader.Value;
                        prop.Pop();
                        continue;
                    }
                    else if (prop.Peek() == "id" && prop.Count == 1)
                    {
                        req.Id = reader.Value;
                        prop.Pop();
                        continue;
                    }
                    else
                    {
                        if (ptype == "Array")
                        {
                            ((List<Object>)req.Params).Add(new JValue(reader.Value));
                        }
                        else if (ptype == "Object")
                        {
                            ((Dictionary<string, Object>)req.Params).Add(prop.Pop(), new JValue(reader.Value));
                        }
                    }
                }
                else
                {
                    if (prop.Count >0 && prop.Peek() == "params")
                    {
                        // this function isn't smart enough to handle deep objects.
                        // make sure we arn't trying to deal with a complex object.
                        // if we are, fall back on the built in deserializer.
                        if (reader.Depth > 1) 
                        {
                            return JsonConvert.DeserializeObject<JsonRequest>(request);
                        }
                        if (reader.TokenType == JsonToken.StartArray)
                        {
                            ptype = "Array";
                            req.Params = new List<Object>();
                        }
                        else if (reader.TokenType == JsonToken.StartObject)
                        {
                            ptype = "Object";
                            req.Params = new Dictionary<string, Object>();
                        }
                        else if (reader.TokenType == JsonToken.EndArray
                            || reader.TokenType == JsonToken.EndObject)
                        {
                            prop.Pop();
                            continue;
                        }
                    }
                    //  Console.WriteLine("Token: {0}", reader.TokenType);
                }
            }

            return req;

        }

        public string SerializeResponse(IJsonResponse response)
        {
            return JsonConvert.SerializeObject(response);
        }

        public IJsonRequest[] DeserializeRequests(string requests)
        {
            return JsonConvert.DeserializeObject<JsonRequest[]>(requests);
        }

        public object DeserializeOrCoerceParameter(object parameter, string name, Type type)
        {
            try
            {

                var jv = parameter as JValue;
                if (jv != null)
                {
                    if(jv.Type == JTokenType.Null)
                    {
                        return null;
                    }

                    return jv.ToObject(type);
                } else if(parameter is JObject)
                {
                    return ((JObject)parameter).ToObject(type);
                }
                //else if (type.Name == "Single") return Convert.ToSingle(parameter);
                //else if (type.Name == "Float") return Convert.ToSingle(parameter);
                //else if (type.Name == "Int32") return Convert.ToInt32(parameter);
                //else if (type.Name == "Int16") return Convert.ToInt16(parameter);
                //else if (type.Name == "Decimal") return Convert.ToDecimal(parameter);
                //else if (type.Name == "Byte") return Convert.ToByte(parameter);
                //else if (type.Name == "Boolean") return Convert.ToBoolean(parameter);
            }
            catch (Exception ex)
            {
                // no need to throw here, they will
                // get an invalid cast exception right after this.
            }
            return parameter;
        }

        public void DeserializeJsonRef<T>(string json, ref ValueTuple<T> functionParameters, ref Object id, KeyValuePair<string, Type>[] info)
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
                        id = reader.Value;
                        prop.Pop();
                        continue;
                    }
                    else
                    {
                        if (ptype == "Array")
                        {
                            if (reader.TokenType == JsonToken.Null)
                                functionParameters.Item1 = default(T);
                            else
                                functionParameters.Item1 = new JValue(reader.Value).ToObject<T>();
                            pidx++;
                        }
                        else if (ptype == "Object")
                        {
                            var propName = prop.Pop();
                            for (int i = 0; i < info.Length; i++)
                            {
                                if (info[i].Key == propName)
                                {
                                    if (reader.TokenType == JsonToken.Null)
                                        functionParameters.Item1 = default(T);
                                    else
                                        functionParameters.Item1 = new JValue(reader.Value).ToObject<T>();
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

        public void DeserializeJsonRef<T1, T2>(string json, ref (T1, T2) functionParameters, ref object id, KeyValuePair<string, Type>[] info)
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
                        id = reader.Value;
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
                            else
                            {
                                if (reader.TokenType == JsonToken.Null)
                                    functionParameters.Item2 = default(T2);
                                else
                                    functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
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
                                    else
                                    {
                                        if (reader.TokenType == JsonToken.Null)
                                            functionParameters.Item2 = default(T2);
                                        else
                                            functionParameters.Item2 = new JValue(reader.Value).ToObject<T2>();
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

        public void DeserializeJsonRef<T1, T2, T3>(string json, ref (T1, T2, T3) functionParameters, ref object id, KeyValuePair<string, Type>[] info)
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
                        id = reader.Value;
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

        public void DeserializeJsonRef<T1, T2, T3, T4>(string json, ref (T1, T2, T3, T4) functionParameters, ref object id, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5>(string json, ref (T1, T2, T3, T4, T5) functionParameters, ref object id, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5, T6>(string json, ref (T1, T2, T3, T4, T5, T6) functionParameters, ref object id, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }

        public void DeserializeJsonRef<T1, T2, T3, T4, T5, T6, T7>(string json, ref (T1, T2, T3, T4, T5, T6, T7) functionParameters, ref object id, KeyValuePair<string, Type>[] functionParameterInfo)
        {
            throw new NotImplementedException();
        }
    }
}
