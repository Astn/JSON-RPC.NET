using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using AustinHarris.JsonRpc;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

// A JSON-RPC server that runs inside the browser. There is no HTTP: JavaScript hands request text to
// JsonRpcProcessor through JS interop and gets the response text back, on the same thread, in the page.
// Useful for running the same service code in the browser and on the server, for offline tools, and for
// sandboxing untrusted-input handling in WebAssembly.

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Constructing a JsonRpcService registers it with the default session, exactly as it does on a server.
_ = new CalculatorService();

await builder.Build().RunAsync();

public class CalculatorService : JsonRpcService
{
    [JsonRpcMethod]                 // "add"
    private double add(double l, double r) => l + r;

    [JsonRpcMethod("echo")]
    public string Echo(string s) => s;

    [JsonRpcMethod("platform")]
    public string Platform() => $"{Environment.OSVersion} / {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
}

/// <summary>
/// The JavaScript-facing surface. Three ways in, so the page can benchmark one against the other:
/// <list type="bullet">
/// <item><see cref="Process"/> / <see cref="ProcessExported"/>: a JSON-RPC document as a string in, the response
/// string out. Any method the service exposes is reachable through this one entry point, and a batch is one call.</item>
/// <item><see cref="ProcessBytes"/>: the same through two pinned byte buffers that JavaScript writes and reads
/// directly (UTF-8 in WebAssembly memory), so no string is marshalled or transcoded in either direction. This is
/// the byte-first entry point the Kestrel package uses.</item>
/// <item><see cref="Add"/> / <see cref="AddExported"/>: plain Blazor interop, one exported method per operation
/// with typed arguments that the runtime marshals itself.</item>
/// </list>
/// <c>[JSInvokable]</c> methods are called as <c>DotNet.invokeMethod('WasmHost', name, ...)</c> (or
/// <c>invokeMethodAsync</c>); Blazor serialises the argument array and the result with System.Text.Json.
/// <c>[JSExport]</c> methods are reached through <c>getAssemblyExports('WasmHost.dll')</c> and marshal each
/// parameter directly, without a JSON round trip.
/// </summary>
public static partial class JsonRpcInterop
{
    // ---- JSON-RPC as strings: one entry point for every method, and for batches ----

    [JSInvokable("Process")]
    public static string Process(string json) => JsonRpcProcessor.ProcessSync(json);

    [JSExport]
    public static string ProcessExported(string json) => JsonRpcProcessor.ProcessSync(json);

    /// <summary>
    /// Runs the same request <paramref name="count"/> times inside .NET and returns the elapsed milliseconds:
    /// the cost of the JSON-RPC server itself in this runtime, with no interop in the loop.
    /// </summary>
    [JSInvokable("ProcessMany")]
    public static double ProcessMany(string json, int count)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++) JsonRpcProcessor.ProcessSync(json);
        return sw.Elapsed.TotalMilliseconds;
    }

    // ---- JSON-RPC as bytes: JavaScript writes UTF-8 into the input buffer and reads the output buffer ----

    private const int BufferSize = 1 << 20;
    // Pinned so the MemoryViews handed to JavaScript stay valid for the life of the page.
    private static readonly byte[] _input = GC.AllocateArray<byte>(BufferSize, pinned: true);
    private static readonly byte[] _output = GC.AllocateArray<byte>(BufferSize, pinned: true);
    private static readonly FixedBufferWriter _outputWriter = new FixedBufferWriter(_output);
    private static readonly string _defaultSession = Handler.DefaultSessionId();

    /// <summary>JavaScript side of the buffer hand-off: receives views of the two pinned arrays.</summary>
    [JSImport("buffersReady", "wasmhost")]
    private static partial void BuffersReady([JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> input, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> output);

    /// <summary>Hands JavaScript zero-copy views of the request and response buffers. Call once after start-up.</summary>
    [JSExport]
    public static void ExposeBuffers() => BuffersReady(new ArraySegment<byte>(_input), new ArraySegment<byte>(_output));

    /// <summary>
    /// Processes the first <paramref name="length"/> bytes of the input buffer (one document or a batch, UTF-8)
    /// and returns how many bytes of response were written to the output buffer (0 for a notification).
    /// </summary>
    [JSExport]
    public static int ProcessBytes(int length)
    {
        _outputWriter.Reset();
        JsonRpcProcessor.Process(_defaultSession, new ReadOnlySpan<byte>(_input, 0, length), _outputWriter);
        return _outputWriter.Written;
    }

    /// <summary>True when the app was AOT-compiled (published with the wasm-tools workload).</summary>
    [JSExport]
    public static bool IsAot() =>
#if WASM_AOT
        true;
#else
        false;
#endif

    // ---- plain Blazor interop: one exported method per operation ----

    [JSInvokable("Add")]
    public static double Add(double l, double r) => l + r;

    [JSExport]
    public static double AddExported(double l, double r) => l + r;

    /// <summary>An <see cref="IBufferWriter{T}"/> over a fixed array; throws if a response would not fit.</summary>
    private sealed class FixedBufferWriter : IBufferWriter<byte>
    {
        private readonly byte[] _buffer;
        public FixedBufferWriter(byte[] buffer) => _buffer = buffer;
        public int Written { get; private set; }
        public void Reset() => Written = 0;
        public void Advance(int count) => Written += count;
        public Memory<byte> GetMemory(int sizeHint = 0) => Check(sizeHint) ? _buffer.AsMemory(Written) : throw Overflow();
        public Span<byte> GetSpan(int sizeHint = 0) => Check(sizeHint) ? _buffer.AsSpan(Written) : throw Overflow();
        private bool Check(int sizeHint) => _buffer.Length - Written >= Math.Max(sizeHint, 1);
        private static Exception Overflow() => new InvalidOperationException("The response does not fit in the output buffer.");
    }
}
