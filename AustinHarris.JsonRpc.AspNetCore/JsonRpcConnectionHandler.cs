using System;
using System.Buffers;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Serialization;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;

namespace AustinHarris.JsonRpc.AspNetCore
{
    /// <summary>
    /// JSON-RPC over a raw Kestrel connection (TCP, Unix socket, named pipe): clients send JSON documents back to
    /// back (optionally whitespace / newline separated) and receive responses in order. Wire it up with
    /// <c>kestrel.ListenLocalhost(port, l => l.UseConnectionHandler&lt;JsonRpcConnectionHandler&gt;())</c>.
    /// The connection's <see cref="ConnectionContext"/> is the RPC context for every call.
    /// </summary>
    public partial class JsonRpcConnectionHandler : ConnectionHandler
    {
        private readonly JsonRpcOptions _options;

        public JsonRpcConnectionHandler(IOptions<JsonRpcOptions> options)
        {
            _options = options?.Value ?? new JsonRpcOptions();
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
                        JsonRpcProcessor.Process(session, in document, output, connection, _options.Serializer);
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
