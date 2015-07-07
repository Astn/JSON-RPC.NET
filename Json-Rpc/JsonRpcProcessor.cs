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
        public static void AsyncProcess(string jsonRpc, Action<string> callback, object context = null)
        {
            Task.Factory.StartNew(() => AsyncProcessInternal(Handler.DefaultSessionId(), jsonRpc, context, callback));
        }

        public static void AsyncProcess(string sessionId,string jsonRpc, Action<string> callback, object context = null)
        {
            Task.Factory.StartNew(() => AsyncProcessInternal(sessionId, jsonRpc, context, callback));
        }

        /// <summary>
        /// The callback will be returned to the user, who needs to invoke it in concrete
        /// service implementation. Call should be made directly from same thread as
        /// service method is executed. 
        /// </summary>
        /// <param name="sessionId">Handler session id</param>
        /// <returns></returns>
        public static Action<JsonResponse> GetAsyncProcessCallback(string sessionId = "")
        {
            Handler handler;
            if ("" == sessionId)
            {
                handler = Handler.GetSessionHandler(Handler.DefaultSessionId());
            }
            else 
            {
                handler = Handler.GetSessionHandler(sessionId);
            } 

            return handler.GetAsyncCallback();
        }

        private static void AsyncProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext, Action<string> callback)
        {
            Handler handler = Handler.GetSessionHandler(sessionId);

            try
            {
                Tuple<JsonRequest>[] batch = null;
                if (isSingleRpc(jsonRpc))
                {
                    batch = new[] { Tuple.Create(JsonConvert.DeserializeObject<JsonRequest>(jsonRpc)) };
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc)
                            .Select(request => new Tuple<JsonRequest>(request))
                            .ToArray();
                }

                if (batch.Length == 0)
                {
                    callback.Invoke(Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                    {
                        Error = handler.ProcessParseException(jsonRpc,
                            new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                    }));
                }

                foreach (var tuple in batch)
                {
                    JsonRequest jsonRequest = tuple.Item1;
                    JsonResponse jsonResponse = new JsonResponse();

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
                            handler.Handle(jsonRequest, jsonRpcContext, 
                                delegate(JsonResponse a) 
                                {
                                    a.Id = jsonRequest.Id;
                                    if (a.Id != null || a.Error != null)
                                    {                                       
                                        callback.Invoke(JsonConvert.SerializeObject(a));
                                    }
                                }
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                callback.Invoke(Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                }));
            }
        }

        public static void Process(JsonRpcStateAsync async, object context = null)
        {
            Task.Factory.StartNew((_async) =>
            {
                var tuple = (Tuple<JsonRpcStateAsync, object>)_async;
                ProcessJsonRpcState(tuple.Item1, tuple.Item2);
            }, new Tuple<JsonRpcStateAsync, object>(async, context));

        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null)
        {
            var t = Task.Factory.StartNew((_async) =>
            {
                var i = (Tuple<string, JsonRpcStateAsync, object>)_async;
                ProcessJsonRpcState(i.Item1, i.Item2, i.Item3);
            }, new Tuple<string, JsonRpcStateAsync, object>(sessionId, async, context));

        }
        internal static void ProcessJsonRpcState(JsonRpcStateAsync async, object jsonRpcContext = null)
        {
            ProcessJsonRpcState(Handler.DefaultSessionId(), async, jsonRpcContext);
        }
        internal static void ProcessJsonRpcState(string sessionId, JsonRpcStateAsync async, object jsonRpcContext = null)
        {
            async.Result = ProcessInternal(sessionId, async.JsonRpc, jsonRpcContext);
            async.SetCompleted();
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
