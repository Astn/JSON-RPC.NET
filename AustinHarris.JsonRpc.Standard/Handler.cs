namespace AustinHarris.JsonRpc
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Collections.Concurrent;
    using System.Threading;

    public class Handler
    {
        #region Members
        private const string Name_of_JSONRPCEXCEPTION = "JsonRpcException&";
        private static int _sessionHandlerMasterVersion = 1;
        [ThreadStatic]
        private static Dictionary<string, Handler> _sessionHandlersLocal;
        [ThreadStatic]
        private static int _sessionHandlerLocalVersion = 0;
        private static ConcurrentDictionary<string, Handler> _sessionHandlersMaster;
        internal static IObjectFactory _objectFactory;
        private static volatile string _defaultSessionId;
        #endregion

        #region Constructors

        static Handler()
        {
            //current = new Handler(Guid.NewGuid().ToString());
            _defaultSessionId = Guid.NewGuid().ToString();
            _sessionHandlersMaster = new ConcurrentDictionary<string, Handler>();
        }

        private Handler(string sessionId)
        {
            SessionId = sessionId;
            this.MetaData = new SMD();
            if(_objectFactory == null)
            {
                throw new InvalidOperationException("Configure must be called before Handler is created.");
            }
        }

        public static void Configure(IObjectFactory factory)
        {
            _objectFactory = factory ?? throw new ArgumentNullException("factory");
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
        static IJsonRpcException __currentRpcException;
        /// <summary>
        /// Allows you to set the exception used in in the JsonRpc response.
        /// Warning: Must be called from the same thread as the jsonRpc method.
        /// </summary>
        /// <param name="exception"></param>
        public static void RpcSetException(IJsonRpcException exception)
        {
            __currentRpcException = exception;
        }
        public static IJsonRpcException RpcGetAndRemoveRpcException()
        {
            var ex = __currentRpcException;
            __currentRpcException = null ;
            return ex;
        }

        private AustinHarris.JsonRpc.PreProcessHandler externalPreProcessingHandler;
        private AustinHarris.JsonRpc.PostProcessHandler externalPostProcessingHandler;
        private Func<IJsonRequest, IJsonRpcException, IJsonRpcException> externalErrorHandler;
        private Func<string, IJsonRpcException, IJsonRpcException> parseErrorHandler;
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
        public IJsonResponse Handle(IJsonRequest Rpc, Object RpcContext = null)
        {
            AddRpcContext(RpcContext);

            var preProcessingException = PreProcess(Rpc, RpcContext);
            if (preProcessingException != null)
            {
                IJsonResponse response = _objectFactory.CreateJsonErrorResponse(preProcessingException);
                //callback is called - if it is empty then nothing will be done
                //return response always- if callback is empty or not
                return PostProcess(Rpc, response, RpcContext);
            }

            SMDService metadata = null;
            Delegate handle = null;
            if (this.MetaData.Services.TryGetValue(Rpc.Method, out metadata))
            {
                handle = metadata.dele; 
            } else if (metadata == null)
            {
                var response = _objectFactory.CreateJsonErrorResponse(_objectFactory.CreateException(-32601, "Method not found", "The method does not exist / is not available."));
                return PostProcess(Rpc, response, RpcContext);
            }

            object[] parameters = null;
            bool expectsRefException = false;
            var metaDataParamCount = metadata.parameters.Length;

            
            var loopCt = 0;
            var getCount = Rpc.Params as ICollection;
            if (getCount != null)
            {
                loopCt = getCount.Count;
            }

            var paramCount = loopCt;
            if (paramCount == metaDataParamCount - 1 && metadata.parameters[metaDataParamCount - 1].Value.Name.Equals(Name_of_JSONRPCEXCEPTION))
            {
                paramCount++;
                expectsRefException = true;
            }
            parameters = new object[paramCount];

            if (Rpc.Params is IList)
            {
                var jarr = (IList)Rpc.Params;
                for (int i = 0; i < loopCt; i++)
                {
                    parameters[i] = _objectFactory.DeserializeOrCoerceParameter(jarr[i], metadata.parameters[i].Key, metadata.parameters[i].Value);
                }
            }
            else if (Rpc.Params is IDictionary<string, Object>)
            {
                var asDict = Rpc.Params as IDictionary<string, Object>;
                for (int i = 0; i < loopCt && i < metadata.parameters.Length; i++)
                {
                    if (asDict.ContainsKey(metadata.parameters[i].Key) == true)
                    {
                        parameters[i] = _objectFactory.DeserializeOrCoerceParameter(asDict[metadata.parameters[i].Key], metadata.parameters[i].Key, metadata.parameters[i].Value);
                        //parameters[i] = CleanUpParameter(, );
                        continue;
                    }
                    else
                    {
                        var response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc,
                            _objectFactory.CreateException(-32602,
                                "Invalid params",
                                string.Format("Named parameter '{0}' was not present.",
                                                metadata.parameters[i].Key)
                                )));
                        return PostProcess(Rpc, response, RpcContext);
                    }
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
                    var response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc,
                            _objectFactory.CreateException(-32602,
                                "Invalid params",
                                string.Format(
                                    "Number of default parameters {0} not sufficient to fill all missing parameters {1}",
                                    metadata.defaultValues.Length, missingParamsCount)
                                )));
                    return PostProcess(Rpc, response, RpcContext);
                }
            }

            if (parameters.Length != metaDataParamCount)
            {
                var response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc,
                    _objectFactory.CreateException(-32602,
                        "Invalid params",
                        string.Format("Expecting {0} parameters, and received {1}",
                                        metadata.parameters.Length,
                                        parameters.Length)
                        )));
                return PostProcess(Rpc, response, RpcContext);
            }

            try
            {
                var results = handle.Method.Invoke(handle.Target, parameters);
                //var results = handle.DynamicInvoke(parameters);

                var contextException = RpcGetAndRemoveRpcException();
                IJsonResponse response = null;
                if (contextException != null)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, contextException));
                }
                else if (expectsRefException && parameters.LastOrDefault() != null)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, parameters.LastOrDefault() as IJsonRpcException));
                }
                else
                {
                    response = _objectFactory.CreateJsonSuccessResponse(results);
                }
                return PostProcess(Rpc, response, RpcContext);
            }
            catch (Exception ex)
            {
                IJsonResponse response;
                if (ex is TargetParameterCountException)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, _objectFactory.CreateException(-32602, "Invalid params", ex)));
                    return PostProcess(Rpc, response, RpcContext);
                }

                // We really dont care about the TargetInvocationException, just pass on the inner exception
                if (ex is IJsonRpcException)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, ex as IJsonRpcException));
                    return PostProcess(Rpc, response, RpcContext);
                }
                else if (ex.InnerException != null && ex.InnerException is IJsonRpcException)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, ex.InnerException as IJsonRpcException));
                    return PostProcess(Rpc, response, RpcContext);
                }
                else if (ex.InnerException != null)
                {
                    response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, _objectFactory.CreateException(-32603, "Internal Error", ex.InnerException)));
                    return PostProcess(Rpc, response, RpcContext);
                }

                response = _objectFactory.CreateJsonErrorResponse(ProcessException(Rpc, _objectFactory.CreateException(-32603, "Internal Error", ex)));
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

        private IJsonRpcException ProcessException(IJsonRequest req, IJsonRpcException ex)
        {
            if (externalErrorHandler != null)
                return externalErrorHandler(req, ex);
            return ex;
        }
        internal IJsonRpcException ProcessParseException(string req, IJsonRpcException ex)
        {
            if (parseErrorHandler != null)
                return parseErrorHandler(req, ex);
            return ex;
        }
        internal void SetErrorHandler(Func<IJsonRequest, IJsonRpcException, IJsonRpcException> handler)
        {
            externalErrorHandler = handler;
        }
        internal void SetParseErrorHandler(Func<string, IJsonRpcException, IJsonRpcException> handler)
        {
            parseErrorHandler = handler;
        }
       
        private object CleanUpParameter(object input, KeyValuePair<string,Type> metaData)
        {
            try
            {
                if (metaData.Value.IsAssignableFrom(input.GetType()))
                {
                    return input;
                }
                else if (input is string)
                {
                    return _objectFactory.DeserializeJson((string)input, metaData.Value);
                }
                else
                {
                    return _objectFactory.DeserializeJson(input.ToString(), metaData.Value);
                }
            }
            catch (Exception ex)
            {
                // no need to throw here, they will
                // get an invalid cast exception right after this.
            }
            return input;
        }

        private IJsonRpcException PreProcess(IJsonRequest request, object context)
        {
            return externalPreProcessingHandler == null ? null : externalPreProcessingHandler(request, context);
        }

        private IJsonResponse PostProcess(IJsonRequest request, IJsonResponse response, object context)
        {
            if (externalPostProcessingHandler != null)
            {
                try
                {
                    IJsonRpcException exception = externalPostProcessingHandler(request, response, context);
                    if (exception != null)
                    {
                        response = _objectFactory.CreateJsonErrorResponse( exception );
                    }
                }
                catch (Exception ex)
                {
                    response = _objectFactory.CreateJsonErrorResponse( ProcessException(request, _objectFactory.CreateException(-32603, "Internal Error", ex)) );
                }
            }
            return response;
        }

    }

}

