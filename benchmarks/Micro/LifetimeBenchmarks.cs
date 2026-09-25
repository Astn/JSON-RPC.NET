using System;
using System.Text;
using AustinHarris.JsonRpc.Serialization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace AustinHarris.JsonRpc.Micro
{
    /// <summary>
    /// What a container-managed receiver costs. The same <c>addInt</c> request through an instance-bound
    /// registration (the singleton default), through a resolver that hands back a cached instance (the
    /// per-invocation resolver call alone), and through a resolver that asks the request's
    /// Microsoft.Extensions.DependencyInjection scope for a scoped or a transient service, with the scope created
    /// and disposed around the document as the raw connection handler does. <c>Scope_Only</c> is that scope
    /// without any request. The instance row must stay where <c>DispatchBenchmarks.AddInt</c> is and allocate
    /// nothing; the other rows are the opt-in price of <c>ServiceLifetime.Scoped</c> and <c>Transient</c>.
    /// </summary>
    [MemoryDiagnoser(displayGenColumns: false)]
    public class LifetimeBenchmarks
    {
        private const string Instance = "lifetime-instance";
        private const string Factory = "lifetime-factory";
        private const string Scoped = "lifetime-scoped";
        private const string Transient = "lifetime-transient";

        private PooledByteBufferWriter _out;
        private ReadOnlyMemory<byte> _addInt;
        private Service _cached;
        private ServiceProvider _provider;
        private IServiceScopeFactory _scopes;

        public sealed class Service
        {
            [JsonRpcMethod] private int addInt(int l, int r) => l + r;
        }

        [GlobalSetup]
        public void Setup()
        {
            ServiceBinder.BindService(Instance, new Service());
            _cached = new Service();
            ServiceBinder.BindService(Factory, typeof(Service), context => _cached);

            var services = new ServiceCollection();
            services.AddScoped<Service>();
            services.AddTransient<Service>();
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            _scopes = _provider.GetRequiredService<IServiceScopeFactory>();
            // the RPC context stands in for the request's provider, as HttpContext.RequestServices or the raw
            // connection's IServiceProvidersFeature does in the AspNetCore package
            ServiceBinder.BindService(Scoped, typeof(Service), context => ((IServiceProvider)context).GetRequiredService<Service>());
            ServiceBinder.BindService(Transient, typeof(Service), context => ((IServiceProvider)context).GetRequiredService<Service>());

            _out = new PooledByteBufferWriter(256);
            _addInt = Encoding.UTF8.GetBytes("{\"method\":\"addInt\",\"params\":[1,7],\"id\":2}");
            Expect(AddInt_Instance);
            Expect(AddInt_Factory);
            Expect(AddInt_Scoped);
            Expect(AddInt_Transient);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _out.Dispose();
            _provider.Dispose();
            foreach (var session in new[] { Instance, Factory, Scoped, Transient }) Handler.DestroySession(session);
        }

        [Benchmark(Baseline = true)]
        public int AddInt_Instance() => Run(Instance, null);

        [Benchmark]
        public int AddInt_Factory() => Run(Factory, _cached);

        [Benchmark]
        public int AddInt_Scoped()
        {
            using (var scope = _scopes.CreateScope()) return Run(Scoped, scope.ServiceProvider);
        }

        [Benchmark]
        public int AddInt_Transient()
        {
            using (var scope = _scopes.CreateScope()) return Run(Transient, scope.ServiceProvider);
        }

        [Benchmark]
        public int Scope_Only()
        {
            using (var scope = _scopes.CreateScope()) return scope.ServiceProvider.GetHashCode();
        }

        private int Run(string session, object context)
        {
            var w = _out;
            w.Clear();
            JsonRpcProcessor.Process(session, _addInt, w, context);
            return w.WrittenCount;
        }

        private void Expect(Func<int> row)
        {
            row();
            var actual = _out.ToString();
            const string expected = "{\"jsonrpc\":\"2.0\",\"result\":8,\"id\":2}";
            if (actual != expected) throw new InvalidOperationException("unexpected response: " + actual);
        }
    }
}
