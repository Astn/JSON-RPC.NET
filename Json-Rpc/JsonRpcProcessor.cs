using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        public static void Process(JsonRpcStateAsync async, object context = null)
        {
            Process(Handler.DefaultSessionId(), async, context);
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null)
        {
            Process(sessionId, async.JsonRpc, context)
                .ContinueWith(t =>
            {
                async.Result = t.Result;
                async.SetCompleted();
            });
        }
        public static Task<string> Process(string jsonRpc, object context = null)
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context);
        }
        public static Task<string> Process(string sessionId, string jsonRpc, object context = null)
        { 
            return Task<string>.Factory.StartNew((_) =>
            {
                var tuple = (Tuple<string, string, object>)_;
                return ProcessInternal(tuple.Item1, tuple.Item2, tuple.Item3);
            }, new Tuple<string, string, object>(sessionId, jsonRpc, context));
        }

        private static string ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);


            JsonRequest[] batch = null;
            try
            {
                if (isSingleRpc(jsonRpc))
                {
                    var foo = JsonConvert.DeserializeObject<JsonRequest>(jsonRpc);
                    batch = new[] { foo };
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc);
                }
            }
            catch (Exception ex)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                });
            }

            if (batch.Length == 0)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                });
            }

            var singleBatch = batch.Length == 1;
            StringBuilder sbResult = null;
            for (var i = 0; i < batch.Length; i++)
            {
                var jsonRequest = batch[i];
                var jsonResponse = new JsonResponse();

                if (jsonRequest == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(-32700, "Parse error",
                            "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                }
                else if (jsonRequest.Method == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'"));
                }
                else
                {
                    jsonResponse.Id = jsonRequest.Id;

                    var data = handler.Handle(jsonRequest, jsonRpcContext);

                    if (data == null) continue;

                    jsonResponse.Error = data.Error;
                    jsonResponse.Result = data.Result;

                }
                if (jsonResponse.Result == null && jsonResponse.Error == null)
                {
                    // Per json rpc 2.0 spec
                    // result : This member is REQUIRED on success.
                    // This member MUST NOT exist if there was an error invoking the method.    
                    // Either the result member or error member MUST be included, but both members MUST NOT be included.
                    jsonResponse.Result = new Newtonsoft.Json.Linq.JValue((Object)null);
                }
                // special case optimization for single Item batch
                if (singleBatch && (jsonResponse.Id != null || jsonResponse.Error != null))
                {
                    StringWriter sw = new StringWriter();
                    JsonTextWriter writer = new JsonTextWriter(sw);
                    writer.WriteStartObject();
                    writer.WritePropertyName("jsonrpc"); writer.WriteValue("2.0");

                    if (jsonResponse.Error != null)
                    {
                        writer.WritePropertyName("error"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Error));
                    }
                    else
                    {
                        writer.WritePropertyName("result"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Result));
                    }
                    writer.WritePropertyName("id"); writer.WriteValue(jsonResponse.Id);
                    writer.WriteEndObject();
                    return sw.ToString();

                    //return JsonConvert.SerializeObject(jsonResponse);
                }
                else if (jsonResponse.Id == null && jsonResponse.Error == null)
                {
                    // do nothing
                    sbResult = new StringBuilder(0);
                }
                else
                {
                    // write out the response
                    if (i == 0)
                    {
                        sbResult = new StringBuilder("[");
                    }

                    sbResult.Append(JsonConvert.SerializeObject(jsonResponse));
                    if (i < batch.Length - 1)
                    {
                        sbResult.Append(',');
                    }
                    else if (i == batch.Length - 1)
                    {
                        sbResult.Append(']');
                    }
                }
            }
            return sbResult.ToString();
        }

        private static bool isSingleRpc(string json)
        {
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') return true;
                else if (json[i] == '[') return false;
            }
            return true;
        }
    }
}
