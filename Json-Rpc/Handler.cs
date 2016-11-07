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

        private static int _sessionHandlerMasterVersion = 1;
        [ThreadStatic]
        private static int _sessionHandlerLocalVersion = 0;
        private static ConcurrentDictionary<string, Handler> _sessionHandlersMaster;
        [ThreadStatic]
        private static Dictionary<string, Handler> _sessionHandlersLocal;
        private static volatile string _defaultSessionId;
        #endregion

        #region Constructors

        static Handler()
        {
            //current = new Handler(Guid.NewGuid().ToString());
            _defaultSessionId = Guid.NewGuid().ToString();
            _sessionHandlersMaster = new ConcurrentDictionary<string, Handler>();
            _sessionHandlersMaster[_defaultSessionId] = new Handler(_defaultSessionId);
        }

        private Handler(string sessionId)
        {
            SessionId = sessionId;
            this.MetaData = new SMD();
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
            if (_sessionHandlerMasterVersion != _sessionHandlerLocalVersion)
            {
                _sessionHandlersLocal = new Dictionary<string, Handler>(_sessionHandlersMaster);
                _sessionHandlerLocalVersion = _sessionHandlerMasterVersion;
            }
            if (_sessionHandlersLocal.ContainsKey(sessionId))
            {
                return _sessionHandlersLocal[sessionId];
            }
            Interlocked.Increment(ref _sessionHandlerMasterVersion);
            return _sessionHandlersMaster.GetOrAdd(sessionId, new Handler(sessionId));
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
            _sessionHandlersMaster.TryRemove(sessionId, out h);
            Interlocked.Increment(ref _sessionHandlerMasterVersion);
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

        /// <summary>
        /// Provides access to a context specific to each JsonRpc method invocation.
        /// Warning: Must be called from within the execution context of the jsonRpc Method to return the context
        /// </summary>
        /// <returns></returns>
        public static object RpcContext()
        {
            return __currentRpcContext;
        }

        [ThreadStatic]
        static JsonRpcException __currentRpcException;
        /// <summary>
        /// Allows you to set the exception used in in the JsonRpc response.
        /// Warning: Must be called from the same thread as the jsonRpc method.
        /// </summary>
        /// <param name="exception"></param>
        public static void RpcSetException(JsonRpcException exception)
        {
            __currentRpcException = exception;
        }
        public static JsonRpcException RpcGetAndRemoveRpcException()
        {
            var ex = __currentRpcException;
            __currentRpcException = null ;
            return ex;
        }

        private AustinHarris.JsonRpc.PreProcessHandler externalPreProcessingHandler;
        private AustinHarris.JsonRpc.PostProcessHandler externalPostProcessingHandler;
        private Func<JsonRequest, JsonRpcException, JsonRpcException> externalErrorHandler;
        private Func<string, JsonRpcException, JsonRpcException> parseErrorHandler;
        #endregion

        /// <summary>
        /// This metadata contains all the types and mappings of all the methods in this handler. Warning: Modifying this directly could cause your handler to no longer function. 
        /// </summary>
        public SMD MetaData { get; set; }

        #region Public Methods

        /// <summary>
        /// Allows you to register all the functions on a Pojo Type that have been attributed as [JsonRpcMethod] to the specified sessionId
        /// </summary>
        /// <param name="sessionID">The session to register against</param>
        /// <param name="instance">The instance containing JsonRpcMethods to register</param>
        public static void RegisterInstance(string sessionID, object instance)
        {
            ServiceBinder.BindService(sessionID, instance);
        }

        /// <summary>
        /// Allows you to register any function, lambda, etc even when not attributed with JsonRpcMethod.
        /// Requires you to specify all types and defaults
        /// </summary>
        /// <param name="methodName">The method name that will map to the registered function</param>
        /// <param name="parameterNameTypeMapping">The parameter names and types that will be positionally bound to the function</param>
        /// <param name="parameterNameDefaultValueMapping">Optional default values for parameters</param>
        /// <param name="implementation">A reference to the Function</param>
        public void RegisterFuction(string methodName, Dictionary<string, Type> parameterNameTypeMapping, Dictionary<string, object> parameterNameDefaultValueMapping, Delegate implementation)
        {
            MetaData.AddService(methodName, parameterNameTypeMapping, parameterNameDefaultValueMapping, implementation);
        }

        public void UnRegisterFunction(string methodName)
        {
            MetaData.Services.Remove(methodName);
        }

        public void SetPreProcessHandler(AustinHarris.JsonRpc.PreProcessHandler handler)
        {
            externalPreProcessingHandler = handler;
        }

        public void SetPostProcessHandler(AustinHarris.JsonRpc.PostProcessHandler handler)
        {
            externalPostProcessingHandler = handler;
        }

        /// <summary>
        /// Invokes a method to handle a JsonRpc request.
        /// </summary>
        /// <param name="Rpc">JsonRpc Request to be processed</param>
        /// <param name="RpcContext">Optional context that will be available from within the jsonRpcMethod.</param>
        /// <returns></returns>
        public JsonResponse Handle(JsonRequest Rpc, Object RpcContext = null)
        {
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
                //return response always- if callback is empty or not
                return PostProcess(Rpc, response, RpcContext);
            }

            SMDService metadata = null;
            Delegate handle = null;
            var haveMetadata = this.MetaData.Services.TryGetValue(Rpc.Method, out metadata);
            if (haveMetadata)
            {
                handle = metadata.dele; 
            }

            if (haveMetadata == false || metadata == null)
            {
                JsonResponse response = new JsonResponse()
                {
                    Result = null,
                    Error = new JsonRpcException(-32601, "Method not found", "The method does not exist / is not available."),
                    Id = Rpc.Id
                };
                return PostProcess(Rpc, response, RpcContext);
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
                for (int i = 0; i < loopCt && i < metadata.parameters.Length; i++)
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
                        return PostProcess(Rpc, response, RpcContext);
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
                    return PostProcess(Rpc, response, RpcContext);
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
                return PostProcess(Rpc, response, RpcContext);
            }

            try
            {
                var results = handle.DynamicInvoke(parameters);
                
                var last = parameters.LastOrDefault();
                var contextException = RpcGetAndRemoveRpcException();
                if (contextException != null)
                {
                    JsonResponse response = new JsonResponse() { Error = ProcessException(Rpc, contextException), Id = Rpc.Id };
                    return PostProcess(Rpc, response, RpcContext);
                }
                if (expectsRefException && last != null && last is JsonRpcException)
                {
                    JsonResponse response = new JsonResponse() { Error = ProcessException(Rpc, last as JsonRpcException), Id = Rpc.Id };
                    return PostProcess(Rpc, response, RpcContext);
                }
                return PostProcess(Rpc, new JsonResponse() { Result = results }, RpcContext);
            }
            catch (Exception ex)
            {
                JsonResponse response;
                if (ex is TargetParameterCountException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32602, "Invalid params", ex)) };
                    return PostProcess(Rpc, response, RpcContext);
                }

                // We really dont care about the TargetInvocationException, just pass on the inner exception
                if (ex is JsonRpcException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, ex as JsonRpcException) };
                    return PostProcess(Rpc, response, RpcContext);
                }
                if (ex.InnerException != null && ex.InnerException is JsonRpcException)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, ex.InnerException as JsonRpcException) };
                    return PostProcess(Rpc, response, RpcContext);
                }
                else if (ex.InnerException != null)
                {
                    response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32603, "Internal Error", ex.InnerException)) };
                    return PostProcess(Rpc, response, RpcContext);
                }

                response = new JsonResponse() { Error = ProcessException(Rpc, new JsonRpcException(-32603, "Internal Error", ex)) };
                return PostProcess(Rpc, response, RpcContext);
            }
            finally
            {
                RemoveRpcContext();
            }
        }
        #endregion

        [ThreadStatic]
        static object __currentRpcContext;
        private void AddRpcContext(object RpcContext)
        {
            __currentRpcContext = RpcContext;
        }
        private void RemoveRpcContext()
        {
            __currentRpcContext = null;
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

        private JsonResponse PostProcess(JsonRequest request, JsonResponse response, object context)
        {
            if (externalPostProcessingHandler != null)
            {
                try
                {
                    JsonRpcException exception = externalPostProcessingHandler(request, response, context);
                    if (exception != null)
                    {
                        response = new JsonResponse() { Error = exception };
                    }
                }
                catch (Exception ex)
                {
                    response = new JsonResponse() { Error = ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex)) };
                }
            }
            return response;
        }

    }

}

