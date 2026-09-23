using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Serialization;
using SimdJson;
using JsonDocument = SimdJson.JsonDocument;
using JsonValueKind = SimdJson.JsonValueKind;

namespace SimdJsonEval
{
    /// <summary>
    /// Hand-rolled Stopwatch harness (warm-up, calibrated trial length, several trials, min/median) so the
    /// whole matrix runs in a couple of minutes. Allocation is measured with GC.GetAllocatedBytesForCurrentThread.
    /// Every measured function returns an int checksum that is accumulated and printed so nothing is dead code.
    /// </summary>
    internal static class Program
    {
        private const string S1 = "{\"method\":\"add\",\"params\":[1,2],\"id\":1}";
        private const string S2 = "{\"method\":\"NullableFloatToNullableFloat\",\"params\":[1.23],\"id\":3}";
        private const string S3 = "{\"method\":\"StringMe\",\"params\":[\"Foo\"],\"id\":5}";

        private static readonly byte[] KeyMethod = Encoding.ASCII.GetBytes("method");
        private static readonly byte[] KeyParams = Encoding.ASCII.GetBytes("params");
        private static readonly byte[] KeyId = Encoding.ASCII.GetBytes("id");

        private static int Main(string[] args)
        {
            bool quick = args.Contains("--quick");

            var inputs = new List<(string name, byte[] bytes)>
            {
                ("add [1,2]", Encoding.UTF8.GetBytes(S1)),
                ("NullableFloat [1.23]", Encoding.UTF8.GetBytes(S2)),
                ("StringMe [\"Foo\"]", Encoding.UTF8.GetBytes(S3)),
                ("batch x40 (~2 KB)", Encoding.UTF8.GetBytes(BuildBatch(40))),
                ("object params x30 (~2 KB)", Encoding.UTF8.GetBytes(BuildBigObject(30))),
                // a notification: no id. The jsmn reader reports IdKind.Absent for free; the simdjson binding
                // signals a missing field by throwing SimdJsonException (TryGetField is a catch wrapper).
                ("notification (no id)", Encoding.UTF8.GetBytes("{\"method\":\"add\",\"params\":[1,2]}")),
            };

            Console.WriteLine("SimdJsonEval: jsmn vs Utf8JsonReader vs SimdJson.Net (simdjson " + SimdJsonParser.GetVersion() + ", kernel " + SimdJsonParser.ActiveImplementation + ", RequiredPadding=" + SimdJsonParser.RequiredPadding + ")");
            Console.WriteLine("Runtime " + RuntimeInformation.FrameworkDescription + " on " + RuntimeInformation.OSDescription + " / " + RuntimeInformation.ProcessArchitecture + ", " + Environment.ProcessorCount + " logical cores, Server GC=" + System.Runtime.GCSettings.IsServerGC + ", tiered PGO on");
            Console.WriteLine();
            foreach (var (name, bytes) in inputs)
                Console.WriteLine($"  input '{name}': {bytes.Length} bytes");
            Console.WriteLine();

            // ---- correctness smoke test: every walker must see the same method / id / param count ----
            foreach (var (name, bytes) in inputs)
            {
                var a = Describe.Jsmn(bytes);
                var b = Describe.Simd(bytes);
                if (a != b) { Console.WriteLine($"MISMATCH on '{name}':\n  jsmn: {a}\n  simd: {b}"); return 1; }
            }
            Console.WriteLine("smoke test: jsmn reader and simdjson walk agree on method/id/param-count for every input");
            Console.WriteLine();

            var rows = new List<Row>();

            // the cost of one trivial P/Invoke through this binding (GetPadding: no arguments, returns a constant)
            rows.Add(Bench.Run("(any)", "P/Invoke floor: SimdJsonParser.RequiredPadding", () => SimdJsonParser.RequiredPadding, quick));

            foreach (var (name, bytes) in inputs)
            {
                var doc = new ReadOnlyMemory<byte>(bytes);

                var tok = new JsmnTokenizer();
                rows.Add(Bench.Run(name, "jsmn: JsmnTokenizer.Parse", () => Walks.JsmnTokenize(tok, doc.Span), quick));

                var reader = new JsmnRequestReader(JsmnSerializer.Instance);
                rows.Add(Bench.Run(name, "jsmn: JsmnRequestReader parse+locate", () => Walks.JsmnReader(reader, doc), quick));

                rows.Add(Bench.Run(name, "STJ: Utf8JsonReader full walk", () => Walks.Utf8JsonReaderWalk(doc.Span), quick));

                using (var parser = new SimdJsonParser())
                {
                    // Parse(span) alone (2 P/Invokes: parse + destroy document): the floor for any walk built on this binding.
                    rows.Add(Bench.Run(name, "simdjson: Parse(span) only, no walk", () => Walks.SimdParseOnly(parser, doc.Span), quick));

                    // Parse(span): the native side copies the bytes into its own padded buffer.
                    rows.Add(Bench.Run(name, "simdjson: Parse(span) + GetField x3", () => Walks.SimdParseGetField(parser, doc.Span), quick));
                    rows.Add(Bench.Run(name, "simdjson: Parse(span) + enumerate", () => Walks.SimdParseEnumerate(parser, doc.Span), quick));

                    // ParseInPlace: the caller supplies RequiredPadding bytes of slack. Two shapes:
                    //   (i) the transport buffer is NOT padded, so the request is copied into a padded pooled buffer first
                    //  (ii) the transport buffer was over-allocated, so no copy
                    var padded = new byte[bytes.Length + SimdJsonParser.RequiredPadding];
                    Buffer.BlockCopy(bytes, 0, padded, 0, bytes.Length);
                    var paddedMem = new ReadOnlyMemory<byte>(padded);
                    rows.Add(Bench.Run(name, "simdjson: copy+ParseInPlace + GetField x3", () => Walks.SimdCopyParseInPlaceGetField(parser, bytes, padded), quick));
                    rows.Add(Bench.Run(name, "simdjson: ParseInPlace(prepadded) + GetField x3", () => Walks.SimdParseInPlaceGetField(parser, paddedMem, bytes.Length), quick));
                }
            }

            Console.WriteLine();
            Console.WriteLine("| input | parser | ns/op (min) | ns/op (median) | B/op | checksum |");
            Console.WriteLine("|---|---|---:|---:|---:|---:|");
            foreach (var r in rows)
                Console.WriteLine($"| {r.Input} | {r.Parser} | {r.MinNs:F1} | {r.MedianNs:F1} | {r.BytesPerOp} | {r.Checksum} |");
            return 0;
        }

