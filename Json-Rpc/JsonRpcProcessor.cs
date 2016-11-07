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

            try
            {
                Tuple<JsonRequest, JsonResponse>[] batch = null;
                if (isSingleRpc(jsonRpc))
                {
                    batch = new[] { Tuple.Create(JsonConvert.DeserializeObject<JsonRequest>(jsonRpc), new JsonResponse()) };
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc)
                            .Select(request => new Tuple<JsonRequest, JsonResponse>(request, new JsonResponse()))
                            .ToArray();
                }

                if (batch.Length == 0)
                {
                    return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                    {
                        Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                    });
                }

                foreach (var tuple in batch)
                {
                    var jsonRequest = tuple.Item1;
                    var jsonResponse = tuple.Item2;

                    if (jsonRequest == null)
                    {
                        jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(-32700, "Parse error",
                                "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                    }
                    else
                    {
                        jsonResponse.Id = jsonRequest.Id;

                        if (jsonRequest.Method == null)
                        {
                            jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                                new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'"));
                        }
                        else
                        {
                            var data = handler.Handle(jsonRequest, jsonRpcContext);

                            if (data == null) continue;

                            jsonResponse.Error = data.Error;
                            jsonResponse.Result = data.Result;
                        }
                    }
                }

                var responses = new string[batch.Count(x => x.Item2.Id != null || x.Item2.Error != null)];
                var idx = 0;
                foreach (var resp in batch.Where(x => x.Item2.Id != null || x.Item2.Error != null))
                {
                    if (resp.Item2.Result == null && resp.Item2.Error == null)
                    {
                        // Per json rpc 2.0 spec
                        // result : This member is REQUIRED on success.
                        // This member MUST NOT exist if there was an error invoking the method.    
                        // Either the result member or error member MUST be included, but both members MUST NOT be included.
                        resp.Item2.Result = new Newtonsoft.Json.Linq.JValue((Object)null);
                    }
                    responses[idx++] = JsonConvert.SerializeObject(resp.Item2);
                }

                return responses.Length == 0 ? string.Empty : responses.Length == 1 ? responses[0] : string.Format("[{0}]", string.Join(",", responses));
            }
            catch (Exception ex)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                });
            }
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
