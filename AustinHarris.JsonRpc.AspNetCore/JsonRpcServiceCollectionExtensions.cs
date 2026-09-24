using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AustinHarris.JsonRpc.AspNetCore
{
    public static class JsonRpcServiceCollectionExtensions
    {
        /// <summary>Registers JSON-RPC options and the binder that registers DI-constructed services at startup.</summary>
        public static IServiceCollection AddJsonRpc(this IServiceCollection services, Action<JsonRpcOptions> configure = null)
        {
            if (configure != null) services.Configure(configure);
            else services.AddOptions<JsonRpcOptions>();
            services.TryAddSingleton<JsonRpcConnectionHandler>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JsonRpcBinderHostedService>());
            return services;
        }

        /// <summary>
        /// Registers <typeparamref name="TService"/> as a singleton built by the container and binds every
        /// <c>[JsonRpcMethod]</c> on it to the session when the host starts. Any class works, controllers included:
        /// dependencies come from DI, the class does not need to derive from <see cref="JsonRpcService"/>.
        /// </summary>
        public static IServiceCollection AddJsonRpcService<TService>(this IServiceCollection services, string sessionId = null) where TService : class
        {
            services.TryAddSingleton<TService>();
            services.AddSingleton(new JsonRpcServiceRegistration(typeof(TService), sessionId));
            return services;
        }

        /// <summary>Registers every class in <paramref name="assembly"/> that declares at least one <c>[JsonRpcMethod]</c>.</summary>
        public static IServiceCollection AddJsonRpcServicesFromAssembly(this IServiceCollection services, Assembly assembly, string sessionId = null)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                bool hasRpc = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Any(m => m.IsDefined(typeof(JsonRpcMethodAttribute), false));
                if (!hasRpc) continue;
                services.TryAddSingleton(type);
                services.AddSingleton(new JsonRpcServiceRegistration(type, sessionId));
            }
            return services;
        }

        /// <summary>
        /// Maps a POST endpoint that processes JSON-RPC documents. Options default to the registered
        /// <see cref="JsonRpcOptions"/> (see <see cref="AddJsonRpc"/>); pass <paramref name="options"/> to override per endpoint.
        /// </summary>
        public static IEndpointConventionBuilder MapJsonRpc(this IEndpointRouteBuilder endpoints, string pattern = "/jsonrpc", JsonRpcOptions options = null)
        {
            var resolved = options ?? endpoints.ServiceProvider.GetService<IOptions<JsonRpcOptions>>()?.Value ?? new JsonRpcOptions();
            return resolved.EnableAsyncMethods
                ? endpoints.MapPost(pattern, http => JsonRpcEndpoint.HandleAsynchronousMethodsAsync(http, resolved))
                : endpoints.MapPost(pattern, http => JsonRpcEndpoint.HandleSynchronousMethodsAsync(http, resolved));
        }

        internal sealed class JsonRpcServiceRegistration
        {
            public JsonRpcServiceRegistration(Type type, string sessionId) { Type = type; SessionId = sessionId; }
            public Type Type { get; }
            public string SessionId { get; }
        }

        /// <summary>Resolves registered services from the container and binds them before the host starts accepting requests.</summary>
        internal sealed class JsonRpcBinderHostedService : IHostedService
        {
            private readonly IServiceProvider _provider;
            private readonly IEnumerable<JsonRpcServiceRegistration> _registrations;
            private readonly JsonRpcOptions _options;

            public JsonRpcBinderHostedService(IServiceProvider provider, IEnumerable<JsonRpcServiceRegistration> registrations, IOptions<JsonRpcOptions> options)
            {
                _provider = provider;
                _registrations = registrations;
                _options = options.Value;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                string defaultSession = Handler.DefaultSessionId();
                foreach (var r in _registrations)
                {
                    var instance = _provider.GetRequiredService(r.Type);
                    // The effective session: the registration's own, then JsonRpcOptions.SessionId, then the default.
                    var session = r.SessionId ?? _options.SessionId ?? defaultSession;
                    // A JsonRpcService subclass already bound itself in its constructor, to the default session
                    // (parameterless base constructor). Bind it here whenever the effective session is a different
                    // one, otherwise the configured session would answer -32601 for it; rebinding the same
                    // instance to the same session is harmless (the method table entry is replaced).
                    if (instance is JsonRpcService && session == defaultSession) continue;
                    ServiceBinder.BindService(session, instance);
                }
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