        private static string BuildBatch(int n)
        {
            var sb = new StringBuilder("[");
            string[] cycle = { S1, S2, S3 };
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(cycle[i % 3]);
            }
            return sb.Append(']').ToString();
        }

        private static string BuildBigObject(int members)
        {
            // ~2 KB single request whose params is one object with `members` members of mixed scalar types.
            var sb = new StringBuilder("{\"method\":\"Configure\",\"params\":{");
            for (int i = 0; i < members; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("\"member").Append(i.ToString("00")).Append("\":");
                switch (i % 5)
                {
                    case 0: sb.Append(i * 1234567L); break;
                    case 1: sb.Append((i * 3.14159).ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
                    case 2: sb.Append("\"value-").Append(i).Append("-the quick brown fox jumps over the lazy dog\""); break;
                    case 3: sb.Append(i % 2 == 0 ? "true" : "false"); break;
                    default: sb.Append("null"); break;
                }
            }
            return sb.Append("},\"id\":7}").ToString();
        }
    }

    internal readonly struct Row
    {
        public readonly string Input, Parser;
        public readonly double MinNs, MedianNs;
        public readonly long BytesPerOp;
        public readonly long Checksum;
        public Row(string input, string parser, double min, double median, long bytes, long checksum)
        { Input = input; Parser = parser; MinNs = min; MedianNs = median; BytesPerOp = bytes; Checksum = checksum; }
    }

    internal static class Bench
    {
        public static Row Run(string input, string parser, Func<int> op, bool quick)
        {
            // warm-up: JIT + tier-up (tiered PGO needs a few thousand calls before the optimised tier kicks in)
            long acc = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < (quick ? 150 : 600)) { for (int i = 0; i < 1000; i++) acc += op(); }

            // calibrate the trial length to ~200 ms
            int n = 1000;
            sw.Restart();
            for (int i = 0; i < n; i++) acc += op();
            double perOp = sw.Elapsed.TotalMilliseconds / n;
            n = (int)Math.Max(1000, Math.Min(20_000_000, (quick ? 60 : 200) / Math.Max(perOp, 1e-6)));

