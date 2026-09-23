using System;
using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.SystemTextJson
{
    /// <summary>
    /// System.Text.Json-backed serializer. The core parses the JSON-RPC envelope itself and hands this class the
    /// raw UTF-8 bytes of single values, which go straight into <see cref="JsonSerializer"/> (no transcoding, no
    /// copies); results are written through a per-thread cached <see cref="Utf8JsonWriter"/> directly into the
    /// caller's <see cref="IBufferWriter{T}"/>.
    ///
    /// With no options the serializer uses <see cref="DefaultOptions"/>, which reproduce the wire conventions of the
    /// built-in serializer (see <see cref="JsonRpcConverters"/>). User options are honoured as given; the only
    /// adjustment is that the library converters are appended when they are missing, on a copy of the options so the
    /// instance the caller holds is never mutated (see <see cref="EffectiveOptions"/>).
    /// </summary>
    public sealed class SystemTextJsonRpcSerializer : JsonRpcSerializer
    {
        private static readonly JsonSerializerOptions _defaultOptions = BuildDefaultOptions();

        private readonly JsonSerializerOptions _options;

        public SystemTextJsonRpcSerializer() : this(null) { }

        /// <param name="options">
        /// Options to use for every conversion, or null for <see cref="DefaultOptions"/>. When the options do not
        /// already carry the <see cref="JsonRpcConverters"/> they are copied and the converters appended to the copy.
        /// </param>
        public SystemTextJsonRpcSerializer(JsonSerializerOptions options)
        {
            Options = options;
            _options = Prepare(options);
        }

        /// <summary>The options passed to the constructor (null when the library defaults are in use).</summary>
        public JsonSerializerOptions Options { get; }

        /// <summary>
        /// The options actually used for conversions: <see cref="DefaultOptions"/>, the constructor argument when it
        /// already contained the library converters, or a copy of it with the converters appended.
        /// </summary>
        public JsonSerializerOptions EffectiveOptions => _options;

        /// <summary>
        /// The immutable options used when none are supplied: compact output, nulls written, no naming policy,
        /// case-insensitive property matching, public fields included, <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>,
        /// numbers readable from strings, plus the <see cref="JsonRpcConverters"/>.
        /// </summary>
        public static JsonSerializerOptions DefaultOptions => _defaultOptions;

        /// <summary>A fresh, mutable copy of <see cref="DefaultOptions"/> to customise and pass back to the constructor.</summary>
        public static JsonSerializerOptions CreateDefaultOptions() => new JsonSerializerOptions(_defaultOptions);

        public override string Name => "stj";

        /// <summary>The envelope reader enforces the same limit as <see cref="JsonSerializerOptions.MaxDepth"/> (64 when unset).</summary>
        public override int MaxDepth => _options.MaxDepth > 0 ? _options.MaxDepth : 64;

        // ------------------------------------------------------------------ read

        public override T Read<T>(ReadOnlySpan<byte> utf8Json)
        {
            return JsonSerializer.Deserialize(utf8Json, TypeInfo<T>.For(_options));
        }

        public override object Read(ReadOnlySpan<byte> utf8Json, Type type)
        {
            // typeof(object) yields a JsonElement: the object model pre/post-process handlers see in JsonRequest.Params.
            return JsonSerializer.Deserialize(utf8Json, type, _options);
        }

        // ------------------------------------------------------------------ write

        public override void Write<T>(IBufferWriter<byte> output, T value)
        {
            var writer = RentWriter(output);
            bool completed = false;
            try
            {
                JsonSerializer.Serialize(writer, value, TypeInfo<T>.For(_options));
                writer.Flush();
                completed = true;
            }
            finally
            {
                ReturnWriter(writer, completed);
            }
        }

        public override void Write(IBufferWriter<byte> output, object value, Type type)
        {
            if (value == null)
            {
                Utf8Json.WriteNull(output);
                return;
            }
            var writer = RentWriter(output);
            bool completed = false;
            try
            {
                JsonSerializer.Serialize(writer, value, type, _options);
                writer.Flush();
                completed = true;
            }
            finally
            {
                ReturnWriter(writer, completed);
            }
        }

        // ------------------------------------------------------------------ writer cache

        // One writer per thread, re-targeted with Reset(output) for every call. A nested Write on the same thread
        // (a custom converter calling back into the serializer) gets a throwaway writer instead.
        //
        // A write that throws (a property getter, a converter, the output itself) leaves the partial value pending
        // inside the writer. That state is discarded before the writer is released and the writer is pointed at a
        // sink that swallows everything: the caller may dispose or rewind its output the moment the exception
        // reaches it, so nothing may ever be flushed into that output afterwards, not even by Dispose when the
        // cached writer is evicted because the next call uses different options.
        [ThreadStatic] private static Utf8JsonWriter _cachedWriter;
        [ThreadStatic] private static JsonSerializerOptions _cachedWriterOptions;
        [ThreadStatic] private static bool _cachedWriterInUse;

        private Utf8JsonWriter RentWriter(IBufferWriter<byte> output)
        {
            if (_cachedWriterInUse) return new Utf8JsonWriter(output, WriterOptions(_options));
            var writer = _cachedWriter;
            if (writer == null || !ReferenceEquals(_cachedWriterOptions, _options))
            {
                if (writer != null) Discard(writer);
                writer = new Utf8JsonWriter(output, WriterOptions(_options));
                _cachedWriter = writer;
                _cachedWriterOptions = _options;
            }
            else
            {
                writer.Reset(output);
            }
            _cachedWriterInUse = true;
            return writer;
        }

        private static void ReturnWriter(Utf8JsonWriter writer, bool completed)
        {
            if (ReferenceEquals(writer, _cachedWriter))
            {
                try
                {
                    if (!completed) Detach(writer);
                }
                finally
                {
                    _cachedWriterInUse = false;
                }
            }
            else if (completed)
            {
                writer.Dispose();
            }
            else
            {
                Discard(writer);
            }
        }

        /// <summary>Drops whatever the writer has pending and points it at the discarding sink, away from the caller's output.</summary>
        private static void Detach(Utf8JsonWriter writer)
        {
            writer.Reset(DiscardingBufferWriter.Instance);
        }

        /// <summary>Disposes a writer without flushing anything into its previous output.</summary>
        private static void Discard(Utf8JsonWriter writer)
        {
            Detach(writer);
            writer.Dispose();
        }

        /// <summary>An <see cref="IBufferWriter{T}"/> that accepts and forgets everything; only ever the target of a detached writer.</summary>
        private sealed class DiscardingBufferWriter : IBufferWriter<byte>
        {
            public static readonly DiscardingBufferWriter Instance = new DiscardingBufferWriter();

            [ThreadStatic] private static byte[] _scratch;

            private DiscardingBufferWriter() { }

            public void Advance(int count) { }

            public Memory<byte> GetMemory(int sizeHint = 0) => Scratch(sizeHint);

            public Span<byte> GetSpan(int sizeHint = 0) => Scratch(sizeHint);

            private static byte[] Scratch(int sizeHint)
            {
                var scratch = _scratch;
                if (scratch == null || scratch.Length < sizeHint)
                {
                    _scratch = scratch = new byte[Math.Max(sizeHint, 4096)];
                }
                return scratch;
            }
        }

        private static JsonWriterOptions WriterOptions(JsonSerializerOptions options)
        {
            return new JsonWriterOptions
            {
                Encoder = options.Encoder,
                Indented = options.WriteIndented,
                IndentCharacter = options.IndentCharacter,
                IndentSize = options.IndentSize,
                NewLine = options.NewLine,
                MaxDepth = options.MaxDepth,
                SkipValidation = true
            };
        }

        // ------------------------------------------------------------------ options

        private static JsonSerializerOptions Prepare(JsonSerializerOptions options)
        {
            if (options == null) return _defaultOptions;
            if (JsonRpcConverters.ContainsAll(options))
            {
                options.MakeReadOnly(populateMissingResolver: true);
                return options;
            }
            var copy = new JsonSerializerOptions(options);
            JsonRpcConverters.AddMissing(copy);
            copy.MakeReadOnly(populateMissingResolver: true);
            return copy;
        }

        private static JsonSerializerOptions BuildDefaultOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                PropertyNamingPolicy = null,
                DictionaryKeyPolicy = null,
                PropertyNameCaseInsensitive = true,
                IncludeFields = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };
            JsonRpcConverters.AddMissing(options);
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }

        /// <summary>Caches the root JsonTypeInfo for the most recently used options per T (skips the options' own lookup).</summary>
        private static class TypeInfo<T>
        {
            private static Entry _last;

            public static JsonTypeInfo<T> For(JsonSerializerOptions options)
            {
                var entry = _last;
                if (entry != null && ReferenceEquals(entry.Options, options)) return entry.Info;
                var info = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
                _last = new Entry(options, info);
                return info;
            }

            private sealed class Entry
            {
                public readonly JsonSerializerOptions Options;
                public readonly JsonTypeInfo<T> Info;
                public Entry(JsonSerializerOptions options, JsonTypeInfo<T> info) { Options = options; Info = info; }
            }
        }
    }
}
