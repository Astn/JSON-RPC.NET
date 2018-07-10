using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            return Task<string>.Factory.StartNew(_ =>
            {
                var tuple = (Tuple<string, string, object>) _;
                return ProcessInternal(tuple.Item1, tuple.Item2, tuple.Item3);
            }, new Tuple<string, string, object>(sessionId, jsonRpc, context));
        }

        private static string ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);


            JsonRequest[] batch = null;
            try
            {
                if (IsSingleRpc(jsonRpc))
                {
                    var foo = JsonConvert.DeserializeObject<JsonRequest>(jsonRpc.ToString());
                    batch = new[] {foo};
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc);
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                });
            }

            if (batch.Length == 0)
                return JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                });

            var singleBatch = batch.Length == 1;
            var sbResult = new StringBuilder();
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
                    jsonResponse.Result = new JValue((object) null);

                // special case optimization for single Item batch
                if (singleBatch && (jsonResponse.Id != null || jsonResponse.Error != null))
                    using (var sw = new StringWriter())
                    {
                        using (var writer = new JsonTextWriter(sw))
                        {
                            writer.WriteStartObject();
                            writer.WritePropertyName("jsonrpc");
                            writer.WriteValue("2.0");

                            if (jsonResponse.Error != null)
                            {
                                writer.WritePropertyName("error");
                                writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Error));
                            }
                            else
                            {
                                writer.WritePropertyName("result");
                                writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Result));
                            }
                            writer.WritePropertyName("id");
                            writer.WriteValue(jsonResponse.Id);
                            writer.WriteEndObject();

                            return sw.ToString();
                        }
                    }
                if (jsonResponse.Id == null && jsonResponse.Error == null)
                {
                    // do nothing
                    sbResult.Clear();
                }
                else
                {
                    // write out the response
                    if (i == 0)
                        sbResult = new StringBuilder("[");

                    sbResult.Append(JsonConvert.SerializeObject(jsonResponse));
                    if (i < batch.Length - 1)
                        sbResult.Append(',');
                    else if (i == batch.Length - 1)
                        sbResult.Append(']');
                }
            }
            return sbResult.ToString();
        }

        private static bool IsSingleRpc(string json)
        {
            for (var i = 0; i < json.Length; i++)
            {
                if (json[i] == '{')
                {
                    return true;
                }
                else if (json[i] == '[')
                {
                    return false;
                }
            }

            return true;
        }
    }
}