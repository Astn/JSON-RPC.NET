using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;
using Microsoft.AspNetCore.Http;

namespace AustinHarris.JsonRpc.AspNetCore
{
    /// <summary>
    /// The HTTP request handler: reads the body through <see cref="PipeReader"/>, runs the processor on the
    /// <see cref="ReadOnlySequence{T}"/> it yields, and writes the response straight into
    /// <see cref="HttpResponse.BodyWriter"/>. No strings, no intermediate byte arrays.
    /// </summary>
    public static class JsonRpcEndpoint
    {
        /// <summary>Processes an HTTP request using the synchronous or asynchronous mode selected in options.</summary>
        public static Task HandleAsync(HttpContext http, JsonRpcOptions options)
        {
            return options?.EnableAsyncMethods == true ? HandleAsynchronousMethodsAsync(http, options) : HandleSynchronousMethodsAsync(http, options);
        }

        internal static async Task HandleSynchronousMethodsAsync(HttpContext http, JsonRpcOptions options)
        {
            options ??= new JsonRpcOptions();
            if (!HttpMethods.IsPost(http.Request.Method))
            {
                http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                http.Response.Headers.Allow = "POST";
                return;
            }

            var reader = http.Request.BodyReader;
            var ct = http.RequestAborted;
            ReadResult result;
            while (true)
            {
                result = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled) break;
                if (result.Buffer.Length > options.MaxRequestBytes)
                {
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                // nothing consumed, everything examined: ReadAsync waits for more bytes
                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }

            var buffer = result.Buffer;
            try
            {
                if (buffer.Length > options.MaxRequestBytes)
                {
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                string session = options.SessionSelector?.Invoke(http) ?? options.SessionId ?? Handler.DefaultSessionId();
                object context = options.ContextFactory != null ? options.ContextFactory(http) : http;

                http.Response.ContentType = options.ResponseContentType;
                var counting = new CountingBufferWriter(http.Response.BodyWriter);
                JsonRpcProcessor.Process(session, in buffer, counting, context, options.Serializer);

                if (counting.Written == 0)
                {
                    if (options.NoContentForNotifications)
                    {
                        http.Response.ContentType = null;
                        http.Response.StatusCode = StatusCodes.Status204NoContent;
                    }
                    else
                    {
                        http.Response.ContentLength = 0;
                    }
                    return;
                }
                await http.Response.BodyWriter.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                reader.AdvanceTo(buffer.End);
            }
        }

        internal static async Task HandleAsynchronousMethodsAsync(HttpContext http, JsonRpcOptions options)
        {
            options ??= new JsonRpcOptions();
            if (!HttpMethods.IsPost(http.Request.Method))
            {
                http.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
                http.Response.Headers.Allow = "POST";
                return;
            }

            var reader = http.Request.BodyReader;
            var ct = http.RequestAborted;
            ReadResult result;
            while (true)
            {
                result = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (result.IsCompleted || result.IsCanceled) break;
                if (result.Buffer.Length > options.MaxRequestBytes)
                {
                    reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                // nothing consumed, everything examined: ReadAsync waits for more bytes
                reader.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            }

            var buffer = result.Buffer;
            try
            {
                if (buffer.Length > options.MaxRequestBytes)
                {
                    http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                string session = options.SessionSelector?.Invoke(http) ?? options.SessionId ?? Handler.DefaultSessionId();
                object context = options.ContextFactory != null ? options.ContextFactory(http) : http;

                http.Response.ContentType = options.ResponseContentType;
                var counting = new CountingBufferWriter(http.Response.BodyWriter);
                await JsonRpcProcessor.ProcessAsync(session, buffer, counting, context, options.Serializer, ct).ConfigureAwait(false);

                if (counting.Written == 0)
                {
                    if (options.NoContentForNotifications)
                    {
                        http.Response.ContentType = null;
                        http.Response.StatusCode = StatusCodes.Status204NoContent;
                    }
                    else
                    {
                        http.Response.ContentLength = 0;
                    }
                    return;
                }
                await http.Response.BodyWriter.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                reader.AdvanceTo(buffer.End);
            }
        }

        /// <summary>Tracks how many bytes a processor call advanced, so an empty (notification) response can be detected before headers are sent.</summary>
        internal sealed class CountingBufferWriter : IBufferWriter<byte>
        {
            private readonly IBufferWriter<byte> _inner;
            public long Written;

            public CountingBufferWriter(IBufferWriter<byte> inner) { _inner = inner; }

            public void Advance(int count) { Written += count; _inner.Advance(count); }
            public Memory<byte> GetMemory(int sizeHint = 0) => _inner.GetMemory(sizeHint);
            public Span<byte> GetSpan(int sizeHint = 0) => _inner.GetSpan(sizeHint);
        }
    }
}
