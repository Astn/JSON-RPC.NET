using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AustinHarris.JsonRpc
{
    public class Handler
    {
        [ThreadStatic] private static object _currentRpcContext;

        /// <summary>
        ///     This metadata contains all the types and mappings of all the methods in this handler. Warning: Modifying this
        ///     directly could cause your handler to no longer function.
        /// </summary>
        public SMD MetaData { get; private set; }

        private void AddRpcContext(object rpcContext)
        {
            _currentRpcContext = rpcContext;
        }

        private void RemoveRpcContext()
        {
            _currentRpcContext = null;
        }

        private JsonRpcException ProcessException(JsonRequest req, JsonRpcException ex)
        {
            if (_externalErrorHandler != null)
                return _externalErrorHandler(req, ex);
            return ex;
        }

        internal JsonRpcException ProcessParseException(string req, JsonRpcException ex)
        {
            if (_parseErrorHandler != null)
                return _parseErrorHandler(req, ex);

            return ex;
        }

        internal void SetErrorHandler(Func<JsonRequest, JsonRpcException, JsonRpcException> handler)
        {
            _externalErrorHandler = handler;
        }

        internal void SetParseErrorHandler(Func<string, JsonRpcException, JsonRpcException> handler)
        {
            _parseErrorHandler = handler;
        }

        private object CleanUpParameter(object p, SMDAdditionalParameters metaData)
        {
            if (p is JValue jsonValue)
            {
                if (jsonValue.Value == null || metaData.ObjectType == jsonValue.Value.GetType())
                    return jsonValue.Value;

                // Avoid calling DeserializeObject on types that JValue has an explicit converter for
                // try to optimize for the most common types
                if (metaData.ObjectType == typeof(string)) return (string) jsonValue;
                if (metaData.ObjectType == typeof(int)) return (int) jsonValue;
                if (metaData.ObjectType == typeof(double)) return (double) jsonValue;
                if (metaData.ObjectType == typeof(float)) return (float) jsonValue;
                //if (metaData.ObjectType == typeof(long)) return (long)bob;
                //if (metaData.ObjectType == typeof(uint)) return (uint)bob;
                //if (metaData.ObjectType == typeof(ulong)) return (ulong)bob;
                //if (metaData.ObjectType == typeof(byte[])) return (byte[])bob;
                //if (metaData.ObjectType == typeof(Guid)) return (Guid)bob;
                if (metaData.ObjectType == typeof(decimal)) return (decimal) jsonValue;
                //if (metaData.ObjectType == typeof(TimeSpan)) return (TimeSpan)bob;
                //if (metaData.ObjectType == typeof(short)) return (short)bob;
                //if (metaData.ObjectType == typeof(ushort)) return (ushort)bob;
                //if (metaData.ObjectType == typeof(char)) return (char)bob;
                //if (metaData.ObjectType == typeof(DateTime)) return (DateTime)bob;
                //if (metaData.ObjectType == typeof(bool)) return (bool)bob;
                //if (metaData.ObjectType == typeof(DateTimeOffset)) return (DateTimeOffset)bob;

                if (metaData.ObjectType.IsAssignableFrom(typeof(JValue)))
                    return jsonValue;

                try
                {
                    return jsonValue.ToObject(metaData.ObjectType);
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
                        return JsonConvert.DeserializeObject((string) p, metaData.ObjectType);

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
            return _externalPreProcessingHandler?.Invoke(request, context);
        }

        private JsonResponse PostProcess(JsonRequest request, JsonResponse response, object context)
        {
            if (_externalPostProcessingHandler != null)
                try
                {
                    var exception = _externalPostProcessingHandler(request, response, context);
                    if (exception != null)
                        response = new JsonResponse {Error = exception};
                }
                catch (Exception ex)
                {
                    response = new JsonResponse
                    {
                        Error = ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex))
                    };
                }
            return response;
        }

        #region Members

        private const string NameOfJsonrpcexception = "JsonRpcException&";
        private static int _sessionHandlerMasterVersion = 1;

        [ThreadStatic] private static Dictionary<string, Handler> _sessionHandlersLocal;

        [ThreadStatic] private static int _sessionHandlerLocalVersion;

        private static readonly ConcurrentDictionary<string, Handler> SessionHandlersMaster;

        private static volatile string _defaultSessionId;

        #endregion

        #region Constructors

        static Handler()
        {
            //current = new Handler(Guid.NewGuid().ToString());
            _defaultSessionId = Guid.NewGuid().ToString();
            SessionHandlersMaster = new ConcurrentDictionary<string, Handler>
            {
                [_defaultSessionId] = new Handler(_defaultSessionId)
            };
        }

        private Handler(string sessionId)
        {
            SessionId = sessionId;
            MetaData = new SMD();
        }

        #endregion

        #region Properties

        /// <summary>
        ///     Returns the SessionID of the default session
        /// </summary>
        /// <returns></returns>
        public static string DefaultSessionId()
        {
            return _defaultSessionId;
        }

        /// <summary>
        ///     Gets a specific session
        /// </summary>
        /// <param name="sessionId">The sessionId of the handler you want to retrieve.</param>
        /// <returns></returns>
        public static Handler GetSessionHandler(string sessionId)
        {
            if (_sessionHandlerMasterVersion != _sessionHandlerLocalVersion)
            {
                _sessionHandlersLocal = new Dictionary<string, Handler>(SessionHandlersMaster);
                _sessionHandlerLocalVersion = _sessionHandlerMasterVersion;
            }

            if (_sessionHandlersLocal.ContainsKey(sessionId))
                return _sessionHandlersLocal[sessionId];

            Interlocked.Increment(ref _sessionHandlerMasterVersion);

            return SessionHandlersMaster.GetOrAdd(sessionId, new Handler(sessionId));
        }

        /// <summary>
        ///     gets the default session
        /// </summary>
        /// <returns>The default Session Handler</returns>
        public static Handler GetSessionHandler()
        {
            return GetSessionHandler(_defaultSessionId);
        }

        /// <summary>
        ///     Removes and clears the Handler with the specific sessionID from the registry of Handlers
        /// </summary>
        /// <param name="sessionId"></param>
        private static void DestroySession(string sessionId)
        {
            if (SessionHandlersMaster.TryRemove(sessionId, out var handler))
            {
                Interlocked.Increment(ref _sessionHandlerMasterVersion);
                handler.MetaData.Services.Clear();
            }
        }

        /// <summary>
        ///     Removes and clears the current Handler from the registry of Handlers
        /// </summary>
        public void Destroy()
        {
            DestroySession(SessionId);
        }

        /// <summary>
        ///     Gets the default session handler
        /// </summary>
        public static Handler DefaultHandler => GetSessionHandler(_defaultSessionId);

        /// <summary>
        ///     The sessionID of this Handler
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        ///     Provides access to a context specific to each JsonRpc method invocation.
        ///     Warning: Must be called from within the execution context of the jsonRpc Method to return the context
        /// </summary>
        /// <returns></returns>
        public static object RpcContext()
        {
            return _currentRpcContext;
        }

        [ThreadStatic] private static JsonRpcException _currentRpcException;

        /// <summary>
        ///     Allows you to set the exception used in in the JsonRpc response.
        ///     Warning: Must be called from the same thread as the jsonRpc method.
        /// </summary>
        /// <param name="exception"></param>
        public static void RpcSetException(JsonRpcException exception)
        {
            _currentRpcException = exception;
        }

        private static JsonRpcException RpcGetAndRemoveRpcException()
        {
            var ex = _currentRpcException;
            _currentRpcException = null;
            return ex;
        }

        private PreProcessHandler _externalPreProcessingHandler;
        private PostProcessHandler _externalPostProcessingHandler;
        private Func<JsonRequest, JsonRpcException, JsonRpcException> _externalErrorHandler;
        private Func<string, JsonRpcException, JsonRpcException> _parseErrorHandler;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Allows you to register all the functions on a Pojo Type that have been attributed as [JsonRpcMethod] to the
        ///     specified sessionId
        /// </summary>
        /// <param name="sessionId">The session to register against</param>
        /// <param name="instance">The instance containing JsonRpcMethods to register</param>
        public static void RegisterInstance(string sessionId, object instance)
        {
            ServiceBinder.BindService(sessionId, instance);
        }

        /// <summary>
        ///     Allows you to register any function, lambda, etc even when not attributed with JsonRpcMethod.
        ///     Requires you to specify all types and defaults
        /// </summary>
        /// <param name="methodName">The method name that will map to the registered function</param>
        /// <param name="parameterNameTypeMapping">The parameter names and types that will be positionally bound to the function</param>
        /// <param name="parameterNameDefaultValueMapping">Optional default values for parameters</param>
        /// <param name="implementation">A reference to the Function</param>
        public void RegisterFuction(string methodName, Dictionary<string, Type> parameterNameTypeMapping,
            Dictionary<string, object> parameterNameDefaultValueMapping, Delegate implementation)
        {
            MetaData.AddService(methodName, parameterNameTypeMapping, parameterNameDefaultValueMapping, implementation);
        }

        public void UnRegisterFunction(string methodName)
        {
            MetaData.Services.Remove(methodName);
        }

        public void SetPreProcessHandler(PreProcessHandler handler)
        {
            _externalPreProcessingHandler = handler;
        }

        public void SetPostProcessHandler(PostProcessHandler handler)
        {
            _externalPostProcessingHandler = handler;
        }

        /// <summary>
        ///     Invokes a method to handle a JsonRpc request.
        /// </summary>
        /// <param name="rpc">JsonRpc Request to be processed</param>
        /// <param name="rpcContext">Optional context that will be available from within the jsonRpcMethod.</param>
        /// <returns></returns>
        public JsonResponse Handle(JsonRequest rpc, object rpcContext = null)
        {
            AddRpcContext(rpcContext);

            var preProcessingException = PreProcess(rpc, rpcContext);
            if (preProcessingException != null)
            {
                var response = new JsonResponse
                {
                    Error = preProcessingException,
                    Id = rpc.Id
                };
                //callback is called - if it is empty then nothing will be done
                //return response always- if callback is empty or not
                return PostProcess(rpc, response, rpcContext);
            }

            Delegate metadataSmdServiceDelegate;
            if (MetaData.Services.TryGetValue(rpc.Method, out var metadata) && metadata != null)
            {
                metadataSmdServiceDelegate = metadata.SmdServiceDelegate;
            }
            else
            {
                var response = new JsonResponse
                {
                    Result = null,
                    Error = new JsonRpcException(-32601, "Method not found",
                        "The method does not exist / is not available."),
                    Id = rpc.Id
                };

                return PostProcess(rpc, response, rpcContext);
            }
             
            var expectsRefException = false;
            var metaDataParamCount = metadata.parameters.Count(x => x != null);


            var loopCt = 0;

            if (rpc.Params is ICollection getCount)
            {
                loopCt = getCount.Count;
            }

            var paramCount = loopCt;
            if (paramCount == metaDataParamCount - 1 && metadata.parameters[metaDataParamCount - 1].ObjectType.Name.Equals(NameOfJsonrpcexception))
            {
                paramCount++;
                expectsRefException = true;
            }
            var parameters = new object[paramCount];

            if (rpc.Params is JArray jarr)
            {
                for (var i = 0; i < loopCt; i++)
                    parameters[i] = CleanUpParameter(jarr[i], metadata.parameters[i]);
            }
            else if (rpc.Params is JObject)
            {
                var asDict = rpc.Params as IDictionary<string, JToken>;
                for (var i = 0; i < loopCt && i < metadata.parameters.Length; i++)
                    if (asDict.ContainsKey(metadata.parameters[i].Name))
                    {
                        parameters[i] = CleanUpParameter(asDict[metadata.parameters[i].Name], metadata.parameters[i]);
                    }
                    else
                    {
                        var response = new JsonResponse
                        {
                            Error = ProcessException(rpc,
                                new JsonRpcException(-32602,
                                    "Invalid params",
                                    $"Named parameter '{metadata.parameters[i].Name}' was not present."
                                )),
                            Id = rpc.Id
                        };
                        return PostProcess(rpc, response, rpcContext);
                    }
            }

            // Optional Parameter support
            // check if we still miss parameters compared to metadata which may include optional parameters.
            // if the rpc-call didn't supply a value for an optional parameter, we should be assinging the default value of it.
            if (parameters.Length < metaDataParamCount && metadata.defaultValues.Length > 0
            ) // rpc call didn't set values for all optional parameters, so we need to assign the default values for them.
            {
                var suppliedParamsCount =
                    parameters.Length; // the index we should start storing default values of optional parameters.
                var missingParamsCount =
                    metaDataParamCount -
                    parameters.Length; // the amount of optional parameters without a value set by rpc-call.
                Array.Resize(ref parameters,
                    parameters.Length + missingParamsCount); // resize the array to include all optional parameters.

                for (int paramIndex = parameters.Length - 1,
                        defaultIndex = metadata.defaultValues.Length - 1; // fill missing parameters from the back 
                    paramIndex >= suppliedParamsCount && defaultIndex >= 0; // to don't overwrite supplied ones.
                    paramIndex--, defaultIndex--)
                    parameters[paramIndex] = metadata.defaultValues[defaultIndex].Value;

                if (missingParamsCount > metadata.defaultValues.Length)
                {
                    var response = new JsonResponse
                    {
                        Error = ProcessException(rpc,
                            new JsonRpcException(-32602,
                                "Invalid params",
                                $"Number of default parameters {metadata.defaultValues.Length} not sufficient to fill all missing parameters {missingParamsCount}"
                            )),
                        Id = rpc.Id
                    };
                    return PostProcess(rpc, response, rpcContext);
                }
            }

            if (parameters.Length != metaDataParamCount)
            {
                var response = new JsonResponse
                {
                    Error = ProcessException(rpc,
                        new JsonRpcException(-32602,
                            "Invalid params",
                            $"Expecting {metadata.parameters.Length} parameters, and received {parameters.Length}"
                        )),
                    Id = rpc.Id
                };
                return PostProcess(rpc, response, rpcContext);
            }

            try
            {
                var results = metadataSmdServiceDelegate.DynamicInvoke(parameters);

                var last = parameters.LastOrDefault();
                var contextException = RpcGetAndRemoveRpcException();

                JsonResponse response;
                if (contextException != null)
                {
                    response = new JsonResponse
                    {
                        Error = ProcessException(rpc, contextException),
                        Id = rpc.Id
                    };
                }
                else if (expectsRefException && last != null && last is JsonRpcException)
                {
                    response = new JsonResponse
                    {
                        Error = ProcessException(rpc, last as JsonRpcException),
                        Id = rpc.Id
                    };
                }
                else
                {
                    response = new JsonResponse
                    {
                        Result = results
                    };
                }

                return PostProcess(rpc, response, rpcContext);
            }
            catch (Exception ex)
            {
                JsonResponse response;
                if (ex is TargetParameterCountException)
                {
                    response = new JsonResponse
                    {
                        Error = ProcessException(rpc, new JsonRpcException(-32602, "Invalid params", ex))
                    };
                    return PostProcess(rpc, response, rpcContext);
                }

                // We really dont care about the TargetInvocationException, just pass on the inner exception
                if (ex is JsonRpcException)
                {
                    response = new JsonResponse {Error = ProcessException(rpc, ex as JsonRpcException)};
                    return PostProcess(rpc, response, rpcContext);
                }
                if (ex.InnerException is JsonRpcException)
                {
                    response = new JsonResponse {Error = ProcessException(rpc, ex.InnerException as JsonRpcException)};
                    return PostProcess(rpc, response, rpcContext);
                }
                else if (ex.InnerException != null)
                {
                    response = new JsonResponse
                    {
                        Error = ProcessException(rpc, new JsonRpcException(-32603, "Internal Error", ex.InnerException))
                    };
                    return PostProcess(rpc, response, rpcContext);
                }

                response = new JsonResponse
                {
                    Error = ProcessException(rpc, new JsonRpcException(-32603, "Internal Error", ex))
                };
                return PostProcess(rpc, response, rpcContext);
            }
            finally
            {
                RemoveRpcContext();
            }
        }

        #endregion
    }
}