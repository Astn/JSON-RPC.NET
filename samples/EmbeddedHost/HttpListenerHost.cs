using System.Net;
using AustinHarris.JsonRpc.Serialization;
using AustinHarris.JsonRpc;

namespace EmbeddedHost;

/// <summary>
/// HTTP without ASP.NET Core: a plugin cannot bring the Microsoft.AspNetCore.App shared framework into a host that
/// is not an ASP.NET Core application, and System.Net.HttpListener is in the base library. What the Kestrel
/// endpoint does for you and this host has to do itself: a body limit, a deadline (HttpListener exposes no
/// "client went away" token), 204 for a notification, and the status codes.
/// </summary>
public static class HttpListenerHost
{
    private const long MaxRequestBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <param name="prefix">An HttpListener prefix such as <c>http://127.0.0.1:5078/rpc/</c> (the trailing slash matters).</param>
    public static async Task RunAsync(string session, string prefix, CancellationToken stop)
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        using var stopping = stop.Register(listener.Stop);
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) when (stop.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
            _ = ServeAsync(session, context);
        }
    }

    private static async Task ServeAsync(string session, HttpListenerContext context)
    {
        var response = context.Response;
        try
        {
            if (context.Request.HttpMethod != "POST") { response.StatusCode = 405; return; }
            if (context.Request.ContentLength64 > MaxRequestBytes) { response.StatusCode = 413; return; }

            // Read the body under the limit: ContentLength64 is -1 for a chunked body, so count while copying.
            using var body = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await context.Request.InputStream.ReadAsync(chunk)) > 0)
            {
                if (body.Length + read > MaxRequestBytes) { response.StatusCode = 413; return; }
                body.Write(chunk, 0, read);
            }

            // The only deadline is the one this host arms; the core has none and HttpListener offers no abort token.
            using var deadline = new CancellationTokenSource(Deadline);
            using var output = new PooledByteBufferWriter();
            try
            {
                await JsonRpcProcessor.ProcessAsync(session, new ReadOnlyMemory<byte>(body.GetBuffer(), 0, (int)body.Length),
                    output, context: context, cancellationToken: deadline.Token);
            }
            catch (OperationCanceledException)
            {
                response.StatusCode = 504;   // the call has terminated and wrote nothing
                return;
            }

            if (output.WrittenCount == 0) { response.StatusCode = 204; return; }   // a notification
            response.ContentType = "application/json";
            response.ContentLength64 = output.WrittenCount;
            await response.OutputStream.WriteAsync(output.WrittenMemory);
        }
        catch (Exception)
        {
            try { response.StatusCode = 500; } catch (Exception) { }
        }
        finally
        {
            response.Close();
        }
    }
}
