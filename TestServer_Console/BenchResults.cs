using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AustinHarris.JsonRpc;

namespace TestServer_Console;

/// <summary>
/// Machine-readable results for benchmarks/charts/ingest.py. When <c>JSONRPC_BENCH_RESULTS=&lt;dir&gt;</c> is set, each
/// published mode writes <c>&lt;dir&gt;/&lt;stem&gt;-&lt;run&gt;.json</c> beside its text output, with the run number from
/// <c>JSONRPC_BENCH_RUN</c> (1 when unset) and the commit from <c>JSONRPC_BENCH_COMMIT</c>. Every row carries the id of the
/// benchmarks.json series it feeds (or an id ingest.py lists as unpublished), its RPC/s and the CPU of its timed window.
/// Nothing here runs inside a timed loop.
/// </summary>
internal static class BenchResults
{
    /// <summary>The output directory, or null when results are not requested.</summary>
    internal static string Directory
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("JSONRPC_BENCH_RESULTS");
            return string.IsNullOrWhiteSpace(dir) ? null : dir;
        }
    }

    /// <summary>True when <c>JSONRPC_BENCH_RESULTS</c> is set.</summary>
    internal static bool Enabled => Directory != null;

    /// <summary>The run number for the file name: <c>JSONRPC_BENCH_RUN</c>, or 1.</summary>
    internal static int Run => int.TryParse(Environment.GetEnvironmentVariable("JSONRPC_BENCH_RUN"), out int run) && run > 0 ? run : 1;

    /// <summary>The runtime as the results record it: framework, build configuration and GC mode.</summary>
    internal static string Runtime =>
#if DEBUG
        $"{RuntimeInformation.FrameworkDescription}, Debug, {(GCSettings.IsServerGC ? "Server GC" : "Workstation GC")}";
#else
        $"{RuntimeInformation.FrameworkDescription}, Release, {(GCSettings.IsServerGC ? "Server GC" : "Workstation GC")}";
#endif

    /// <summary>A results document with the fields every mode shares; the caller adds its parameters and rows.</summary>
    internal static JsonObject Document(string mode, double seconds) => new JsonObject
    {
        ["mode"] = mode,
        ["machine"] = MachineDescription.Current(),
        ["runtime"] = Runtime,
        ["serializer"] = Config.Serializer.Name,
        ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        ["seconds"] = seconds,
        ["commit"] = Environment.GetEnvironmentVariable("JSONRPC_BENCH_COMMIT") is { Length: > 0 } commit ? commit : null,
        ["run"] = Run,
        ["cores"] = Environment.ProcessorCount,
    };

    /// <summary>One result row: its series id, RPC/s rounded to a whole request, the request count and the CPU of the window.</summary>
    internal static JsonObject Row(string id, double rpcPerSec, long rpcs, CpuUsage cpu)
    {
        var row = new JsonObject { ["id"] = id, ["rpcPerSec"] = Math.Round(rpcPerSec), ["rpcs"] = rpcs };
        var usage = Cpu(cpu);
        if (usage.Count > 0) row["cpu"] = usage;
        return row;
    }

    /// <summary>The CPU percentages that were measured, to one decimal; a figure the platform cannot provide is left out.</summary>
    internal static JsonObject Cpu(CpuUsage cpu)
    {
        var o = new JsonObject();
        if (cpu.SystemPercent is double system) o["system"] = Math.Round(system, 1);
        if (cpu.ProcessPercent is double process) o["process"] = Math.Round(process, 1);
        if (cpu.ClientPercent is double clients) o["clients"] = Math.Round(clients, 1);
        return o;
    }

    /// <summary>The path a mode's file goes to: <c>&lt;dir&gt;/&lt;stem&gt;-&lt;run&gt;.json</c>.</summary>
    internal static string PathFor(string stem) => Path.Combine(Directory, $"{stem}-{Run}.json");

    /// <summary>Writes the document as indented UTF-8 JSON without a byte order mark and prints the path.</summary>
    internal static void Write(string stem, JsonObject document, Action<string> print, string path = null)
    {
        path ??= PathFor(stem);
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
        print?.Invoke($"wrote {path}");
    }

    /// <summary>A JSON array of numbers.</summary>
    internal static JsonArray Numbers(IEnumerable<double> values) => new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)).ToArray());
}
