using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestServer_Console;

/// <summary>
/// End-to-end throughput through the AustinHarris.JsonRpc.AspNetCore package: a real Kestrel on loopback
/// answering the same five requests over HTTP (one request per POST, and a batch per POST) and over a raw
/// TCP connection with pipelined requests. Clients and server share the machine, so each figure is an
/// upper bound on what one box can do talking to itself; the in-process rows are the same requests through
/// the byte entry point with no transport at all, for scale.
/// </summary>
internal static class KestrelBenchmark
{
    private static readonly byte[] ResultPrefix = Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"result\":");
    private static readonly byte[] BatchResultPrefix = Encoding.UTF8.GetBytes("[{\"jsonrpc\":\"2.0\",\"result\":");

    internal static async Task RunAsync(Action<string> print, double seconds = 3, int clients = 0, int pipeline = 256)
    {
        print ??= Console.WriteLine;
        if (clients <= 0) clients = Environment.ProcessorCount;

        var inputs = BenchmarkRunner.taskInputs.Select(t => Encoding.UTF8.GetBytes(t)).ToArray();
        const int batchSize = 100;
        var batch = BuildBatch(inputs, batchSize);

        int tcpPort = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Loopback, 0);
            k.Listen(IPAddress.Loopback, tcpPort, l => l.UseConnectionHandler<JsonRpcConnectionHandler>());
        });
        builder.Services.AddJsonRpc();
        var app = builder.Build();
        app.MapJsonRpc("/rpc");
        await app.StartAsync();

        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses;
            var httpUrl = addresses.First(a => !a.EndsWith(":" + tcpPort)) + "/rpc";

            print($"Kestrel on {httpUrl} (HTTP) and 127.0.0.1:{tcpPort} (TCP), {clients} clients, pipeline {pipeline}, {seconds:0.#} s per row\n");

            var rows = new List<BenchmarkRunner.ChartRow>();
            var session = Handler.DefaultSessionId();
            var memInputs = inputs.Select(i => (ReadOnlyMemory<byte>)i).ToArray();

            // Scale rows: the same requests with no transport at all.
            BenchmarkRunner.RunSync(session, memInputs, Config.Serializer, 1, 0.5, out _); // warm-up
            var elapsed = BenchmarkRunner.RunSync(session, memInputs, Config.Serializer, 1, seconds, out long total);
            rows.Add(new BenchmarkRunner.ChartRow("in-process, 1 thread", total / elapsed, $"{total,12:N0} RPCs"));
            elapsed = BenchmarkRunner.RunSync(session, memInputs, Config.Serializer, clients, seconds, out total);
            rows.Add(new BenchmarkRunner.ChartRow($"in-process, {clients} threads", total / elapsed, $"{total,12:N0} RPCs"));
            print($"  in-process rows done ({rows[0].RpcPerSec:N0} / {rows[1].RpcPerSec:N0} RPC/s)");

            // HTTP, one request per POST.
            await HttpRun(httpUrl, inputs, 1, clients, 0.5);                                   // warm-up
            var (count, secs) = await HttpRun(httpUrl, inputs, 1, clients, seconds);
            rows.Add(new BenchmarkRunner.ChartRow("HTTP, 1 request/POST", count / secs, $"{count,12:N0} RPCs  {secs / count * 1e6 * clients:N1} us/request per client"));
            print($"  HTTP single done ({count / secs:N0} RPC/s)");

            // HTTP, a batch per POST.
            await HttpRun(httpUrl, new[] { batch }, batchSize, clients, 0.5);
            (count, secs) = await HttpRun(httpUrl, new[] { batch }, batchSize, clients, seconds);
            rows.Add(new BenchmarkRunner.ChartRow($"HTTP, batch of {batchSize}/POST", count / secs, $"{count,12:N0} RPCs"));
            print($"  HTTP batch done ({count / secs:N0} RPC/s)");

            // TCP, pipelined.
            TcpRun(tcpPort, inputs, clients, 0.5, pipeline, ResultPrefix);
            (count, secs) = TcpRun(tcpPort, inputs, clients, seconds, pipeline, ResultPrefix);
            rows.Add(new BenchmarkRunner.ChartRow($"TCP, {pipeline} pipelined", count / secs, $"{count,12:N0} RPCs"));
            print($"  TCP done ({count / secs:N0} RPC/s)");

            BenchmarkRunner.PrintBarChart($"Kestrel benchmark - {Config.Serializer.Name} - RPC/s by transport", "Transport", rows);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    internal static byte[] BuildBatch(byte[][] inputs, int size)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < size; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Encoding.UTF8.GetString(inputs[i % inputs.Length]));
        }
        return Encoding.UTF8.GetBytes(sb.Append(']').ToString());
    }

    /// <summary>Each client POSTs its next document, reads the body, checks it is a result, and repeats.</summary>
    internal static async Task<(long count, double seconds)> HttpRun(string url, byte[][] documents, int rpcsPerDocument, int clients, double seconds)
    {
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = clients * 2, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        using var http = new HttpClient(handler);
        var prefix = rpcsPerDocument > 1 ? BatchResultPrefix : ResultPrefix;
        using var cts = new CancellationTokenSource();
        var counts = new long[clients];
        var sw = Stopwatch.StartNew();

        var tasks = new Task[clients];
        for (int c = 0; c < clients; c++)
        {
            int slot = c;
            tasks[c] = Task.Run(async () =>
            {
                int i = slot;
                long n = 0;
                while (!cts.IsCancellationRequested)
                {
                    var content = new ByteArrayContent(documents[i % documents.Length]);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var response = await http.PostAsync(url, content);
                    var body = await response.Content.ReadAsByteArrayAsync();
                    if (!body.AsSpan().StartsWith(prefix))
                        throw new InvalidOperationException("Unexpected response: " + Encoding.UTF8.GetString(body));
                    n += rpcsPerDocument;
                    i++;
                }
                counts[slot] = n;
            });
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds));
        cts.Cancel();
        await Task.WhenAll(tasks);
        sw.Stop();
        return (counts.Sum(), sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// One synchronous thread per client, one connection each. The requests live in a precomputed ring
    /// (<see cref="BuildRing"/>); a cursor walks it, and whenever the responses drain the pipeline below
    /// <paramref name="pipeline"/> the free slots are refilled with a single send of the next contiguous
    /// slice of the ring. Responses are counted (and checked) by a small streaming framer, so the client does
    /// no allocation and no parsing beyond bracket depth.
    /// </summary>
    /// <param name="expectedPrefix">Bytes every response must start with, or null to skip the check (framings that put headers first).</param>
    internal static (long count, double seconds) TcpRun(int port, byte[][] inputs, int clients, double seconds, int pipeline, byte[] expectedPrefix)
    {
        const int ringCount = 4096;
        var offsets = new int[ringCount + pipeline + 1];
        var ring = BuildRing(inputs, ringCount, pipeline, offsets);

        var counts = new long[clients];
        int stop = 0;
        Exception failure = null;
        var ready = new Barrier(clients + 1);
        var threads = new Thread[clients];

        for (int c = 0; c < clients; c++)
        {
            int slot = c;
            threads[c] = new Thread(() =>
            {
                try
                {
                    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    socket.Connect(IPAddress.Loopback, port);
                    var receive = new byte[64 * 1024];
                    var framer = new ResponseCounter(expectedPrefix);
                    int cursor = slot % ringCount;
                    int inFlight = 0;
                    long received = 0;

                    ready.SignalAndWait();
                    while (Volatile.Read(ref stop) == 0)
                    {
                        int free = pipeline - inFlight;
                        if (free > 0)
                        {
                            // The ring has `pipeline` documents duplicated past its end, so this slice never wraps.
                            int start = offsets[cursor];
                            int end = offsets[cursor + free];
                            socket.Send(ring, start, end - start, SocketFlags.None);   // blocking: sends everything
                            cursor += free;
                            if (cursor >= ringCount) cursor -= ringCount;
                            inFlight += free;
                        }

                        int n = socket.Receive(receive);
                        if (n == 0) break;
                        int docs = framer.Count(receive.AsSpan(0, n));
                        received += docs;
                        inFlight -= docs;
                    }

                    // Drain what is still in flight so the server is idle before the next row starts.
                    socket.ReceiveTimeout = 2000;
                    try
                    {
                        while (inFlight > 0)
                        {
                            int n = socket.Receive(receive);
                            if (n == 0) break;
                            int docs = framer.Count(receive.AsSpan(0, n));
                            received += docs;
                            inFlight -= docs;
                        }
                    }
                    catch (SocketException) { }

                    counts[slot] = received;
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref failure, e, null);
                    Volatile.Write(ref stop, 1);
                }
            }) { IsBackground = true, Name = "tcp-client-" + c };
            threads[c].Start();
        }

        ready.SignalAndWait();
        var sw = Stopwatch.StartNew();
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        Volatile.Write(ref stop, 1);
        foreach (var t in threads) t.Join();
        sw.Stop();

        if (failure != null) throw new InvalidOperationException("TCP client failed", failure);
        return (counts.Sum(), sw.Elapsed.TotalSeconds);
    }

    /// <summary>
    /// <paramref name="ringCount"/> documents back to back, followed by a copy of the first <paramref name="pipeline"/>
    /// of them, so that any window of up to <paramref name="pipeline"/> requests starting anywhere in the ring is one
    /// contiguous slice. <paramref name="offsets"/>[i] is where document i starts; the last entry is the total length.
    /// </summary>
    internal static byte[] BuildRing(byte[][] inputs, int ringCount, int pipeline, int[] offsets)
    {
        int total = 0;
        for (int i = 0; i < ringCount + pipeline; i++)
        {
            offsets[i] = total;
            total += inputs[i % inputs.Length].Length;
        }
        offsets[ringCount + pipeline] = total;

        var ring = new byte[total];
        for (int i = 0; i < ringCount + pipeline; i++)
        {
            var doc = inputs[i % inputs.Length];
            Buffer.BlockCopy(doc, 0, ring, offsets[i], doc.Length);
        }
        return ring;
    }

    /// <summary>
    /// Counts complete top-level JSON documents in a byte stream across reads (bracket depth, string and escape
    /// state carried over) and checks that each one starts with the expected result prefix.
    /// </summary>
    internal sealed class ResponseCounter
    {
        private readonly byte[] _prefix;
        private readonly byte[] _capture;
        private int _captured;
        private int _depth;
        private bool _inString;
        private bool _escape;

        public ResponseCounter(byte[] prefix)
        {
            _prefix = prefix ?? Array.Empty<byte>();
            _capture = new byte[_prefix.Length];
        }

        public int Count(ReadOnlySpan<byte> bytes)
        {
            int docs = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                // Capture the prefix from the first byte of a document; whitespace or headers between documents are skipped.
                if (_captured < _prefix.Length && (_depth > 0 || b == (byte)'{' || b == (byte)'['))
                {
                    _capture[_captured++] = b;
                    if (_captured == _prefix.Length && !_capture.AsSpan().SequenceEqual(_prefix))
                        throw new InvalidOperationException("Unexpected response: " + Encoding.UTF8.GetString(_capture));
                }

                if (_inString)
                {
                    if (_escape) _escape = false;
                    else if (b == (byte)'\\') _escape = true;
                    else if (b == (byte)'"') _inString = false;
                    continue;
                }

                switch (b)
                {
                    case (byte)'"': _inString = true; break;
                    case (byte)'{':
                    case (byte)'[': _depth++; break;
                    case (byte)'}':
                    case (byte)']':
                        if (--_depth == 0) { docs++; _captured = 0; }
                        break;
                }
            }
            return docs;
        }
    }

    internal static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
