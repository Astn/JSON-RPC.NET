using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>A scoped dependency: one per request scope, disposed with it.</summary>
    public sealed class RequestMarker : IDisposable
    {
        public static int Disposed;
        public readonly string Id = Guid.NewGuid().ToString("N");
        public void Dispose() => Interlocked.Increment(ref Disposed);
    }

    /// <summary>A service registered with ServiceLifetime.Scoped: takes the scoped dependency in its constructor, as a DbContext would be.</summary>
    public sealed class ScopedRpcService
    {
        private readonly RequestMarker _marker;
        public ScopedRpcService(RequestMarker marker) { _marker = marker; }

        [JsonRpcMethod("scoped.id")] public string Id() => _marker.Id;
        [JsonRpcMethod("scoped.idAsync")] public async Task<string> IdAsync() { await Task.Yield(); return _marker.Id; }
        [JsonRpcMethod("scoped.context")] public string ContextTypeName() => JsonRpcContext.Current().Value?.GetType().Name;
        [JsonRpcMethod("scoped.static")] public static int Static() => 3;
    }

    /// <summary>A service registered with ServiceLifetime.Transient: a new instance per call.</summary>
    public sealed class TransientRpcService
    {
        private static int _instances;
        private readonly int _instance = Interlocked.Increment(ref _instances);
        [JsonRpcMethod("transient.instance")] public int Instance() => _instance;
    }

    /// <summary>Scoped and transient services over HTTP and a raw connection, synchronous processing.</summary>
    [TestFixture]
    [NonParallelizable]
    public class DiLifetimeTests
    {
        private const string Session = "di-lifetimes";
        private WebApplication _app;
        private HttpClient _http;
        private int _tcpPort;

        [OneTimeSetUp]
        public async Task StartHost()
        {
            _tcpPort = FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, _tcpPort, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
            });
            builder.Services.AddScoped<RequestMarker>();
            builder.Services.AddJsonRpc(o => o.SessionId = Session);
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            builder.Services.AddJsonRpcService<TransientRpcService>(ServiceLifetime.Transient);
            _app = builder.Build();
            _app.MapJsonRpc("/rpc");
            await _app.StartAsync();
            var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            _http = new HttpClient { BaseAddress = new Uri(addresses.First(a => !a.EndsWith(":" + _tcpPort))), Timeout = TimeSpan.FromSeconds(10) };
        }

        [OneTimeTearDown]
        public async Task StopHost()
        {
            _http?.Dispose();
            if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            Handler.DestroySession(Session);
        }

        internal static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<string> PostAsync(string json)
        {
            using var response = await _http.PostAsync("/rpc", new StringContent(json, Encoding.UTF8, "application/json"));
            return await response.Content.ReadAsStringAsync();
        }

        internal static async Task<string> ExchangeAsync(int port, string document, int expectedResponses)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(document));
            var text = new StringBuilder();
            var bytes = new byte[4096];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (CountResponses(text.ToString()) < expectedResponses)
            {
                int count = await stream.ReadAsync(bytes.AsMemory(), cts.Token);
                if (count == 0) break;
                text.Append(Encoding.UTF8.GetString(bytes, 0, count));
            }
            return text.ToString();
        }

        private static int CountResponses(string text)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf("\"jsonrpc\":\"2.0\"", at, StringComparison.Ordinal)) >= 0) { n++; at++; }
            return n;
        }

        internal static void AssertHexId(string value)
        {
            Assert.AreEqual(32, value?.Length, "a marker id: " + value);
            Assert.IsTrue(value.All(c => Uri.IsHexDigit(c)), value);
        }

        [Test]
        public async Task Http_ScopedService_IsResolvedFromTheRequestScope_OncePerRequest()
        {
            var first = JObject.Parse(await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":1}"));
            var second = JObject.Parse(await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":2}"));
            AssertHexId((string)first["result"]);
            AssertHexId((string)second["result"]);
            Assert.AreNotEqual((string)first["result"], (string)second["result"], "each HTTP request has its own scope");
        }

        [Test]
        public async Task Http_Batch_SharesOneScope_AndCreatesATransientPerCall()
        {
            var batch = JArray.Parse(await PostAsync(@"[{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":1},{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":2},{""jsonrpc"":""2.0"",""method"":""transient.instance"",""id"":3},{""jsonrpc"":""2.0"",""method"":""transient.instance"",""id"":4}]"));
            Assert.AreEqual(4, batch.Count);
            Assert.AreEqual((string)batch[0]["result"], (string)batch[1]["result"], "one scope per document");
            Assert.AreNotEqual((int)batch[2]["result"], (int)batch[3]["result"], "a transient per call");
        }

        [Test]
        public async Task Http_ScopedDependency_IsDisposedWithTheRequest()
        {
            int before = Volatile.Read(ref RequestMarker.Disposed);
            AssertHexId((string)JObject.Parse(await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":1}"))["result"]);
            // the request scope is disposed by the pipeline after the response is sent
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Volatile.Read(ref RequestMarker.Disposed) == before && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Greater(Volatile.Read(ref RequestMarker.Disposed), before);
        }

        [Test]
        public async Task Http_ContextIsStillTheHttpContext_AndStaticMethodsNeedNoInstance()
        {
            Assert.AreEqual("DefaultHttpContext", (string)JObject.Parse(await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""scoped.context"",""id"":1}"))["result"]);
            Assert.AreEqual(3, (int)JObject.Parse(await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""scoped.static"",""id"":2}"))["result"]);
        }

        [Test]
        public async Task Tcp_EachDocumentGetsItsOwnScope_DisposedBeforeTheResponseIsFlushed()
        {
            int before = Volatile.Read(ref RequestMarker.Disposed);
            var text = await ExchangeAsync(_tcpPort, "{\"method\":\"scoped.id\",\"id\":1}{\"method\":\"scoped.id\",\"id\":2}", 2);
            var ids = text.Split(new[] { "{\"jsonrpc\"" }, StringSplitOptions.RemoveEmptyEntries).Select(s => (string)JObject.Parse("{\"jsonrpc\"" + s)["result"]).ToArray();
            Assert.AreEqual(2, ids.Length, text);
            AssertHexId(ids[0]);
            AssertHexId(ids[1]);
            Assert.AreNotEqual(ids[0], ids[1], "one scope per document");
            // the document scope is disposed before the connection flushes, so both markers are gone by the time the client reads
            Assert.GreaterOrEqual(Volatile.Read(ref RequestMarker.Disposed), before + 2);
        }

        [Test]
        public async Task Tcp_Batch_SharesTheDocumentScope()
        {
            var text = await ExchangeAsync(_tcpPort, "[{\"method\":\"scoped.id\",\"id\":1},{\"method\":\"scoped.id\",\"id\":2},{\"method\":\"transient.instance\",\"id\":3},{\"method\":\"transient.instance\",\"id\":4}]", 4);
            var batch = JArray.Parse(text);
            Assert.AreEqual((string)batch[0]["result"], (string)batch[1]["result"]);
            Assert.AreNotEqual((int)batch[2]["result"], (int)batch[3]["result"]);
        }

        [Test]
        public async Task Tcp_ContextIsTheConnection()
        {
            var text = await ExchangeAsync(_tcpPort, "{\"method\":\"scoped.context\",\"id\":1}", 1);
            StringAssert.Contains("Connection", (string)JObject.Parse(text)["result"]);
        }
    }

    /// <summary>The same services with EnableAsyncMethods: the document scope lives until the awaited operation is done.</summary>
    [TestFixture]
    [NonParallelizable]
    public class DiLifetimeAsyncTests
    {
        private const string Session = "di-lifetimes-async";
        private WebApplication _app;
        private HttpClient _http;
        private int _tcpPort;

        [OneTimeSetUp]
        public async Task StartHost()
        {
            _tcpPort = DiLifetimeTests.FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, _tcpPort, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
            });
            builder.Services.AddScoped<RequestMarker>();
            builder.Services.AddJsonRpc(o => { o.SessionId = Session; o.EnableAsyncMethods = true; });
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            builder.Services.AddJsonRpcService<TransientRpcService>(ServiceLifetime.Transient);
            _app = builder.Build();
            _app.MapJsonRpc("/rpc");
            await _app.StartAsync();
            var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            _http = new HttpClient { BaseAddress = new Uri(addresses.First(a => !a.EndsWith(":" + _tcpPort))), Timeout = TimeSpan.FromSeconds(10) };
        }

        [OneTimeTearDown]
        public async Task StopHost()
        {
            _http?.Dispose();
            if (_app != null) { await _app.StopAsync(); await _app.DisposeAsync(); }
            Handler.DestroySession(Session);
        }

        [Test]
        public async Task Http_AsyncScopedMethod_ResolvesBeforeTheFirstAwait()
        {
            using var a = await _http.PostAsync("/rpc", new StringContent(@"{""jsonrpc"":""2.0"",""method"":""scoped.idAsync"",""id"":1}", Encoding.UTF8, "application/json"));
            using var b = await _http.PostAsync("/rpc", new StringContent(@"{""jsonrpc"":""2.0"",""method"":""scoped.idAsync"",""id"":2}", Encoding.UTF8, "application/json"));
            var first = (string)JObject.Parse(await a.Content.ReadAsStringAsync())["result"];
            var second = (string)JObject.Parse(await b.Content.ReadAsStringAsync())["result"];
            DiLifetimeTests.AssertHexId(first);
            DiLifetimeTests.AssertHexId(second);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public async Task Tcp_AsyncDocuments_EachHaveAScope_SharedByTheBatch_DisposedAfterTheAwait()
        {
            int before = Volatile.Read(ref RequestMarker.Disposed);
            var text = await DiLifetimeTests.ExchangeAsync(_tcpPort,
                "{\"method\":\"scoped.idAsync\",\"id\":1}[{\"method\":\"scoped.idAsync\",\"id\":2},{\"method\":\"scoped.id\",\"id\":3},{\"method\":\"transient.instance\",\"id\":4},{\"method\":\"transient.instance\",\"id\":5}]", 5);
            int split = text.IndexOf('[');
            var single = (string)JObject.Parse(text.Substring(0, split))["result"];
            var batch = JArray.Parse(text.Substring(split));
            DiLifetimeTests.AssertHexId(single);
            Assert.AreEqual((string)batch[0]["result"], (string)batch[1]["result"], "the batch shares one scope, async and sync calls alike");
            Assert.AreNotEqual(single, (string)batch[0]["result"], "the next document gets a new scope");
            Assert.AreNotEqual((int)batch[2]["result"], (int)batch[3]["result"]);
            Assert.GreaterOrEqual(Volatile.Read(ref RequestMarker.Disposed), before + 2, "both document scopes were disposed before their responses were flushed");
        }
    }

    /// <summary>Registration and startup validation, custom contexts and the selector.</summary>
    [TestFixture]
    [NonParallelizable]
    public class DiLifetimeValidationTests
    {
        public sealed class SelfBindingService : JsonRpcService
        {
            public SelfBindingService() : base(false) { }
            [JsonRpcMethod("validation.self")] public int Self() => 1;
        }

        /// <summary>A custom RPC context produced by ContextFactory.</summary>
        public sealed class Envelope
        {
            public HttpContext Http;
        }

        private static WebApplicationBuilder NewBuilder()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
            builder.Services.AddScoped<RequestMarker>();
            return builder;
        }

        private static async Task<string> PostAsync(WebApplication app, string path, string json)
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
            using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
            return await response.Content.ReadAsStringAsync();
        }

        [Test]
        public void SingletonRegistration_OverAScopedDescriptor_IsRefused()
        {
            var services = new ServiceCollection();
            services.AddScoped<ScopedRpcService>();
            var ex = Assert.Throws<InvalidOperationException>(() => services.AddJsonRpcService<ScopedRpcService>());
            StringAssert.Contains(nameof(ScopedRpcService), ex.Message);
            StringAssert.Contains("Scoped", ex.Message);
        }

        [Test]
        public void MatchingLifetime_ReusesTheExistingDescriptor()
        {
            var services = new ServiceCollection();
            services.AddScoped<ScopedRpcService>();
            services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            Assert.AreEqual(1, services.Count(d => d.ServiceType == typeof(ScopedRpcService)));
            services.AddJsonRpcService<TransientRpcService>(ServiceLifetime.Transient);
            Assert.AreEqual(ServiceLifetime.Transient, services.Single(d => d.ServiceType == typeof(TransientRpcService)).Lifetime);
        }

        [Test]
        public void NonSingletonJsonRpcServiceSubclass_IsRefused()
        {
            var services = new ServiceCollection();
            var ex = Assert.Throws<ArgumentException>(() => services.AddJsonRpcService<SelfBindingService>(ServiceLifetime.Scoped));
            StringAssert.Contains(nameof(SelfBindingService), ex.Message);
            Assert.DoesNotThrow(() => services.AddJsonRpcService<SelfBindingService>(), "as a singleton it is fine");
        }

        [Test]
        public async Task ConflictingRegistrationAddedLater_FailsAtStartup()
        {
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o => o.SessionId = "validation-conflict");
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            builder.Services.AddSingleton<ScopedRpcService>();
            await using var app = builder.Build();
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await app.StartAsync());
            StringAssert.Contains(nameof(ScopedRpcService), ex.Message);
            StringAssert.Contains("Singleton", ex.Message);
            Handler.DestroySession("validation-conflict");
        }

        [Test]
        public async Task ContextFactoryWithoutSelector_AndAScopedService_FailsAtStartup()
        {
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o => { o.SessionId = "validation-nosel"; o.ContextFactory = http => new Envelope { Http = http }; });
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            await using var app = builder.Build();
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await app.StartAsync());
            StringAssert.Contains("ServiceProviderSelector", ex.Message);
            StringAssert.Contains(nameof(ScopedRpcService), ex.Message);
            Handler.DestroySession("validation-nosel");
        }

        [Test]
        public async Task ContextFactoryWithoutSelector_AndOnlySingletons_IsFine()
        {
            const string session = "validation-singletons";
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o => { o.SessionId = session; o.ContextFactory = http => new Envelope { Http = http }; });
            builder.Services.AddJsonRpcService<DiEchoService>();
            await using var app = builder.Build();
            app.MapJsonRpc("/rpc");
            await app.StartAsync();
            try
            {
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"Envelope\",\"id\":1}", await PostAsync(app, "/rpc", @"{""jsonrpc"":""2.0"",""method"":""di.context"",""id"":1}"));
            }
            finally { await app.StopAsync(); Handler.DestroySession(session); }
        }

        [Test]
        public async Task PerEndpointContextFactoryWithoutSelector_FailsWhenMapped()
        {
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o => o.SessionId = "validation-endpoint");
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            await using var app = builder.Build();
            var ex = Assert.Throws<InvalidOperationException>(() => app.MapJsonRpc("/custom", new JsonRpcOptions { SessionId = "validation-endpoint", ContextFactory = http => new Envelope { Http = http } }));
            StringAssert.Contains("ServiceProviderSelector", ex.Message);
            Handler.DestroySession("validation-endpoint");
        }

        [Test]
        public async Task Selector_LocatesTheProviderFromACustomContext_GloballyAndPerEndpoint()
        {
            const string session = "validation-selector";
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o =>
            {
                o.SessionId = session;
                o.ContextFactory = http => new Envelope { Http = http };
                o.ServiceProviderSelector = context => context is Envelope e ? e.Http.RequestServices : null;
            });
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            await using var app = builder.Build();
            app.MapJsonRpc("/rpc");
            // a second endpoint with its own context type and its own selector
            app.MapJsonRpc("/tuple", new JsonRpcOptions
            {
                SessionId = session,
                ContextFactory = http => Tuple.Create(http),
                ServiceProviderSelector = context => context is Tuple<HttpContext> t ? t.Item1.RequestServices : null
            });
            await app.StartAsync();
            try
            {
                var global = JObject.Parse(await PostAsync(app, "/rpc", @"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":1}"));
                DiLifetimeTests.AssertHexId((string)global["result"]);
                Assert.AreEqual("Envelope", (string)JObject.Parse(await PostAsync(app, "/rpc", @"{""jsonrpc"":""2.0"",""method"":""scoped.context"",""id"":2}"))["result"]);

                var endpoint = JObject.Parse(await PostAsync(app, "/tuple", @"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":3}"));
                DiLifetimeTests.AssertHexId((string)endpoint["result"]);
                Assert.AreNotEqual((string)global["result"], (string)endpoint["result"]);
                StringAssert.StartsWith("Tuple", (string)JObject.Parse(await PostAsync(app, "/tuple", @"{""jsonrpc"":""2.0"",""method"":""scoped.context"",""id"":4}"))["result"]);
            }
            finally { await app.StopAsync(); Handler.DestroySession(session); }
        }

        [Test]
        public async Task SelectorFindingNoProvider_IsAnInternalError_NamingTheService_NeverTheRootProvider()
        {
            const string session = "validation-null-selector";
            var builder = NewBuilder();
            builder.Services.AddJsonRpc(o =>
            {
                o.SessionId = session;
                o.ContextFactory = http => new object();
                o.ServiceProviderSelector = context => null;
            });
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            await using var app = builder.Build();
            app.MapJsonRpc("/rpc");
            Exception seen = null;
            Config.SetErrorHandler(session, (request, error) => { seen = error.data as Exception; return error; });
            await app.StartAsync();
            try
            {
                var response = JObject.Parse(await PostAsync(app, "/rpc", @"{""jsonrpc"":""2.0"",""method"":""scoped.id"",""id"":1}"));
                Assert.AreEqual(-32603, (int)response["error"]["code"], response.ToString());
                Assert.AreEqual(JTokenType.Null, response["error"]["data"].Type, "redacted on the wire");
                Assert.IsInstanceOf<InvalidOperationException>(seen);
                StringAssert.Contains(nameof(ScopedRpcService), seen.Message);
                // the static method needs no provider at all
                Assert.AreEqual(3, (int)JObject.Parse(await PostAsync(app, "/rpc", @"{""jsonrpc"":""2.0"",""method"":""scoped.static"",""id"":2}"))["result"]);
            }
            finally { await app.StopAsync(); Handler.DestroySession(session); }
        }

        [Test]
        public async Task RawConnection_WithoutNonSingletonServices_PublishesNoScope()
        {
            // the scope per document is opt-in: with singletons only, the handler leaves the connection's features alone
            const string session = "validation-raw-singleton";
            int port = DiLifetimeTests.FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port, l => l.UseConnectionHandler<JsonRpcConnectionHandler>()));
            builder.Services.AddJsonRpc(o => o.SessionId = session);
            builder.Services.AddJsonRpcService<FeatureProbeService>();
            await using var app = builder.Build();
            await app.StartAsync();
            try
            {
                var text = await DiLifetimeTests.ExchangeAsync(port, "{\"method\":\"probe.hasScope\",\"id\":1}", 1);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":1}", text);
            }
            finally { await app.StopAsync(); Handler.DestroySession(session); }
        }

        [Test]
        public async Task RawConnection_WithAScopedService_PublishesTheScope_OnlyDuringTheDocument()
        {
            const string session = "validation-raw-scoped";
            int port = DiLifetimeTests.FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port, l => l.UseConnectionHandler<JsonRpcConnectionHandler>()));
            builder.Services.AddScoped<RequestMarker>();
            builder.Services.AddJsonRpc(o => o.SessionId = session);
            builder.Services.AddJsonRpcService<FeatureProbeService>();
            builder.Services.AddJsonRpcService<ScopedRpcService>(ServiceLifetime.Scoped);
            await using var app = builder.Build();
            await app.StartAsync();
            try
            {
                var text = await DiLifetimeTests.ExchangeAsync(port, "{\"method\":\"probe.hasScope\",\"id\":1}{\"method\":\"probe.sameScope\",\"id\":2}", 2);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":1}{\"jsonrpc\":\"2.0\",\"result\":false,\"id\":2}", text,
                    "the feature is present during a document, and the next document sees a different scope");
            }
            finally { await app.StopAsync(); Handler.DestroySession(session); }
        }

        public sealed class FeatureProbeService
        {
            private IServiceProvider _last;

            [JsonRpcMethod("probe.hasScope")]
            public bool HasScope()
            {
                var connection = (ConnectionContext)JsonRpcContext.Current().Value;
                _last = connection.Features.Get<Microsoft.AspNetCore.Http.Features.IServiceProvidersFeature>()?.RequestServices;
                return _last != null;
            }

            [JsonRpcMethod("probe.sameScope")]
            public bool SameScope()
            {
                var connection = (ConnectionContext)JsonRpcContext.Current().Value;
                var now = connection.Features.Get<Microsoft.AspNetCore.Http.Features.IServiceProvidersFeature>()?.RequestServices;
                return now == null || ReferenceEquals(now, _last);
            }
        }
    }

    /// <summary>The core seam on its own: ServiceBinder.BindService(sessionId, type, resolve) without any container.</summary>
    [TestFixture]
    public class FactoryBindingTests
    {
        public class TaggedService
        {
            public static int Resolved;
            public static int StaticCalls;
            private readonly string _tag;
            public TaggedService(string tag) { _tag = tag; }

            [JsonRpcMethod("fb.tag")] public string Tag() => _tag;
            [JsonRpcMethod("fb.tagWith")] public string TagWith(string suffix, int times = 1) => _tag + string.Concat(Enumerable.Repeat(suffix, times));
            [JsonRpcMethod("fb.async", ContextFlow = RpcContextFlow.Flow)]
            public async Task<string> TagAsync() { await Task.Yield(); return _tag + ":" + (JsonRpcContext.Current().Value as string); }
            [JsonRpcMethod("fb.static")] public static int Static() { StaticCalls++; return 42; }
        }

        public sealed class DerivedService : TaggedService
        {
            public DerivedService(string tag) : base(tag) { }
            [JsonRpcMethod("fb.derived")] public string Derived() => "derived:" + Tag();
        }

        private string _session;
        [SetUp] public void SetUp() => _session = "factory-" + Guid.NewGuid().ToString("N");
        [TearDown] public void TearDown() { Handler.DestroySession(_session); }

        private string Run(string method, object context, string parameters = null, string serializer = "jsmn")
        {
            var json = "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\"" + (parameters == null ? "" : ",\"params\":" + parameters) + ",\"id\":1}";
            return JsonRpcProcessor.ProcessSync(_session, json, context, SerializerCatalog.Create(serializer));
        }

        [TestCase("jsmn")] [TestCase("newtonsoft")] [TestCase("stj")]
        public void TheResolverGetsTheContext_AndItsInstanceIsInvoked(string serializer)
        {
            ServiceBinder.BindService(_session, typeof(TaggedService), context => new TaggedService((string)context));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"A\",\"id\":1}", Run("fb.tag", "A", serializer: serializer));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"B\",\"id\":1}", Run("fb.tag", "B", serializer: serializer));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"B--\",\"id\":1}", Run("fb.tagWith", "B", "{\"suffix\":\"-\",\"times\":2}", serializer));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"C-\",\"id\":1}", Run("fb.tagWith", "C", "[\"-\"]", serializer), "defaults still apply");
        }

        [Test]
        public void TheResolverRunsOncePerCall_BeforeTheMethod_AndNeverForStaticMethods()
        {
            int resolved = 0;
            ServiceBinder.BindService(_session, typeof(TaggedService), context => { resolved++; return new TaggedService("x"); });
            int staticBefore = TaggedService.StaticCalls;
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":42,\"id\":1}", Run("fb.static", null));
            Assert.AreEqual(0, resolved, "a static method has no receiver");
            Assert.AreEqual(staticBefore + 1, TaggedService.StaticCalls);
            Run("fb.tag", null);
            Assert.AreEqual(1, resolved);
            JsonRpcProcessor.ProcessSync(_session, "[{\"jsonrpc\":\"2.0\",\"method\":\"fb.tag\",\"id\":1},{\"jsonrpc\":\"2.0\",\"method\":\"fb.tag\",\"id\":2},{\"jsonrpc\":\"2.0\",\"method\":\"fb.tag\"}]", null);
            Assert.AreEqual(4, resolved, "once per call, notifications included");
            // an unknown method or a binding error never reaches the resolver
            StringAssert.Contains("-32601", Run("fb.missing", null));
            StringAssert.Contains("-32602", Run("fb.tagWith", null, "[1, \"not an int\"]"));
            Assert.AreEqual(4, resolved);
        }

        [Test]
        public void ANullOrForeignInstance_IsAnInternalError_NamingTheServiceType()
        {
            object answer = null;
            ServiceBinder.BindService(_session, typeof(TaggedService), context => answer);
            Exception seen = null;
            Config.SetErrorHandler(_session, (request, error) => { seen = error.data as Exception; return error; });

            var response = JObject.Parse(Run("fb.tag", null));
            Assert.AreEqual(-32603, (int)response["error"]["code"]);
            Assert.AreEqual(JTokenType.Null, response["error"]["data"].Type);
            Assert.IsInstanceOf<InvalidOperationException>(seen);
            StringAssert.Contains("null", seen.Message);
            StringAssert.Contains(typeof(TaggedService).FullName, seen.Message);

            answer = "not a service";
            seen = null;
            response = JObject.Parse(Run("fb.tag", null));
            Assert.AreEqual(-32603, (int)response["error"]["code"]);
            Assert.IsInstanceOf<InvalidOperationException>(seen);
            StringAssert.Contains("System.String", seen.Message);
            StringAssert.Contains(typeof(TaggedService).FullName, seen.Message);
        }

        [Test]
        public void ADerivedInstance_IsAccepted_AndInheritedMethodsBindOnTheDerivedType()
        {
            ServiceBinder.BindService(_session, typeof(DerivedService), context => new DerivedService((string)context));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"derived:D\",\"id\":1}", Run("fb.derived", "D"));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"D\",\"id\":1}", Run("fb.tag", "D"), "the base class method is bound too");
        }

        [Test]
        public async Task AsyncMethods_ResolveOnTheInvokingThread_BeforeTheFirstAwait()
        {
            int thread = -1;
            ServiceBinder.BindService(_session, typeof(TaggedService), context => { thread = Thread.CurrentThread.ManagedThreadId; return new TaggedService((string)context); });
            int caller = Thread.CurrentThread.ManagedThreadId;
            var response = await JsonRpcProcessor.ProcessAsync(_session, "{\"jsonrpc\":\"2.0\",\"method\":\"fb.async\",\"id\":1}", "ctx");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"ctx:ctx\",\"id\":1}", response, "the receiver saw the context, and so did the method after its await (Flow)");
            Assert.AreEqual(caller, thread, "resolved synchronously on the invoking thread");
        }

        [Test]
        public void Arguments_AreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindService(_session, (Type)null, c => null));
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindService(_session, typeof(TaggedService), null));
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindService(null, typeof(TaggedService), c => null));
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindService(_session, (object)null));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindService(_session, typeof(List<>), c => null));
            var method = typeof(TaggedService).GetMethod(nameof(TaggedService.Tag));
            var ex = Assert.Throws<ArgumentException>(() => AustinHarris.JsonRpc.Invocation.RpcMethod.FromMethod("fb.tag", method, typeof(string), c => null));
            StringAssert.Contains(typeof(TaggedService).FullName, ex.Message);
        }
    }
}
