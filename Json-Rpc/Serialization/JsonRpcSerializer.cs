using System;
using System.Buffers;
using System.Text;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// The pluggable JSON layer. The core never touches strings: requests arrive as UTF-8 bytes
    /// (a <see cref="ReadOnlySequence{T}"/> from a PipeReader, a span, or a transcoded string) and
    /// responses are written to an <see cref="IBufferWriter{T}"/> (a PipeWriter, an HTTP BodyWriter,
    /// or a pooled buffer that is transcoded back to a string).
    ///
    /// A serializer only has to convert values: raw JSON bytes to a CLR value and a CLR value to JSON bytes.
    /// Envelope scanning (method / params / id) is done by a <see cref="JsonRpcRequestReader"/>; the default
    /// reader is the built-in jsmn tokenizer, which every serializer may reuse or replace.
    /// </summary>
    public abstract class JsonRpcSerializer
    {
        /// <summary>Short name used in diagnostics and benchmarks ("jsmn", "newtonsoft", "stj", ...).</summary>
        public abstract string Name { get; }

        /// <summary>
        /// When true the default envelope reader accepts non-strict JSON (single-quoted strings, unquoted keys,
        /// trailing commas). Serializers such as Json.NET that are lenient by nature turn this on.
        /// </summary>
        public virtual bool Lenient => false;

        /// <summary>
        /// Maximum nesting depth (objects and arrays, the root counts as one) accepted by the envelope reader for
        /// this serializer. Enforced before any hook or binding runs, so it bounds every recursive reader
        /// downstream. Serializers override it to report their own limit; the default is 64, the same as
        /// System.Text.Json and Json.NET.
        /// </summary>
        public virtual int MaxDepth => Jsmn.JsmnTokenizer.DefaultMaxDepth;

        /// <summary>Creates the envelope reader used for this serializer. Readers are pooled per thread by the processor.</summary>
        public virtual JsonRpcRequestReader CreateReader() => new Jsmn.JsmnRequestReader(this);

        /// <summary>Converts a JSON value (the raw bytes of exactly one value, e.g. <c>"abc"</c>, <c>12</c>, <c>{"a":1}</c>) to <typeparamref name="T"/>.</summary>
        public abstract T Read<T>(ReadOnlySpan<byte> utf8Json);

        /// <summary>Converts a JSON value to <paramref name="type"/>. Used by the boxed compatibility path (pre/post handlers, <c>Handler.Handle</c>).</summary>
        public abstract object Read(ReadOnlySpan<byte> utf8Json, Type type);

        /// <summary>Writes <paramref name="value"/> as one JSON value, compact, no trailing whitespace.</summary>
        public abstract void Write<T>(IBufferWriter<byte> output, T value);

        /// <summary>Writes <paramref name="value"/> (typed as <paramref name="type"/>, or its runtime type when null) as one JSON value.</summary>
        public abstract void Write(IBufferWriter<byte> output, object value, Type type);

        // ---- string adapters (transcoding); convenient for callers that still hold strings ----

        public T Deserialize<T>(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return Read<T>(bytes);
        }

        public object Deserialize(string json, Type type)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return Read(bytes, type);
        }

        public string Serialize<T>(T value)
        {
            using (var w = new PooledByteBufferWriter(256))
            {
                Write(w, value);
                return w.ToString();
            }
        }

        public string Serialize(object value, Type type)
        {
            using (var w = new PooledByteBufferWriter(256))
            {
                Write(w, value, type);
                return w.ToString();
            }
        }
    }

    public enum JsonRpcIdKind : byte
    {
        /// <summary>No id member: the request is a notification.</summary>
        Absent = 0,
        Null,
        Integer,
        String,
        /// <summary>Anything else (fraction, bool, object, array): the request is invalid.</summary>
        Invalid
    }

    public enum JsonRpcParamsKind : byte
    {
        Absent = 0,
        Array,
        Object,
        /// <summary>A primitive; JSON-RPC 2.0 requires a structured value.</summary>
        Invalid
    }

    /// <summary>What the request's <c>jsonrpc</c> member says. Judged against <see cref="JsonRpcVersionPolicy"/> by the handler.</summary>
    public enum JsonRpcVersionKind : byte
    {
        /// <summary>No <c>jsonrpc</c> member.</summary>
        Absent = 0,
        /// <summary>The string <c>"2.0"</c>.</summary>
        V2,
        /// <summary>Present but not the string <c>"2.0"</c> (another version, a number, null, ...).</summary>
        Other
    }

    /// <summary>
    /// Cursor over one JSON-RPC document (a single request or a batch). Implementations keep the parsed
    /// structure and hand the core slices of the original bytes, so nothing is materialised until a
    /// parameter is bound to a CLR type.
    /// </summary>
    public abstract class JsonRpcRequestReader
    {
        /// <summary>Parses a document. Returns false on a JSON syntax error (the core answers -32700).</summary>
        public abstract bool TryParse(ReadOnlyMemory<byte> utf8Document, out string error);

        /// <summary>The document bytes handed to <see cref="TryParse"/> (used only to feed the parse-error handler).</summary>
        public virtual ReadOnlyMemory<byte> Document => default;

        /// <summary>True when the document root is an array.</summary>
        public abstract bool IsBatch { get; }

        /// <summary>Number of requests (1 for a single request, N for a batch, 0 for an empty batch).</summary>
        public abstract int Count { get; }

        /// <summary>Positions the cursor on request <paramref name="index"/>. False when that element is not a JSON object.</summary>
        public abstract bool Select(int index);

        public abstract bool HasMethod { get; }
        /// <summary>The method name as UTF-8 (decoded when the document escaped it); used for the span-keyed lookup.</summary>
        public abstract ReadOnlySpan<byte> MethodUtf8 { get; }
        /// <summary>The decoded method name.</summary>
        public abstract string Method { get; }

        /// <summary>
        /// The request's <c>jsonrpc</c> member. Readers that do not inspect it report <see cref="JsonRpcVersionKind.V2"/>,
        /// which every <see cref="JsonRpcVersionPolicy"/> accepts.
        /// </summary>
        public virtual JsonRpcVersionKind VersionKind => JsonRpcVersionKind.V2;

        public abstract JsonRpcIdKind IdKind { get; }
        /// <summary>The raw JSON of the id (including quotes for strings). Empty when absent.</summary>
        public abstract ReadOnlySpan<byte> IdRaw { get; }
        /// <summary>The id as a CLR value: long, string or null.</summary>
        public abstract object IdValue { get; }

        public abstract JsonRpcParamsKind ParamsKind { get; }
        /// <summary>Number of elements (array) or members (object) in params; 0 when absent.</summary>
        public abstract int ParamCount { get; }
        /// <summary>Member name of object params as UTF-8 (decoded when escaped). Empty for array params.</summary>
        public abstract ReadOnlySpan<byte> ParamNameUtf8(int i);
        /// <summary>The raw JSON of parameter <paramref name="i"/>.</summary>
        public abstract ReadOnlySpan<byte> ParamRaw(int i);
        /// <summary>True when parameter <paramref name="i"/> is the JSON literal null.</summary>
        public abstract bool ParamIsNull(int i);

        public abstract T ReadParam<T>(int i);
        public abstract object ReadParam(int i, Type type);

        /// <summary>The params value materialised with the serializer's own object model (only used for pre/post handlers).</summary>
        public abstract object ParamsValue { get; }

        /// <summary>Releases pooled memory; the reader may be reused via <see cref="TryParse"/>.</summary>
        public abstract void Release();
    }

    /// <summary>Thrown by serializers when a JSON value cannot be converted to the requested type.</summary>
    public class JsonRpcBindException : Exception
    {
        public JsonRpcBindException(string message) : base(message) { }
        public JsonRpcBindException(string message, Exception inner) : base(message, inner) { }
    }
}
