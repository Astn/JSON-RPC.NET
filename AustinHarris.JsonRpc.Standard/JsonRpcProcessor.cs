using System;
using System.Threading.Tasks;
using System.IO;
using System.Text;

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

        struct ParamBox
        {
            public string sessionId;
            public string jsonRpc;
            public object context;
        }

        public static Task<string> Process(string sessionId, string jsonRpc, object context = null)
        {
            ParamBox __pq;
            __pq.sessionId = sessionId;
            __pq.jsonRpc = jsonRpc;
            __pq.context = context;
               
            return Task<string>.Factory.StartNew((_) =>
            {
                return ProcessInternal(((ParamBox)_).sessionId, ((ParamBox)_).jsonRpc, ((ParamBox)_).context);
            }, __pq);
        }

        private static string ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext)
        {
            var handler = Handler.GetSessionHandler(sessionId);


            IJsonRequest[] batch = null;
            try
            {
                if (isSingleRpc(jsonRpc))
                {
                    var foo = Handler._objectFactory.DeserializeRequest(jsonRpc);
                    batch = new[] { foo };
                }
                else
                {
                    batch = Handler._objectFactory.DeserializeRequests(jsonRpc);
                }
            }
            catch (Exception ex)
            {
                return Handler._objectFactory.SerializeResponse(Handler._objectFactory.CreateJsonErrorResponse(handler.ProcessParseException(jsonRpc, Handler._objectFactory.CreateException(-32700, "Parse error", ex))));
            }

            if (batch.Length == 0)
            {
                return Handler._objectFactory.SerializeResponse(Handler._objectFactory.CreateJsonErrorResponse(handler.ProcessParseException(jsonRpc, Handler._objectFactory.CreateException(3200, "Invalid Request", "Batch of calls was empty."))));
            }

            var singleBatch = batch.Length == 1;
            StringBuilder sbResult = null;
            for (var i = 0; i < batch.Length; i++)
            {
                var jsonRequest = batch[i];
                var jsonResponse = Handler._objectFactory.CreateJsonResponse();

                if (jsonRequest == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        Handler._objectFactory.CreateException(-32700, "Parse error",
                            "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                }
                else if (jsonRequest.Method == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        Handler._objectFactory.CreateException(-32600, "Invalid Request", "Missing property 'method'"));
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
                    jsonResponse.Result = null;
                }
                // special case optimization for single Item batch
                if (singleBatch && (jsonResponse.Id != null || jsonResponse.Error != null))
                {
                    return Handler._objectFactory.SerializeResponse(jsonResponse);
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

                    sbResult.Append(Handler._objectFactory.SerializeResponse(jsonResponse));
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
