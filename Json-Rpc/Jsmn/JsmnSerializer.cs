using System;
using System.Buffers;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.Jsmn
{
    /// <summary>
    /// The built-in, dependency-free serializer. Tokenizes with <see cref="JsmnTokenizer"/> and binds values
    /// with <see cref="JsmnMapper"/> (primitives without boxing, POCOs and collections through cached
    /// reflection plans). It is the default when no other serializer is configured.
    /// </summary>
    public sealed class JsmnSerializer : JsonRpcSerializer
    {
        public static readonly JsmnSerializer Instance = new JsmnSerializer();

        [ThreadStatic] private static JsmnTokenizer _scratch;
        [ThreadStatic] private static bool _scratchInUse;

        private readonly bool _lenient;
        private readonly int _maxDepth;

        public JsmnSerializer() : this(false) { }

        /// <param name="lenient">Accept single-quoted strings, unquoted keys and trailing commas.</param>
        public JsmnSerializer(bool lenient) : this(lenient, JsmnTokenizer.DefaultMaxDepth) { }

        /// <param name="lenient">Accept single-quoted strings, unquoted keys and trailing commas.</param>
        /// <param name="maxDepth">
        /// Maximum number of nested objects/arrays in a request or value (the root counts as one). Enforced by
        /// the tokenizer before any hook or binding runs, so it also bounds every recursive reader downstream.
        /// Default 64, the same as System.Text.Json and Json.NET.
        /// </param>
        public JsmnSerializer(bool lenient, int maxDepth)
        {
            if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth), "maxDepth must be at least 1.");
            _lenient = lenient;
            _maxDepth = maxDepth;
        }

        public override string Name => "jsmn";
        public override bool Lenient => _lenient;

        /// <summary>Maximum nesting depth accepted by the envelope reader and by <see cref="Read{T}"/>.</summary>
        public override int MaxDepth => _maxDepth;

        public override T Read<T>(ReadOnlySpan<byte> utf8Json)
        {
            var tok = RentTokenizer();
            try
            {
                Tokenize(tok, utf8Json);
                var c = new JsmnCursor(utf8Json, tok.Tokens, 0);
                return JsmnReader<T>.Read(ref c);
            }
            finally
            {
                ReturnTokenizer(tok);
            }
        }

        public override object Read(ReadOnlySpan<byte> utf8Json, Type type)
        {
            var tok = RentTokenizer();
            try
            {
                Tokenize(tok, utf8Json);
                var c = new JsmnCursor(utf8Json, tok.Tokens, 0);
                return JsmnMapper.ReadObject(ref c, type);
            }
            finally
            {
                ReturnTokenizer(tok);
            }
        }

        public override void Write<T>(IBufferWriter<byte> output, T value) => JsmnWriter<T>.Write(output, value);

        public override void Write(IBufferWriter<byte> output, object value, Type type) => JsmnMapper.WriteObject(output, value, type, 0);

        private void Tokenize(JsmnTokenizer tok, ReadOnlySpan<byte> json)
        {
            tok.Lenient = _lenient;
            tok.MaxDepth = _maxDepth;
            int n = tok.Parse(json);
            if (n <= 0)
            {
                switch (n)
                {
                    case JsmnTokenizer.ErrorPartial: throw new JsonRpcBindException("Unexpected end of JSON input.");
                    case JsmnTokenizer.ErrorDepth: throw new JsonRpcBindException("The maximum nesting depth of " + _maxDepth + " was exceeded.");
                    default: throw new JsonRpcBindException("Invalid JSON.");
                }
            }
        }

        private static JsmnTokenizer RentTokenizer()
        {
            if (_scratchInUse) return new JsmnTokenizer();
            var t = _scratch ?? (_scratch = new JsmnTokenizer());
            _scratchInUse = true;
            return t;
        }

        private static void ReturnTokenizer(JsmnTokenizer t)
        {
            if (ReferenceEquals(t, _scratch)) _scratchInUse = false;
        }
    }
}
