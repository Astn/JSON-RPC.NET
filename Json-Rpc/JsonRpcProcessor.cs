using System;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        
        private static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
        {
             PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
             IgnoreNullValues = true,
             Converters =
             {
                 new JsonRpcExceptionConverter(),
                 new JsonResponseConverter(),
                 new JsonStringEnumConverter()
             }
        };

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
        public static Task<string> Process(string jsonRpc, object context = null, JsonSerializerOptions serializerOptions = null )
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context, serializerOptions);
        }
        public static Task<string> Process(string sessionId, string jsonRpc, object context = null, JsonSerializerOptions serializerOptions = null)
        { 
            return Task<string>.Factory.StartNew((_) =>
            {
                var tuple = (Tuple<string, string, object>)_;
                return ProcessInternal(tuple.Item1, tuple.Item2, tuple.Item3, serializerOptions ?? DefaultOptions);
            }, new Tuple<string, string, object>(sessionId, jsonRpc, context));
        }

        // todo: Work with and return utf8 at this level. We can deal with converting to and from utf8 in the 'Process' overloads
        private static string ProcessInternal(string sessionId, string jsonRpc, object jsonRpcContext, JsonSerializerOptions serializerOptions)
        {
            var handler = Handler.GetSessionHandler(sessionId);


            JsonRequest[] batch = null;
            try
            {
                if (isSingleRpc(jsonRpc))
                {
                    var foo = JsonSerializer.Deserialize<JsonRequest>(jsonRpc, serializerOptions);
                    batch = new[] { foo };
                }
                else
                {
                    batch = JsonSerializer.Deserialize<JsonRequest[]>(jsonRpc, serializerOptions);
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, JsonRpcException.WithData( JsonRpcException.Ex_32700,ex))
                }, serializerOptions);
            }

            if (batch.Length == 0)
            {
                return JsonSerializer.Serialize(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, JsonRpcException.Ex_3200)
                }, serializerOptions);
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
                        JsonRpcException.WithData(JsonRpcException.Ex_32700,
                            "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                }
                else if (jsonRequest.Method == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        JsonRpcException.WithData(JsonRpcException.Ex_32600, "Missing property 'method'"));
                }
                else if (!isSimpleValueType(jsonRequest.Id))
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        JsonRpcException.WithData(JsonRpcException.Ex_32600,"Id property must be either null or string or integer."));
                }
                else
                {
                    jsonResponse.Id = jsonRequest.Id;

                    var data = handler.Handle(jsonRequest, jsonRpcContext);

                    if (data == null) continue;

                    jsonResponse.Jsonrpc = data.Jsonrpc;
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
                if (singleBatch && (jsonResponse.Id.ValueKind != JsonValueKind.Null || jsonResponse.Error != null))
                {
                    return JsonSerializer.Serialize(jsonResponse, serializerOptions);
                }
                else if (jsonResponse.Id.ValueKind == JsonValueKind.Null && jsonResponse.Error == null)
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

                    sbResult.Append(JsonSerializer.Serialize(jsonResponse, serializerOptions));
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

        private static bool isSimpleValueType(JsonElement property)
        {
                JsonElement p = (JsonElement)property;
                return p.ValueKind == JsonValueKind.Number
                        || p.ValueKind == JsonValueKind.String
                        || p.ValueKind == JsonValueKind.True
                        || p.ValueKind == JsonValueKind.False
                        || p.ValueKind == JsonValueKind.Null;

            return false;
        }
    }
}
