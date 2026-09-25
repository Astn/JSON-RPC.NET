using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AustinHarris.JsonRpc.AspNetCore
{
    /// <summary>
    /// JSON-RPC over a raw Kestrel connection (TCP, Unix socket, named pipe): clients send JSON documents back to
    /// back (optionally whitespace / newline separated) and receive responses in order. Wire it up with
    /// <c>kestrel.ListenLocalhost(port, l => l.UseConnectionHandler&lt;JsonRpcConnectionHandler&gt;())</c>.
    /// The connection's <see cref="ConnectionContext"/> is the RPC context for every call.
    /// When a scoped or transient service is bound to the handler's session, each document runs inside one
    /// host-owned service scope, published on the connection as <see cref="IServiceProvidersFeature"/> and disposed
    /// once the document's response is written; the scope never spans documents.
    /// </summary>
    public partial class JsonRpcConnectionHandler : ConnectionHandler
    {
        private readonly JsonRpcOptions _options;
        /// <summary>Set only when a scoped or transient service is bound to this handler's session: the cost of a scope per document is opt-in.</summary>
        private readonly IServiceScopeFactory _scopes;

        public JsonRpcConnectionHandler(IOptions<JsonRpcOptions> options, IServiceProvider services = null)
        {
            _options = options?.Value ?? new JsonRpcOptions();
            _scopes = NeedsDocumentScope(services) ? services.GetService<IServiceScopeFactory>() : null;
        }

        private bool NeedsDocumentScope(IServiceProvider services)
        {
            var registrations = services?.GetService<IEnumerable<JsonRpcServiceCollectionExtensions.JsonRpcServiceRegistration>>();
            if (registrations == null) return false;
            string session = _options.SessionId ?? Handler.DefaultSessionId();
            foreach (var r in registrations)
            {
                if (r.Lifetime == ServiceLifetime.Singleton) continue;
                if ((r.SessionId ?? _options.SessionId ?? Handler.DefaultSessionId()) == session) return true;
            }
            return false;
        }

        /// <summary>
        /// One scope per document: created from <see cref="IServiceScopeFactory"/>, handed to the methods through the
        /// connection's <see cref="IServiceProvidersFeature"/> (what the default <see cref="JsonRpcOptions.ServiceProviderSelector"/>
        /// reads), removed and disposed once the whole document, batch included, has been answered. Every call of a
        /// batch shares it; a transient service is still created per call.
        /// </summary>
        private void ProcessInScope(string session, in ReadOnlySequence<byte> document, IBufferWriter<byte> output, ConnectionContext connection)
        {
            using (var scope = _scopes.CreateScope())
            {
                connection.Features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
                try
                {
                    JsonRpcProcessor.Process(session, in document, output, connection, _options.Serializer);
                }
                finally
                {
                    connection.Features.Set<IServiceProvidersFeature>(null);
                }
            }
        }

        /// <summary>Processes a connection using the hosting mode selected in options.</summary>
        public override Task OnConnectedAsync(ConnectionContext connection)
        {
            return _options.EnableAsyncMethods ? RunAsynchronousMethodsAsync(connection) : RunSynchronousMethodsAsync(connection);
        }

        private async Task RunSynchronousMethodsAsync(ConnectionContext connection)
        {
            var input = connection.Transport.Input;
            var output = connection.Transport.Output;
            string session = _options.SessionId ?? Handler.DefaultSessionId();

            while (true)
            {
                var result = await input.ReadAsync(connection.ConnectionClosed).ConfigureAwait(false);
                var buffer = result.Buffer;
                bool wrote = false;

                while (JsonFramer.TryReadDocument(ref buffer, out var document))
                {
                    // the framer hands back a one-byte slice for anything that is not '{' or '[': drop it
                    if (document.Length > 1 || document.First.Span[0] == (byte)'{' || document.First.Span[0] == (byte)'[')
                    {
                        if (document.Length > _options.MaxRequestBytes)
                        {
                            connection.Abort(new ConnectionAbortedException("JSON-RPC document exceeds MaxRequestBytes."));
                            return;
                        }
                        if (_scopes == null) JsonRpcProcessor.Process(session, in document, output, connection, _options.Serializer);
                        else ProcessInScope(session, in document, output, connection);
                        wrote = true;
                    }
                }

                if (wrote)
                {
                    var flush = await output.FlushAsync(connection.ConnectionClosed).ConfigureAwait(false);
                    if (flush.IsCompleted || flush.IsCanceled) break;
                }

                if (result.IsCompleted || result.IsCanceled)
                {
                    input.AdvanceTo(buffer.Start, buffer.End);
                    break;
                }
                if (buffer.Length > _options.MaxRequestBytes)
                {
                    connection.Abort(new ConnectionAbortedException("JSON-RPC document exceeds MaxRequestBytes."));
                    return;
                }
                // consumed up to the last complete document, examined everything
                input.AdvanceTo(buffer.Start, buffer.End);
            }
        }
    }
}
