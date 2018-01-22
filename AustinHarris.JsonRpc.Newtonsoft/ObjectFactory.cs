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

            return JsonConvert.DeserializeObject<JsonRequest>(request);
            
            //var prop = "";
            //var ptype = "ArrayList";
            //JsonTextReader reader = new JsonTextReader(new StringReader(request));
            //while (reader.Read())
            //{
            //    if (reader.Value != null)
            //    {
            //       // Console.WriteLine("Token: {0}, Value: {1}", reader.TokenType, reader.Value);
            //        if(reader.TokenType == JsonToken.PropertyName)
            //        {
            //            prop = (string)reader.Value;
            //        }
            //        else if(reader.TokenType == JsonToken.String && prop == "method")
            //        {
            //            req.Method = (string)reader.Value;
            //            prop = null;
            //        }
            //        else if (prop == "id")
            //        {
            //            req.Id = reader.Value;
            //            prop = null;
            //        }
            //        else if (prop == "params")
            //        {
            //            if (ptype == "JArray") {
            //                ((JArray)req.Params).Add (reader.Value );
            //            } else if (ptype == "JObject") {
            //                ((JObject)req.Params).Add(reader.Value);
            //            }
                        
            //        }
            //    }
            //    else
            //    {
            //        if(prop == "params")
            //        {
            //            if(reader.TokenType == JsonToken.StartArray)
            //            {
            //                ptype = "JArray";
            //                req.Params = new JArray();
            //            }
            //            else if (reader.TokenType == JsonToken.StartObject)
            //            {
            //                ptype = "JObject";
            //                req.Params = new JObject();
            //            }
            //        }
            //      //  Console.WriteLine("Token: {0}", reader.TokenType);
            //    }
            //}

            //return req;
        }

        public string SerializeResponse(IJsonResponse response)
        {
            return JsonConvert.SerializeObject(response);
        }

        public IJsonRequest[] DeserializeRequests(string requests)
        {
            return JsonConvert.DeserializeObject<JsonRequest[]>(requests);
        }
    }
}
