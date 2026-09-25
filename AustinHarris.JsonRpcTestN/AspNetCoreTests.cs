using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>A service built by the container (constructor dependency) rather than deriving from JsonRpcService.</summary>
    public class DiEchoService
    {
        private readonly ILogger<DiEchoService> _log;

        public DiEchoService(ILogger<DiEchoService> log)
        {
            _log = log;
        }

        [JsonRpcMethod("di.echo")]
        public string Echo(string s)
        {
            _log.LogDebug("echo {S}", s);
            return s;
        }

        [JsonRpcMethod("di.context")]
        public string ContextTypeName()
        {
            return JsonRpcContext.Current().Value?.GetType().Name;
        }
    }

    /// <summary>Kestrel end to end: HTTP endpoint (PipeReader in, BodyWriter out) and JSON-RPC over a raw TCP connection.</summary>
    [TestFixture]
    [NonParallelizable]
    public class AspNetCoreTests
    {
        private WebApplication _app;
        private HttpClient _http;
        private int _tcpPort;

        [OneTimeSetUp]
        public async Task StartHost()
        {
            _ = new CalculatorService(); // binds the shared test service to the default session (idempotent)

            _tcpPort = FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, _tcpPort, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
            });
            builder.Services.AddJsonRpc();
            builder.Services.AddJsonRpcService<DiEchoService>();

            _app = builder.Build();
            _app.MapJsonRpc("/rpc");
            await _app.StartAsync();

            var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            var httpAddress = addresses.First(a => !a.EndsWith(":" + _tcpPort));
            _http = new HttpClient { BaseAddress = new Uri(httpAddress) };
        }

        [OneTimeTearDown]
        public async Task StopHost()
        {
            _http?.Dispose();
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private async Task<HttpResponseMessage> PostAsync(string json)
        {
            return await _http.PostAsync("/rpc", new StringContent(json, Encoding.UTF8, "application/json"));
        }

        [Test]
        public async Task Http_SingleRequest_IsAnsweredAsJson()
        {
            var response = await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""IntToInt"",""params"":[5],""id"":1}");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json", response.Content.Headers.ContentType.MediaType);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":5,\"id\":1}", await response.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Http_Notification_Is204()
        {
            var response = await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""Notify"",""params"":[""hi""]}");
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual(string.Empty, await response.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Http_Batch_IsAnsweredAsArray()
        {
            var response = await PostAsync(@"[{""jsonrpc"":""2.0"",""method"":""IntToInt"",""params"":[1],""id"":1},{""jsonrpc"":""2.0"",""method"":""IntToInt"",""params"":[2],""id"":2}]");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("[{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1},{\"jsonrpc\":\"2.0\",\"result\":2,\"id\":2}]", await response.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Http_ParseError_IsReportedInBody()
        {
            var response = await PostAsync("{not json");
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            StringAssert.Contains("-32700", await response.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Http_Get_IsMethodNotAllowed()
        {
            var response = await _http.GetAsync("/rpc");
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }

        [Test]
        public async Task Http_DiService_IsBoundAndSeesHttpContext()
        {
            var echo = await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""di.echo"",""params"":[""abc""],""id"":7}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"abc\",\"id\":7}", await echo.Content.ReadAsStringAsync());

            var ctx = await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""di.context"",""id"":8}");
            StringAssert.Contains("HttpContext", await ctx.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Http_LargeBody_Is413()
        {
            var big = "{\"jsonrpc\":\"2.0\",\"method\":\"internal.echo\",\"params\":[\"" + new string('x', 5 * 1024 * 1024) + "\"],\"id\":1}";
            var response = await PostAsync(big);
            Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        }

        [Test]
        public async Task Tcp_TwoDocumentsInOneWrite_AreAnsweredInOrder()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _tcpPort);
            using var stream = client.GetStream();
            var payload = Encoding.UTF8.GetBytes("{\"method\":\"IntToInt\",\"params\":[1],\"id\":1}\n{\"method\":\"IntToInt\",\"params\":[2],\"id\":2}\n");
            await stream.WriteAsync(payload, 0, payload.Length);

            var text = await ReadUntilAsync(stream, "\"id\":2}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}{\"jsonrpc\":\"2.0\",\"result\":2,\"id\":2}", text);
        }

        [Test]
        public async Task Tcp_DocumentSplitAcrossWrites_IsReassembled()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _tcpPort);
            using var stream = client.GetStream();
            var first = Encoding.UTF8.GetBytes("{\"method\":\"internal.echo\",\"params\":[\"sp");
            var second = Encoding.UTF8.GetBytes("lit\"],\"id\":3}");
            await stream.WriteAsync(first, 0, first.Length);
            await stream.FlushAsync();
            await Task.Delay(100);
            await stream.WriteAsync(second, 0, second.Length);

            var text = await ReadUntilAsync(stream, "\"id\":3}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"split\",\"id\":3}", text);
        }

        // ------------------------------------------------------------------ DI binding honours JsonRpcOptions.SessionId

        /// <summary>A JsonRpcService subclass built by DI: its base constructor binds it to the default session, and the host binds it to the configured one as well.</summary>
        public class TenantAutoService : JsonRpcService
        {
            [JsonRpcMethod("tenant.ping")]
            public int Ping() => 7;
        }

        /// <summary>A plain DI service in the same host, bound to the configured session by the binder.</summary>
        public class TenantPlainService
        {
            [JsonRpcMethod("tenant.echo")]
            public string Echo(string s) => s;
        }

        [Test]
        public async Task Http_JsonRpcServiceSubclass_IsBoundToTheConfiguredSession()
        {
            const string session = "aspnetcore-tenant";
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
            builder.Services.AddJsonRpc(o => o.SessionId = session);
            builder.Services.AddJsonRpcService<TenantAutoService>();
            builder.Services.AddJsonRpcService<TenantPlainService>();

            var app = builder.Build();
            app.MapJsonRpc("/rpc");
            await app.StartAsync();
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
                using var http = new HttpClient { BaseAddress = new Uri(address) };

                // the subclass answers in the configured session (it used to be skipped by the binder: -32601)
                var ping = await http.PostAsync("/rpc", new StringContent(@"{""jsonrpc"":""2.0"",""method"":""tenant.ping"",""id"":1}", Encoding.UTF8, "application/json"));
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}", await ping.Content.ReadAsStringAsync());

                var echo = await http.PostAsync("/rpc", new StringContent(@"{""jsonrpc"":""2.0"",""method"":""tenant.echo"",""params"":[""t""],""id"":2}", Encoding.UTF8, "application/json"));
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"t\",\"id\":2}", await echo.Content.ReadAsStringAsync());

                // both live in the configured session's method table
                var handler = Handler.GetSessionHandler(session);
                Assert.IsTrue(handler.MetaData.Services.ContainsKey("tenant.ping"));
                Assert.IsTrue(handler.MetaData.Services.ContainsKey("tenant.echo"));

                // the plain service is not exposed in the default session
                var defaultResponse = await PostAsync(@"{""jsonrpc"":""2.0"",""method"":""tenant.echo"",""params"":[""t""],""id"":3}");
                StringAssert.Contains("-32601", await defaultResponse.Content.ReadAsStringAsync());
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
                Handler.DestroySession(session);
            }
        }

        /// <summary>A JsonRpcService subclass that does not bind itself: the host is its only binder.</summary>
        public class TenantUnboundService : JsonRpcService
        {
            public TenantUnboundService() : base(false) { }

            [JsonRpcMethod("tenant.unbound")]
            public int Unbound() => 9;
        }

        [Test]
        public async Task Http_JsonRpcServiceSubclass_WithoutAutoBind_IsBoundOnlyToTheConfiguredSession()
        {
            const string session = "aspnetcore-tenant-unbound";
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
            builder.Services.AddJsonRpc(o => o.SessionId = session);
            builder.Services.AddJsonRpcService<TenantUnboundService>();

            var app = builder.Build();
            app.MapJsonRpc("/rpc");
            await app.StartAsync();
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
                using var http = new HttpClient { BaseAddress = new Uri(address) };
                var response = await http.PostAsync("/rpc", new StringContent(@"{""jsonrpc"":""2.0"",""method"":""tenant.unbound"",""id"":1}", Encoding.UTF8, "application/json"));
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":9,\"id\":1}", await response.Content.ReadAsStringAsync());

                Assert.IsTrue(Handler.GetSessionHandler(session).MetaData.Services.ContainsKey("tenant.unbound"));
                Assert.IsFalse(Handler.DefaultHandler.MetaData.Services.ContainsKey("tenant.unbound"), "base(false) keeps it off the default session");
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
                Handler.DestroySession(session);
            }
        }

        private static async Task<string> ReadUntilAsync(NetworkStream stream, string terminator)
        {
            var sb = new StringBuilder();
            var buffer = new byte[4096];
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                if (await Task.WhenAny(readTask, Task.Delay(5000)) != readTask) break;
                int n = await readTask;
                if (n == 0) break;
                sb.Append(Encoding.UTF8.GetString(buffer, 0, n));
                if (sb.ToString().EndsWith(terminator)) break;
            }
            return sb.ToString();
        }
    }

    [TestFixture]
    [NonParallelizable]
    public sealed class AsyncAspNetCoreTests
    {
        private WebApplication _app;
        private HttpClient _http;
        private int _port;
        private string _session;
        private AsyncHostService _service;

        public sealed class AsyncHostService
        {
            internal TaskCompletionSource<int> Gate;
            internal TaskCompletionSource<int> Started;
            internal TaskCompletionSource<int> Canceled;
            [JsonRpcMethod("fast")] public Task<int> Fast(int value) => Task.FromResult(value);
            [JsonRpcMethod("slow")]
            public async Task<int> Slow([JsonRpcCancellation] CancellationToken token)
            {
                Started.TrySetResult(1);
                return await Gate.Task.WaitAsync(token);
            }
            [JsonRpcMethod("disconnect")]
            public async Task<int> Disconnect([JsonRpcCancellation] CancellationToken token)
            {
                Started.TrySetResult(1);
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { Canceled.TrySetResult(1); throw; }
                return 1;
            }
        }

        private static TaskCompletionSource<int> NewGate() => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        [SetUp]
        public async Task StartAsyncHost()
        {
            _session = "async-host-" + Guid.NewGuid().ToString("N");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); _port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Listen(IPAddress.Loopback, 0);
                k.Listen(IPAddress.Loopback, _port, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
            });
            _service = new AsyncHostService { Gate = NewGate(), Started = NewGate(), Canceled = NewGate() };
            builder.Services.AddSingleton(_service);
            builder.Services.AddJsonRpc(o => { o.SessionId = _session; o.EnableAsyncMethods = true; });
            builder.Services.AddJsonRpcService<AsyncHostService>();
            _app = builder.Build();
            _app.MapJsonRpc("/rpc");
            _app.MapJsonRpc("/empty200", new JsonRpcOptions { SessionId = _session, EnableAsyncMethods = true, NoContentForNotifications = false });
            await _app.StartAsync();
            var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First(a => !a.EndsWith(":" + _port));
            _http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
        }

        [TearDown]
        public async Task StopAsyncHost()
        {
            _service.Gate.TrySetResult(7);
            _http?.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
            Handler.DestroySession(_session);
        }

        private Task<HttpResponseMessage> Post(string json, string path = "/rpc", CancellationToken token = default) => _http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"), token);

        [Test]
        public async Task Http_AwaitsSuspendedDiMethod()
        {
            var pending = Post("{\"method\":\"slow\",\"id\":1}");
            await _service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(pending.IsCompleted);
            _service.Gate.SetResult(7);
            using var response = await pending;
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(7, (int)JObject.Parse(await response.Content.ReadAsStringAsync())["result"]);
        }

        [TestCase("/rpc", HttpStatusCode.NoContent)]
        [TestCase("/empty200", HttpStatusCode.OK)]
        public async Task Http_NotificationsKeepStatusAndAreAwaited(string path, HttpStatusCode status)
        {
            var pending = Post("{\"method\":\"slow\"}", path);
            await _service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(pending.IsCompleted);
            _service.Gate.SetResult(7);
            using var response = await pending;
            Assert.AreEqual(status, response.StatusCode);
            Assert.AreEqual("", await response.Content.ReadAsStringAsync());
        }

        [Test]
        public async Task Tcp_FlushesCompletedRepliesBeforeSlowDocument_AndKeepsOrder()
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"method\":\"fast\",\"params\":[1],\"id\":1}{\"method\":\"fast\",\"params\":[2],\"id\":2}{\"method\":\"slow\",\"id\":3}{\"method\":\"fast\",\"params\":[4],\"id\":4}"));
            await _service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var early = await ReadThrough(stream, "\"id\":2}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}{\"jsonrpc\":\"2.0\",\"result\":2,\"id\":2}", early);
            Assert.IsFalse(_service.Gate.Task.IsCompleted);
            _service.Gate.SetResult(7);
            var later = await ReadThrough(stream, "\"id\":4}");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":3}{\"jsonrpc\":\"2.0\",\"result\":4,\"id\":4}", later);
        }

        [TestCase(false)] [TestCase(true)]
        public async Task Disconnect_CancelsInvocation_AndHostRemainsAvailable(bool tcp)
        {
            if (tcp)
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _port);
                await client.GetStream().WriteAsync(Encoding.UTF8.GetBytes("{\"method\":\"disconnect\",\"id\":1}"));
                await _service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                client.Client.LingerState = new LingerOption(true, 0);
                client.Close();
            }
            else
            {
                using var cts = new CancellationTokenSource();
                var pending = Post("{\"method\":\"disconnect\",\"id\":1}", token: cts.Token);
                await _service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                cts.Cancel();
                try { await pending; Assert.Fail("expected client cancellation"); } catch (OperationCanceledException) { }
            }
            await _service.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var response = await Post("{\"method\":\"fast\",\"params\":[7],\"id\":2}");
            Assert.AreEqual(7, (int)JObject.Parse(await response.Content.ReadAsStringAsync())["result"]);
        }

        public sealed class InvalidAsyncDiService
        {
            [JsonRpcMethod] public async void Invalid() => await Task.Yield();
        }

        [Test]
        public async Task DiRegistration_RejectsAsyncVoid()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddJsonRpc(o => o.SessionId = _session);
            builder.Services.AddJsonRpcService<InvalidAsyncDiService>();
            await using var app = builder.Build();
            Assert.ThrowsAsync<NotSupportedException>(async () => await app.StartAsync());
        }

        private static async Task<string> ReadThrough(NetworkStream stream, string terminator)
        {
            var text = new StringBuilder();
            var bytes = new byte[4096];
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!text.ToString().EndsWith(terminator, StringComparison.Ordinal))
            {
                int count = await stream.ReadAsync(bytes.AsMemory(), cts.Token);
                if (count == 0) break;
                text.Append(Encoding.UTF8.GetString(bytes, 0, count));
            }
            return text.ToString();
        }
    }

}
