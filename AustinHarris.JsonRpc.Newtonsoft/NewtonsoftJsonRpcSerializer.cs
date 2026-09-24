using System;
using System.Buffers;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// Json.NET-backed serializer. The core parses the JSON-RPC envelope itself and hands this class the raw
    /// UTF-8 bytes of individual values (params, results, error data); every conversion goes through one
    /// <see cref="JsonSerializer"/> built from <see cref="Settings"/>, so converters, contract resolvers,
    /// date/float handling and formatting behave exactly as they do with <see cref="JsonConvert"/>.
    /// <para>
    /// Reading decodes the value bytes into a pooled char buffer and feeds it to a <see cref="JsonTextReader"/>
    /// whose scratch buffer is rented from <see cref="ArrayPool{T}"/>; nothing is materialised as a string.
    /// Writing runs a per-thread <see cref="JsonTextWriter"/> over a <see cref="System.IO.TextWriter"/> that
    /// UTF-8 encodes straight into the caller's <see cref="IBufferWriter{T}"/>. Output is compact, exactly one
    /// JSON value, no trailing whitespace.
    /// </para>
    /// </summary>
    public sealed class NewtonsoftJsonRpcSerializer : JsonRpcSerializer
    {
        private readonly JsonSerializer _serializer;

        public NewtonsoftJsonRpcSerializer() : this(null) { }

        /// <param name="settings">
        /// Settings applied to every conversion (null = Json.NET defaults). The serializer is built once from
        /// the settings; like <see cref="JsonConvert"/>, <see cref="JsonConvert.DefaultSettings"/> is honoured
        /// as the baseline.
        /// </param>
        public NewtonsoftJsonRpcSerializer(JsonSerializerSettings settings)
        {
            Settings = settings;
            _serializer = JsonSerializer.CreateDefault(settings);
        }

        /// <summary>The settings used for every conversion (null = Json.NET defaults).</summary>
        public JsonSerializerSettings Settings { get; }

        /// <summary>The <see cref="JsonSerializer"/> built from <see cref="Settings"/>. Shared by every conversion; do not mutate it while requests are in flight.</summary>
        public JsonSerializer Serializer => _serializer;

        public override string Name => "newtonsoft";

        /// <summary>Json.NET accepts single quotes, unquoted names and comments; let the envelope reader do the same.</summary>
        public override bool Lenient => true;

        /// <summary>The envelope reader enforces the same limit as <see cref="JsonSerializerSettings.MaxDepth"/> (64 when unset).</summary>
        public override int MaxDepth => _serializer.MaxDepth ?? 64;

        // ------------------------------------------------------------------ read

        public override T Read<T>(ReadOnlySpan<byte> utf8Json)
        {
            var scratch = Scratch.Current;
            var text = scratch.RentReader();
            try
            {
                text.Reset(utf8Json);
                using (var json = CreateReader(text))
                {
                    return _serializer.Deserialize<T>(json);
                }
            }
            finally
            {
                scratch.ReturnReader(text);
            }
        }

        public override object Read(ReadOnlySpan<byte> utf8Json, Type type)
        {
            var scratch = Scratch.Current;
            var text = scratch.RentReader();
            try
            {
                text.Reset(utf8Json);
                using (var json = CreateReader(text))
                {
                    // typeof(object) yields JObject / JArray / unwrapped primitives, exactly like JsonConvert.DeserializeObject(text)
                    return _serializer.Deserialize(json, type);
                }
            }
            finally
            {
                scratch.ReturnReader(text);
            }
        }

        private static JsonTextReader CreateReader(Utf8CharReader text)
        {
            return new JsonTextReader(text)
            {
                ArrayPool = JsonArrayPool.Instance,
                CloseInput = false
            };
        }

        // ------------------------------------------------------------------ write

        public override void Write<T>(IBufferWriter<byte> output, T value) => WriteCore(output, value, typeof(T));

        public override void Write(IBufferWriter<byte> output, object value, Type type) => WriteCore(output, value, type ?? value?.GetType() ?? typeof(object));

        private void WriteCore(IBufferWriter<byte> output, object value, Type type)
        {
            var scratch = Scratch.Current;
            if (scratch.WriterInUse)
            {
                // re-entrant call on this thread (a converter serializing through the RPC serializer): use throwaway instances
                var fresh = new BufferWriterTextWriter();
                fresh.Reset(output);
                Serialize(CreateWriter(fresh), fresh, value, type);
                return;
            }

            scratch.WriterInUse = true;
            var text = scratch.Text;
            var json = scratch.Writer ?? (scratch.Writer = CreateWriter(text));
            text.Reset(output);
            bool completed = false;
            try
            {
                Serialize(json, text, value, type);
                // a JsonTextWriter can write any number of root values; it is only reusable while it is back at the root
                completed = json.WriteState == WriteState.Start;
            }
            finally
            {
                if (!completed) scratch.Writer = null;
                text.Detach();
                scratch.WriterInUse = false;
            }
        }

        private void Serialize(JsonTextWriter json, BufferWriterTextWriter text, object value, Type type)
        {
            _serializer.Serialize(json, value, type);
            json.Flush();   // JsonTextWriter buffers nothing itself; this flushes the TextWriter (and its UTF-8 encoder)
            text.Flush();
        }

        private static JsonTextWriter CreateWriter(BufferWriterTextWriter text)
        {
            return new JsonTextWriter(text)
            {
                ArrayPool = JsonArrayPool.Instance,
                CloseOutput = false,
                AutoCompleteOnClose = false,
                Formatting = Formatting.None
            };
        }

        // ------------------------------------------------------------------ per-thread scratch

        /// <summary>
        /// One decoder/encoder pair per thread. The <see cref="JsonTextWriter"/> is kept between calls (Json.NET
        /// lets a writer emit successive root values); a <see cref="JsonTextReader"/> cannot be rewound, so one is
        /// created per read and only its char buffer is pooled.
        /// </summary>
        private sealed class Scratch
        {
            [ThreadStatic] private static Scratch _current;

            public static Scratch Current => _current ?? (_current = new Scratch());

            private readonly Utf8CharReader _reader = new Utf8CharReader();
            private bool _readerInUse;

            public readonly BufferWriterTextWriter Text = new BufferWriterTextWriter();
            public JsonTextWriter Writer;
            public bool WriterInUse;

            public Utf8CharReader RentReader()
            {
                if (_readerInUse) return new Utf8CharReader();
                _readerInUse = true;
                return _reader;
            }

            public void ReturnReader(Utf8CharReader reader)
            {
                if (ReferenceEquals(reader, _reader)) _readerInUse = false;
                else reader.Dispose();
            }
        }
    }
}
