using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.SystemTextJson
{
    /// <summary>
    /// The converters that make System.Text.Json produce the wire conventions shared by every JSON-RPC.Net
    /// serializer: whole float/double/decimal values carry ".0", DateTime is ISO-8601 as Json.NET writes it (fraction only when non-zero, trailing zeros trimmed)
    /// and the offset, char is a one-character string, and reads accept the coercions the library has always
    /// accepted (numeric strings, true/false as 1/0, numbers as bool, numbers as char, integers to floats).
    /// </summary>
    public static class JsonRpcConverters
    {
        /// <summary>A fresh list of the converters, in registration order.</summary>
        public static IEnumerable<JsonConverter> Create()
        {
            yield return new JsonRpcNumberConverterFactory();
            yield return new JsonRpcBooleanConverter();
            yield return new JsonRpcCharConverter();
            yield return new JsonRpcDateTimeConverter();
            yield return new JsonRpcDateTimeOffsetConverter();
        }

        /// <summary>
        /// Appends every converter whose type is not already present in <paramref name="options"/>. They are added
        /// after the existing ones, so converters the caller registered keep precedence (System.Text.Json uses the
        /// first converter that can handle a type).
        /// </summary>
        public static void AddMissing(JsonSerializerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            foreach (var converter in Create())
            {
                if (!Contains(options, converter.GetType())) options.Converters.Add(converter);
            }
        }

        /// <summary>True when every converter type from <see cref="Create"/> is registered on <paramref name="options"/>.</summary>
        public static bool ContainsAll(JsonSerializerOptions options)
        {
            if (options == null) return false;
            foreach (var converter in Create())
            {
                if (!Contains(options, converter.GetType())) return false;
            }
            return true;
        }

        private static bool Contains(JsonSerializerOptions options, Type converterType)
        {
            var converters = options.Converters;
            for (int i = 0; i < converters.Count; i++)
            {
                if (converters[i].GetType() == converterType) return true;
            }
            return false;
        }
    }

    /// <summary>Shared read coercions and number formatting (mirrors the built-in serializer's JsmnMapper / Utf8Json).</summary>
    internal static class Wire
    {
        // ------------------------------------------------------------------ reads

        public static long ReadInt64(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long l)) return l;
                    return RoundToInt64(reader.GetDouble());
                case JsonTokenType.True:
                    return 1;
                case JsonTokenType.False:
                    return 0;
                case JsonTokenType.String:
                    {
                        var s = reader.GetString();
                        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                        if (s == "true") return 1;
                        if (s == "false") return 0;
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return RoundToInt64(d);
                        break;
                    }
            }
            throw Bind(ref reader, "integer");
        }

        public static ulong ReadUInt64(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetUInt64(out ulong v)) return v;
            if (reader.TokenType == JsonTokenType.String && ulong.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return checked((ulong)ReadInt64(ref reader));
        }

        public static double ReadDouble(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    return reader.GetDouble();
                case JsonTokenType.True:
                    return 1;
                case JsonTokenType.False:
                    return 0;
                case JsonTokenType.String:
                    {
                        var s = reader.GetString();
                        // "NaN", "Infinity", "-Infinity": what Json.NET (and every serializer here) writes for non-finite values
                        if (Utf8Json.TryParseNonFinite(s, out double d)) return d;
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                        if (s == "true") return 1;
                        if (s == "false") return 0;
                        break;
                    }
            }
            throw Bind(ref reader, "number");
        }

        public static float ReadSingle(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number) return reader.GetSingle();
            return (float)ReadDouble(ref reader);
        }

        public static decimal ReadDecimal(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetDecimal(out decimal m)) return m;
                    return (decimal)reader.GetDouble();
                case JsonTokenType.True:
                    return 1;
                case JsonTokenType.False:
                    return 0;
                case JsonTokenType.String:
                    {
                        var s = reader.GetString();
                        if (decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return m;
                        if (s == "true") return 1;
                        if (s == "false") return 0;
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return (decimal)d;
                        break;
                    }
            }
            throw Bind(ref reader, "decimal");
        }

        public static bool ReadBoolean(ref Utf8JsonReader reader)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.True:
                    return true;
                case JsonTokenType.False:
                    return false;
                case JsonTokenType.Number:
                    return reader.GetDouble() != 0;
                case JsonTokenType.String:
                    {
                        var s = reader.GetString();
                        if (bool.TryParse(s, out bool b)) return b;
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d != 0;
                        break;
                    }
            }
            throw Bind(ref reader, "boolean");
        }

        public static char ReadChar(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (s.Length == 1) return s[0];
                throw Bind(ref reader, "single character");
            }
            return checked((char)ReadInt64(ref reader));
        }

        public static DateTime ReadDateTime(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;
            }
            throw Bind(ref reader, "DateTime");
        }

        public static DateTimeOffset ReadDateTimeOffset(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)) return dto;
            }
            throw Bind(ref reader, "DateTimeOffset");
        }

        private static long RoundToInt64(double d) => checked((long)Math.Round(d, MidpointRounding.ToEven));

        private static JsonException Bind(ref Utf8JsonReader reader, string expected)
        {
            string text;
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    text = "\"" + reader.GetString() + "\"";
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                case JsonTokenType.Null:
                    {
                        var span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
                        text = System.Text.Encoding.UTF8.GetString(span, 0, span.Length);
                        break;
                    }
                default:
                    text = reader.TokenType.ToString();
                    break;
            }
            if (text.Length > 64) text = text.Substring(0, 64) + "...";
            return new JsonException("Could not convert " + text + " to " + expected + ".");
        }

        // ------------------------------------------------------------------ writes

        public static void WriteDouble(Utf8JsonWriter writer, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { WriteNonFinite(writer, value); return; }
            Span<byte> buffer = stackalloc byte[40];
#if NETSTANDARD2_0
            int written = WriteAscii(value.ToString("R", CultureInfo.InvariantCulture), buffer);
#else
            Utf8Formatter.TryFormat(value, buffer, out int written);
#endif
            written = EnsureDecimalPlace(buffer, written);
            writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
        }

        public static void WriteSingle(Utf8JsonWriter writer, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { WriteNonFinite(writer, value); return; }
            Span<byte> buffer = stackalloc byte[40];
#if NETSTANDARD2_0
            int written = WriteAscii(value.ToString("R", CultureInfo.InvariantCulture), buffer);
#else
            Utf8Formatter.TryFormat(value, buffer, out int written);
#endif
            written = EnsureDecimalPlace(buffer, written);
            writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
        }

        public static void WriteDecimal(Utf8JsonWriter writer, decimal value)
        {
            Span<byte> buffer = stackalloc byte[48];
            Utf8Formatter.TryFormat(value, buffer, out int written);
            written = EnsureDecimalPlace(buffer, written);
            writer.WriteRawValue(buffer.Slice(0, written), skipInputValidation: true);
        }

        public static void WriteChar(Utf8JsonWriter writer, char value)
        {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            writer.WriteStringValue(one);
        }

        /// <summary>The core's (Json.NET-compatible) DateTime text, see <see cref="Utf8Json.FormatDateTime"/>: fraction only when non-zero, trailing zeros trimmed; Z for Utc, offset for Local, nothing for Unspecified.</summary>
        public static void WriteDateTime(Utf8JsonWriter writer, DateTime value)
        {
            // The raw write bypasses the encoder so a '+' in the offset is never escaped, whatever encoder the options carry.
            Span<byte> buffer = stackalloc byte[Utf8Json.MaxDateTimeLength + 2];
            buffer[0] = (byte)'"';
            int written = Utf8Json.FormatDateTime(buffer.Slice(1), value);
            buffer[written + 1] = (byte)'"';
            writer.WriteRawValue(buffer.Slice(0, written + 2), skipInputValidation: true);
        }

        /// <summary>Same text as <see cref="WriteDateTime"/>, with the offset always written.</summary>
        public static void WriteDateTimeOffset(Utf8JsonWriter writer, DateTimeOffset value)
        {
            Span<byte> buffer = stackalloc byte[Utf8Json.MaxDateTimeLength + 2];
            buffer[0] = (byte)'"';
            int written = Utf8Json.FormatDateTimeOffset(buffer.Slice(1), value);
            buffer[written + 1] = (byte)'"';
            writer.WriteRawValue(buffer.Slice(0, written + 2), skipInputValidation: true);
        }

        private static void WriteNonFinite(Utf8JsonWriter writer, double value)
        {
            // Bare NaN / Infinity are not JSON. Json.NET's default (FloatFormatHandling.String) and the built-in
            // serializer write the quoted strings "NaN", "Infinity", "-Infinity"; keep the wire identical.
            writer.WriteStringValue(Utf8Json.NonFiniteText(value));
        }

        private static int WriteAscii(string text, Span<byte> buffer)
        {
            for (int i = 0; i < text.Length; i++) buffer[i] = (byte)text[i];
            return text.Length;
        }

        /// <summary>Appends ".0" when the formatted (finite) number has neither a fraction nor an exponent.</summary>
        private static int EnsureDecimalPlace(Span<byte> buffer, int written)
        {
            for (int i = 0; i < written; i++)
            {
                byte b = buffer[i];
                if (b == (byte)'.' || b == (byte)'E' || b == (byte)'e') return written;
            }
            buffer[written] = (byte)'.';
            buffer[written + 1] = (byte)'0';
            return written + 2;
        }
    }

    /// <summary>
    /// Replaces the built-in converters for the 11 primitive numeric types. Writes float/double/decimal with a
    /// ".0" on whole values; reads numbers, numeric strings and true/false (as 1/0) for every numeric type, and
    /// rounds fractional input to the nearest even integer for integer types. Nullable variants are handled by
    /// System.Text.Json's own nullable wrapping.
    /// </summary>
    public sealed class JsonRpcNumberConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            switch (Type.GetTypeCode(typeToConvert))
            {
                case TypeCode.Double:
                case TypeCode.Single:
                case TypeCode.Decimal:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Int16:
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return !typeToConvert.IsEnum;
                default:
                    return false;
            }
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            switch (Type.GetTypeCode(typeToConvert))
            {
                case TypeCode.Double: return DoubleConverter.Instance;
                case TypeCode.Single: return SingleConverter.Instance;
                case TypeCode.Decimal: return DecimalConverter.Instance;
                case TypeCode.Int32: return Int32Converter.Instance;
                case TypeCode.Int64: return Int64Converter.Instance;
                case TypeCode.Int16: return Int16Converter.Instance;
                case TypeCode.Byte: return ByteConverter.Instance;
                case TypeCode.SByte: return SByteConverter.Instance;
                case TypeCode.UInt16: return UInt16Converter.Instance;
                case TypeCode.UInt32: return UInt32Converter.Instance;
                case TypeCode.UInt64: return UInt64Converter.Instance;
                default: throw new NotSupportedException(typeToConvert.FullName);
            }
        }

        private sealed class DoubleConverter : JsonConverter<double>
        {
            public static readonly DoubleConverter Instance = new DoubleConverter();
            public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadDouble(ref reader);
            public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => Wire.WriteDouble(writer, value);
        }

        private sealed class SingleConverter : JsonConverter<float>
        {
            public static readonly SingleConverter Instance = new SingleConverter();
            public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadSingle(ref reader);
            public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) => Wire.WriteSingle(writer, value);
        }

        private sealed class DecimalConverter : JsonConverter<decimal>
        {
            public static readonly DecimalConverter Instance = new DecimalConverter();
            public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadDecimal(ref reader);
            public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => Wire.WriteDecimal(writer, value);
        }

        private sealed class Int32Converter : JsonConverter<int>
        {
            public static readonly Int32Converter Instance = new Int32Converter();
            public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int v)) return v;
                return checked((int)Wire.ReadInt64(ref reader));
            }
            public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class Int64Converter : JsonConverter<long>
        {
            public static readonly Int64Converter Instance = new Int64Converter();
            public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadInt64(ref reader);
            public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class Int16Converter : JsonConverter<short>
        {
            public static readonly Int16Converter Instance = new Int16Converter();
            public override short Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => checked((short)Wire.ReadInt64(ref reader));
            public override void Write(Utf8JsonWriter writer, short value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class ByteConverter : JsonConverter<byte>
        {
            public static readonly ByteConverter Instance = new ByteConverter();
            public override byte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => checked((byte)Wire.ReadInt64(ref reader));
            public override void Write(Utf8JsonWriter writer, byte value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class SByteConverter : JsonConverter<sbyte>
        {
            public static readonly SByteConverter Instance = new SByteConverter();
            public override sbyte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => checked((sbyte)Wire.ReadInt64(ref reader));
            public override void Write(Utf8JsonWriter writer, sbyte value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class UInt16Converter : JsonConverter<ushort>
        {
            public static readonly UInt16Converter Instance = new UInt16Converter();
            public override ushort Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => checked((ushort)Wire.ReadInt64(ref reader));
            public override void Write(Utf8JsonWriter writer, ushort value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class UInt32Converter : JsonConverter<uint>
        {
            public static readonly UInt32Converter Instance = new UInt32Converter();
            public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => checked((uint)Wire.ReadInt64(ref reader));
            public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }

        private sealed class UInt64Converter : JsonConverter<ulong>
        {
            public static readonly UInt64Converter Instance = new UInt64Converter();
            public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadUInt64(ref reader);
            public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
        }
    }

    /// <summary>Reads true/false, numbers (non-zero is true) and "true"/"false"/numeric strings.</summary>
    public sealed class JsonRpcBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadBoolean(ref reader);
        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }

    /// <summary>Writes a one-character string; reads a one-character string or a number (the UTF-16 code unit).</summary>
    public sealed class JsonRpcCharConverter : JsonConverter<char>
    {
        public override char Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadChar(ref reader);
        public override void Write(Utf8JsonWriter writer, char value, JsonSerializerOptions options) => Wire.WriteChar(writer, value);
    }

    /// <summary>
    /// Writes the core DateTime text (see Utf8Json.FormatDateTime); reads any ISO-8601 text with
    /// DateTimeStyles.RoundtripKind, so an input with an offset yields a Local DateTime.
    /// </summary>
    public sealed class JsonRpcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadDateTime(ref reader);
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => Wire.WriteDateTime(writer, value);
    }

    /// <summary>Writes the core DateTimeOffset text (see Utf8Json.FormatDateTimeOffset); the offset is always written.</summary>
    public sealed class JsonRpcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Wire.ReadDateTimeOffset(ref reader);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => Wire.WriteDateTimeOffset(writer, value);
    }
}
