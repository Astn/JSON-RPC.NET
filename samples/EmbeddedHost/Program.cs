using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using EmbeddedHost;

// Modes:
//   http [prefix]   HttpListener at http://127.0.0.1:5078/rpc/ (or the prefix given)
//   pipe [name]     named pipe "jsonrpc-embedded" (or the name given), newline-delimited documents
//   ui   [name]     the pipe host, every document run on a thread that owns the state
//   check           all three in process, driven by a client; exit code 0 when every check passes
string mode = args.Length > 0 ? args[0] : "check";
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

switch (mode)
{
    case "http":
    {
        var operations = new Operations("embedded-http");
        string prefix = args.Length > 1 ? args[1] : "http://127.0.0.1:5078/rpc/";
        Console.WriteLine("POST " + prefix + " (Ctrl+C stops)");
        await HttpListenerHost.RunAsync(operations.Session, prefix, stop.Token);
        return 0;
    }
    case "pipe":
    {
        var operations = new Operations("embedded-pipe");
        string name = args.Length > 1 ? args[1] : "jsonrpc-embedded";
        Console.WriteLine("named pipe " + name + " (Ctrl+C stops)");
        await StreamHost.RunPipeAsync(name, StreamHost.DirectDispatcher(operations.Session), operations, stop.Token);
        return 0;
    }
    case "ui":
    {
        using var ui = new UiThread();
        var operations = new Operations("embedded-ui", ui.ManagedThreadId);
        string name = args.Length > 1 ? args[1] : "jsonrpc-embedded";
        Console.WriteLine("named pipe " + name + ", methods run on thread " + ui.ManagedThreadId + " (Ctrl+C stops)");
        await StreamHost.RunPipeAsync(name, ui.Dispatcher(operations.Session), operations, stop.Token);
        return 0;
    }
    case "check":
        return await Check.RunAsync(stop.Token) ? 0 : 1;
    default:
        Console.Error.WriteLine("usage: EmbeddedHost http [prefix] | pipe [name] | ui [name] | check");
        return 2;
}

static class Check
{
    private static int _failures;

    public static async Task<bool> RunAsync(CancellationToken stop)
    {
        using var hosts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        await CheckHttpAsync(hosts.Token);
        await CheckPipeAsync(hosts.Token);
        await CheckUiAsync(hosts.Token);
        hosts.Cancel();
        Console.WriteLine(_failures == 0 ? "all checks passed" : _failures + " check(s) failed");
        return _failures == 0;
    }

    private static async Task CheckHttpAsync(CancellationToken stop)
    {
        var operations = new Operations("check-http");
        string prefix = "http://127.0.0.1:" + FreePort() + "/rpc/";
        var host = HttpListenerHost.RunAsync(operations.Session, prefix, stop);
        using var client = new HttpClient();

        var ok = await client.PostAsync(prefix, Json("{\"jsonrpc\":\"2.0\",\"method\":\"add\",\"params\":[1,2],\"id\":1}"), stop);
        Expect("http add", (int)ok.StatusCode == 200 && await ok.Content.ReadAsStringAsync(stop) == "{\"jsonrpc\":\"2.0\",\"result\":3.0,\"id\":1}");

        var notification = await client.PostAsync(prefix, Json("{\"jsonrpc\":\"2.0\",\"method\":\"notes/set\",\"params\":[\"a\",\"first\"]}"), stop);
        Expect("http notification is 204", (int)notification.StatusCode == 204);

        var missing = await client.PostAsync(prefix, Json("{\"jsonrpc\":\"2.0\",\"method\":\"notes/get\",\"params\":{\"key\":\"zz\"},\"id\":2}"), stop);
        string body = await missing.Content.ReadAsStringAsync(stop);
        Expect("http application error 1001 with data", body.Contains("\"code\":1001") && body.Contains("\"Key\":\"zz\""), body);

        var wait = await client.PostAsync(prefix, Json("{\"jsonrpc\":\"2.0\",\"method\":\"wait\",\"params\":{\"milliseconds\":10,\"result\":9},\"id\":3}"), stop);
        Expect("http token mid-signature, named params", (await wait.Content.ReadAsStringAsync(stop)).Contains("\"result\":9"));

        var get = await client.GetAsync(prefix, stop);
        Expect("http GET is 405", (int)get.StatusCode == 405);
        operations.Dispose();
    }

