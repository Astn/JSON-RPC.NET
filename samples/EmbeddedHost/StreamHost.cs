using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Threading.Channels;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;

namespace EmbeddedHost;

/// <summary>
/// A JSON-RPC connection over any <see cref="Stream"/> (here a named pipe) without Kestrel. Documents are
/// newline-delimited in both directions, which is what MCP's stdio transport and most line-oriented clients
/// expect. Two loops per connection: the read loop frames and processes documents in order, the write loop
/// drains a queue of replies and notifications. One loop that reads, processes and writes in turn deadlocks
/// against a client that pipelines requests, because both ends then wait for the other to read.
/// </summary>
public static class StreamHost
{
    private const int MaxDocumentBytes = 4 * 1024 * 1024;
    private static readonly byte[] NewLine = { (byte)'\n' };

    /// <summary>Runs one document: the default processes it on the calling thread; a UI host dispatches it elsewhere.</summary>
    public delegate Task<ReadOnlyMemory<byte>> Dispatcher(ReadOnlyMemory<byte> document, object context, CancellationToken cancellationToken);

    public static Dispatcher DirectDispatcher(string session) => async (document, context, cancellationToken) =>
    {
        var output = new ArrayBufferWriter<byte>();
        await JsonRpcProcessor.ProcessAsync(session, document, output, context: context, cancellationToken: cancellationToken);
        return output.WrittenMemory;   // nothing for a notification
    };

    /// <summary>Accepts clients on a named pipe and serves each on its own pair of loops.</summary>
    public static async Task RunPipeAsync(string pipeName, Dispatcher dispatch, Operations operations, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try { await pipe.WaitForConnectionAsync(stop); }
            catch (OperationCanceledException) { pipe.Dispose(); break; }
            _ = ServeConnectionAsync(pipe, dispatch, operations, stop);
        }
    }

    /// <summary>Serves one connection until the peer closes it, <paramref name="stop"/> fires, or the peer sends something that is not JSON.</summary>
    public static async Task ServeConnectionAsync(Stream stream, Dispatcher dispatch, Operations operations, CancellationToken stop)
    {
        using (stream)
        {
            var outgoing = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true });
            // An outbound notification: a server-side event becomes a document on the same queue as the replies, so
            // it never interleaves with a reply mid-write. Order relative to a reply is whatever the queue says.
            void OnNoteChanged(string key) => outgoing.Writer.TryWrite(Notification("notes/changed", "{\"key\":" + Quote(key) + "}"));
            if (operations != null) operations.NoteChanged += OnNoteChanged;
            var writing = WriteLoopAsync(stream, outgoing.Reader, stop);
            try
            {
                await ReadLoopAsync(stream, dispatch, outgoing.Writer, stop);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            finally
            {
                if (operations != null) operations.NoteChanged -= OnNoteChanged;
                outgoing.Writer.TryComplete();
                try { await writing; } catch (Exception) { }
            }
        }
    }

    private static async Task ReadLoopAsync(Stream stream, Dispatcher dispatch, ChannelWriter<ReadOnlyMemory<byte>> outgoing, CancellationToken stop)
    {
        var buffer = new byte[16 * 1024];
        int filled = 0;
        while (true)
        {
            if (filled == buffer.Length)
            {
                // Nothing outside Kestrel bounds this buffer: a client that never finishes a document would grow it forever.
                if (buffer.Length >= MaxDocumentBytes)
                {
                    outgoing.TryWrite(Error(-32600, "Invalid Request", "{\"limit\":\"maxDocumentBytes\",\"maximum\":" + MaxDocumentBytes + "}"));
                    return;
                }
                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaxDocumentBytes));
            }
            int read = await stream.ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), stop);
            if (read == 0) return;
            filled += read;

            int start = 0;
            while (start < filled)
            {
                int end = JsonFramer.FindDocumentEnd(buffer.AsSpan(start, filled - start));
                if (end < 0)
                {
                    // -1 means either "not complete yet" or "does not start with { or [". Tell them apart here,
                    // or a line of garbage stalls the connection while the buffer grows to the limit.
                    int first = FirstNonWhitespace(buffer, start, filled);
                    if (first < 0) { start = filled; break; }                  // only whitespace so far: drop it
                    if (buffer[first] != (byte)'{' && buffer[first] != (byte)'[')
                    {
                        outgoing.TryWrite(Error(-32700, "Parse error", null));   // then close: the stream is out of sync
                        return;
                    }
                    break;                                                      // incomplete: read more
                }

                // Documents on one connection run one at a time, in order. The document memory belongs to the call
                // until the await returns, and only then is the buffer compacted.
                var reply = await dispatch(buffer.AsMemory(start, end), stream, stop);
                start += end;
                if (reply.Length > 0) await outgoing.WriteAsync(reply, stop);
            }
            Buffer.BlockCopy(buffer, start, buffer, 0, filled - start);
            filled -= start;
        }
    }

    private static async Task WriteLoopAsync(Stream stream, ChannelReader<ReadOnlyMemory<byte>> outgoing, CancellationToken stop)
    {
        await foreach (var frame in outgoing.ReadAllAsync(stop))
        {
            await stream.WriteAsync(frame, stop);
            await stream.WriteAsync(NewLine, stop);    // the reply separator: the core writes none
            await stream.FlushAsync(stop);
        }
    }

    private static int FirstNonWhitespace(byte[] buffer, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            byte b = buffer[i];
            if (b != (byte)' ' && b != (byte)'\t' && b != (byte)'\r' && b != (byte)'\n') return i;
        }
        return -1;
    }

    private static ReadOnlyMemory<byte> Error(int code, string message, string dataJson)
        => Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":" + code + ",\"message\":" + Quote(message) + ",\"data\":" + (dataJson ?? "null") + "},\"id\":null}");

    private static ReadOnlyMemory<byte> Notification(string method, string paramsJson)
        => Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":" + Quote(method) + ",\"params\":" + paramsJson + "}");

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
