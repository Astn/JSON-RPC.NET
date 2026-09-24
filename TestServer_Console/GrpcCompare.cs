using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using TestServer_Console.Proto;

namespace TestServer_Console;

/// <summary>
/// gRPC for .NET answering the benchmark's five calls (see Protos/calculator.proto), for the compare mode. The
/// service is hosted on an HTTP/2 listener of the same Kestrel that serves the JSON-RPC rows; the client is
/// Grpc.Net.Client with one channel (one HTTP/2 connection) per client and <c>inFlight</c> calls outstanding on
/// each, the same shape as the pipelined TCP client. Two rows: unary calls, the way gRPC is normally used, and one
/// bidirectional stream per channel carrying the same five calls, the closest gRPC has to a pipelined connection.
/// </summary>
internal static class GrpcCompare
{
    private static readonly WriteOptions Buffered = new WriteOptions(WriteFlags.BufferHint);
    private static readonly WriteOptions Flushed = new WriteOptions();

    public sealed class CalculatorGrpc : Calculator.CalculatorBase
    {
        public override Task<DoubleReply> Add(AddRequest r, ServerCallContext c) => Task.FromResult(new DoubleReply { Value = r.L + r.R });
        public override Task<IntReply> AddInt(AddIntRequest r, ServerCallContext c) => Task.FromResult(new IntReply { Value = r.L + r.R });
        public override Task<NullableFloatReply> NullableFloatToNullableFloat(NullableFloatRequest r, ServerCallContext c) => Task.FromResult(NullableFloat(r));
        public override Task<NullableDecimalReply> Test2(DecimalRequest r, ServerCallContext c) => Task.FromResult(new NullableDecimalReply { Value = r.X });
        public override Task<StringReply> StringMe(StringRequest r, ServerCallContext c) => Task.FromResult(new StringReply { Value = r.X });

        public override async Task Stream(IAsyncStreamReader<Call> requests, IServerStreamWriter<Reply> responses, ServerCallContext c)
        {
            // Flush when the input runs dry, buffer while more calls are already queued: the same once-per-read-group
            // flush the JSON-RPC connection handler does, expressed with gRPC's BufferHint.
            var next = requests.MoveNext(c.CancellationToken);
            while (await next)
            {
                var call = requests.Current;
                next = requests.MoveNext(c.CancellationToken);
                responses.WriteOptions = next.IsCompleted ? Buffered : Flushed;
                await responses.WriteAsync(Answer(call));
            }
        }

        private static NullableFloatReply NullableFloat(NullableFloatRequest r)
        {
            var reply = new NullableFloatReply();
            if (r.HasA) reply.Value = r.A;
            return reply;
        }

        private static Reply Answer(Call call)
        {
            switch (call.CallCase)
            {
                case Call.CallOneofCase.Add: return new Reply { Add = new DoubleReply { Value = call.Add.L + call.Add.R } };
                case Call.CallOneofCase.AddInt: return new Reply { AddInt = new IntReply { Value = call.AddInt.L + call.AddInt.R } };
                case Call.CallOneofCase.NullableFloat: return new Reply { NullableFloat = NullableFloat(call.NullableFloat) };
                case Call.CallOneofCase.Test2: return new Reply { Test2 = new NullableDecimalReply { Value = call.Test2.X } };
                case Call.CallOneofCase.StringMe: return new Reply { StringMe = new StringReply { Value = call.StringMe.X } };
                default: throw new RpcException(new Status(StatusCode.InvalidArgument, "empty call"));
            }
        }
    }

    // ---- the five calls, as messages (built once; protobuf messages are not mutated after construction)

    private static DecimalValue ToDecimalValue(decimal d)
    {
        long units = (long)decimal.Truncate(d);
        int nanos = (int)((d - units) * 1_000_000_000m);
        return new DecimalValue { Units = units, Nanos = nanos };
    }

    private static decimal FromDecimalValue(DecimalValue v) => v.Units + v.Nanos / 1_000_000_000m;

    private static readonly AddRequest AddReq = new AddRequest { L = 1, R = 2 };
    private static readonly AddIntRequest AddIntReq = new AddIntRequest { L = 1, R = 7 };
    private static readonly NullableFloatRequest NullableFloatReq = new NullableFloatRequest { A = 1.23f };
    private static readonly DecimalRequest Test2Req = new DecimalRequest { X = ToDecimalValue(3.456m) };
    private static readonly StringRequest StringMeReq = new StringRequest { X = "Foo" };

    private static readonly Call[] StreamCalls =
    {
        new Call { Add = AddReq },
        new Call { AddInt = AddIntReq },
        new Call { NullableFloat = NullableFloatReq },
        new Call { Test2 = Test2Req },
        new Call { StringMe = StringMeReq },
    };

    /// <summary>The five unary calls in sequence, every reply checked (a wrong answer would otherwise inflate a row).</summary>
    private static async Task Cycle(Calculator.CalculatorClient client)
    {
        if ((await client.AddAsync(AddReq)).Value != 3) throw new InvalidOperationException("gRPC add");
        if ((await client.AddIntAsync(AddIntReq)).Value != 8) throw new InvalidOperationException("gRPC addInt");
        var f = await client.NullableFloatToNullableFloatAsync(NullableFloatReq);
        if (!f.HasValue || f.Value != 1.23f) throw new InvalidOperationException("gRPC NullableFloatToNullableFloat");
        var d = await client.Test2Async(Test2Req);
        if (d.Value == null || FromDecimalValue(d.Value) != 3.456m) throw new InvalidOperationException("gRPC Test2");
        if ((await client.StringMeAsync(StringMeReq)).Value != "Foo") throw new InvalidOperationException("gRPC StringMe");
    }