    private static async Task CheckPipeAsync(CancellationToken stop)
    {
        var operations = new Operations("check-pipe");
        string name = "jsonrpc-check-" + Environment.ProcessId;
        _ = StreamHost.RunPipeAsync(name, StreamHost.DirectDispatcher(operations.Session), operations, stop);

        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, stop);
        // Pipelined: three requests and a notification in one write, before reading anything.
        string batch = "{\"jsonrpc\":\"2.0\",\"method\":\"add\",\"params\":[1,2],\"id\":1}\n"
            + "{\"jsonrpc\":\"2.0\",\"method\":\"notes/set\",\"params\":[\"k\",\"v\"]}\n"
            + "{\"jsonrpc\":\"2.0\",\"method\":\"notes/get\",\"params\":[\"k\"],\"id\":2}"
            + "{\"jsonrpc\":\"2.0\",\"method\":\"wait\",\"params\":[5],\"id\":3}\n";
        await pipe.WriteAsync(Encoding.UTF8.GetBytes(batch), stop);
        await pipe.FlushAsync(stop);
        var reader = new StreamReader(pipe, Encoding.UTF8);
        var lines = new List<string>();
        for (int i = 0; i < 4; i++) lines.Add(await reader.ReadLineAsync(stop));
        Expect("pipe reply 1", lines[0] == "{\"jsonrpc\":\"2.0\",\"result\":3.0,\"id\":1}", lines[0]);
        Expect("pipe outbound notification after notes/set", lines[1] == "{\"jsonrpc\":\"2.0\",\"method\":\"notes/changed\",\"params\":{\"key\":\"k\"}}", lines[1]);
        Expect("pipe reply 2, no separator needed between requests", lines[2] == "{\"jsonrpc\":\"2.0\",\"result\":\"v\",\"id\":2}", lines[2]);
        Expect("pipe reply 3, positional params skip the token", lines[3] == "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":3}", lines[3]);

        await pipe.WriteAsync(Encoding.UTF8.GetBytes("this is not json\n"), stop);
        await pipe.FlushAsync(stop);
        string parseError = await reader.ReadLineAsync(stop);
        Expect("pipe garbage answers -32700 and closes", parseError != null && parseError.Contains("-32700") && await reader.ReadLineAsync(stop) == null, parseError);
        operations.Dispose();
    }

    private static async Task CheckUiAsync(CancellationToken stop)
    {
        using var ui = new UiThread();
        var operations = new Operations("check-ui", ui.ManagedThreadId);
        string name = "jsonrpc-check-ui-" + Environment.ProcessId;
        _ = StreamHost.RunPipeAsync(name, ui.Dispatcher(operations.Session), operations, stop);

        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, stop);
        var reader = new StreamReader(pipe, Encoding.UTF8);
        await pipe.WriteAsync(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"method\":\"thread\",\"id\":1}\n{\"jsonrpc\":\"2.0\",\"method\":\"notes/set\",\"params\":[\"a\",\"b\"],\"id\":2}\n"), stop);
        await pipe.FlushAsync(stop);
        string thread = await reader.ReadLineAsync(stop);
        Expect("ui method body runs on the owning thread", thread == "{\"jsonrpc\":\"2.0\",\"result\":" + ui.ManagedThreadId + ",\"id\":1}", thread);
        string changed = await reader.ReadLineAsync(stop);
        string set = await reader.ReadLineAsync(stop);
        Expect("ui state guarded by thread is reachable through the queue", changed.Contains("notes/changed") && set == "{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":2}", set);
        operations.Dispose();
    }

    private static StringContent Json(string document) => new(document, Encoding.UTF8, "application/json");

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void Expect(string check, bool condition, string detail = null)
    {
        if (!condition) _failures++;
        Console.WriteLine((condition ? "ok   " : "FAIL ") + check + (condition || detail == null ? "" : ": " + detail));
    }
}
