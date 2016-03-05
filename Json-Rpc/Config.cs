﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        /// <summary>
        /// Sets the the PreProcessing Handler on the default session.
        /// </summary>
        /// <param name="handler"></param>
        public static void SetPreProcessHandler(PreProcessHandler handler)
        {
            Handler.DefaultHandler.SetPreProcessHandler(handler);
        }

        /// <summary>
        /// Sets the the PostProcessing Handler on the default session.
        /// </summary>
        /// <param name="handler"></param>
        public static void SetPostProcessHandler(PostProcessHandler handler)
        {
            Handler.DefaultHandler.SetPostProcessHandler(handler);
        }

        /// <summary>
        /// Sets the PreProcessing Handler on a specific session
        /// </summary>
        /// <param name="sessionId"></param>
        /// <param name="handler"></param>
        public static void SetBeforeProcessHandler(string sessionId, PreProcessHandler handler)
        {
            Handler.GetSessionHandler(sessionId).SetPreProcessHandler(handler);
        }

        /// <summary>
        /// For exceptions thrown after the routed method has been called.
        /// Allows you to specify an error handler that will be invoked prior to returning the JsonResponse to the client.
        /// You are able to modify the error that is returned inside the provided handler.
        /// </summary>
        /// <param name="handler"></param>
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
        /// </summary>
        /// <param name="handler"></param>
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
