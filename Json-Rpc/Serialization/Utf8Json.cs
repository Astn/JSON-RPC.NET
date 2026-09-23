using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// Wire-format helpers shared by the core and the built-in serializer. They encode the conventions the
    /// library has always produced (and its tests assert): compact output, whole float/double/decimal values
    /// carry a ".0", DateTime as ISO-8601 the way Json.NET writes it (fraction only when non-zero, trailing zeros
    /// trimmed, Z / offset / nothing by Kind), char as a one-character string.
    /// </summary>
    public static class Utf8Json
    {
        /// <summary>Upper bound on the bytes <see cref="FormatDateTime"/> and <see cref="FormatDateTimeOffset"/> write (no quotes).</summary>
        public const int MaxDateTimeLength = 33;

        // ------------------------------------------------------------------ literals

        public static void WriteNull(IBufferWriter<byte> w)
        {
            var s = w.GetSpan(4);
            s[0] = (byte)'n'; s[1] = (byte)'u'; s[2] = (byte)'l'; s[3] = (byte)'l';
            w.Advance(4);
        }

        // ---- overloads on the concrete pooled writer, used by the compiled built-in invokers so a formatted value
        // costs no interface calls. Each shares the formatting code of its IBufferWriter twin: same bytes.

        public static void WriteNull(PooledByteBufferWriter w)
        {
            var s = w.GetSpan(4);
            s[0] = (byte)'n'; s[1] = (byte)'u'; s[2] = (byte)'l'; s[3] = (byte)'l';
            w.Advance(4);
        }

        public static void WriteBool(PooledByteBufferWriter w, bool value)
        {
            var s = w.GetSpan(5);
            int n = FormatBool(s, value);
            w.Advance(n);
        }

        public static void WriteInt64(PooledByteBufferWriter w, long value)
        {
            var s = w.GetSpan(20);
            Utf8Formatter.TryFormat(value, s, out int written);
            w.Advance(written);
        }

        public static void WriteDouble(PooledByteBufferWriter w, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { WriteNonFinite(w, value); return; }
            var s = w.GetSpan(40);
            w.Advance(FormatDouble(s, value));
        }

        public static void WriteSingle(PooledByteBufferWriter w, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { WriteNonFinite(w, value); return; }
            var s = w.GetSpan(40);
            w.Advance(FormatSingle(s, value));
        }

        public static void WriteDecimal(PooledByteBufferWriter w, decimal value)
        {
            var s = w.GetSpan(48);
            w.Advance(FormatDecimal(s, value));
        }

        public static void WriteString(PooledByteBufferWriter w, string value)
        {
            if (value == null) { WriteNull(w); return; }
            var v = value.AsSpan();
            if (v.Length > StringChunk) { WriteLongString(w, v); return; }
            var s = w.GetSpan(v.Length * 6 + 2);
            w.Advance(FormatString(s, v));
        }

        public static void WriteBool(IBufferWriter<byte> w, bool value)
        {
            var s = w.GetSpan(5);
            int n = FormatBool(s, value);
            w.Advance(n);
        }

        private static int FormatBool(Span<byte> s, bool value)
        {
            if (value)
            {
                s[0] = (byte)'t'; s[1] = (byte)'r'; s[2] = (byte)'u'; s[3] = (byte)'e';
                return 4;
            }
            s[0] = (byte)'f'; s[1] = (byte)'a'; s[2] = (byte)'l'; s[3] = (byte)'s'; s[4] = (byte)'e';
            return 5;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteByte(IBufferWriter<byte> w, byte b)
        {
            w.GetSpan(1)[0] = b;
            w.Advance(1);
        }

        public static void WriteRaw(IBufferWriter<byte> w, ReadOnlySpan<byte> raw)
        {
            var s = w.GetSpan(raw.Length);
            raw.CopyTo(s);
            w.Advance(raw.Length);
        }

        /// <summary>Writes ASCII text (used for literals and formatted numbers/dates).</summary>
        public static void WriteAscii(IBufferWriter<byte> w, ReadOnlySpan<char> chars)
        {
            var s = w.GetSpan(chars.Length);
            for (int i = 0; i < chars.Length; i++) s[i] = (byte)chars[i];
            w.Advance(chars.Length);
        }

        // ------------------------------------------------------------------ numbers

        public static void WriteInt64(IBufferWriter<byte> w, long value)
        {
            var s = w.GetSpan(20);
            Utf8Formatter.TryFormat(value, s, out int written);
            w.Advance(written);
        }

        public static void WriteUInt64(IBufferWriter<byte> w, ulong value)
        {
            var s = w.GetSpan(20);
            Utf8Formatter.TryFormat(value, s, out int written);
            w.Advance(written);
        }

        public static void WriteDouble(IBufferWriter<byte> w, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { WriteNonFinite(w, value); return; }
            var s = w.GetSpan(40);
            w.Advance(FormatDouble(s, value));
        }

        public static void WriteSingle(IBufferWriter<byte> w, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) { WriteNonFinite(w, value); return; }
            var s = w.GetSpan(40);
            w.Advance(FormatSingle(s, value));
        }

        public static void WriteDecimal(IBufferWriter<byte> w, decimal value)
        {
            var s = w.GetSpan(48);
            w.Advance(FormatDecimal(s, value));
        }

        /// <summary>A finite double into at least 40 bytes: shortest round-trip text, with ".0" when it has no fraction or exponent.</summary>
        private static int FormatDouble(Span<byte> s, double value)
        {
#if NETSTANDARD2_0
            int written = WriteAsciiInto(value.ToString("R", CultureInfo.InvariantCulture), s);
#else
            Utf8Formatter.TryFormat(value, s, out int written);
#endif
            return EnsureDecimalPlace(s, written);
        }

        private static int FormatSingle(Span<byte> s, float value)
        {
#if NETSTANDARD2_0
            int written = WriteAsciiInto(value.ToString("R", CultureInfo.InvariantCulture), s);
#else
            Utf8Formatter.TryFormat(value, s, out int written);
#endif
            return EnsureDecimalPlace(s, written);
        }

        private static int FormatDecimal(Span<byte> s, decimal value)
        {
            Utf8Formatter.TryFormat(value, s, out int written);
            return EnsureDecimalPlace(s, written);
        }

        /// <summary>The JSON text of NaN / +Infinity / -Infinity: the strings "NaN", "Infinity" and "-Infinity" (quoted).</summary>
        public const string NaNText = "NaN";
        public const string PositiveInfinityText = "Infinity";
        public const string NegativeInfinityText = "-Infinity";

        private static void WriteNonFinite(IBufferWriter<byte> w, double value)
        {
            // Bare NaN / Infinity are not JSON. Json.NET's default (FloatFormatHandling.String) writes the quoted
            // strings "NaN", "Infinity", "-Infinity" and reads them back; every serializer here does the same.
            WriteQuotedAscii(w, NonFiniteText(value));
        }

        /// <summary>Returns "NaN", "Infinity" or "-Infinity" for a non-finite value.</summary>
        public static string NonFiniteText(double value)
        {
            return double.IsNaN(value) ? NaNText : value > 0 ? PositiveInfinityText : NegativeInfinityText;
        }

        /// <summary>Recognises the non-finite float spellings Json.NET reads back: "NaN", "Infinity", "-Infinity" (case-sensitive, UTF-8 bytes without quotes).</summary>
        public static bool TryParseNonFinite(ReadOnlySpan<byte> text, out double value)
        {
            switch (text.Length)
            {
                case 3:
                    if (text[0] == (byte)'N' && text[1] == (byte)'a' && text[2] == (byte)'N') { value = double.NaN; return true; }
                    break;
                case 8:
                    if (IsInfinity(text)) { value = double.PositiveInfinity; return true; }
                    break;
                case 9:
                    if (text[0] == (byte)'-' && IsInfinity(text.Slice(1))) { value = double.NegativeInfinity; return true; }
                    break;
            }
            value = 0;
            return false;
        }

        /// <summary>Same as <see cref="TryParseNonFinite(ReadOnlySpan{byte}, out double)"/> for decoded text.</summary>
        public static bool TryParseNonFinite(string text, out double value)
        {
            if (text == NaNText) { value = double.NaN; return true; }
            if (text == PositiveInfinityText) { value = double.PositiveInfinity; return true; }
            if (text == NegativeInfinityText) { value = double.NegativeInfinity; return true; }
            value = 0;
            return false;
        }

        private static bool IsInfinity(ReadOnlySpan<byte> text)
        {
            return text.Length == 8
                && text[0] == (byte)'I' && text[1] == (byte)'n' && text[2] == (byte)'f' && text[3] == (byte)'i'
                && text[4] == (byte)'n' && text[5] == (byte)'i' && text[6] == (byte)'t' && text[7] == (byte)'y';
        }

        private static int WriteAsciiInto(string text, Span<byte> s)
        {
            for (int i = 0; i < text.Length; i++) s[i] = (byte)text[i];
            return text.Length;
        }

        /// <summary>Appends ".0" when the formatted (finite) number has neither a fraction nor an exponent.</summary>
        private static int EnsureDecimalPlace(Span<byte> s, int written)
        {
            for (int i = 0; i < written; i++)
            {
                byte b = s[i];
                if (b == (byte)'.' || b == (byte)'E' || b == (byte)'e') return written;
            }
            s[written] = (byte)'.';
            s[written + 1] = (byte)'0';
            return written + 2;
        }

        // ------------------------------------------------------------------ strings

        public static void WriteString(IBufferWriter<byte> w, string value)
        {
            if (value == null) { WriteNull(w); return; }
            WriteString(w, value.AsSpan());
        }

        public static void WriteString(IBufferWriter<byte> w, ReadOnlySpan<char> value)
        {
            if (value.Length > StringChunk) { WriteLongString(w, value); return; }
            // worst case: every char becomes \uXXXX (6 bytes); surrogate pairs are 4 bytes for 2 chars
            var s = w.GetSpan(value.Length * 6 + 2);
            w.Advance(FormatString(s, value));
        }

        /// <summary>Strings longer than this go out in chunks, so the worst-case reservation (6 bytes per char) stays bounded.</summary>
        private const int StringChunk = 512;

        private static void WriteLongString(IBufferWriter<byte> w, ReadOnlySpan<char> value)
        {
            WriteByte(w, (byte)'"');
            while (value.Length > 0)
            {
                int n = Math.Min(StringChunk, value.Length);
                if (n < value.Length && char.IsHighSurrogate(value[n - 1])) n++;   // never split a surrogate pair
                var s = w.GetSpan(n * 6);
                w.Advance(FormatChars(s, value.Slice(0, n)));
                value = value.Slice(n);
            }
            WriteByte(w, (byte)'"');
        }

        /// <summary>Quotes and escapes <paramref name="value"/> into <paramref name="s"/> (at least 6 bytes per char plus 2). Returns the length.</summary>
        private static int FormatString(Span<byte> s, ReadOnlySpan<char> value)
        {
            s[0] = (byte)'"';
            int p = 1 + FormatChars(s.Slice(1), value);
            s[p] = (byte)'"';
            return p + 1;
        }

        /// <summary>Escapes <paramref name="value"/> into <paramref name="s"/> without quotes (at least 6 bytes per char). Returns the length.</summary>
        private static int FormatChars(Span<byte> s, ReadOnlySpan<char> value)
        {
            int p = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < 0x80)
                {
                    if (c >= 0x20 && c != '"' && c != '\\')
                    {
                        s[p++] = (byte)c;
                        continue;
                    }
                    s[p++] = (byte)'\\';
                    switch (c)
                    {
                        case '"': s[p++] = (byte)'"'; break;
                        case '\\': s[p++] = (byte)'\\'; break;
                        case '\n': s[p++] = (byte)'n'; break;
                        case '\r': s[p++] = (byte)'r'; break;
                        case '\t': s[p++] = (byte)'t'; break;
                        case '\b': s[p++] = (byte)'b'; break;
                        case '\f': s[p++] = (byte)'f'; break;
                        default:
                            s[p++] = (byte)'u'; s[p++] = (byte)'0'; s[p++] = (byte)'0';
                            s[p++] = Hex(c >> 4); s[p++] = Hex(c & 0xF);
                            break;
                    }
                }
                else if (c < 0x800)
                {
                    s[p++] = (byte)(0xC0 | (c >> 6));
                    s[p++] = (byte)(0x80 | (c & 0x3F));
                }
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    int cp = char.ConvertToUtf32(c, value[i + 1]);
                    i++;
                    s[p++] = (byte)(0xF0 | (cp >> 18));
                    s[p++] = (byte)(0x80 | ((cp >> 12) & 0x3F));
                    s[p++] = (byte)(0x80 | ((cp >> 6) & 0x3F));
                    s[p++] = (byte)(0x80 | (cp & 0x3F));
                }
                else if (char.IsSurrogate(c))
                {
                    // lone surrogate: escape it so the output stays valid UTF-8
                    s[p++] = (byte)'\\'; s[p++] = (byte)'u';
                    s[p++] = Hex(c >> 12); s[p++] = Hex((c >> 8) & 0xF); s[p++] = Hex((c >> 4) & 0xF); s[p++] = Hex(c & 0xF);
                }
                else
                {
                    s[p++] = (byte)(0xE0 | (c >> 12));
                    s[p++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                    s[p++] = (byte)(0x80 | (c & 0x3F));
                }
            }
            return p;
        }

        private static byte Hex(int nibble) => (byte)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

        public static void WriteChar(IBufferWriter<byte> w, char value)
        {
            Span<char> one = stackalloc char[1];
            one[0] = value;
            WriteString(w, one);
        }

        public static void WriteDateTime(IBufferWriter<byte> w, DateTime value)
        {
            var span = w.GetSpan(MaxDateTimeLength + 2);
            span[0] = (byte)'"';
            int n = FormatDateTime(span.Slice(1), value);
            span[n + 1] = (byte)'"';
            w.Advance(n + 2);
        }

        public static void WriteDateTimeOffset(IBufferWriter<byte> w, DateTimeOffset value)
        {
            var span = w.GetSpan(MaxDateTimeLength + 2);
            span[0] = (byte)'"';
            int n = FormatDateTimeOffset(span.Slice(1), value);
            span[n + 1] = (byte)'"';
            w.Advance(n + 2);
        }

        /// <summary>
        /// Json.NET's DateTime text: <c>yyyy-MM-ddTHH:mm:ss</c>, then the fraction only when it is non-zero with
        /// trailing zeros trimmed, then <c>Z</c> for Utc, <c>+HH:mm</c>/<c>-HH:mm</c> for Local, nothing for
        /// Unspecified. Returns the number of bytes written (at most <see cref="MaxDateTimeLength"/>).
        /// </summary>
        public static int FormatDateTime(Span<byte> dest, DateTime value)
        {
            int n = FormatDateTimeCore(dest, value);
            switch (value.Kind)
            {
                case DateTimeKind.Utc:
                    dest[n++] = (byte)'Z';
                    break;
                case DateTimeKind.Local:
                    n += FormatOffset(dest.Slice(n), TimeZoneInfo.Local.GetUtcOffset(value));
                    break;
            }
            return n;
        }

        /// <summary>Same as <see cref="FormatDateTime"/> for the clock time, and the offset is always written.</summary>
        public static int FormatDateTimeOffset(Span<byte> dest, DateTimeOffset value)
        {
            int n = FormatDateTimeCore(dest, value.DateTime);
            return n + FormatOffset(dest.Slice(n), value.Offset);
        }

        private static int FormatDateTimeCore(Span<byte> d, DateTime dt)
        {
            int year = dt.Year;
            d[0] = (byte)('0' + year / 1000); d[1] = (byte)('0' + year / 100 % 10); d[2] = (byte)('0' + year / 10 % 10); d[3] = (byte)('0' + year % 10);
            d[4] = (byte)'-'; WriteTwoDigits(d, 5, dt.Month);
            d[7] = (byte)'-'; WriteTwoDigits(d, 8, dt.Day);
            d[10] = (byte)'T'; WriteTwoDigits(d, 11, dt.Hour);
            d[13] = (byte)':'; WriteTwoDigits(d, 14, dt.Minute);
            d[16] = (byte)':'; WriteTwoDigits(d, 17, dt.Second);
            int n = 19;
            int fraction = (int)(dt.Ticks % TimeSpan.TicksPerSecond);
            if (fraction != 0)
            {
                int digits = 7;
                while (fraction % 10 == 0) { fraction /= 10; digits--; }
                d[n++] = (byte)'.';
                for (int i = digits - 1; i >= 0; i--) { d[n + i] = (byte)('0' + fraction % 10); fraction /= 10; }
                n += digits;
            }
            return n;
        }

        private static int FormatOffset(Span<byte> d, TimeSpan offset)
        {
            if (offset < TimeSpan.Zero) { d[0] = (byte)'-'; offset = -offset; }
            else d[0] = (byte)'+';
            WriteTwoDigits(d, 1, offset.Hours);
            d[3] = (byte)':';
            WriteTwoDigits(d, 4, offset.Minutes);
            return 6;
        }

        private static void WriteTwoDigits(Span<byte> d, int at, int value)
        {
            d[at] = (byte)('0' + value / 10);
            d[at + 1] = (byte)('0' + value % 10);
        }

        public static void WriteQuotedAscii(IBufferWriter<byte> w, string text)
        {
            var s = w.GetSpan(text.Length + 2);
            s[0] = (byte)'"';
            for (int i = 0; i < text.Length; i++) s[i + 1] = (byte)text[i];
            s[text.Length + 1] = (byte)'"';
            w.Advance(text.Length + 2);
        }

        /// <summary>Writes a property name followed by ':' (the name is written with full escaping).</summary>
        public static void WritePropertyName(IBufferWriter<byte> w, string name)
        {
            WriteString(w, name);
            WriteByte(w, (byte)':');
        }

        // ------------------------------------------------------------------ decoding

        /// <summary>Decodes the contents of a JSON string (without the surrounding quotes), resolving escapes.</summary>
        public static string DecodeString(ReadOnlySpan<byte> contents)
        {
            if (contents.IndexOf((byte)'\\') < 0)
            {
                return ToStringUtf8(contents);
            }
            return DecodeEscaped(contents);
        }

        /// <summary>Unescapes JSON string contents into <paramref name="dest"/> (UTF-8). Returns bytes written; dest must be at least contents.Length.</summary>
        public static int Unescape(ReadOnlySpan<byte> contents, Span<byte> dest)
        {
            int p = 0;
            for (int i = 0; i < contents.Length; i++)
            {
                byte b = contents[i];
                if (b != (byte)'\\') { dest[p++] = b; continue; }
                i++;
                if (i >= contents.Length) throw new JsonRpcBindException("Unterminated escape sequence.");
                switch (contents[i])
                {
                    case (byte)'"': dest[p++] = (byte)'"'; break;
                    case (byte)'\\': dest[p++] = (byte)'\\'; break;
                    case (byte)'/': dest[p++] = (byte)'/'; break;
                    case (byte)'\'': dest[p++] = (byte)'\''; break;   // lenient single-quoted strings
                    case (byte)'b': dest[p++] = (byte)'\b'; break;
                    case (byte)'f': dest[p++] = (byte)'\f'; break;
                    case (byte)'n': dest[p++] = (byte)'\n'; break;
                    case (byte)'r': dest[p++] = (byte)'\r'; break;
                    case (byte)'t': dest[p++] = (byte)'\t'; break;
                    case (byte)'u':
                        {
                            int cp = ParseHex4(contents, i + 1);
                            i += 4;
                            if (cp >= 0xD800 && cp <= 0xDBFF && i + 6 < contents.Length && contents[i + 1] == (byte)'\\' && contents[i + 2] == (byte)'u')
                            {
                                int low = ParseHex4(contents, i + 3);
                                if (low >= 0xDC00 && low <= 0xDFFF)
                                {
                                    cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                                    i += 6;
                                }
                            }
                            p += EncodeCodePoint(cp, dest.Slice(p));
                            break;
                        }
                    default:
                        throw new JsonRpcBindException("Invalid escape sequence.");
                }
            }
            return p;
        }

        private static string DecodeEscaped(ReadOnlySpan<byte> contents)
        {
            byte[] rented = ArrayPool<byte>.Shared.Rent(contents.Length);
            try
            {
                int n = Unescape(contents, rented);
                return Encoding.UTF8.GetString(rented, 0, n);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static int EncodeCodePoint(int cp, Span<byte> dest)
        {
            if (cp < 0x80) { dest[0] = (byte)cp; return 1; }
            if (cp < 0x800) { dest[0] = (byte)(0xC0 | (cp >> 6)); dest[1] = (byte)(0x80 | (cp & 0x3F)); return 2; }
            if (cp < 0x10000) { dest[0] = (byte)(0xE0 | (cp >> 12)); dest[1] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[2] = (byte)(0x80 | (cp & 0x3F)); return 3; }
            dest[0] = (byte)(0xF0 | (cp >> 18)); dest[1] = (byte)(0x80 | ((cp >> 12) & 0x3F)); dest[2] = (byte)(0x80 | ((cp >> 6) & 0x3F)); dest[3] = (byte)(0x80 | (cp & 0x3F));
            return 4;
        }

        private static int ParseHex4(ReadOnlySpan<byte> s, int at)
        {
            if (at + 4 > s.Length) throw new JsonRpcBindException("Truncated \\u escape.");
            int v = 0;
            for (int k = 0; k < 4; k++)
            {
                int b = s[at + k];
                int d = b >= '0' && b <= '9' ? b - '0' : b >= 'a' && b <= 'f' ? b - 'a' + 10 : b >= 'A' && b <= 'F' ? b - 'A' + 10 : -1;
                if (d < 0) throw new JsonRpcBindException("Invalid \\u escape.");
                v = (v << 4) | d;
            }
            return v;
        }

        public static string ToStringUtf8(ReadOnlySpan<byte> utf8)
        {
            if (utf8.Length == 0) return string.Empty;
#if NETSTANDARD2_0
            return Encoding.UTF8.GetString(utf8.ToArray());
#else
            return Encoding.UTF8.GetString(utf8);
#endif
        }

        // ------------------------------------------------------------------ comparisons / hashing

        /// <summary>Compares raw name bytes ignoring ASCII case.</summary>
        public static bool EqualsIgnoreAsciiCase(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                int x = a[i], y = b[i];
                if (x == y) continue;
                if ((uint)(x - 'A') <= 'Z' - 'A') x += 32;
                if ((uint)(y - 'A') <= 'Z' - 'A') y += 32;
                if (x != y) return false;
            }
            return true;
        }

        /// <summary>
        /// A non-cryptographic hash of the bytes, eight at a time (FNV-style multiply-xor over 64-bit words);
        /// used by the span-keyed method table. Only meaningful within one process.
        /// </summary>
        public static int Hash(ReadOnlySpan<byte> bytes)
        {
            ulong h = 0x9E3779B97F4A7C15UL ^ (ulong)bytes.Length;
            while (bytes.Length >= 8)
            {
                h = (h ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes)) * 0x100000001B3UL;
                h ^= h >> 29;
                bytes = bytes.Slice(8);
            }
            if (bytes.Length > 0)
            {
                ulong tail = 0;
                for (int i = 0; i < bytes.Length; i++) tail |= (ulong)bytes[i] << (8 * i);
                h = (h ^ tail) * 0x100000001B3UL;
                h ^= h >> 29;
            }
            return (int)(h ^ (h >> 32));
        }

        /// <summary>Classifies raw id bytes per JSON-RPC 2.0: null, string, or integer are valid.</summary>
        public static JsonRpcIdKind ClassifyId(ReadOnlySpan<byte> raw)
        {
            if (raw.Length == 0) return JsonRpcIdKind.Absent;
            byte b = raw[0];
            if (b == (byte)'"' || b == (byte)'\'') return JsonRpcIdKind.String;
            if (b == (byte)'n') return raw.Length == 4 ? JsonRpcIdKind.Null : JsonRpcIdKind.Invalid;
            if (b == (byte)'-' || (b >= (byte)'0' && b <= (byte)'9'))
            {
                for (int i = 1; i < raw.Length; i++)
                {
                    byte c = raw[i];
                    if (c < (byte)'0' || c > (byte)'9') return JsonRpcIdKind.Invalid;
                }
                return JsonRpcIdKind.Integer;
            }
            return JsonRpcIdKind.Invalid;
        }

        public static object IdToObject(ReadOnlySpan<byte> raw, JsonRpcIdKind kind)
        {
            switch (kind)
            {
                case JsonRpcIdKind.Integer:
                    if (Utf8Parser.TryParse(raw, out long l, out int consumed) && consumed == raw.Length) return l;
                    return ToStringUtf8(raw);
                case JsonRpcIdKind.String:
                    return DecodeString(raw.Slice(1, raw.Length - 2));
                default:
                    return null;
            }
        }

        /// <summary>Serializes an id object (integer/string/null) back to raw JSON bytes.</summary>
        public static void WriteId(IBufferWriter<byte> w, object id)
        {
            switch (id)
            {
                case null: WriteNull(w); break;
                case string s: WriteString(w, s); break;
                case long l: WriteInt64(w, l); break;
                case int i: WriteInt64(w, i); break;
                case short sh: WriteInt64(w, sh); break;
                case ulong ul: WriteUInt64(w, ul); break;
                case uint ui: WriteUInt64(w, ui); break;
                default: WriteString(w, Convert.ToString(id, CultureInfo.InvariantCulture)); break;
            }
        }
    }
}
