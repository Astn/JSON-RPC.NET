using System;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace AustinHarris.JsonRpc.AspNetCore
{
    public partial class JsonRpcConnectionHandler
    {
        private async Task RunAsynchronousMethodsAsync(ConnectionContext connection)
        {
            var input = connection.Transport.Input;
            var output = connection.Transport.Output;
            var token = connection.ConnectionClosed;
            string session = _options.SessionId ?? Handler.DefaultSessionId();
            // Keep result commits separate from transport flushes: a method can finish on another
            // thread while an earlier reply is waiting for transport backpressure.
            using var reply = new PooledByteBufferWriter();
            try
            {
                while (true)
                {
                    var result = await input.ReadAsync(token).ConfigureAwait(false);
                    var buffer = result.Buffer;
                    bool wrote = false;
                    try
                    {
                        while (JsonFramer.TryReadDocument(ref buffer, out var document))
                        {
                            token.ThrowIfCancellationRequested();
                            if (document.Length <= 1 && document.First.Span[0] != (byte)'{' && document.First.Span[0] != (byte)'[') continue;
                            if (document.Length > _options.MaxRequestBytes)
                            {
                                connection.Abort(new ConnectionAbortedException("JSON-RPC document exceeds MaxRequestBytes."));
                                return;
                            }
                            reply.Clear();
                            // The document's service scope, when a scoped or transient service is bound: published for
                            // the duration of the document and disposed asynchronously after its last response is
                            // written, which is after the running operation has been awaited on every path below.
                            var scope = _scopes == null ? default : _scopes.CreateAsyncScope();
                            if (_scopes != null) connection.Features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
                            try
                            {
                                var pending = JsonRpcProcessor.ProcessAsync(session, document, reply, connection, _options.Serializer, token);
                                if (!pending.IsCompleted && wrote)
                                {
                                    bool closed = false;
                                    try
                                    {
                                        var flush = await output.FlushAsync(token).ConfigureAwait(false);
                                        closed = flush.IsCompleted || flush.IsCanceled;
                                        wrote = false;
                                    }
                                    finally
                                    {
                                        // Even a failed flush cannot release the input or reply while invocation runs.
                                        await pending.ConfigureAwait(false);
                                    }
                                    if (closed) return;
                                }
                                else await pending.ConfigureAwait(false);
                                token.ThrowIfCancellationRequested();
                                if (reply.WrittenCount != 0) { reply.CopyTo(output); wrote = true; }
                            }
                            finally
                            {
                                if (_scopes != null)
                                {
                                    connection.Features.Set<IServiceProvidersFeature>(null);
                                    await scope.DisposeAsync().ConfigureAwait(false);
                                }
                            }
                        }
                        if (wrote)
                        {
                            var flush = await output.FlushAsync(token).ConfigureAwait(false);
                            if (flush.IsCompleted || flush.IsCanceled) return;
                        }
                        if (result.IsCompleted || result.IsCanceled) return;
                        if (buffer.Length > _options.MaxRequestBytes)
                        {
                            connection.Abort(new ConnectionAbortedException("JSON-RPC document exceeds MaxRequestBytes."));
                            return;
                        }
                    }
                    finally { input.AdvanceTo(buffer.Start, buffer.End); }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ConnectionAbortedException) { }
        }
    }
}
