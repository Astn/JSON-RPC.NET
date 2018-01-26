using System;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        static JsonRpcProcessor()
        {
            for (int i = 0; i < _privatepool.Length; i++)
            {
                _privatepool[i] = new Thread(new ThreadStart(_workPool));
                _privatepool[i].IsBackground = true;
                _privatepool[i].Start();
            }
        }

        public static void ProcessAsync(JsonRpcStateAsync async, object context = null)
        {
            ProcessAsync(Handler.DefaultSessionId(), async, context);
        }

        public static void ProcessAsync(string sessionId, JsonRpcStateAsync async, object context = null)
        {
            ProcessAsync(sessionId, async.JsonRpc, context)
                .ContinueWith(t =>
            {
                async.Result = t.Result;
                async.SetCompleted();
            });
        }
        public static Task<string> ProcessAsync(string jsonRpc, object context = null)
        {
            return ProcessAsync(Handler.DefaultSessionId(), jsonRpc, context);
        }

        struct ParamBox
        {
            public string sessionId;
            public string jsonRpc;
            public object context;
        }
        struct ParamBox2
        {
            public Handler handler;
            public string jsonRpc;
            public object context;
        }
        struct ParamBox3
        {
            public TaskCompletionSource<string> tcs;
            public Handler handler;
            public string jsonRpc;
            public object context;
        }

        private static TaskFactory<string> _tf =  new TaskFactory<string>(TaskScheduler.Default);
        
        public static Task<string> ProcessAsync(string sessionId, string jsonRpc, object context = null)
        {
            ParamBox __pq;
            __pq.sessionId = sessionId;
            __pq.jsonRpc = jsonRpc;
            __pq.context = context;
            
            return _tf.StartNew((_) =>
            {
                return Process(Handler.GetSessionHandler(((ParamBox)_).sessionId), ((ParamBox)_).jsonRpc, ((ParamBox)_).context);
            }, __pq);
        }
        public static Task<string> ProcessAsync(Handler handler, string jsonRpc, object context = null)
        {
            ParamBox2 __pq;
            __pq.handler = handler;
            __pq.jsonRpc = jsonRpc;
            __pq.context = context;
            
            return _tf.StartNew((_) =>
            {
                return Process(((ParamBox2)_).handler, ((ParamBox2)_).jsonRpc, ((ParamBox2)_).context);
            }, __pq);
        }

        private static BlockingCollection<ParamBox3> _pendingWork = new BlockingCollection<ParamBox3>();
        private static Thread[] _privatepool = new Thread[8];
        private static void _workPool()
        {
            ParamBox3 item;
            while (true)
            {
                item = _pendingWork.Take();
                item.tcs.SetResult(Process(item.handler, item.jsonRpc, item.context));
            }
        }

        public static Task<string> ProcessAsync2(Handler handler, string jsonRpc, object context = null)
        {
            var tcs = new TaskCompletionSource<string>();
            var task = tcs.Task;
            
            ParamBox3 __pq;
            __pq.tcs = tcs;
            __pq.handler = handler;
            __pq.jsonRpc = jsonRpc;
            __pq.context = context;

            _pendingWork.Add(__pq);
            return tcs.Task;
        }

        [ThreadStatic]
        static IJsonRequest[] array1 = null;

        public static string Process(Handler handler, string jsonRpc, object jsonRpcContext)
        {
            var singleBatch = true;

            IJsonRequest[] batch = null;
            try
            {
                if (IsSingleRpc(jsonRpc))
                {
                    var name = Handler._objectFactory.MethodName(jsonRpc);
                    if (array1 == null)
                        array1 = new[] { Handler._objectFactory.CreateRequest() };
                    array1[0].Method = name;
                    array1[0].Raw = jsonRpc;
                    batch = array1; 
                }
                else
                {
                    batch = Handler._objectFactory.DeserializeRequests(jsonRpc);
                    singleBatch = batch.Length == 1;
                    if (batch.Length == 0)
                    {
                        InvokeResult ir = new InvokeResult
                        {
                            SerializedResult = Handler._objectFactory.Serialize(handler.ProcessParseException(jsonRpc, Handler._objectFactory.CreateException(3200, "Invalid Request", "Batch of calls was empty.")))
                        };
                        return Handler._objectFactory.ToJsonRpcResponse( ref ir );
                    }
                }
            }
            catch (Exception ex)
            {
                InvokeResult ir = new InvokeResult
                {
                    SerializedResult = Handler._objectFactory.Serialize(handler.ProcessParseException(jsonRpc, Handler._objectFactory.CreateException(-32700, "Parse error", ex)))
                };
                return Handler._objectFactory.ToJsonRpcResponse(ref ir);
            }
            
            StringBuilder sbResult = null;
            for (var i = 0; i < batch.Length; i++)
            {
                var jsonRequest = batch[i];
                InvokeResult jsonResponse = new InvokeResult();

                if (jsonRequest == null)
                {
                    jsonResponse.SerializedError = Handler._objectFactory.Serialize(handler.ProcessParseException(jsonRpc,
                        Handler._objectFactory.CreateException(-32700, "Parse error",
                            "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text.")));
                }
                else if (jsonRequest.Method == null)
                {
                    jsonResponse.SerializedError = Handler._objectFactory.Serialize(handler.ProcessParseException(jsonRpc,
                        Handler._objectFactory.CreateException(-32600, "Invalid Request", "Missing property 'method'")));
                }
                else
                {
                    handler.Handle(jsonRequest, ref jsonResponse, jsonRpcContext);
                    handler.PostProcess(jsonRequest, ref jsonResponse, jsonRpcContext);

                    if (jsonResponse.SerializedResult == null) continue;
                }

                // special case optimization for single Item batch
                if (singleBatch && (jsonResponse.SerializedId != null || jsonResponse.SerializedError != null))
                {
                    return Handler._objectFactory.ToJsonRpcResponse(ref jsonResponse);
                }
                if (jsonResponse.SerializedId == null && jsonResponse.SerializedError == null)
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

                    sbResult.Append(Handler._objectFactory.ToJsonRpcResponse(ref jsonResponse));
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

        private static bool IsSingleRpc(string json)
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
