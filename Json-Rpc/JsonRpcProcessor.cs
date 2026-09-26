using System;
using System.Buffers;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Entry points for processing JSON-RPC documents. The native shape is bytes in, bytes out:
    /// <see cref="Process(string, in ReadOnlySequence{byte}, IBufferWriter{byte}, object, JsonRpcSerializer)"/>
    /// takes what a PipeReader hands you and writes to a PipeWriter / HTTP BodyWriter. The string overloads
    /// transcode once at the edge and are kept for existing hosts.
    /// </summary>
    public static partial class JsonRpcProcessor
    {
        // ------------------------------------------------------------------ bytes in, bytes out

        /// <summary>Processes one document (a request or a batch). Writes the response bytes to <paramref name="output"/>; writes nothing for notifications.</summary>
        public static void Process(string sessionId, in ReadOnlySequence<byte> request, IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null)
        {
            var handler = GetRequestHandler(sessionId);
            var limits = handler.Limits ?? Config.Limits;
            if (ExceedsDocumentLimit(request.Length, limits))
            {
                RejectDocument(handler, serializer, output, limits.MaxDocumentBytes,
                    handler.HasParseErrorHandler ? Utf8Json.ToStringUtf8(request.ToArray()) : null);
                return;
            }
            if (request.IsSingleSegment)
            {
                ProcessMemory(handler, limits, request.First, output, context, serializer);
                return;
            }
            int length = checked((int)request.Length);
            var scratch = Scratch.Rent();
            try
            {
                var buffer = scratch.Input(length);
                request.CopyTo(buffer);
                ProcessCore(handler, limits, new ReadOnlyMemory<byte>(buffer, 0, length), output, context, serializer, scratch);
            }
            finally
            {
                scratch.Return();
            }
        }

        /// <summary>Processes one document held in memory. The memory must stay valid until the call returns.</summary>
        public static void Process(string sessionId, ReadOnlyMemory<byte> request, IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null)
        {
            var handler = GetRequestHandler(sessionId);
            var limits = handler.Limits ?? Config.Limits;
            if (ExceedsDocumentLimit(request.Length, limits))
            {
                RejectDocument(handler, serializer, output, limits.MaxDocumentBytes,
                    handler.HasParseErrorHandler ? Utf8Json.ToStringUtf8(request.Span) : null);
                return;
            }
            ProcessMemory(handler, limits, request, output, context, serializer);
        }

        private static void ProcessMemory(Handler handler, JsonRpcLimits limits, ReadOnlyMemory<byte> request,
            IBufferWriter<byte> output, object context, JsonRpcSerializer serializer)
        {
            var scratch = Scratch.Rent();
            try
            {
                ProcessCore(handler, limits, request, output, context, serializer, scratch);
            }
            finally
            {
                scratch.Return();
            }
        }

        /// <summary>Processes one document from a span (copied into a pooled buffer).</summary>
        public static void Process(string sessionId, ReadOnlySpan<byte> request, IBufferWriter<byte> output, object context = null, JsonRpcSerializer serializer = null)
        {
            var handler = GetRequestHandler(sessionId);
            var limits = handler.Limits ?? Config.Limits;
            if (ExceedsDocumentLimit(request.Length, limits))
            {
                RejectDocument(handler, serializer, output, limits.MaxDocumentBytes,
                    handler.HasParseErrorHandler ? Utf8Json.ToStringUtf8(request) : null);
                return;
            }
            var scratch = Scratch.Rent();
            try
            {
                var buffer = scratch.Input(request.Length);
                request.CopyTo(buffer);
                ProcessCore(handler, limits, new ReadOnlyMemory<byte>(buffer, 0, request.Length), output, context, serializer, scratch);
            }
            finally
            {
                scratch.Return();
            }
        }

        /// <summary>Processes a UTF-8 document and returns the UTF-8 response (empty for notifications).</summary>
        public static byte[] ProcessBytes(string sessionId, ReadOnlySpan<byte> request, object context = null, JsonRpcSerializer serializer = null)
        {
            var handler = GetRequestHandler(sessionId);
            var limits = handler.Limits ?? Config.Limits;
            if (ExceedsDocumentLimit(request.Length, limits))
            {
                var rejected = Scratch.Rent();
                try
                {
                    var error = rejected.Output;
                    error.Clear();
                    WriteLimitError(error, handler, serializer ?? handler.Serializer ?? Config.Serializer,
                        "maxDocumentBytes", limits.MaxDocumentBytes,
                        handler.HasParseErrorHandler ? Utf8Json.ToStringUtf8(request) : null);
                    return error.ToArray();
                }
                finally { rejected.Return(); }
            }
            var scratch = Scratch.Rent();
            try
            {
                var buffer = scratch.Input(request.Length);
                request.CopyTo(buffer);
                var output = scratch.Output;
                output.Clear();
                ProcessCore(handler, limits, new ReadOnlyMemory<byte>(buffer, 0, request.Length), output, context, serializer, scratch, true);
                return output.ToArray();
            }
            finally
            {
                scratch.Return();
            }
        }

        // ------------------------------------------------------------------ strings (compatibility)

        public static void Process(JsonRpcStateAsync async, object context = null, JsonRpcSerializer serializer = null)
        {
            Process(Handler.DefaultSessionId(), async, context, serializer);
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null, JsonRpcSerializer serializer = null)
        {
            Process(sessionId, async.JsonRpc, context, serializer)
                .ContinueWith(t =>
                {
                    async.Result = t.Result;
                    async.SetCompleted();
                });
        }

        public static Task<string> Process(string jsonRpc, object context = null)
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context, null);
        }

        /// <summary>
        /// Processes on the default session with an explicit serializer. The serializer comes first so that
        /// <c>Process(sessionId, json, context)</c> can never bind here by mistake.
        /// </summary>
        public static Task<string> Process(JsonRpcSerializer serializer, string jsonRpc, object context = null)
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context, serializer);
        }

        public static Task<string> Process(string sessionId, string jsonRpc, object context = null, JsonRpcSerializer serializer = null)
        {
            return Task.Factory.StartNew(state =>
            {
                var tuple = (Tuple<string, string, object, JsonRpcSerializer>)state;
                return ProcessSync(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
            }, Tuple.Create(sessionId, jsonRpc, context, serializer));
        }

        public static string ProcessSync(string jsonRpc, object context = null)
        {
            return ProcessSync(Handler.DefaultSessionId(), jsonRpc, context, null);
        }

        /// <summary>Synchronous processing on the default session with an explicit serializer (serializer first, see <see cref="Process(JsonRpcSerializer, string, object)"/>).</summary>
        public static string ProcessSync(JsonRpcSerializer serializer, string jsonRpc, object context = null)
        {
            return ProcessSync(Handler.DefaultSessionId(), jsonRpc, context, serializer);
        }

        /// <summary>Processes a request synchronously on the calling thread. Returns the response JSON, or an empty string for notifications.</summary>
        // jsonRpcContext is deliberately required: with it optional, ProcessSync(json, null) would bind here
        // (null converts to string better than to object) with a null document.
        public static string ProcessSync(string sessionId, string jsonRpc, object jsonRpcContext, JsonRpcSerializer serializer = null)
        {
            var handler = GetRequestHandler(sessionId);
            var limits = handler.Limits ?? Config.Limits;
            int length = Encoding.UTF8.GetByteCount(jsonRpc);
            if (ExceedsDocumentLimit(length, limits))
            {
                var rejected = Scratch.Rent();
                try
                {
                    var error = rejected.Output;
                    error.Clear();
                    WriteLimitError(error, handler, serializer ?? handler.Serializer ?? Config.Serializer,
                        "maxDocumentBytes", limits.MaxDocumentBytes, handler.HasParseErrorHandler ? jsonRpc : null);
                    return error.ToString();
                }
                finally { rejected.Return(); }
            }
            var scratch = Scratch.Rent();
            try
            {
                var buffer = scratch.Input(length);
                Encoding.UTF8.GetBytes(jsonRpc, 0, jsonRpc.Length, buffer, 0);
                var output = scratch.Output;
                output.Clear();
                ProcessCore(handler, limits, new ReadOnlyMemory<byte>(buffer, 0, length), output, jsonRpcContext, serializer, scratch, true);
                return output.ToString();
            }
            finally
            {
                scratch.Return();
            }
        }

        // ------------------------------------------------------------------ core

        private static void ProcessCore(Handler handler, JsonRpcLimits limits, ReadOnlyMemory<byte> document, IBufferWriter<byte> destination, object context, JsonRpcSerializer serializer, Scratch scratch, bool destinationIsScratch = false)
        {
            serializer = serializer ?? handler.Serializer ?? Config.Serializer;

            // Always render into the rewindable scratch buffer, then hand the bytes to the caller's writer.
            PooledByteBufferWriter output = scratch.Output;
            if (!destinationIsScratch) output.Clear();

            var reader = scratch.GetReader(serializer);
            try
            {
                if (!reader.TryParse(document, out var parseError))
                {
                    var ex = new JsonRpcException(-32700, "Parse error", parseError);
                    if (handler.HasParseErrorHandler) ex = handler.ProcessParseException(Utf8Json.ToStringUtf8(document.Span), ex);
                    Handler.WriteErrorEnvelope(output, serializer, ex, default);
                }
                else if (!reader.IsBatch)
                {
                    handler.HandleRequest(reader, 0, serializer, output, context);
                }
                else if (limits.MaxBatchCount != 0 && reader.Count > limits.MaxBatchCount)
                {
                    WriteLimitError(output, handler, serializer, "maxBatchCount", limits.MaxBatchCount,
                        handler.HasParseErrorHandler ? Utf8Json.ToStringUtf8(document.Span) : null);
                }
                else if (reader.Count == 0)
                {
                    var ex = new JsonRpcException(-32600, "Invalid Request", "Batch of calls was empty.");
                    if (handler.HasParseErrorHandler) ex = handler.ProcessParseException(Utf8Json.ToStringUtf8(document.Span), ex);
                    Handler.WriteErrorEnvelope(output, serializer, ex, default);
                }
                else
                {
                    int start = output.WrittenCount;
                    output.Write((byte)'[');
                    int written = 0;
                    for (int i = 0; i < reader.Count; i++)
                    {
                        int before = output.WrittenCount;
                        if (written > 0) output.Write((byte)',');
                        if (handler.HandleRequest(reader, i, serializer, output, context)) written++;
                        else output.Rewind(before);
                    }
                    // A batch answers with an array whenever it produced a response (even a single one);
                    // a batch of notifications only produces nothing at all.
                    if (written == 0)
                    {
                        output.Rewind(start);
                    }
                    else
                    {
                        output.Write((byte)']');
                    }
                }
            }
            finally
            {
                reader.Release();
            }

            if (!destinationIsScratch && output.WrittenCount > 0)
            {
                output.CopyTo(destination);
            }
        }

        private static Handler GetRequestHandler(string sessionId)
        {
            return Handler.TryGetSessionHandler(sessionId, out var handler) ? handler : Handler.UnknownSessionHandler;
        }

        private static bool ExceedsDocumentLimit(long length, JsonRpcLimits limits)
        {
            return limits.MaxDocumentBytes != 0 && length > limits.MaxDocumentBytes;
        }

        private static void WriteLimitError(PooledByteBufferWriter output, Handler handler, JsonRpcSerializer serializer,
            string limit, long maximum, string rawDocument)
        {
            var error = new JsonRpcException(-32600, "Invalid Request", new LimitExceededInfo(limit, maximum));
            if (handler.HasParseErrorHandler) error = handler.ProcessParseException(rawDocument, error);
            Handler.WriteErrorEnvelope(output, serializer, error, default);
        }

        private static void RejectDocument(Handler handler, JsonRpcSerializer serializer, IBufferWriter<byte> destination,
            long maximum, string rawDocument)
        {
            var scratch = Scratch.Rent();
            try
            {
                var output = scratch.Output;
                output.Clear();
                WriteLimitError(output, handler, serializer ?? handler.Serializer ?? Config.Serializer,
                    "maxDocumentBytes", maximum, rawDocument);
                output.CopyTo(destination);
            }
            finally { scratch.Return(); }
        }

        /// <summary>Per-thread pooled buffers and a cached reader. Re-entrant calls get a fresh instance.</summary>
        private sealed class Scratch
        {
            [ThreadStatic] private static Scratch _current;

            private byte[] _input = ArrayPool<byte>.Shared.Rent(4096);
            public readonly PooledByteBufferWriter Output = new PooledByteBufferWriter(4096);
            private JsonRpcSerializer _readerOwner;
            private JsonRpcRequestReader _reader;
            private bool _inUse;

            public static Scratch Rent()
            {
                var s = _current;
                if (s == null)
                {
                    s = new Scratch();
                    _current = s;
                }
                else if (s._inUse)
                {
                    return new Scratch { _inUse = true };
                }
                s._inUse = true;
                return s;
            }

            public void Return()
            {
                if (ReferenceEquals(this, _current))
                {
                    _inUse = false;
                    return;
                }
                // a nested (re-entrant) instance is used once: give its buffers back to the pool
                ArrayPool<byte>.Shared.Return(_input);
                _input = null;
                Output.Dispose();
            }

            public byte[] Input(int length)
            {
                if (_input.Length < length)
                {
                    ArrayPool<byte>.Shared.Return(_input);
                    _input = ArrayPool<byte>.Shared.Rent(length);
                }
                return _input;
            }

            public JsonRpcRequestReader GetReader(JsonRpcSerializer serializer)
            {
                if (!ReferenceEquals(_readerOwner, serializer) || _reader == null)
                {
                    _reader = serializer.CreateReader();
                    _readerOwner = serializer;
                }
                return _reader;
            }
        }
    }
}
