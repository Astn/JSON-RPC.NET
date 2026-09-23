using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;
using SjrMethod = StreamJsonRpc.JsonRpcMethodAttribute;

namespace TestServer_Console;

/// <summary>
/// The same five requests through JSON-RPC.Net and through StreamJsonRpc (Microsoft's JSON-RPC library, the one
/// behind Visual Studio and the language-server stack), on the same Kestrel TCP listener with the same
/// pipelining client, so the only variable is the RPC library. Every request carries <c>"jsonrpc":"2.0"</c>
/// because StreamJsonRpc requires it. StreamJsonRpc has no "document in, document out" call, so its in-process
/// row runs over a pair of <see cref="Pipe"/>s, the closest thing it has to a direct call; a sequential
/// proxy row shows what a typical <c>await proxy.AddAsync(1, 2)</c> costs end to end. gRPC for .NET answers the
/// same five calls on an HTTP/2 listener of the same Kestrel (<see cref="GrpcCompare"/>), unary and streamed.
/// </summary>
internal static class CompareBenchmark
{
    private enum Framing { NewLine, Header }
    private enum Formatter { SystemTextJson, Newtonsoft }

    private static readonly string[] Requests = BenchmarkRunner.taskInputs.Select(t => "{\"jsonrpc\":\"2.0\"," + t.Substring(1)).ToArray();
    private static readonly byte[] JsonRpcPrefix = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",");

    /// <summary>Warm-up per row before the timed run. At 0.5 s the StreamJsonRpc rows read 15 to 50 % low (tiered JIT); 2 s settles them. The gRPC rows did not move between 0.5 and 2 s.</summary>
    private const double Warm = 2;

    /// <summary>StreamJsonRpc target with the same five methods as <see cref="CalculatorService"/>.</summary>
    public class Target
    {
        [SjrMethod("add")] public double Add(double l, double r) => l + r;
        [SjrMethod("addInt")] public int AddInt(int l, int r) => l + r;
        [SjrMethod("NullableFloatToNullableFloat")] public float? NullableFloatToNullableFloat(float? a) => a;
        [SjrMethod("Test2")] public decimal? Test2(decimal x) => x;
        [SjrMethod("StringMe")] public string StringMe(string x) => x;
    }

    /// <summary>Client proxy for the sequential round-trip row.</summary>
    public interface ICalculator
    {
        [SjrMethod("add")] Task<double> AddAsync(double l, double r);
        [SjrMethod("addInt")] Task<int> AddIntAsync(int l, int r);
        [SjrMethod("NullableFloatToNullableFloat")] Task<float?> NullableFloatToNullableFloatAsync(float? a);
        [SjrMethod("Test2")] Task<decimal?> Test2Async(decimal x);
        [SjrMethod("StringMe")] Task<string> StringMeAsync(string x);
    }

    internal static async Task RunAsync(Action<string> print, double seconds = 3, int clients = 0, int pipeline = 256)
    {
        print ??= Console.WriteLine;
        if (clients <= 0) clients = Environment.ProcessorCount;

        var raw = Requests.Select(r => Encoding.UTF8.GetBytes(r)).ToArray();
        var newLine = Requests.Select(r => Encoding.UTF8.GetBytes(r + "\n")).ToArray();
        var header = Requests.Select(r => Encoding.UTF8.GetBytes("Content-Length: " + Encoding.UTF8.GetByteCount(r) + "\r\n\r\n" + r)).ToArray();

        await using var h = await StartAsync(pipeline);
        int oursPort = h.OursPort, sjrNewLineStjPort = h.SjrNewLineStjPort, sjrHeaderStjPort = h.SjrHeaderStjPort, sjrHeaderNewtonsoftPort = h.SjrHeaderNewtonsoftPort, grpcPort = h.GrpcPort;

        {
            print($"{Versions()}; Kestrel on loopback, {clients} clients, pipeline {pipeline}, {seconds:0.#} s per row\n");
            var rows = new List<BenchmarkRunner.ChartRow>();
            var session = Handler.DefaultSessionId();

            // ---- in-process floors
            var memInputs = raw.Select(i => (ReadOnlyMemory<byte>)i).ToArray();
            BenchmarkRunner.RunSync(session, memInputs, Config.Serializer, 1, Warm, out _);
            var elapsed = BenchmarkRunner.RunSync(session, memInputs, Config.Serializer, 1, seconds, out long total);
            rows.Add(new BenchmarkRunner.ChartRow("JSON-RPC.Net in-process, 1 thread", total / elapsed, $"{total,12:N0} RPCs  direct call, bytes in, bytes out"));
            print($"  JSON-RPC.Net in-process done ({rows[^1].RpcPerSec:N0} RPC/s)");

            PipeRun(newLine, Warm, pipeline);
            var (count, secs) = PipeRun(newLine, seconds, pipeline);
            rows.Add(new BenchmarkRunner.ChartRow("StreamJsonRpc in-process, 1 client", count / secs, $"{count,12:N0} RPCs  Pipe pair, newline framing, STJ formatter, {pipeline} pipelined"));
            print($"  StreamJsonRpc in-process done ({count / secs:N0} RPC/s)");

            (count, secs) = await ProxyRun(Warm);
            (count, secs) = await ProxyRun(seconds);
            rows.Add(new BenchmarkRunner.ChartRow("StreamJsonRpc proxy, sequential await", count / secs, $"{count,12:N0} RPCs  {secs / count * 1e6:N1} us per round trip"));
            print($"  StreamJsonRpc proxy done ({count / secs:N0} RPC/s)");

            // ---- Kestrel TCP, same client for every row
            foreach (var (label, port, inputs, prefix) in new[]
            {
                ("JSON-RPC.Net TCP, raw documents", oursPort, raw, JsonRpcPrefix),
                ("StreamJsonRpc TCP, newline + STJ", sjrNewLineStjPort, newLine, JsonRpcPrefix),
                ("StreamJsonRpc TCP, Content-Length + STJ", sjrHeaderStjPort, header, (byte[])null),
                ("StreamJsonRpc TCP, Content-Length + Json.NET", sjrHeaderNewtonsoftPort, header, (byte[])null),
            })
            {
                Probe(port, inputs, label);
                KestrelBenchmark.TcpRun(port, inputs, clients, Warm, pipeline, prefix);
                (count, secs) = KestrelBenchmark.TcpRun(port, inputs, clients, seconds, pipeline, prefix);
                rows.Add(new BenchmarkRunner.ChartRow(label, count / secs, $"{count,12:N0} RPCs"));
                print($"  {label} done ({count / secs:N0} RPC/s)");
            }

            // ---- gRPC for .NET on the same Kestrel: HTTP/2 + protobuf, one channel per client
            await GrpcCompare.UnaryRun(grpcPort, clients, pipeline, Warm);
            (count, secs) = await GrpcCompare.UnaryRun(grpcPort, clients, pipeline, seconds);
            rows.Add(new BenchmarkRunner.ChartRow("gRPC for .NET unary, HTTP/2", count / secs, $"{count,12:N0} RPCs  {clients} channels, {pipeline} calls in flight each"));
            print($"  gRPC unary done ({count / secs:N0} RPC/s)");

            await GrpcCompare.StreamRun(grpcPort, clients, pipeline, Warm);
            (count, secs) = await GrpcCompare.StreamRun(grpcPort, clients, pipeline, seconds);
            rows.Add(new BenchmarkRunner.ChartRow("gRPC for .NET bidirectional stream", count / secs, $"{count,12:N0} RPCs  {clients} streams, {pipeline} calls in flight each"));
            print($"  gRPC stream done ({count / secs:N0} RPC/s)");

            BenchmarkRunner.PrintBarChart("JSON-RPC.Net vs StreamJsonRpc vs gRPC - RPC/s", "Library / transport", rows);
        }
    }

    /// <summary>
    /// Every library and transport at 1, 2, 4, 8 and 16 client connections: the data for the README's
    /// multi-series chart. Same host, same five calls, same pipeline depth per connection as
    /// <see cref="RunAsync"/>. Prints a table and, when <paramref name="outputPath"/> is given, writes the
    /// numbers as JSON for benchmarks/charts/render.py.
    /// </summary>
    internal static async Task SweepAsync(Action<string> print, double seconds = 2, string outputPath = null, int pipeline = 256)
    {
        print ??= Console.WriteLine;
        var raw = Requests.Select(r => Encoding.UTF8.GetBytes(r)).ToArray();
        var newLine = Requests.Select(r => Encoding.UTF8.GetBytes(r + "\n")).ToArray();
        var header = Requests.Select(r => Encoding.UTF8.GetBytes("Content-Length: " + Encoding.UTF8.GetByteCount(r) + "\r\n\r\n" + r)).ToArray();
        var batch = new[] { KestrelBenchmark.BuildBatch(raw, 100) };

        await using var h = await StartAsync(pipeline);
        int[] connections = { 1, 2, 4, 8, 16 };
        (string name, string kind, Func<int, double, Task<(long count, double seconds)>> run)[] series =
        {
            ("JSON-RPC.Net, TCP", "ours", (c, s) => Task.FromResult(KestrelBenchmark.TcpRun(h.OursPort, raw, c, s, pipeline, JsonRpcPrefix))),
            ("JSON-RPC.Net, HTTP, batch of 100 per POST", "ours", (c, s) => KestrelBenchmark.HttpRun(h.HttpUrl, batch, 100, c, s)),
            ("JSON-RPC.Net, HTTP, 1 request per POST", "ours", (c, s) => KestrelBenchmark.HttpRun(h.HttpUrl, raw, 1, c, s)),
            ("StreamJsonRpc, TCP, newline + System.Text.Json", "theirs", (c, s) => Task.FromResult(KestrelBenchmark.TcpRun(h.SjrNewLineStjPort, newLine, c, s, pipeline, JsonRpcPrefix))),
            ("StreamJsonRpc, TCP, Content-Length + System.Text.Json", "theirs", (c, s) => Task.FromResult(KestrelBenchmark.TcpRun(h.SjrHeaderStjPort, header, c, s, pipeline, null))),
            ("StreamJsonRpc, TCP, Content-Length + Json.NET", "theirs", (c, s) => Task.FromResult(KestrelBenchmark.TcpRun(h.SjrHeaderNewtonsoftPort, header, c, s, pipeline, null))),
            ("gRPC for .NET, unary", "grpc", (c, s) => GrpcCompare.UnaryRun(h.GrpcPort, c, pipeline, s)),
            ("gRPC for .NET, bidirectional stream", "grpc", (c, s) => GrpcCompare.StreamRun(h.GrpcPort, c, pipeline, s)),
        };
        Probe(h.OursPort, raw, series[0].name);
        Probe(h.SjrNewLineStjPort, newLine, series[3].name);
        Probe(h.SjrHeaderStjPort, header, series[4].name);
        Probe(h.SjrHeaderNewtonsoftPort, header, series[5].name);

        print($"{Versions()}; Kestrel on loopback, pipeline {pipeline}, {seconds:0.#} s per cell\n");
        var results = new double[series.Length, connections.Length];
        for (int ci = 0; ci < connections.Length; ci++)
        {
            for (int si = 0; si < series.Length; si++)
            {
                await series[si].run(connections[ci], Warm);
                var (count, secs) = await series[si].run(connections[ci], seconds);
                results[si, ci] = count / secs;
                print($"  {connections[ci],2} connections  {series[si].name,-55} {results[si, ci],14:N0} RPC/s");
            }
        }

        print("\nRPC/s by connections:");
        print("  " + "series".PadRight(56) + string.Join("", connections.Select(c => $"{c,14}")));
        for (int si = 0; si < series.Length; si++)
            print("  " + series[si].name.PadRight(56) + string.Join("", connections.Select((_, ci) => $"{results[si, ci],14:N0}")));

        if (outputPath != null)
        {
            var doc = new
            {
                machine = "AMD Ryzen 7 7800X3D, 8 cores / 16 threads, Windows 11, .NET 10, Release, Server GC",
                date = DateTime.Now.ToString("yyyy-MM-dd"),
                secondsPerCell = seconds,
                pipeline,
                connections,
                series = series.Select((s, si) => new { s.name, s.kind, rpcPerSec = connections.Select((_, ci) => Math.Round(results[si, ci])).ToArray() }).ToArray(),
            };
            File.WriteAllText(outputPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            print($"\nwrote {outputPath}");
        }
    }

    private static string Versions() =>
        $"JSON-RPC.Net {typeof(JsonRpcProcessor).Assembly.GetName().Version} vs StreamJsonRpc {PackageVersion(typeof(JsonRpc))} vs gRPC for .NET {PackageVersion(typeof(Grpc.Net.Client.GrpcChannel))}";

    /// <summary>One Kestrel with a listener per library and setting; disposing it stops the host.</summary>
    private sealed class Hosted : IAsyncDisposable
    {
        public WebApplication App;
        public int OursPort, SjrNewLineStjPort, SjrHeaderStjPort, SjrHeaderNewtonsoftPort, GrpcPort, HttpPort;
        public string HttpUrl => $"http://127.0.0.1:{HttpPort}/rpc";
        public async ValueTask DisposeAsync() { await App.StopAsync(); await App.DisposeAsync(); }
    }

    private static async Task<Hosted> StartAsync(int pipeline)
    {
        var h = new Hosted
        {
            OursPort = KestrelBenchmark.FreePort(), SjrNewLineStjPort = KestrelBenchmark.FreePort(), SjrHeaderStjPort = KestrelBenchmark.FreePort(),
            SjrHeaderNewtonsoftPort = KestrelBenchmark.FreePort(), GrpcPort = KestrelBenchmark.FreePort(), HttpPort = KestrelBenchmark.FreePort(),
        };
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, h.HttpPort);
            k.Listen(IPAddress.Loopback, h.OursPort, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
            k.Listen(IPAddress.Loopback, h.SjrNewLineStjPort, l => l.Run(c => ServeStreamJsonRpc(c, Framing.NewLine, Formatter.SystemTextJson)));
            k.Listen(IPAddress.Loopback, h.SjrHeaderStjPort, l => l.Run(c => ServeStreamJsonRpc(c, Framing.Header, Formatter.SystemTextJson)));
            k.Listen(IPAddress.Loopback, h.SjrHeaderNewtonsoftPort, l => l.Run(c => ServeStreamJsonRpc(c, Framing.Header, Formatter.Newtonsoft)));
            k.Listen(IPAddress.Loopback, h.GrpcPort, l => l.Protocols = HttpProtocols.Http2);
            k.Limits.Http2.MaxStreamsPerConnection = pipeline;   // Kestrel's default of 100 would cap the gRPC rows below the pipeline depth
        });
        builder.Services.AddJsonRpc();
        builder.Services.AddGrpc();
        var app = builder.Build();
        app.MapJsonRpc("/rpc");
        app.MapGrpcService<GrpcCompare.CalculatorGrpc>();
        await app.StartAsync();
        h.App = app;
        return h;
    }

    /// <summary>The NuGet package version of an assembly (its informational version without the commit suffix).</summary>
    private static string PackageVersion(Type t)
    {
        var info = t.Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
        var text = info.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)info[0]).InformationalVersion : t.Assembly.GetName().Version.ToString();
        int plus = text.IndexOf('+');
        return plus > 0 ? text.Substring(0, plus) : text;
    }

    private static IJsonRpcMessageHandler CreateHandler(PipeWriter writer, PipeReader reader, Framing framing, Formatter formatter)
    {
        IJsonRpcMessageTextFormatter f = formatter == Formatter.SystemTextJson ? new SystemTextJsonFormatter() : new JsonMessageFormatter();
        return framing == Framing.NewLine
            ? new NewLineDelimitedMessageHandler(writer, reader, f)
            : new HeaderDelimitedMessageHandler(writer, reader, f);
    }

    private static async Task ServeStreamJsonRpc(ConnectionContext connection, Framing framing, Formatter formatter)
    {
        using var rpc = new JsonRpc(CreateHandler(connection.Transport.Output, connection.Transport.Input, framing, formatter));
        rpc.AddLocalRpcTarget(new Target());
        rpc.StartListening();
        try { await rpc.Completion; }
        catch (Exception) { /* the client hung up */ }
    }

    /// <summary>Sends each request once and checks every response is a result; a wrong method name or a formatter quirk would otherwise inflate a row.</summary>
    private static void Probe(int port, byte[][] inputs, string label)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true, ReceiveTimeout = 5000 };
        socket.Connect(IPAddress.Loopback, port);
        foreach (var doc in inputs) socket.Send(doc);
        var counter = new KestrelBenchmark.ResponseCounter(null);
        var received = new List<byte>();
        var buffer = new byte[16 * 1024];
        int docs = 0;
        while (docs < inputs.Length)
        {
            int n = socket.Receive(buffer);
            if (n == 0) break;
            received.AddRange(buffer.AsSpan(0, n).ToArray());
            docs += counter.Count(buffer.AsSpan(0, n));
        }
        var text = Encoding.UTF8.GetString(received.ToArray());
        if (docs != inputs.Length || text.Contains("\"error\"") || !text.Contains("\"result\""))
            throw new InvalidOperationException($"{label}: unexpected responses:\n{text}");
    }

    /// <summary>
    /// StreamJsonRpc served over an in-process <see cref="Pipe"/> pair, driven exactly like the TCP client: a ring
    /// of newline-framed requests, a cursor, and refills whenever the pipeline has room.
    /// </summary>
    private static (long count, double seconds) PipeRun(byte[][] inputs, double seconds, int pipeline)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var rpc = new JsonRpc(CreateHandler(toClient.Writer, toServer.Reader, Framing.NewLine, Formatter.SystemTextJson));
        rpc.AddLocalRpcTarget(new Target());
        rpc.StartListening();

        const int ringCount = 4096;
        var offsets = new int[ringCount + pipeline + 1];
        var ring = KestrelBenchmark.BuildRing(inputs, ringCount, pipeline, offsets);
        var counter = new KestrelBenchmark.ResponseCounter(JsonRpcPrefix);
        int cursor = 0, inFlight = 0;
        long received = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            int free = pipeline - inFlight;
            if (free > 0)
            {
                toServer.Writer.WriteAsync(new ReadOnlyMemory<byte>(ring, offsets[cursor], offsets[cursor + free] - offsets[cursor])).AsTask().GetAwaiter().GetResult();
                cursor += free;
                if (cursor >= ringCount) cursor -= ringCount;
                inFlight += free;
            }
            var result = toClient.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
            foreach (var segment in result.Buffer)
            {
                int docs = counter.Count(segment.Span);
                received += docs;
                inFlight -= docs;
            }
            toClient.Reader.AdvanceTo(result.Buffer.End);
            if (result.IsCompleted) break;
        }
        // Drain so the server is idle before the next row.
        while (inFlight > 0)
        {
            var result = toClient.Reader.ReadAsync().AsTask().GetAwaiter().GetResult();
            foreach (var segment in result.Buffer)
            {
                int docs = counter.Count(segment.Span);
                received += docs;
                inFlight -= docs;
            }
            toClient.Reader.AdvanceTo(result.Buffer.End);
            if (result.IsCompleted) break;
        }
        sw.Stop();
        toServer.Writer.Complete();
        return (received, sw.Elapsed.TotalSeconds);
    }

    /// <summary>A typed StreamJsonRpc proxy awaiting one call at a time over an in-process pipe pair: the usual way the library is used.</summary>
    private static async Task<(long count, double seconds)> ProxyRun(double seconds)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        using var server = new JsonRpc(CreateHandler(toClient.Writer, toServer.Reader, Framing.Header, Formatter.SystemTextJson));
        server.AddLocalRpcTarget(new Target());
        server.StartListening();
        using var client = new JsonRpc(CreateHandler(toServer.Writer, toClient.Reader, Framing.Header, Formatter.SystemTextJson));
        var proxy = client.Attach<ICalculator>();
        client.StartListening();

        long n = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds)
        {
            if (await proxy.AddAsync(1, 2) != 3) throw new InvalidOperationException("add");
            if (await proxy.AddIntAsync(1, 7) != 8) throw new InvalidOperationException("addInt");
            if (await proxy.NullableFloatToNullableFloatAsync(1.23f) != 1.23f) throw new InvalidOperationException("NullableFloatToNullableFloat");
            if (await proxy.Test2Async(3.456m) != 3.456m) throw new InvalidOperationException("Test2");
            if (await proxy.StringMeAsync("Foo") != "Foo") throw new InvalidOperationException("StringMe");
            n += 5;
        }
        sw.Stop();
        toServer.Writer.Complete();
        toClient.Writer.Complete();
        return (n, sw.Elapsed.TotalSeconds);
    }
}
