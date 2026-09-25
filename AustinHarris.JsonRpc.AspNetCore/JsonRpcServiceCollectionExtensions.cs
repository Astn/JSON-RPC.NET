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
            AddCore(services);
            services.TryAddSingleton<JsonRpcConnectionHandler>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JsonRpcBinderHostedService>());
            return services;
        }

        /// <summary>
        /// Registers <typeparamref name="TService"/> as a singleton built by the container and binds every
        /// <c>[JsonRpcMethod]</c> on it to the session when the host starts. Any class works, controllers included:
        /// dependencies come from DI, the class does not need to derive from <see cref="JsonRpcService"/>. The one
        /// instance serves every request on every thread; for a service resolved per request, pass a lifetime to
        /// <see cref="AddJsonRpcService{TService}(IServiceCollection, ServiceLifetime, string)"/>.
        /// </summary>
        public static IServiceCollection AddJsonRpcService<TService>(this IServiceCollection services, string sessionId = null) where TService : class
        {
            return services.AddJsonRpcService<TService>(ServiceLifetime.Singleton, sessionId);
        }

        /// <summary>
        /// Registers <typeparamref name="TService"/> with <paramref name="lifetime"/> and binds every <c>[JsonRpcMethod]</c>
        /// on it to the session when the host starts. <see cref="ServiceLifetime.Singleton"/> resolves the service once
        /// from the root container at startup. <see cref="ServiceLifetime.Scoped"/> and <see cref="ServiceLifetime.Transient"/>
        /// resolve nothing at startup: right before each call the service is resolved from the request's
        /// <see cref="IServiceProvider"/> (<c>HttpContext.RequestServices</c> on HTTP; the scope the raw connection handler
        /// opens per document), so a scoped dependency such as a <c>DbContext</c> goes in the constructor as usual, every
        /// call of a batch shares one scope and a transient is created per call. A registration of
        /// <typeparamref name="TService"/> already in the container must have the same lifetime; otherwise this throws,
        /// or the host fails at startup when the conflicting registration is added later. A class deriving from
        /// <see cref="JsonRpcService"/> binds itself in its constructor and is accepted as a singleton only.
        /// </summary>
        public static IServiceCollection AddJsonRpcService<TService>(this IServiceCollection services, ServiceLifetime lifetime, string sessionId = null) where TService : class
        {
            AddRegistration(services, typeof(TService), lifetime, sessionId);
            return services;
        }

        /// <summary>Registers every class in <paramref name="assembly"/> that declares at least one <c>[JsonRpcMethod]</c>, as singletons.</summary>
        public static IServiceCollection AddJsonRpcServicesFromAssembly(this IServiceCollection services, Assembly assembly, string sessionId = null)
        {
            return services.AddJsonRpcServicesFromAssembly(assembly, ServiceLifetime.Singleton, sessionId);
        }

        /// <summary>
        /// Registers every class in <paramref name="assembly"/> that declares at least one <c>[JsonRpcMethod]</c> with
        /// <paramref name="lifetime"/>; see <see cref="AddJsonRpcService{TService}(IServiceCollection, ServiceLifetime, string)"/>.
        /// </summary>
        public static IServiceCollection AddJsonRpcServicesFromAssembly(this IServiceCollection services, Assembly assembly, ServiceLifetime lifetime, string sessionId = null)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                bool hasRpc = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Any(m => m.IsDefined(typeof(JsonRpcMethodAttribute), false));
                if (!hasRpc) continue;
                AddRegistration(services, type, lifetime, sessionId);
            }
            return services;
        }

        /// <summary>
        /// Maps a POST endpoint that processes JSON-RPC documents. Options default to the registered
        /// <see cref="JsonRpcOptions"/> (see <see cref="AddJsonRpc"/>); pass <paramref name="options"/> to override per endpoint.
        /// An endpoint whose <see cref="JsonRpcOptions.ContextFactory"/> replaces the <see cref="HttpContext"/> must also
        /// carry a <see cref="JsonRpcOptions.ServiceProviderSelector"/> when a scoped or transient service is registered.
        /// </summary>
        public static IEndpointConventionBuilder MapJsonRpc(this IEndpointRouteBuilder endpoints, string pattern = "/jsonrpc", JsonRpcOptions options = null)
        {
            var resolved = options ?? endpoints.ServiceProvider.GetService<IOptions<JsonRpcOptions>>()?.Value ?? new JsonRpcOptions();
            if (resolved.ContextFactory != null && resolved.ServiceProviderSelector == null)
            {
                var registrations = endpoints.ServiceProvider.GetService<IEnumerable<JsonRpcServiceRegistration>>();
                var perRequest = registrations?.FirstOrDefault(r => r.Lifetime != ServiceLifetime.Singleton);
                if (perRequest != null) throw MissingSelector(perRequest.Type);
            }
            if (options != null && options.ServiceProviderSelector != null)
                endpoints.ServiceProvider.GetService<JsonRpcServiceResolver>()?.Add(options.ServiceProviderSelector);
            return resolved.EnableAsyncMethods
                ? endpoints.MapPost(pattern, http => JsonRpcEndpoint.HandleAsynchronousMethodsAsync(http, resolved))
                : endpoints.MapPost(pattern, http => JsonRpcEndpoint.HandleSynchronousMethodsAsync(http, resolved));
        }

        private static void AddCore(IServiceCollection services)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            services.TryAddSingleton(new JsonRpcServiceDescriptors(services));
            services.TryAddSingleton<JsonRpcServiceResolver>();
        }

        private static void AddRegistration(IServiceCollection services, Type type, ServiceLifetime lifetime, string sessionId)
        {
            AddCore(services);
            if (lifetime != ServiceLifetime.Singleton && typeof(JsonRpcService).IsAssignableFrom(type))
                throw new ArgumentException("JSON-RPC service '" + type + "' derives from JsonRpcService, whose constructor binds the instance itself, so it cannot be " + lifetime + ". Use a class that does not derive from JsonRpcService.", nameof(lifetime));
            bool registered = false;
            foreach (var descriptor in services)
            {
                if (descriptor.ServiceType != type) continue;
                if (descriptor.Lifetime != lifetime) throw LifetimeConflict(type, lifetime, descriptor.Lifetime);
                registered = true;
            }
            if (!registered) services.Add(new ServiceDescriptor(type, type, lifetime));
            services.AddSingleton(new JsonRpcServiceRegistration(type, lifetime, sessionId));
        }

        private static InvalidOperationException LifetimeConflict(Type type, ServiceLifetime declared, ServiceLifetime registered)
        {
            return new InvalidOperationException("JSON-RPC service '" + type + "' is registered in the container as " + registered + " but AddJsonRpcService declared it " + declared + ". Pass the container's lifetime to AddJsonRpcService, or register the service once with the lifetime you want.");
        }

        private static InvalidOperationException MissingSelector(Type type)
        {
            return new InvalidOperationException("JSON-RPC service '" + type + "' is scoped or transient and JsonRpcOptions.ContextFactory replaces the HttpContext as the RPC context, so set JsonRpcOptions.ServiceProviderSelector to return the request's IServiceProvider from that context.");
        }

        internal sealed class JsonRpcServiceRegistration
        {
            public JsonRpcServiceRegistration(Type type, ServiceLifetime lifetime, string sessionId) { Type = type; Lifetime = lifetime; SessionId = sessionId; }
            public Type Type { get; }
            public ServiceLifetime Lifetime { get; }
            public string SessionId { get; }
        }

        /// <summary>
        /// The service collection the host was built from, kept so the binder can compare each registration's declared
        /// lifetime with the container's descriptors at startup: a scoped descriptor behind a singleton registration
        /// would otherwise be captured silently at startup and leak.
        /// </summary>
        internal sealed class JsonRpcServiceDescriptors
        {
            private readonly IServiceCollection _services;

            public JsonRpcServiceDescriptors(IServiceCollection services) { _services = services; }

            public void Check(Type type, ServiceLifetime lifetime)
            {
                foreach (var descriptor in _services)
                    if (descriptor.ServiceType == type && descriptor.Lifetime != lifetime) throw LifetimeConflict(type, lifetime, descriptor.Lifetime);
            }
        }

        /// <summary>
        /// Locates the request's <see cref="IServiceProvider"/> from the RPC context and resolves the receiver of a
        /// scoped or transient service from it, once per call. Selectors are tried most recently added first
        /// (per-endpoint options, then <see cref="JsonRpcOptions.ServiceProviderSelector"/> from <see cref="AddJsonRpc"/>),
        /// the built-in one last; the root provider is never a fallback.
        /// </summary>
        internal sealed class JsonRpcServiceResolver
        {
            private Func<object, IServiceProvider>[] _selectors;

            public JsonRpcServiceResolver(IOptions<JsonRpcOptions> options)
            {
                var configured = options?.Value?.ServiceProviderSelector;
                _selectors = configured != null
                    ? new[] { configured, new Func<object, IServiceProvider>(JsonRpcOptions.DefaultServiceProviderSelector) }
                    : new[] { new Func<object, IServiceProvider>(JsonRpcOptions.DefaultServiceProviderSelector) };
            }

            public void Add(Func<object, IServiceProvider> selector)
            {
                if (selector == null) return;
                lock (this)
                {
                    if (Array.IndexOf(_selectors, selector) >= 0) return;
                    var next = new Func<object, IServiceProvider>[_selectors.Length + 1];
                    next[0] = selector;
                    Array.Copy(_selectors, 0, next, 1, _selectors.Length);
                    Volatile.Write(ref _selectors, next);
                }
            }

            public object Resolve(object context, Type serviceType)
            {
                var selectors = Volatile.Read(ref _selectors);
                for (int i = 0; i < selectors.Length; i++)
                {
                    var provider = selectors[i](context);
                    if (provider != null) return provider.GetRequiredService(serviceType);
                }
                throw new InvalidOperationException("JSON-RPC service '" + serviceType + "' is scoped or transient, but no IServiceProvider was found for the RPC context" +
                    (context == null ? " (null)" : " of type '" + context.GetType() + "'") + ". Set JsonRpcOptions.ServiceProviderSelector to locate the request's provider from that context.");
            }
        }

        /// <summary>Resolves registered services from the container and binds them before the host starts accepting requests.</summary>
        internal sealed class JsonRpcBinderHostedService : IHostedService
        {
            private readonly IServiceProvider _provider;
            private readonly IEnumerable<JsonRpcServiceRegistration> _registrations;
            private readonly JsonRpcOptions _options;
            private readonly JsonRpcServiceResolver _resolver;
            private readonly JsonRpcServiceDescriptors _descriptors;

            public JsonRpcBinderHostedService(IServiceProvider provider, IEnumerable<JsonRpcServiceRegistration> registrations, IOptions<JsonRpcOptions> options,
                JsonRpcServiceResolver resolver, JsonRpcServiceDescriptors descriptors)
            {
                _provider = provider;
                _registrations = registrations;
                _options = options.Value;
                _resolver = resolver;
                _descriptors = descriptors;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                foreach (var r in _registrations)
                {
                    _descriptors.Check(r.Type, r.Lifetime);
                    // The effective session: the registration's own, then JsonRpcOptions.SessionId, then the default.
                    var session = r.SessionId ?? _options.SessionId ?? Handler.DefaultSessionId();
                    if (r.Lifetime == ServiceLifetime.Singleton)
                    {
                        // Always bind, whatever the type: attribute binding replaces entries by name, so binding a
                        // JsonRpcService subclass that already bound itself to the default session is harmless, and a
                        // subclass constructed with base(false) is bound nowhere else.
                        ServiceBinder.BindService(session, _provider.GetRequiredService(r.Type));
                        continue;
                    }
                    // Scoped and transient: bind the type; the receiver is resolved per call from the request's provider,
                    // which the selector chain finds from the RPC context. Nothing is resolved or probed here.
                    if (_options.ContextFactory != null && _options.ServiceProviderSelector == null) throw MissingSelector(r.Type);
                    var type = r.Type;
                    var resolver = _resolver;
                    ServiceBinder.BindService(session, type, context => resolver.Resolve(context, type));
                }
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