    private static void Check(Reply reply)
    {
        switch (reply.ReplyCase)
        {
            case Reply.ReplyOneofCase.Add: if (reply.Add.Value != 3) throw new InvalidOperationException("gRPC stream add"); break;
            case Reply.ReplyOneofCase.AddInt: if (reply.AddInt.Value != 8) throw new InvalidOperationException("gRPC stream addInt"); break;
            case Reply.ReplyOneofCase.NullableFloat: if (!reply.NullableFloat.HasValue || reply.NullableFloat.Value != 1.23f) throw new InvalidOperationException("gRPC stream NullableFloatToNullableFloat"); break;
            case Reply.ReplyOneofCase.Test2: if (reply.Test2.Value == null || FromDecimalValue(reply.Test2.Value) != 3.456m) throw new InvalidOperationException("gRPC stream Test2"); break;
            case Reply.ReplyOneofCase.StringMe: if (reply.StringMe.Value != "Foo") throw new InvalidOperationException("gRPC stream StringMe"); break;
            default: throw new InvalidOperationException("gRPC stream: empty reply");
        }
    }

    private static GrpcChannel[] OpenChannels(int port, int count)
    {
        return Enumerable.Range(0, count).Select(_ => GrpcChannel.ForAddress("http://127.0.0.1:" + port)).ToArray();
    }

    /// <summary>
    /// Unary calls: <paramref name="channels"/> HTTP/2 connections, each with <paramref name="inFlight"/> workers
    /// that await one call at a time, so at most <c>channels × inFlight</c> calls are outstanding.
    /// </summary>
    internal static async Task<(long count, double seconds)> UnaryRun(int port, int channels, int inFlight, double seconds)
    {
        var chans = OpenChannels(port, channels);
        try
        {
            var clients = chans.Select(c => new Calculator.CalculatorClient(c)).ToArray();
            await Cycle(clients[0]);   // probe: the service answers, and answers correctly

            var counts = new long[channels * inFlight];
            using var stop = new CancellationTokenSource();
            var sw = Stopwatch.StartNew();
            var workers = new Task[counts.Length];
            for (int i = 0; i < workers.Length; i++)
            {
                var client = clients[i / inFlight];
                int slot = i;
                workers[i] = Task.Run(async () =>
                {
                    while (!stop.IsCancellationRequested)
                    {
                        await Cycle(client);
                        counts[slot] += 5;
                    }
                });
            }
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            stop.Cancel();
            await Task.WhenAll(workers);
            sw.Stop();
            return (counts.Sum(), sw.Elapsed.TotalSeconds);
        }
        finally
        {
            foreach (var c in chans) c.Dispose();
        }
    }

    /// <summary>
    /// One bidirectional stream per channel: a writer keeps <paramref name="inFlight"/> calls outstanding, a reader
    /// counts and checks the replies. The same five calls, in the same rotation, as every other row. Like the TCP
    /// client, the writer sends whatever has room as one batch: BufferHint on every message but the last, which
    /// flushes. The server answers each message as it arrives (gRPC's default write, one flush per reply).
    /// </summary>
    internal static async Task<(long count, double seconds)> StreamRun(int port, int channels, int inFlight, double seconds)
    {
        var chans = OpenChannels(port, channels);
        try
        {
            var counts = new long[channels];
            using var stop = new CancellationTokenSource();
            var sw = Stopwatch.StartNew();
            var streams = new Task[channels];
            for (int i = 0; i < channels; i++)
            {
                var client = new Calculator.CalculatorClient(chans[i]);
                int slot = i;
                streams[i] = Task.Run(async () =>
                {
                    using var call = client.Stream();
                    using var room = new SemaphoreSlim(inFlight, inFlight);
                    var reader = Task.Run(async () =>
                    {
                        long n = 0;
                        await foreach (var reply in call.ResponseStream.ReadAllAsync())
                        {
                            if ((n & 1023) == 0) Check(reply);   // every reply is a reply; spot-check the values
                            n++;
                            room.Release();
                        }
                        counts[slot] = n;
                    });
                    int next = 0;
                    while (!stop.IsCancellationRequested)
                    {
                        await room.WaitAsync();
                        int batch = 1;
                        while (room.Wait(0)) batch++;   // everything the reader has freed since the last refill
                        for (int b = 0; b < batch; b++)
                        {
                            call.RequestStream.WriteOptions = b == batch - 1 ? Flushed : Buffered;
                            await call.RequestStream.WriteAsync(StreamCalls[next]);
                            if (++next == StreamCalls.Length) next = 0;
                        }
                    }
                    await call.RequestStream.CompleteAsync();
                    await reader;
                });
            }
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            stop.Cancel();
            await Task.WhenAll(streams);
            sw.Stop();
            return (counts.Sum(), sw.Elapsed.TotalSeconds);
        }
        finally
        {
            foreach (var c in chans) c.Dispose();
        }
    }
}
