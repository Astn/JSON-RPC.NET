namespace AustinHarris.JsonRpc
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using AustinHarris.JsonRpc.Invocation;
    using AustinHarris.JsonRpc.Jsmn;
    using AustinHarris.JsonRpc.Serialization;
    using System.Collections.Concurrent;

    /// <summary>Dispatches requests for one session: a named set of JSON-RPC methods and configuration whose lifetime is managed explicitly.</summary>
    public sealed partial class Handler
    {
        #region Members
        private static int _sessionHandlerMasterVersion = 1;
        [ThreadStatic]
        private static Dictionary<string, Handler> _sessionHandlersLocal;
        [ThreadStatic]
        private static int _sessionHandlerLocalVersion = 0;
        // The last hit on this thread: a transport hands the same session-id string instance to every request, so
        // one reference comparison replaces hashing a GUID-length string. Dropped with the local snapshot.
        [ThreadStatic]
        private static string _lastSessionId;
        [ThreadStatic]
        private static Handler _lastSessionHandler;
        private static readonly ConcurrentDictionary<string, Handler> _sessionHandlersMaster = new ConcurrentDictionary<string, Handler>();

        private static readonly string _defaultSessionId = Guid.NewGuid().ToString();
        private static readonly Handler _unknownSessionHandler = new Handler(null);
        #endregion

        #region Constructors

        static Handler()
        {
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
        public static string DefaultSessionId() { return _defaultSessionId; }

        /// <summary>
        /// Gets a specific session, creating it when it does not exist yet. Registration paths (ServiceBinder,
        /// the Config setters) use this; the request path uses <see cref="TryGetSessionHandler"/>, so a request
        /// for an unknown session id never creates a session.
        /// </summary>
        /// <param name="sessionId">The sessionId of the handler you want to retrieve.</param>
        public static Handler GetSessionHandler(string sessionId)
        {
            if (TryGetSessionHandler(sessionId, out var handler)) return handler;
            // Add first, publish the version second: a thread that refreshes its snapshot in between copies the
            // new entry, so no thread can hold a current-looking snapshot that lacks it.
            handler = _sessionHandlersMaster.GetOrAdd(sessionId, id => new Handler(id));
            Interlocked.Increment(ref _sessionHandlerMasterVersion);
            return handler;
        }

        /// <summary>
        /// Looks a session up without creating it: this thread's last hit, then its snapshot of the registry, then
        /// the master registry itself (a registration can land after the snapshot was taken). False for an id that
        /// is not registered.
        /// </summary>
        internal static bool TryGetSessionHandler(string sessionId, out Handler handler)
        {
            if (_sessionHandlerMasterVersion != _sessionHandlerLocalVersion)
            {
                _sessionHandlersLocal = new Dictionary<string, Handler>(_sessionHandlersMaster);
                _sessionHandlerLocalVersion = _sessionHandlerMasterVersion;
                _lastSessionId = null;
                _lastSessionHandler = null;
            }
            else if (ReferenceEquals(sessionId, _lastSessionId))
            {
                handler = _lastSessionHandler;
                return true;
            }
            if (_sessionHandlersLocal.TryGetValue(sessionId, out handler) || _sessionHandlersMaster.TryGetValue(sessionId, out handler))
            {
                _lastSessionId = sessionId;
                _lastSessionHandler = handler;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Serves requests whose session id is not registered. It has no methods, no hooks and no serializer or
        /// version policy of its own (the global ones apply), so every call answers -32601, parse errors and
        /// batches behave as usual, and nothing is allocated or kept per unknown id.
        /// </summary>
        internal static Handler UnknownSessionHandler => _unknownSessionHandler;

        /// <summary>
        /// gets the default session
        /// </summary>
        public static Handler GetSessionHandler()
        {
            return GetSessionHandler(_defaultSessionId);
        }

        /// <summary>
        /// Removes and clears the Handler with the specific sessionId from the registry of Handlers
        /// </summary>
        public static void DestroySession(string sessionId)
        {
            Handler h;
            _sessionHandlersMaster.TryRemove(sessionId, out h);
            Interlocked.Increment(ref _sessionHandlerMasterVersion);
            h?.MetaData.Clear();
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
        /// The sessionId of this Handler
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>
        /// The serializer for this session. Null (the default) means <see cref="Config.Serializer"/>.
        /// A serializer passed to a JsonRpcProcessor call overrides both.
        /// </summary>
        public JsonRpcSerializer Serializer { get; set; }

        /// <summary>
        /// The <c>jsonrpc</c> member policy for this session. Null (the default) means <see cref="Config.VersionPolicy"/>.
        /// </summary>
        public JsonRpcVersionPolicy? VersionPolicy { get; set; }

        private volatile JsonRpcLimits _limits;

        /// <summary>The limits for this session. Null inherits <see cref="Config.Limits"/>.</summary>
        public JsonRpcLimits Limits
        {
            get { return _limits; }
            set { _limits = value; }
        }

        /// <summary>
        /// Provides access to a context specific to each JsonRpc method invocation.
        /// Warning: Must be called from within the execution context of the jsonRpc Method to return the context
        /// </summary>
        public static object RpcContext()
        {
            return __state?.Context;
        }

        /// <summary>
        /// The raw JSON of the id of the request being served (<c>12</c>, <c>"abc"</c>, <c>null</c>), sliced from the
        /// request bytes: nothing is decoded or allocated. Empty for a notification and outside an invocation.
        /// The span is a borrow of a pooled buffer: use it before returning from the method and never store it;
        /// <see cref="RpcRequestId"/> returns a snapshot that can be kept. Like <see cref="RpcContext"/> this reads
        /// the per-thread frame, so it is empty on any other thread the method starts work on.
        /// </summary>
        public static ReadOnlySpan<byte> RpcRequestIdRaw()
        {
            var s = __state;
            if (s == null) return default;
            var reader = s.Reader;
            if (reader == null) return default;
            return reader.IdRaw;
        }

        /// <summary>The kind of the id of the request being served; <see cref="JsonRpcIdKind.Absent"/> for a notification and outside an invocation.</summary>
        public static JsonRpcIdKind RpcRequestIdKind()
        {
            var s = __state;
            if (s == null) return JsonRpcIdKind.Absent;
            var reader = s.Reader;
            if (reader == null) return JsonRpcIdKind.Absent;
            return reader.IdKind;
        }

        /// <summary>
        /// An owned snapshot of the id of the request being served (see <see cref="JsonRpcRequestId"/>). An integer
        /// id costs nothing beyond the parse; a string id allocates its decoded string. Absent for a notification
        /// and outside an invocation. Must be called on the thread that runs the method; capture it before handing
        /// work to another thread.
        /// </summary>
        public static JsonRpcRequestId RpcRequestId()
        {
            var s = __state;
            if (s == null) return default;
            var reader = s.Reader;
            if (reader == null) return default;
            return JsonRpcRequestId.FromRaw(reader.IdRaw, reader.IdKind);
        }

        /// <summary>
        /// The per-thread invocation frame: the context of the method currently executing, the exception it
        /// set through <see cref="RpcSetException"/>, and the reader positioned on the request it serves (the
        /// source of the request id, read on demand). Every dispatch saves the frame on entry and restores it on
        /// exit (finally), so a method that synchronously processes another request keeps its own context, error
        /// state and id.
        /// </summary>
        private sealed class InvocationState
        {
            public object Context;
            public JsonRpcException Exception;
            public JsonRpcRequestReader Reader;
        }

        [ThreadStatic]
        private static InvocationState __state;

        private static InvocationState State
        {
            get
            {
                var s = __state;
                if (s == null) __state = s = new InvocationState();
                return s;
            }
        }

        /// <summary>
        /// Allows you to set the exception used in in the JsonRpc response.
        /// Warning: Must be called from the same thread as the jsonRpc method.
        /// </summary>
        public static void RpcSetException(JsonRpcException exception)
        {
            State.Exception = exception;
        }
        public static JsonRpcException RpcGetAndRemoveRpcException()
        {
            var s = __state;
            if (s == null) return null;
            var ex = s.Exception;
            s.Exception = null;
            return ex;
        }

        private PreProcessHandler externalPreProcessingHandler;
        private PostProcessHandler externalPostProcessingHandler;
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
        public static void RegisterInstance(string sessionId, object instance)
        {
            ServiceBinder.BindService(sessionId, instance);
        }

        /// <summary>
        /// Allows you to register any function, lambda, etc even when not attributed with JsonRpcMethod.
        /// Requires you to specify all types and defaults
        /// </summary>
        /// <param name="methodName">The method name that will map to the registered function</param>
        /// <param name="parameterNameTypeMapping">The parameter names and types that will be positionally bound to the function; the last entry is the return type</param>
        /// <param name="parameterNameDefaultValueMapping">Optional default values for parameters</param>
        /// <param name="implementation">A reference to the Function</param>
        [Obsolete("Use ServiceBinder.BindMethod; unlike RegisterFuction it throws when the name is already registered instead of replacing it.")]
        public void RegisterFuction(string methodName, Dictionary<string, Type> parameterNameTypeMapping, Dictionary<string, object> parameterNameDefaultValueMapping, Delegate implementation)
        {
            MetaData.AddService(methodName, parameterNameTypeMapping, parameterNameDefaultValueMapping ?? new Dictionary<string, object>(), implementation);
        }

        [Obsolete("Use ServiceBinder.UnbindMethod.")]
        public void UnRegisterFunction(string methodName)
        {
            MetaData.RemoveService(methodName);
        }

        public void SetPreProcessHandler(PreProcessHandler handler)
        {
            externalPreProcessingHandler = handler;
        }

        public void SetPostProcessHandler(PostProcessHandler handler)
        {
            externalPostProcessingHandler = handler;
        }

        /// <summary>
        /// Invokes a method to handle an already-materialised JsonRpc request (the compatibility path; the
        /// processor binds parameters directly from bytes instead).
        /// </summary>
        /// <param name="Rpc">JsonRpc Request to be processed</param>
        /// <param name="RpcContext">Optional context that will be available from within the jsonRpcMethod.</param>
        public JsonResponse Handle(JsonRequest Rpc, Object RpcContext = null)
        {
            var serializer = Serializer ?? Config.Serializer;
            using (var buffer = new PooledByteBufferWriter(512))
            {
                serializer.Write(buffer, Rpc, typeof(JsonRequest));
                var reader = serializer.CreateReader();
                try
                {
                    if (!reader.TryParse(buffer.WrittenMemory, out var error) || !reader.Select(0))
                    {
                        return new JsonResponse { Error = new JsonRpcException(-32600, "Invalid Request", error), Id = Rpc.Id };
                    }
                    if (!reader.HasMethod)
                    {
                        return new JsonResponse { Error = new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'"), Id = Rpc.Id };
                    }
                    var response = HandleBoxed(reader, serializer, RpcContext);
                    response.Id = Rpc.Id;
                    return response;
                }
                finally
                {
                    reader.Release();
                }
            }
        }
        #endregion

        #region Request pipeline

        private static readonly byte[] ResultPrefix = System.Text.Encoding.ASCII.GetBytes("{\"jsonrpc\":\"2.0\",\"result\":");
        private static readonly byte[] ErrorPrefix = System.Text.Encoding.ASCII.GetBytes("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":");
        private static readonly byte[] MessageInfix = System.Text.Encoding.ASCII.GetBytes(",\"message\":");
        private static readonly byte[] DataInfix = System.Text.Encoding.ASCII.GetBytes(",\"data\":");
        private static readonly byte[] IdInfix = System.Text.Encoding.ASCII.GetBytes(",\"id\":");
        private static readonly byte[] ErrorIdInfix = System.Text.Encoding.ASCII.GetBytes("},\"id\":");

        /// <summary>
        /// Handles request <paramref name="index"/> of the parsed document, writing the response (if any) to
        /// <paramref name="output"/>. Returns false when nothing was written: a notification (a request without an
        /// id) never gets a wire response, whatever its outcome; the error handlers still run for it server-side.
        /// </summary>
        internal bool HandleRequest(JsonRpcRequestReader reader, int index, JsonRpcSerializer serializer, PooledByteBufferWriter output, object context)
        {
            int envelopeStart = output.WrittenCount;

            if (!reader.Select(index))
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Request must be an object.")), default);
                return true;
            }

            var idKind = reader.IdKind;
            if (idKind == JsonRpcIdKind.Invalid)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Id property must be either null or string or integer.")), default);
                return true;
            }
            var idRaw = reader.IdRaw;
            var policy = VersionPolicy ?? Config.VersionPolicy;
            if (policy != JsonRpcVersionPolicy.Ignore)
            {
                var version = reader.VersionKind;
                if (version == JsonRpcVersionKind.Other)
                {
                    WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "The 'jsonrpc' member must be \"2.0\".")), idRaw);
                    return true;
                }
                if (version == JsonRpcVersionKind.Absent && policy == JsonRpcVersionPolicy.Strict)
                {
                    WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Missing property 'jsonrpc'")), idRaw);
                    return true;
                }
            }
            if (!reader.HasMethod)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'")), idRaw);
                return true;
            }
            if (reader.ParamsKind == JsonRpcParamsKind.Invalid)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "The 'params' member must be an array or an object.")), idRaw);
                return true;
            }

            // From here on the request object is valid: without an id it is a notification and gets no response.
            bool notification = idKind == JsonRpcIdKind.Absent;

            if (externalPreProcessingHandler != null || externalPostProcessingHandler != null)
            {
                object originalId = reader.IdValue;
                var response = HandleBoxed(reader, serializer, context);
                if (notification) return false;
                if (Equals(response.Id, originalId))
                {
                    WriteResponse(output, serializer, response, idRaw, envelopeStart);
                }
                else
                {
                    // a handler replaced the id: echo the new one
                    using (var idBuffer = new PooledByteBufferWriter(64))
                    {
                        WriteIdValue(idBuffer, serializer, response.Id);
                        WriteResponse(output, serializer, response, idBuffer.WrittenSpan, envelopeStart);
                    }
                }
                return true;
            }

            var service = Resolve(reader);
            if (service == null)
            {
                if (notification && externalErrorHandler == null) return false;
                var notFound = ProcessException(reader, MethodNotFound(reader.Method));
                if (notification) return false;
                WriteErrorEnvelope(output, serializer, notFound, idRaw);
                return true;
            }
            var method = service.Method;
            var map = BindMap(reader, method, out var bindError);
            if (bindError != null)
            {
                bindError = ProcessException(reader, bindError);
                if (notification) return false;
                WriteErrorEnvelope(output, serializer, bindError, idRaw);
                return true;
            }

            var state = State;
            var outerContext = state.Context;
            var outerException = state.Exception;
            var outerReader = state.Reader;
            state.Context = context;
            state.Exception = null;
            state.Reader = reader;
            try
            {
                output.Write(ResultPrefix);
                try
                {
                    // The built-in serializer's requests take the invoker compiled against the concrete reader and
                    // writer: typed reads straight from the tokens and formatted writes into the pooled buffer, with
                    // no virtual, delegate or interface call in between. Everything else goes through the serializer.
                    if (reader is JsmnRequestReader jsmn && jsmn.IsBuiltIn) method.InvokeJsmn(jsmn, map, output);
                    else method.Invoke(reader, map, serializer, output);
                }
                catch (Exception ex)
                {
                    output.Rewind(envelopeStart);
                    var error = BindingFailure(reader, method, map, ex);
                    error = error != null ? ProcessException(reader, error) : MapException(reader, ex);
                    if (notification) return false;
                    WriteErrorEnvelope(output, serializer, error, idRaw);
                    return true;
                }
                var contextException = state.Exception;
                if (contextException != null)
                {
                    output.Rewind(envelopeStart);
                    contextException = ProcessException(reader, contextException);
                    if (notification) return false;
                    WriteErrorEnvelope(output, serializer, contextException, idRaw);
                    return true;
                }
                if (notification)
                {
                    output.Rewind(envelopeStart);
                    return false;
                }
                output.Write(IdInfix);
                WriteIdRaw(output, idRaw);
                output.Write((byte)'}');
                return true;
            }
            finally
            {
                state.Context = outerContext;
                state.Exception = outerException;
                state.Reader = outerReader;
                ReturnMap(method, map);
            }
        }

        /// <summary>
        /// The boxed path: materialises a JsonRequest/JsonResponse so pre/post handlers can see them. Everything
        /// (materialisation, the hooks, binding and invocation) runs inside one error boundary: this never throws.
        /// </summary>
        internal JsonResponse HandleBoxed(JsonRpcRequestReader reader, JsonRpcSerializer serializer, object context)
        {
            var state = State;
            var outerContext = state.Context;
            var outerException = state.Exception;
            var outerReader = state.Reader;
            state.Context = context;
            state.Exception = null;
            state.Reader = reader;
            try
            {
                return HandleBoxedCore(reader, serializer, context, state);
            }
            catch (Exception ex)
            {
                // last resort: a failure the boundaries below could not attribute still becomes a JSON-RPC error
                return new JsonResponse { Error = new JsonRpcException(-32603, "Internal Error", ex), Id = SafeIdValue(reader) };
            }
            finally
            {
                state.Context = outerContext;
                state.Exception = outerException;
                state.Reader = outerReader;
            }
        }

        private JsonResponse HandleBoxedCore(JsonRpcRequestReader reader, JsonRpcSerializer serializer, object context, InvocationState state)
        {
            string method = reader.Method;
            object id = reader.IdValue;
            object parameters;
            try
            {
                parameters = reader.ParamsValue;
            }
            catch (Exception ex)
            {
                // the serializer could not materialise params in its object model: report it without repeating the conversion
                var failed = new JsonRequest(method, null, id);
                return PostProcess(failed, new JsonResponse { Error = MapException(failed, ex), Id = id }, context);
            }

            var request = new JsonRequest(method, parameters, id);
            JsonRpcException preProcessingException;
            try
            {
                preProcessingException = PreProcess(request, context);
            }
            catch (Exception ex)
            {
                preProcessingException = ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex));
            }
            if (preProcessingException != null)
            {
                return PostProcess(request, new JsonResponse { Error = preProcessingException, Id = request.Id }, context);
            }

            // Dispatch what the pre-process handler returned. When it left the request alone this is the same
            // reader the fast path uses; when it replaced Method, Params or Id, the request is round-tripped
            // through the serializer and dispatched from the result (as 1.x dispatched from the request object).
            if (request.Method == method && ReferenceEquals(request.Params, parameters) && Equals(request.Id, id))
            {
                return PostProcess(request, InvokeBoxed(reader, request, state), context);
            }
            return PostProcess(request, InvokeModified(request, serializer, state), context);
        }

        private JsonResponse InvokeModified(JsonRequest request, JsonRpcSerializer serializer, InvocationState state)
        {
            JsonRpcRequestReader reader = null;
            try
            {
                using (var buffer = new PooledByteBufferWriter(512))
                {
                    serializer.Write(buffer, request, typeof(JsonRequest));
                    reader = serializer.CreateReader();
                    if (!reader.TryParse(buffer.WrittenMemory, out var error) || !reader.Select(0))
                    {
                        return new JsonResponse { Error = ProcessException(request, new JsonRpcException(-32600, "Invalid Request", error)), Id = request.Id };
                    }
                    if (!reader.HasMethod)
                    {
                        return new JsonResponse { Error = ProcessException(request, new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'")), Id = request.Id };
                    }
                    if (reader.IdKind == JsonRpcIdKind.Invalid)
                    {
                        // the hook replaced the id with something JSON-RPC does not allow (a fraction, a bool, an object...)
                        return new JsonResponse { Error = ProcessException(request, new JsonRpcException(-32600, "Invalid Request", "Id property must be either null or string or integer.")), Id = request.Id };
                    }
                    if (reader.ParamsKind == JsonRpcParamsKind.Invalid)
                    {
                        return new JsonResponse { Error = ProcessException(request, new JsonRpcException(-32600, "Invalid Request", "The 'params' member must be an array or an object.")), Id = request.Id };
                    }
                    return InvokeBoxed(reader, request, state);
                }
            }
            catch (Exception ex)
            {
                // the modified request could not be serialized or parsed
                return new JsonResponse { Error = MapException(request, ex), Id = request.Id };
            }
            finally
            {
                reader?.Release();
            }
        }

        /// <summary>Resolves, binds and invokes from <paramref name="reader"/>, returning the boxed response for <paramref name="request"/>.</summary>
        private JsonResponse InvokeBoxed(JsonRpcRequestReader reader, JsonRequest request, InvocationState state)
        {
            var service = Resolve(reader);
            if (service == null)
            {
                return new JsonResponse { Error = ProcessException(request, MethodNotFound(reader.Method)), Id = request.Id };
            }

            var method = service.Method;
            var map = BindMap(reader, method, out var bindError);
            if (bindError != null)
            {
                return new JsonResponse { Error = ProcessException(request, bindError), Id = request.Id };
            }
            // the reader in hand is the effective request: the original one, or the re-parsed request a hook modified
            var outerReader = state.Reader;
            state.Reader = reader;
            try
            {
                var result = method.InvokeBoxed(reader, map);
                var contextException = state.Exception;
                return contextException != null
                    ? new JsonResponse { Error = ProcessException(request, contextException), Id = request.Id }
                    : new JsonResponse { Result = result, Id = request.Id };
            }
            catch (Exception ex)
            {
                var error = BindingFailure(reader, method, map, ex);
                return new JsonResponse { Error = error != null ? ProcessException(request, error) : MapException(request, ex), Id = request.Id };
            }
            finally
            {
                state.Exception = null;
                state.Reader = outerReader;
                ReturnMap(method, map);
            }
        }

        private static JsonRpcException MethodNotFound(string method)
        {
            return new JsonRpcException(-32601, "Method not found", new MethodNotFoundInfo(method));
        }

        private SMDService Resolve(JsonRpcRequestReader reader)
        {
            return MetaData.Find(reader.MethodUtf8) ?? MetaData.Find(reader.Method);
        }

        /// <summary>
        /// Computes reader-index → parameter mapping. Positional params fill from the front and defaults from
        /// the back; named params match by exact name, every supplied name must match a parameter (an unknown
        /// or repeated name is -32602) and defaults fill only the names that are absent. Returns the method's
        /// identity map when nothing needs mapping.
        /// </summary>
        private static int[] BindMap(JsonRpcRequestReader reader, RpcMethod method, out JsonRpcException error)
        {
            error = null;
            var parameters = method.Parameters;
            int expected = parameters.Length;
            int given = reader.ParamCount;

            if (reader.ParamsKind != JsonRpcParamsKind.Object)
            {
                if (given == expected) return method.IdentityMap;
                if (given > expected)
                {
                    error = new JsonRpcException(-32602, "Invalid params", string.Format("Expecting {0} parameters, and received {1}", expected, given));
                    return null;
                }
                int missing = expected - given;
                if (missing > method.DefaultCount)
                {
                    error = new JsonRpcException(-32602, "Invalid params", string.Format("Number of default parameters {0} not sufficient to fill all missing parameters {1}", method.DefaultCount, missing));
                    return null;
                }
                var map = ArrayPool<int>.Shared.Rent(expected);
                for (int i = 0; i < expected; i++) map[i] = i < given ? i : -1;
                return map;
            }
            else
            {
                if (given == expected && method.HasUniqueNames)
                {
                    // names supplied in declaration order: the common case for generated clients, and the identity map
                    int p = 0;
                    while (p < expected && reader.ParamNameUtf8(p).SequenceEqual(parameters[p].NameUtf8)) p++;
                    if (p == expected) return method.IdentityMap;
                }
                var map = ArrayPool<int>.Shared.Rent(Math.Max(1, expected));
                int matched = 0;
                for (int p = 0; p < expected; p++)
                {
                    var name = parameters[p].NameUtf8;
                    int found = -1;
                    for (int j = 0; j < given; j++)
                    {
                        if (reader.ParamNameUtf8(j).SequenceEqual(name)) { found = j; break; }
                    }
                    if (found >= 0) matched++;
                    map[p] = found;
                }
                if (matched != given)
                {
                    // a supplied member matched no parameter, or a name was supplied more than once
                    error = UnmatchedNamedParameter(reader, parameters, map, given);
                    ArrayPool<int>.Shared.Return(map);
                    return null;
                }
                for (int p = 0; p < expected; p++)
                {
                    if (map[p] < 0 && !parameters[p].HasDefault)
                    {
                        ArrayPool<int>.Shared.Return(map);
                        error = new JsonRpcException(-32602, "Invalid params", string.Format("Named parameter '{0}' was not present.", parameters[p].Name));
                        return null;
                    }
                }
                return map;
            }
        }

        private static JsonRpcException UnmatchedNamedParameter(JsonRpcRequestReader reader, RpcParameter[] parameters, int[] map, int given)
        {
            for (int j = 0; j < given; j++)
            {
                bool used = false;
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (map[p] == j) { used = true; break; }
                }
                if (used) continue;
                var name = reader.ParamNameUtf8(j);
                string text = Utf8Json.ToStringUtf8(name);
                for (int p = 0; p < parameters.Length; p++)
                {
                    if (name.SequenceEqual(parameters[p].NameUtf8))
                    {
                        return new JsonRpcException(-32602, "Invalid params", string.Format("Named parameter '{0}' was supplied more than once.", text));
                    }
                }
                return new JsonRpcException(-32602, "Invalid params", string.Format("Unknown named parameter '{0}'.", text));
            }
            return new JsonRpcException(-32602, "Invalid params", string.Format("Expecting {0} parameters, and received {1}", parameters.Length, given));
        }

        private static void ReturnMap(RpcMethod method, int[] map)
        {
            if (map != null && !ReferenceEquals(map, method.IdentityMap)) ArrayPool<int>.Shared.Return(map);
        }

        // ---- response writing ----

        internal static void WriteResponse(PooledByteBufferWriter output, JsonRpcSerializer serializer, JsonResponse response, ReadOnlySpan<byte> idRaw, int envelopeStart)
        {
            if (response.Error != null)
            {
                WriteErrorEnvelope(output, serializer, response.Error, idRaw);
                return;
            }
            output.Write(ResultPrefix);
            try
            {
                if (response.Result == null) Utf8Json.WriteNull(output);
                else serializer.Write(output, response.Result, response.Result.GetType());
            }
            catch (Exception ex)
            {
                output.Rewind(envelopeStart);
                WriteErrorEnvelope(output, serializer, new JsonRpcException(-32603, "Internal Error", ex), idRaw);
                return;
            }
            output.Write(IdInfix);
            WriteIdRaw(output, idRaw);
            output.Write((byte)'}');
        }

        internal static void WriteErrorEnvelope(PooledByteBufferWriter output, JsonRpcSerializer serializer, JsonRpcException error, ReadOnlySpan<byte> idRaw)
        {
            int start = output.WrittenCount;
            output.Write(ErrorPrefix);
            Utf8Json.WriteInt64(output, error.code);
            output.Write(MessageInfix);
            Utf8Json.WriteString(output, error.message);
            output.Write(DataInfix);
            try
            {
                WriteErrorData(output, serializer, error.data);
            }
            catch (Exception)
            {
                // the data object could not be serialized; fall back to its text
                output.Rewind(start);
                output.Write(ErrorPrefix);
                Utf8Json.WriteInt64(output, error.code);
                output.Write(MessageInfix);
                Utf8Json.WriteString(output, error.message);
                output.Write(DataInfix);
                if (error.data is Exception && !Config.IncludeExceptionDetails) Utf8Json.WriteNull(output);
                else Utf8Json.WriteString(output, Convert.ToString(error.data));
            }
            output.Write(ErrorIdInfix);
            WriteIdRaw(output, idRaw);
            output.Write((byte)'}');
        }

        private static void WriteErrorData(IBufferWriter<byte> output, JsonRpcSerializer serializer, object data)
        {
            switch (data)
            {
                case null:
                    Utf8Json.WriteNull(output);
                    break;
                case string s:
                    Utf8Json.WriteString(output, s);
                    break;
                case MethodNotFoundInfo notFound:
                    notFound.WriteTo(output);
                    break;
                case LimitExceededInfo limitExceeded:
                    limitExceeded.WriteTo(output);
                    break;
                case ParameterErrorInfo parameterError:
                    parameterError.WriteTo(output);
                    break;
                case JsonRpcException nested:
                    {
                        bool details = Config.IncludeExceptionDetails;
                        serializer.Write(output, new ExceptionInfo { ClassName = nested.GetType().FullName, Message = nested.message, HResult = nested.code, StackTraceString = details ? nested.StackTrace : null, Source = details ? nested.Source : null }, typeof(ExceptionInfo));
                    }
                    break;
                case Exception ex:
                    // With details off nothing about an unhandled exception leaves the process, not even its type
                    // name or message. Error handlers already ran and saw the exception itself in error.data.
                    if (Config.IncludeExceptionDetails) serializer.Write(output, ExceptionInfo.From(ex), typeof(ExceptionInfo));
                    else Utf8Json.WriteNull(output);
                    break;
                default:
                    serializer.Write(output, data, data.GetType());
                    break;
            }
        }

        private static void WriteIdRaw(PooledByteBufferWriter output, ReadOnlySpan<byte> idRaw)
        {
            if (idRaw.Length == 0) Utf8Json.WriteNull(output);
            else output.Write(idRaw);
        }

        /// <summary>Writes an id held as a CLR value (a handler replaced the request id).</summary>
        private static void WriteIdValue(PooledByteBufferWriter output, JsonRpcSerializer serializer, object id)
        {
            switch (id)
            {
                case null: Utf8Json.WriteNull(output); break;
                case string s: Utf8Json.WriteString(output, s); break;
                case long l: Utf8Json.WriteInt64(output, l); break;
                case int i: Utf8Json.WriteInt64(output, i); break;
                default: serializer.Write(output, id, id.GetType()); break;
            }
        }

        // ---- exception mapping / handler hooks ----

        private JsonRpcException MapException(JsonRpcRequestReader reader, Exception ex)
        {
            return MapException(externalErrorHandler == null ? null : MaterializeForHandler(reader), ex);
        }

        /// <summary>
        /// The request as an error handler sees it. Params is null when the serializer cannot materialise it
        /// (that failure is usually the error being reported, so it must not be repeated here).
        /// </summary>
        private static JsonRequest MaterializeForHandler(JsonRpcRequestReader reader)
        {
            object parameters;
            try
            {
                parameters = reader.ParamsValue;
            }
            catch (Exception)
            {
                parameters = null;
            }
            return new JsonRequest(reader.Method, parameters, SafeIdValue(reader));
        }

        private static object SafeIdValue(JsonRpcRequestReader reader)
        {
            try
            {
                return reader.IdValue;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private JsonRpcException MapException(JsonRequest request, Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
            if (ex is JsonRpcException rpcEx) return ProcessException(request, rpcEx);
            if (ex.InnerException is JsonRpcException innerRpc) return ProcessException(request, innerRpc);
            if (ex is JsonRpcBindException) return ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex));
            if (ex.InnerException != null) return ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex.InnerException));
            return ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex));
        }

        /// <summary>
        /// Tells a bad argument from a failure inside the method after an invocation threw <paramref name="ex"/>: the
        /// arguments are read again, in order, and the first one the serializer refuses is the culprit (reads are
        /// deterministic, and the method never ran when an argument failed). A conversion failure is the client's
        /// fault: -32602 with a <see cref="ParameterErrorInfo"/> naming the parameter. Null when every argument reads
        /// back fine (the method itself failed), when the failure is not a conversion (a type the serializer does not
        /// support), or when the exception is an authored <see cref="JsonRpcException"/>; those keep the -32603
        /// mapping of <see cref="MapException(JsonRequest, Exception)"/>. Only ever runs on the error path.
        /// </summary>
        private static JsonRpcException BindingFailure(JsonRpcRequestReader reader, RpcMethod method, int[] map, Exception ex)
        {
            if (ex is TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
            if (ex is JsonRpcException || ex.InnerException is JsonRpcException) return null;
            if (!ParameterErrorInfo.IsConversionFailure(ex)) return null;
            var parameters = method.Parameters;
            for (int p = 0; p < parameters.Length; p++)
            {
                int index = map[p];
                if (index < 0) continue;
                try
                {
                    reader.ReadParam(index, parameters[p].Type);
                }
                catch (Exception)
                {
                    return new JsonRpcException(-32602, "Invalid params", new ParameterErrorInfo(parameters[p], p, ex));
                }
            }
            return null;
        }

        private JsonRpcException ProcessException(JsonRpcRequestReader reader, JsonRpcException ex)
        {
            if (externalErrorHandler != null)
                return externalErrorHandler(MaterializeForHandler(reader), ex);
            return ex;
        }

        private JsonRpcException ProcessException(JsonRequest req, JsonRpcException ex)
        {
            if (externalErrorHandler != null)
                return externalErrorHandler(req, ex);
            return ex;
        }

        internal JsonRpcException ProcessParseException(JsonRpcRequestReader reader, JsonRpcException ex)
        {
            if (parseErrorHandler != null)
                return parseErrorHandler(Utf8Json.ToStringUtf8(reader.Document.Span), ex);
            return ex;
        }

        internal JsonRpcException ProcessParseException(string req, JsonRpcException ex)
        {
            if (parseErrorHandler != null)
                return parseErrorHandler(req, ex);
            return ex;
        }

        internal bool HasParseErrorHandler => parseErrorHandler != null;

        internal void SetErrorHandler(Func<JsonRequest, JsonRpcException, JsonRpcException> handler)
        {
            externalErrorHandler = handler;
        }
        internal void SetParseErrorHandler(Func<string, JsonRpcException, JsonRpcException> handler)
        {
            parseErrorHandler = handler;
        }

        private JsonRpcException PreProcess(JsonRequest request, object context)
        {
            return externalPreProcessingHandler?.Invoke(request, context);
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
                        response = new JsonResponse() { Error = exception, Id = request.Id };
                    }
                }
                catch (Exception ex)
                {
                    response = new JsonResponse() { Error = ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex)), Id = request.Id };
                }
            }
            return response;
        }

        #endregion
    }
}
