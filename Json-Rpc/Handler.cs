namespace AustinHarris.JsonRpc
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Newtonsoft.Json;
    using System.Threading.Tasks;
    using System.Collections.Concurrent;
    using Newtonsoft.Json.Linq;
    using System.Threading;

    public class Handler
    {
        #region Members

        //private static Handler current;
        private static ConcurrentDictionary<string, Handler> _sessionHandlers;
        private static string _defaultSessionId;
        #endregion

        #region Constructors

        static Handler()
        {
            //current = new Handler(Guid.NewGuid().ToString());
            _defaultSessionId = Guid.NewGuid().ToString();
            _sessionHandlers = new ConcurrentDictionary<string, Handler>();
            _sessionHandlers[_defaultSessionId] = new Handler(_defaultSessionId);
        }

        private Handler(string sessionId)
        {
            SessionId = sessionId;
            this.MetaData = new SMD();
            this.Handlers = new Dictionary<string, Delegate>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the SessionID of the default session
        /// </summary>
        /// <returns></returns>
        public static string DefaultSessionId() { return _defaultSessionId; }

        /// <summary>
        /// Gets a specific session
        /// </summary>
        /// <param name="sessionId">The sessionId of the handler you want to retrieve.</param>
        /// <returns></returns>
        public static Handler GetSessionHandler(string sessionId)
        {
            return _sessionHandlers.GetOrAdd(sessionId, new Handler(sessionId));
        }

        /// <summary>
        /// gets the default session
        /// </summary>
        /// <returns>The default Session Handler</returns>
        public static Handler GetSessionHandler()
        {
            return GetSessionHandler(_defaultSessionId);
        }

        /// <summary>
        /// Removes and clears the Handler with the specific sessionID from the registry of Handlers
        /// </summary>
        /// <param name="sessionId"></param>
        public static void DestroySession(string sessionId)
        {
            Handler h;
            _sessionHandlers.TryRemove(sessionId, out h);
            h.Handlers.Clear();
            h.MetaData.Services.Clear();
        }
        /// <summary>
        /// Removes and clears the current Handler from the registry of Handlers
        /// </summary>
        public void Destroy()
        {
            DestroySession(SessionId);
        }

        /// <summary>
        /// Gets the default session handler
        /// </summary>
        public static Handler DefaultHandler { get { return GetSessionHandler(_defaultSessionId); } }

        /// <summary>
        /// The sessionID of this Handler
        /// </summary>
        public string SessionId { get; private set; }

        private static ConcurrentDictionary<int, object> RpcContexts = new ConcurrentDictionary<int, object>();
        private static ConcurrentDictionary<int, JsonRpcException> RpcExceptions = new ConcurrentDictionary<int, JsonRpcException>();

        /// <summary>
        /// Provides access to a context specific to each JsonRpc method invocation.
        /// Warning: Must be called from within the execution context of the jsonRpc Method to return the context
        /// </summary>
        /// <returns></returns>
        public static object RpcContext()
        {
            if (Task.CurrentId == null)
                return null;

            if (RpcContexts.ContainsKey(Task.CurrentId.Value) == false)
                return null;

            return RpcContexts[Task.CurrentId.Value];
        }

        /// <summary>
        /// Allows you to set the exception used in in the JsonRpc response.
        /// Warning: Must be called from within the execution context of the jsonRpc method.
        /// </summary>
        /// <param name="exception"></param>
        public static void RpcSetException(JsonRpcException exception)
        {
            if (Task.CurrentId != null)
                RpcExceptions[Task.CurrentId.Value] = exception;
            else
                throw new InvalidOperationException("This method is only valid when used within the context of a method marked as a JsonRpcMethod, and that method must of been invoked by the JsonRpc Handler.");
        }

        private void RemoveRpcException()
        {
            if (Task.CurrentId != null)
            {
                var id = Task.CurrentId.Value;
                RpcExceptions[id] = null;
                JsonRpcException va;
                RpcExceptions.TryRemove(id, out va);
            }
        }

        private AustinHarris.JsonRpc.PreProcessHandler externalPreProcessingHandler;
        private Func<JsonRequest, JsonRpcException, JsonRpcException> externalErrorHandler;
        private Func<string, JsonRpcException, JsonRpcException> parseErrorHandler;
        private Dictionary<string, Delegate> Handlers { get; set; }
        #endregion

        /// <summary>
        /// This metadata contains all the types and mappings of all the methods in this handler. Warning: Modifying this directly could cause your handler to no longer function. 
        /// </summary>
        public SMD MetaData { get; set; }

        private const string THREAD_CALLBACK_SLOT_NAME ="Callback";

        #region Public Methods

        /// <summary>
        /// Registers a jsonRpc method name (key) to be mapped to a specific function
        /// </summary>
        /// <param name="key">The Method as it will be called from JsonRpc</param>
        /// <param name="handle">The method that will be invoked</param>
        /// <returns></returns>
        public bool Register(string key, Delegate handle)
        {
            var result = false;

            if (!this.Handlers.ContainsKey(key))
            {
                this.Handlers.Add(key, handle);
            }

            return result;
        }

        public void UnRegister(string key)
        {
            this.Handlers.Remove(key);
            MetaData.Services.Remove(key);
        }

        /// <summary>
        /// Invokes a method to handle a JsonRpc request.
        /// </summary>
        /// <param name="Rpc">JsonRpc Request to be processed</param>
        /// <param name="RpcContext">Optional context that will be available from within the jsonRpcMethod.</param>
        /// <returns></returns>
        public JsonResponse Handle(JsonRequest Rpc, Object RpcContext = null, Action<JsonResponse> callback = null)
        {
            //empty delegate declaration if callback is not provided
            if (null == callback)
            {
                callback = delegate(JsonResponse a) { };
            }

            AddRpcContext(RpcContext);

            var preProcessingException = PreProcess(Rpc, RpcContext);
            if (preProcessingException != null)
            {
                JsonResponse response = new JsonResponse()
                {
                    Error = preProcessingException,
                    Id = Rpc.Id
                };
                //callback is called - if it is empty then nothing will be done
                callback.Invoke(response);
                //return response always- if callback is empty or not
                return response;
            }

            SMDService metadata = null;
            Delegate handle = null;
            var haveDelegate = this.Handlers.TryGetValue(Rpc.Method, out handle);
            var haveMetadata = this.MetaData.Services.TryGetValue(Rpc.Method, out metadata);

            if (haveDelegate == false || haveMetadata == false || metadata == null || handle == null)
            {
                JsonResponse response = new JsonResponse()
                {
                    Result = null,
                    Error = new JsonRpcException(-32601, "Method not found", "The method does not exist / is not available."),
                    Id = Rpc.Id
                };
                callback.Invoke(response);
                return response;
            }

            bool isJObject = Rpc.Params is Newtonsoft.Json.Linq.JObject;
            bool isJArray = Rpc.Params is Newtonsoft.Json.Linq.JArray;
            object[] parameters = null;
            bool expectsRefException = false;
            var metaDataParamCount = metadata.parameters.Count(x => x != null);

            var getCount = Rpc.Params as ICollection;
            var loopCt = 0;

            if (getCount != null)
            {
                loopCt = getCount.Count;
            }

            var paramCount = loopCt;
            if (paramCount == metaDataParamCount - 1 && metadata.parameters[metaDataParamCount - 1].ObjectType.Name.Contains(typeof(JsonRpcException).Name))
            {
                paramCount++;
                expectsRefException = true;
            }
            parameters = new object[paramCount];

            if (isJArray)
            {
                var jarr = ((Newtonsoft.Json.Linq.JArray)Rpc.Params);
                //var loopCt = jarr.Count;
                //var pCount = loopCt;
                //if (pCount == metaDataParamCount - 1 && metadata.parameters[metaDataParamCount].GetType() == typeof(JsonRpcException))
                //    pCount++;
                //parameters = new object[pCount];
                for (int i = 0; i < loopCt; i++)
                {
                    parameters[i] = CleanUpParameter(jarr[i], metadata.parameters[i]);
                }
            }
            else if (isJObject)
            {
                var jo = Rpc.Params as Newtonsoft.Json.Linq.JObject;
                //var loopCt = jo.Count;
                //var pCount = loopCt;
                //if (pCount == metaDataParamCount - 1 && metadata.parameters[metaDataParamCount].GetType() == typeof(JsonRpcException))
                //    pCount++;
                //parameters = new object[pCount];
                var asDict = jo as IDictionary<string, Newtonsoft.Json.Linq.JToken>;
                for (int i = 0; i < loopCt; i++)
                {
                    if (asDict.ContainsKey(metadata.parameters[i].Name) == false)
                    {
                        JsonResponse response = new JsonResponse()
                        {
                            Error = ProcessException(Rpc,
                            new JsonRpcException(-32602,
                                "Invalid params",
                                string.Format("Named parameter '{0}' was not present.",
                                                metadata.parameters[i].Name)
                                )),
                            Id = Rpc.Id
                        };
                        callback.Invoke(response);
                        return response;
                    }
                    parameters[i] = CleanUpParameter(jo[metadata.parameters[i].Name], metadata.parameters[i]);
                }
            }

            // Optional Parameter support
            // check if we still miss parameters compared to metadata which may include optional parameters.
            // if the rpc-call didn't supply a value for an optional parameter, we should be assinging the default value of it.
            if (parameters.Length < metaDataParamCount && metadata.defaultValues.Length > 0) // rpc call didn't set values for all optional parameters, so we need to assign the default values for them.
            {
                var suppliedParamsCount = parameters.Length; // the index we should start storing default values of optional parameters.
                var missingParamsCount = metaDataParamCount - parameters.Length; // the amount of optional parameters without a value set by rpc-call.
                Array.Resize(ref parameters, parameters.Length + missingParamsCount); // resize the array to include all optional parameters.

                for (int paramIndex = parameters.Length - 1, defaultIndex = metadata.defaultValues.Length - 1;     // fill missing parameters from the back 
                    paramIndex >= suppliedParamsCount && defaultIndex >= 0;                                        // to don't overwrite supplied ones.
                    paramIndex--, defaultIndex--)
                {
                    parameters[paramIndex] = metadata.defaultValues[defaultIndex].Value;
                }

                if (missingParamsCount > metadata.defaultValues.Length)
                {
                    JsonResponse response = new JsonResponse
                    {
                        Error = ProcessException(Rpc,
                            new JsonRpcException(-32602,
                                "Invalid params",
                                string.Format(
                                    "Number of default parameters {0} not sufficient to fill all missing parameters {1}",
                                    metadata.defaultValues.Length, missingParamsCount)
                                )),
                        Id = Rpc.Id
                    };
                    callback.Invoke(response);
                    return response;
                }
            }

            if (parameters.Length != metaDataParamCount)
            {
                JsonResponse response = new JsonResponse()
                {
                    Error = ProcessException(Rpc,
                    new JsonRpcException(-32602,
                        "Invalid params",
                        string.Format("Expecting {0} parameters, and received {1}",
                                        metadata.parameters.Length,
                                        parameters.Length)
                        )),
                    Id = Rpc.Id
                };
                callback.Invoke(response);
                return response;
            }

            try
            {
                //callback is stored to thread's local storage in order to get it directly from concrete JsonRpcService method implementation
                //where callback is just returned from method
               Thread.SetData(Thread.GetNamedDataSlot(THREAD_CALLBACK_SLOT_NAME), callback);
               
                
                var results = handle.DynamicInvoke(parameters);
                
                var last = parameters.LastOrDefault();
                JsonRpcException contextException;
                if (Task.CurrentId.HasValue && RpcExceptions.TryRemove(Task.CurrentId.Value, out contextException))
                {
                    JsonResponse response = new JsonResponse() { Error = ProcessException(Rpc, contextException), Id = Rpc.Id };
                    callback.Invoke(response);
                    return response;
                }
                if (expectsRefException && last != null && last is JsonRpcException)
                {
                    JsonResponse response = new JsonResponse() { Error = ProcessException(Rpc, last as JsonRpcException), Id = Rpc.Id };
                    callback.Invoke(response);
                    return response;
                }
                //return response, if callback is set (method is asynchronous) - result could be empty string and future result operations
                //will be processed in the callback
                return new JsonResponse() { Result = results };
            }
            catch (Exception ex)
            {
                JsonResponse response;
                if (ex is TargetParameterCountException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32602, "Invalid params", ex)) };
                    callback.Invoke(response);
                    return response;
                }

                // We really dont care about the TargetInvocationException, just pass on the inner exception
                if (ex is JsonRpcException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, ex as JsonRpcException) };
                    callback.Invoke(response);
                    return response;
                }
                if (ex.InnerException != null && ex.InnerException is JsonRpcException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, ex.InnerException as JsonRpcException) };
                    callback.Invoke(response);
                    return response;
                }
                else if (ex.InnerException != null)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32603, "Internal Error", ex.InnerException)) };
                    callback.Invoke(response);
                    return response;
                }

                response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32603, "Internal Error", ex)) };
                callback.Invoke(response);
                return response;
            }
            finally
            {
                RemoveRpcContext();
            }
        }

        /// <summary>
        /// Method returns the actual callback set to this thread in Handle() method.
        /// If callback is not set, then empty callback is returned.
        /// </summary>
        /// <returns></returns>
        internal Action<JsonResponse> GetAsyncCallback()
        {
            object o = Thread.GetData(Thread.GetNamedDataSlot(THREAD_CALLBACK_SLOT_NAME));
            Action<JsonResponse> callback;
            if(o is Action<JsonResponse>)
            {
                callback = o as Action<JsonResponse>;
            }
            else
            {
                callback = delegate(JsonResponse a) { };
            }
            return callback;
        }

        private void AddRpcContext(object RpcContext)
        {
            if (Task.CurrentId != null)
                RpcContexts[Task.CurrentId.Value] = RpcContext;
        }
        private void RemoveRpcContext()
        {
            if (Task.CurrentId != null)
            {
                var id = Task.CurrentId.Value;
                RpcContexts[id] = null;
                object va;
                RpcContexts.TryRemove(id, out va);
            }
        }

        private JsonRpcException ProcessException(JsonRequest req, JsonRpcException ex)
        {
            if (externalErrorHandler != null)
                return externalErrorHandler(req, ex);
            return ex;
        }
        internal JsonRpcException ProcessParseException(string req, JsonRpcException ex)
        {
            if (parseErrorHandler != null)
                return parseErrorHandler(req, ex);
            return ex;
        }
        internal void SetErrorHandler(Func<JsonRequest, JsonRpcException, JsonRpcException> handler)
        {
            externalErrorHandler = handler;
        }
        internal void SetParseErrorHandler(Func<string, JsonRpcException, JsonRpcException> handler)
        {
            parseErrorHandler = handler;
        }

        #endregion
        private object CleanUpParameter(object p, SMDAdditionalParameters metaData)
        {
            var bob = p as JValue;
            //if (bob != null && (bob.Value == null || bob.Value.GetType() == metaData.ObjectType))
            if (bob != null && (bob.Value == null))
            {
                return bob.Value;
            }
            if (bob != null)
            {

                // Avoid calling DeserializeObject on types that JValue has an explicit converter for
                // try to optimize for the most common types
                if (metaData.ObjectType == typeof(string)) return (string)bob;
                if (metaData.ObjectType == typeof(int)) return (int)bob;
                if (metaData.ObjectType == typeof(double)) return (double)bob;
                if (metaData.ObjectType == typeof(float)) return (float)bob;
                //if (metaData.ObjectType == typeof(long)) return (long)bob;
                //if (metaData.ObjectType == typeof(uint)) return (uint)bob;
                //if (metaData.ObjectType == typeof(ulong)) return (ulong)bob;
                //if (metaData.ObjectType == typeof(byte[])) return (byte[])bob;
                //if (metaData.ObjectType == typeof(Guid)) return (Guid)bob;
                if (metaData.ObjectType == typeof(decimal)) return (decimal)bob;
                //if (metaData.ObjectType == typeof(TimeSpan)) return (TimeSpan)bob;
                //if (metaData.ObjectType == typeof(short)) return (short)bob;
                //if (metaData.ObjectType == typeof(ushort)) return (ushort)bob;
                //if (metaData.ObjectType == typeof(char)) return (char)bob;
                //if (metaData.ObjectType == typeof(DateTime)) return (DateTime)bob;
                //if (metaData.ObjectType == typeof(bool)) return (bool)bob;
                //if (metaData.ObjectType == typeof(DateTimeOffset)) return (DateTimeOffset)bob;

                if (metaData.ObjectType.IsAssignableFrom(typeof(JValue)))
                    return bob;

                try
                {
                    return bob.ToObject(metaData.ObjectType);
                }
                catch (Exception ex)
                {
                    // no need to throw here, they will
                    // get an invalid cast exception right after this.
                }
            }
            else
            {
                try
                {
                    if (p is string)
                        return JsonConvert.DeserializeObject((string)p, metaData.ObjectType);
                    return JsonConvert.DeserializeObject(p.ToString(), metaData.ObjectType);
                }
                catch (Exception ex)
                {
                    // no need to throw here, they will
                    // get an invalid cast exception right after this.
                }
            }

            return p;
        }

        private JsonRpcException PreProcess(JsonRequest request, object context)
        {
            if (externalPreProcessingHandler == null)
                return null;
            return externalPreProcessingHandler(request, context);
        }

        internal void SetPreProcessHandler(AustinHarris.JsonRpc.PreProcessHandler handler)
        {
            externalPreProcessingHandler = handler;
        }
    }

}

