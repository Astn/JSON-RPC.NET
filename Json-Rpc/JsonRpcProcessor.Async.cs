using System;
using System.Buffers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    public static partial class JsonRpcProcessor
    {
        /// <summary>
        /// Processes a document asynchronously. Keep the request bytes immutable and valid, and the output
        /// writer valid and exclusive, until completion. Successful inline completion returns Task.CompletedTask.
        /// Cancellation waits for the running method to terminate and discards all staged output.
        /// </summary>
        public static Task ProcessAsync(string sessionId, ReadOnlySequence<byte> request, IBufferWriter<byte> output,
            object context = null, JsonRpcSerializer serializer = null, CancellationToken cancellationToken = default)
        {
            if (request.IsSingleSegment) return ProcessAsync(sessionId, request.First, output, context, serializer, cancellationToken);
            var scratch = AsyncScratch.Rent();
            int length;
            byte[] buffer;
            try
            {
                length = checked((int)request.Length);
                buffer = scratch.Input(length);
                request.CopyTo(buffer);
            }
            catch { scratch.Return(); throw; }
            return StartAsyncDocument(sessionId, new ReadOnlyMemory<byte>(buffer, 0, length), output, context, serializer, cancellationToken, scratch);
        }

        /// <summary>
        /// Processes borrowed UTF-8 memory. Keep the bytes immutable and valid, and the output writer valid
        /// and exclusive, until the task completes. Cancellation commits no response bytes.
        /// </summary>
        public static Task ProcessAsync(string sessionId, ReadOnlyMemory<byte> request, IBufferWriter<byte> output,
            object context = null, JsonRpcSerializer serializer = null, CancellationToken cancellationToken = default)
        {
            return StartAsyncDocument(sessionId, request, output, context, serializer, cancellationToken, AsyncScratch.Rent());
        }

        /// <summary>
        /// Copies a UTF-8 span into owned pooled storage before processing. The output writer must stay valid
        /// and exclusive until completion; the caller may reuse the input span as soon as this method returns.
        /// </summary>
        public static Task ProcessAsync(string sessionId, ReadOnlySpan<byte> request, IBufferWriter<byte> output,
            object context = null, JsonRpcSerializer serializer = null, CancellationToken cancellationToken = default)
        {
            var scratch = AsyncScratch.Rent();
            byte[] buffer;
            try
            {
                buffer = scratch.Input(request.Length);
                request.CopyTo(buffer);
            }
            catch { scratch.Return(); throw; }
            return StartAsyncDocument(sessionId, new ReadOnlyMemory<byte>(buffer, 0, request.Length), output, context, serializer, cancellationToken, scratch);
        }

        /// <summary>Processes a string asynchronously on the selected session, returning an empty string for notifications.</summary>
        public static async Task<string> ProcessAsync(string sessionId, string jsonRpc, object context = null,
            JsonRpcSerializer serializer = null, CancellationToken cancellationToken = default)
        {
            var input = Encoding.UTF8.GetBytes(jsonRpc);
            using (var output = new PooledByteBufferWriter())
            {
                await ProcessAsync(sessionId, new ReadOnlyMemory<byte>(input), output, context, serializer, cancellationToken).ConfigureAwait(false);
                return output.ToString();
            }
        }

        /// <summary>Processes a string asynchronously on the default session; notifications return an empty string.</summary>
        public static Task<string> ProcessAsync(string jsonRpc, object context = null, CancellationToken cancellationToken = default)
        {
            return ProcessAsync(Handler.DefaultSessionId(), jsonRpc, context, null, cancellationToken);
        }

        private static Task StartAsyncDocument(string sessionId, ReadOnlyMemory<byte> document, IBufferWriter<byte> destination,
            object context, JsonRpcSerializer serializer, CancellationToken token, AsyncScratch scratch)
        {
            bool transferred = false;
            try
            {
                token.ThrowIfCancellationRequested();
                if (!Handler.TryGetSessionHandler(sessionId, out var handler)) handler = Handler.UnknownSessionHandler;
                serializer = serializer ?? handler.Serializer ?? Config.Serializer;
                scratch.DocumentLength = document.Length;
                var reader = scratch.GetReader(serializer);
                var output = scratch.Output;
                if (!reader.TryParse(document, out var parseError))
                {
                    var ex = new JsonRpcException(-32700, "Parse error", parseError);
                    if (handler.HasParseErrorHandler) ex = handler.ProcessParseException(Utf8Json.ToStringUtf8(document.Span), ex);
                    Handler.WriteErrorEnvelope(output, serializer, ex, default);
                }
                else if (!reader.IsBatch)
                {
                    var pending = handler.HandleRequestAsync(reader, 0, serializer, output, context, token);
                    if (!pending.IsCompleted)
                    {
                        var completion = FinishSingleDocumentAsync(pending, scratch, destination, token);
                        transferred = true;
                        return completion;
                    }
                    pending.GetAwaiter().GetResult();
                }
                else if (reader.Count == 0)
                {
                    var ex = new JsonRpcException(-32600, "Invalid Request", "Batch of calls was empty.");
                    if (handler.HasParseErrorHandler) ex = handler.ProcessParseException(Utf8Json.ToStringUtf8(document.Span), ex);
                    Handler.WriteErrorEnvelope(output, serializer, ex, default);
                }
                else
                {
                    output.Write((byte)'[');
                    int written = 0;
                    for (int i = 0; i < reader.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        int before = output.WrittenCount;
                        if (written > 0) output.Write((byte)',');
                        var pending = handler.HandleRequestAsync(reader, i, serializer, output, context, token);
                        if (!pending.IsCompleted)
                        {
                            var completion = FinishBatchDocumentAsync(pending, i, before, written, handler, scratch, serializer, context, destination, token);
                            transferred = true;
                            return completion;
                        }
                        if (pending.GetAwaiter().GetResult()) written++;
                        else output.Rewind(before);
                    }
                    if (written == 0) output.Rewind(0);
                    else output.Write((byte)']');
                }
                CommitAsyncDocument(scratch, destination, token);
                return Task.CompletedTask;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return Task.FromCanceled(token);
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
            finally { if (!transferred) scratch.Return(); }
        }

        private static async Task FinishSingleDocumentAsync(ValueTask<bool> pending, AsyncScratch scratch, IBufferWriter<byte> destination, CancellationToken token)
        {
            try
            {
                await pending.ConfigureAwait(false);
                CommitAsyncDocument(scratch, destination, token);
            }
            finally { scratch.Return(); }
        }

        private static async Task FinishBatchDocumentAsync(ValueTask<bool> pending, int index, int before, int written,
            Handler handler, AsyncScratch scratch, JsonRpcSerializer serializer, object context, IBufferWriter<byte> destination, CancellationToken token)
        {
            try
            {
                var output = scratch.Output;
                var reader = scratch.Reader;
                if (await pending.ConfigureAwait(false)) written++;
                else output.Rewind(before);
                for (int i = index + 1; i < reader.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    before = output.WrittenCount;
                    if (written > 0) output.Write((byte)',');
                    if (await handler.HandleRequestAsync(reader, i, serializer, output, context, token).ConfigureAwait(false)) written++;
                    else output.Rewind(before);
                }
                if (written == 0) output.Rewind(0);
                else output.Write((byte)']');
                CommitAsyncDocument(scratch, destination, token);
            }
            finally { scratch.Return(); }
        }

        private static void CommitAsyncDocument(AsyncScratch scratch, IBufferWriter<byte> destination, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (scratch.Output.WrittenCount != 0) scratch.Output.CopyTo(destination);
        }

        // Transferable exclusive leases. A bounded shared array avoids thread-affine ownership and
        // linked-list node allocations. Nothing from synchronous Scratch is used here.
        private sealed class AsyncScratch
        {
            private const int CapacityLimit = 64 * 1024;
            private static readonly AsyncScratch[] Pool = new AsyncScratch[64];
            private static int _count;
            private byte[] _input;
            private JsonRpcSerializer _readerOwner;
            internal JsonRpcRequestReader Reader;
            internal PooledByteBufferWriter Output = new PooledByteBufferWriter(4096);
            internal int DocumentLength;
            private bool _readerLeased;

            internal static AsyncScratch Rent()
            {
                lock (Pool)
                {
                    if (_count > 0)
                    {
                        var scratch = Pool[--_count];
                        Pool[_count] = null;
                        return scratch;
                    }
                }
                return new AsyncScratch();
            }

            internal byte[] Input(int length)
            {
                if (_input == null || _input.Length < length)
                {
                    if (_input != null) ArrayPool<byte>.Shared.Return(_input);
                    _input = ArrayPool<byte>.Shared.Rent(length);
                }
                return _input;
            }

            internal JsonRpcRequestReader GetReader(JsonRpcSerializer serializer)
            {
                if (!ReferenceEquals(_readerOwner, serializer) || Reader == null)
                {
                    Reader = serializer.CreateReader();
                    _readerOwner = serializer;
                }
                _readerLeased = true;
                return Reader;
            }

            internal void Return()
            {
                bool reusable = false;
                try
                {
                    if (_readerLeased)
                    {
                        _readerLeased = false;
                        Reader.Release();
                    }
                    reusable = true;
                }
                finally
                {
                    if (DocumentLength > CapacityLimit) { Reader = null; _readerOwner = null; }
                    DocumentLength = 0;
                    if (_input != null && _input.Length > CapacityLimit)
                    {
                        ArrayPool<byte>.Shared.Return(_input);
                        _input = null;
                    }
                    if (Output.WrittenSegment.Array.Length > CapacityLimit)
                    {
                        Output.Dispose();
                        Output = new PooledByteBufferWriter(4096);
                    }
                    else Output.Clear();
                    bool retained = false;
                    if (reusable)
                    {
                        lock (Pool)
                        {
                            if (_count < Pool.Length) { Pool[_count++] = this; retained = true; }
                        }
                    }
                    if (!retained)
                    {
                        if (_input != null) ArrayPool<byte>.Shared.Return(_input);
                        _input = null;
                        Output.Dispose();
                        Reader = null;
                        _readerOwner = null;
                    }
                }
            }
        }
    }
}
