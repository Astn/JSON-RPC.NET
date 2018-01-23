using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    }
}
