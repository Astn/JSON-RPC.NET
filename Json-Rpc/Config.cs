using System;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// The PreProcessHandler is called after the request has been parsed and prior to calling the associated method on the JsonRpcService.
    /// If any non-null result is returned from the PreProcessHandler, the operation is aborted and the error is returned to the caller.
    /// </summary>
    /// <param name="request">The jsonRpc Request that is pending processing.</param>
    /// <param name="context">The context associated with this request</param>
    /// <returns>Any non-null result causes the operation to be aborted, and the JsonRpcException is returned to the caller.</returns>
    public delegate JsonRpcException PreProcessHandler(JsonRequest request, object context);
    /// <summary>
    /// The PostProcessHandler is called after the response has been created and prior to returning the data to the caller.
    /// If any non-null result is returned from the PostProcessHandler, the current return value is discared and the new return value used
    /// in preference.
    /// </summary>
    /// <param name="request">The jsonRpc Request that has been processed.</param>
    /// <param name="response">The jsonRpc Response that has been created.</param>
    /// <param name="context">The context associated with this request/response pair</param>
    /// <returns>Any non-null result causes the result to be discarded and the JsonRpcException is returned to the caller.</returns>
    public delegate JsonRpcException PostProcessHandler(JsonRequest request, JsonResponse response, object context);

    /// <summary>
    /// Global configurations for JsonRpc
    /// </summary>
    public static class Config
    {
        private static volatile JsonRpcSerializer _serializer;

        /// <summary>
        /// The serializer used when neither the call nor the session specifies one. Defaults to the built-in
        /// dependency-free serializer (<see cref="Jsmn.JsmnSerializer"/>). Install the Json.NET or System.Text.Json
        /// package and assign its serializer here to switch the whole process.
        /// </summary>
        public static JsonRpcSerializer Serializer
        {
            get { return _serializer ?? Jsmn.JsmnSerializer.Instance; }
            set { _serializer = value; }
        }

        private static volatile bool _includeExceptionDetails;

        /// <summary>
        /// Whether an ordinary exception thrown by a method (reported as a -32603 error) carries its diagnostics
        /// (Source, StackTraceString, HResult and the InnerException chain) to the client in <c>error.data</c>.
        /// False (the default) sends the exception type name and message only. Exceptions authored by the
        /// application (<see cref="JsonRpcException"/> and its <c>data</c>) are not affected.
        /// </summary>
        public static bool IncludeExceptionDetails
        {
            get { return _includeExceptionDetails; }
            set { _includeExceptionDetails = value; }
        }

        private static volatile JsonRpcVersionPolicy _versionPolicy = JsonRpcVersionPolicy.Lenient;

        /// <summary>
        /// How the <c>jsonrpc</c> member of incoming requests is checked. <see cref="JsonRpcVersionPolicy.Lenient"/>
        /// (the default) accepts a missing member and requires <c>"2.0"</c> when present; see the enum for the
        /// other choices. A session can override it with <see cref="SetVersionPolicy(string, JsonRpcVersionPolicy?)"/>.
        /// </summary>
        public static JsonRpcVersionPolicy VersionPolicy
        {
            get { return _versionPolicy; }
            set { _versionPolicy = value; }
        }

        private static volatile JsonRpcLimits _limits = JsonRpcLimits.Default;

        /// <summary>The process-wide document and batch limits, used when a session has no override.</summary>
        public static JsonRpcLimits Limits => _limits;

        /// <summary>Sets the process-wide document and batch limits. Null is not allowed.</summary>
        public static void SetLimits(JsonRpcLimits limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        /// <summary>Sets one session's limits; null makes it inherit <see cref="Limits"/>. Creates the session if needed.</summary>
        public static void SetLimits(string sessionId, JsonRpcLimits limits)
        {
            Handler.GetSessionHandler(sessionId).Limits = limits;
        }

        /// <summary>Sets the version policy for one session; null makes the session follow <see cref="VersionPolicy"/>.</summary>
        public static void SetVersionPolicy(string sessionId, JsonRpcVersionPolicy? policy)
        {
            Handler.GetSessionHandler(sessionId).VersionPolicy = policy;
        }

        /// <summary>Sets the process-wide default serializer (null restores the built-in one).</summary>
        public static void SetSerializer(JsonRpcSerializer serializer)
        {
            _serializer = serializer;
        }

        /// <summary>Sets the serializer for one session; null makes the session follow the global default.</summary>
        public static void SetSerializer(string sessionId, JsonRpcSerializer serializer)
        {
            Handler.GetSessionHandler(sessionId).Serializer = serializer;
        }

        /// <summary>
        /// Sets the pre-process handler of the default session only; other sessions do not inherit it.
        /// </summary>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetPreProcessHandler(PreProcessHandler handler)
        {
            Handler.DefaultHandler.SetPreProcessHandler(handler);
        }

        /// <summary>
        /// Sets the post-process handler of the default session only; other sessions do not inherit it.
        /// </summary>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetPostProcessHandler(PostProcessHandler handler)
        {
            Handler.DefaultHandler.SetPostProcessHandler(handler);
        }

        /// <summary>
        /// Sets the pre-process handler of one session. Null clears that session's handler; no other session is affected.
        /// </summary>
        /// <param name="sessionId">The session; it is created when it does not exist yet.</param>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetPreProcessHandler(string sessionId, PreProcessHandler handler)
        {
            Handler.GetSessionHandler(sessionId).SetPreProcessHandler(handler);
        }

        /// <summary>
        /// Sets the post-process handler of one session. Null clears that session's handler; no other session is affected.
        /// </summary>
        /// <param name="sessionId">The session; it is created when it does not exist yet.</param>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetPostProcessHandler(string sessionId, PostProcessHandler handler)
        {
            Handler.GetSessionHandler(sessionId).SetPostProcessHandler(handler);
        }

        /// <summary>The former name of <see cref="SetPreProcessHandler(string, PreProcessHandler)"/>.</summary>
#if NET5_0_OR_GREATER
        [Obsolete(Obsoletions.SetBeforeProcessHandlerMessage, DiagnosticId = Obsoletions.SetBeforeProcessHandlerDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
#else
        [Obsolete(Obsoletions.SetBeforeProcessHandlerDiagId + ": " + Obsoletions.SetBeforeProcessHandlerMessage)]
#endif
        public static void SetBeforeProcessHandler(string sessionId, PreProcessHandler handler)
        {
            SetPreProcessHandler(sessionId, handler);
        }

        /// <summary>
        /// For exceptions thrown after the routed method has been called.
        /// Allows you to specify an error handler that will be invoked prior to returning the JsonResponse to the client.
        /// You are able to modify the error that is returned inside the provided handler.
        /// Applies to the default session only; other sessions do not inherit it.
        /// </summary>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetErrorHandler(Func<JsonRequest, JsonRpcException, JsonRpcException> handler)
        {
            Handler.DefaultHandler.SetErrorHandler(handler);
        }

        /// <summary>
        /// For exceptions thrown after the routed method has been called.
        /// Allows you to specify an error handler that will be invoked prior to returning the JsonResponse to the client.
        /// You are able to modify the error that is returned inside the provided handler.
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="handler"></param>
        public static void SetErrorHandler(string sessionId, Func<JsonRequest, JsonRpcException, JsonRpcException> handler)
        {
            Handler.GetSessionHandler(sessionId).SetErrorHandler(handler);
        }

        /// <summary>
        /// For exceptions thrown during parsing and prior to a routed method being called.
        /// Allows you to specify an error handler that will be invoked prior to returning the JsonResponse to the client.
        /// You are able to modify the error that is returned inside the provided handler.
        /// Applies to the default session only; other sessions do not inherit it.
        /// </summary>
        /// <param name="handler">The handler, or null to clear it.</param>
        public static void SetParseErrorHandler(Func<string,JsonRpcException,JsonRpcException> handler)
        {
            Handler.DefaultHandler.SetParseErrorHandler(handler);
        }

        /// <summary>
        /// For exceptions thrown during parsing and prior to a routed method being called.
        /// Allows you to specify an error handler that will be invoked prior to returning the JsonResponse to the client.
        /// You are able to modify the error that is returned inside the provided handler.
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="handler"></param>
        public static void SetParseErrorHandler(string sessionId, Func<string, JsonRpcException, JsonRpcException> handler)
        {
            Handler.GetSessionHandler(sessionId).SetParseErrorHandler(handler);
        }
    }
}
