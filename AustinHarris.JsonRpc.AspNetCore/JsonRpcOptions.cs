using System;
using AustinHarris.JsonRpc.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AustinHarris.JsonRpc.AspNetCore
{
    /// <summary>Settings shared by the HTTP endpoint and the raw connection handler.</summary>
    public class JsonRpcOptions
    {
        /// <summary>Awaits Task and ValueTask methods through ProcessAsync. False preserves synchronous processing.</summary>
        public bool EnableAsyncMethods { get; set; }

        /// <summary>The session whose registered methods answer requests. Null means the default session.</summary>
        public string SessionId { get; set; }

        /// <summary>
        /// Picks the session per HTTP request (for example from a route value or a header). When set it takes
        /// precedence over <see cref="SessionId"/>. Not used by the raw connection handler.
        /// </summary>
        public Func<HttpContext, string> SessionSelector { get; set; }

        /// <summary>The serializer to use. Null means the session's serializer, falling back to <see cref="Config.Serializer"/>.</summary>
        public JsonRpcSerializer Serializer { get; set; }

        /// <summary>
        /// Produces the object handed to methods through <see cref="JsonRpcContext.Current"/> and to pre/post handlers.
        /// Defaults to the <see cref="HttpContext"/> itself for HTTP and the connection context for raw connections.
        /// </summary>
        public Func<HttpContext, object> ContextFactory { get; set; }

        /// <summary>
        /// Finds the <see cref="IServiceProvider"/> that scoped and transient services (see
        /// <c>AddJsonRpcService&lt;T&gt;(ServiceLifetime)</c>) are resolved from, given the RPC context of the request
        /// (what <see cref="JsonRpcContext.Current"/> returns). Without one, the host handles an <see cref="HttpContext"/>
        /// (its <c>RequestServices</c>) and a raw <see cref="ConnectionContext"/> (the scope the connection handler opens
        /// per document, published as <see cref="IServiceProvidersFeature"/>). Required when <see cref="ContextFactory"/>
        /// produces anything else and a non-singleton service is registered: the host refuses to start, and
        /// <c>MapJsonRpc(pattern, options)</c> refuses to map, otherwise. A selector that returns null falls through to
        /// the built-in one; when no provider is found the call fails with <c>-32603</c>. The root provider is never used.
        /// </summary>
        public Func<object, IServiceProvider> ServiceProviderSelector { get; set; }

        /// <summary>The built-in selection: the HTTP request's services, or the per-document scope of a raw connection.</summary>
        internal static IServiceProvider DefaultServiceProviderSelector(object context)
        {
            if (context is HttpContext http) return http.RequestServices;
            if (context is ConnectionContext connection) return connection.Features.Get<IServiceProvidersFeature>()?.RequestServices;
            return null;
        }

        /// <summary>Largest request body accepted, in bytes. Larger bodies get 413. Default 4 MB.</summary>
        public long MaxRequestBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>Content type of responses. Default "application/json".</summary>
        public string ResponseContentType { get; set; } = "application/json";

        /// <summary>Whether a notification (no response) answers 204 No Content (default) or 200 with an empty body.</summary>
        public bool NoContentForNotifications { get; set; } = true;
    }
}