            int trials = quick ? 5 : 9;
            var results = new double[trials];
            for (int t = 0; t < trials; t++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                sw.Restart();
                for (int i = 0; i < n; i++) acc += op();
                sw.Stop();
                results[t] = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / n;
            }
            Array.Sort(results);

            // allocation: one extra trial, bytes allocated on this thread divided by n
            int m = Math.Max(1000, n / 10);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < m; i++) acc += op();
            long perOpBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / m;

            Console.WriteLine($"{input,-28} {parser,-46} {results[0],9:F1} ns (min) {results[trials / 2],9:F1} ns (median) {perOpBytes,6} B/op");
            return new Row(input, parser, results[0], results[trials / 2], perOpBytes, acc);
        }
    }

    internal static class Describe
    {
        public static string Jsmn(byte[] bytes)
        {
            var r = new JsmnRequestReader(JsmnSerializer.Instance);
            if (!r.TryParse(bytes, out var err)) return "parse error: " + err;
            var sb = new StringBuilder();
            for (int i = 0; i < r.Count; i++)
            {
                r.Select(i);
                sb.Append(Encoding.UTF8.GetString(r.MethodUtf8)).Append('|').Append(Encoding.UTF8.GetString(r.IdRaw)).Append('|').Append(r.ParamCount).Append(';');
            }
            r.Release();
            return sb.ToString();
        }

        public static string Simd(byte[] bytes)
        {
            using var parser = new SimdJsonParser();
            using var doc = parser.Parse(bytes);
            var sb = new StringBuilder();
            if (doc.ValueKind == JsonValueKind.Array)
            {
                using var arr = doc.GetArray();
                foreach (var item in arr) { using (item) { using var o = item.GetObject(); One(o, sb); } }
            }
            else
            {
                using var o = doc.GetObject();
                One(o, sb);
            }
            return sb.ToString();

            static void One(JsonObject o, StringBuilder sb)
            {
                using var m = o.GetField("method");
                sb.Append(m.GetString()).Append('|');
                using var p = o.GetField("params");
                int count = 0;
                if (p.ValueKind == JsonValueKind.Array) { using var a = p.GetArray(); foreach (var e in a) { e.Dispose(); count++; } }
                else if (p.ValueKind == JsonValueKind.Object) { using var po = p.GetObject(); foreach (var e in po) { e.Value.Dispose(); count++; } }
                if (o.TryGetField("id", out var id)) { using (id) sb.Append(id.GetRawJsonToken()); }
                sb.Append('|').Append(count).Append(';');
            }
        }
    }

    internal static class Walks
    {
        // (a) the tokenizer alone, one instance reused as the reader does
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int JsmnTokenize(JsmnTokenizer tok, ReadOnlySpan<byte> doc)
        {
            int n = tok.Parse(doc);
            if (n < 0) throw new InvalidOperationException("jsmn error " + n);
            return n;
        }

        // (a') the full envelope read the core performs: tokenize, then per request locate method / params / id
        // and touch the slices the binder would consume (MethodUtf8, IdRaw, ParamRaw(i), ParamNameUtf8(i))
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int JsmnReader(JsmnRequestReader r, ReadOnlyMemory<byte> doc)
        {
            if (!r.TryParse(doc, out _)) throw new InvalidOperationException("jsmn parse error");
            int acc = 0;
            for (int i = 0; i < r.Count; i++)
            {
                r.Select(i);
                acc += r.MethodUtf8.Length + r.IdRaw.Length;
                int n = r.ParamCount;
                for (int p = 0; p < n; p++) acc += r.ParamRaw(p).Length + r.ParamNameUtf8(p).Length;
            }
            r.Release();
            return acc;
        }

        // (b) System.Text.Json reference: a full forward token walk, comparing property names to the three keys
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Utf8JsonReaderWalk(ReadOnlySpan<byte> doc)
        {
            var reader = new Utf8JsonReader(doc, isFinalBlock: true, state: default);
            int acc = 0;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        if (reader.ValueTextEquals(KeyMethod) || reader.ValueTextEquals(KeyParams) || reader.ValueTextEquals(KeyId)) acc++;
                        break;
                    case JsonTokenType.String:
                    case JsonTokenType.Number:
                        acc += reader.ValueSpan.Length;
                        break;
                }
            }
            return acc;
        }

        private static readonly byte[] KeyMethod = Encoding.ASCII.GetBytes("method");
        private static readonly byte[] KeyParams = Encoding.ASCII.GetBytes("params");
        private static readonly byte[] KeyId = Encoding.ASCII.GetBytes("id");

        // (c0) simdjson On-Demand: parse and dispose, nothing else
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SimdParseOnly(SimdJsonParser parser, ReadOnlySpan<byte> doc)
        {
            using var d = parser.Parse(doc);
            return doc.Length;
        }

        // (c) simdjson On-Demand: parse, then the minimum envelope walk (method, params elements, id)
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SimdParseGetField(SimdJsonParser parser, ReadOnlySpan<byte> doc)
        {
            using var d = parser.Parse(doc);
            return SimdWalkGetField(d);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SimdCopyParseInPlaceGetField(SimdJsonParser parser, byte[] source, byte[] padded)
        {
            // what the processor would have to do when the transport buffer has no slack: copy into a padded buffer
            Buffer.BlockCopy(source, 0, padded, 0, source.Length);
            using var d = parser.ParseInPlace(padded, source.Length);
            return SimdWalkGetField(d);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SimdParseInPlaceGetField(SimdJsonParser parser, ReadOnlyMemory<byte> padded, int length)
        {
            using var d = parser.ParseInPlace(padded, length);
            return SimdWalkGetField(d);
        }

        private static int SimdWalkGetField(JsonDocument d)
        {
            if (d.ValueKind == JsonValueKind.Array)
            {
                int acc = 0;
                using var arr = d.GetArray();
                foreach (var item in arr)
                {
                    using (item)
                    {
                        using var o = item.GetObject();
                        acc += SimdRequestGetField(o);
                    }
                }
                return acc;
            }
            else
            {
                using var o = d.GetObject();
                return SimdRequestGetField(o);
            }
        }

        // GetField = simdjson find_field_unordered; the three keys are in document order so each is a forward scan
        private static int SimdRequestGetField(JsonObject o)
        {
            int acc;
            using (var m = o.GetField("method")) acc = m.GetStringSpan().Length;
            using (var p = o.GetField("params")) acc += SimdParams(p);
            // id is optional (notifications); TryGetField is the binding's non-throwing lookup, but it is
            // implemented as catch(SimdJsonException) around GetField, so a miss still costs a throw.
            if (o.TryGetField("id", out var id)) { using (id) acc += id.GetRawJsonTokenSpan().Length; }
            return acc;
        }

        private static int SimdParams(JsonValue p)
        {
            int acc = 0;
            var kind = p.ValueKind;
            if (kind == JsonValueKind.Array)
            {
                using var a = p.GetArray();
                foreach (var e in a) { acc += e.GetRawJsonTokenSpan().Length; e.Dispose(); }
            }
            else if (kind == JsonValueKind.Object)
            {
                using var po = p.GetObject();
                foreach (var prop in po) { acc += prop.EscapedNameSpan.Length + prop.Value.GetRawJsonTokenSpan().Length; prop.Value.Dispose(); }
            }
            return acc;
        }

        // (c') the same, but locating the keys by enumerating the object's members (as the jsmn reader does)
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int SimdParseEnumerate(SimdJsonParser parser, ReadOnlySpan<byte> doc)
        {
            using var d = parser.Parse(doc);
            if (d.ValueKind == JsonValueKind.Array)
            {
                int acc = 0;
                using var arr = d.GetArray();
                foreach (var item in arr)
                {
                    using (item)
                    {
                        using var o = item.GetObject();
                        acc += SimdRequestEnumerate(o);
                    }
                }
                return acc;
            }
            else
            {
                using var o = d.GetObject();
                return SimdRequestEnumerate(o);
            }
        }

        private static int SimdRequestEnumerate(JsonObject o)
        {
            int acc = 0;
            foreach (var prop in o)
            {
                var name = prop.EscapedNameSpan;
                if (name.SequenceEqual(KeyMethod)) acc += prop.Value.GetStringSpan().Length;
                else if (name.SequenceEqual(KeyParams)) acc += SimdParams(prop.Value);
                else if (name.SequenceEqual(KeyId)) acc += prop.Value.GetRawJsonTokenSpan().Length;
                prop.Value.Dispose();
            }
            return acc;
        }
    }
}
