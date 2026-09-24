using System;
using System.Buffers;
using System.Buffers.Text;
using System.Globalization;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// An owned snapshot of a request's <c>id</c>: its kind, the integer when it fits in an <see cref="long"/>, the
    /// decoded text of a string id, or the exact digits of an integer too large for Int64. Read it inside a method
    /// with <see cref="Handler.RpcRequestId"/> or <see cref="JsonRpcContext.CurrentRequestId"/>; unlike the raw span
    /// from <see cref="Handler.RpcRequestIdRaw"/> it may be stored, captured and handed to other threads.
    /// The default value is <see cref="JsonRpcIdKind.Absent"/>, the id of a notification and of code that runs outside an invocation.
    /// </summary>
    public readonly struct JsonRpcRequestId : IEquatable<JsonRpcRequestId>
    {
        private readonly JsonRpcIdKind _kind;
        private readonly long _integer;
        private readonly string _text;

        private JsonRpcRequestId(JsonRpcIdKind kind, long integer, string text)
        {
            _kind = kind;
            _integer = integer;
            _text = text;
        }

        /// <summary>No id: a notification, or no invocation in progress.</summary>
        public static JsonRpcRequestId Absent => default;

        /// <summary>The JSON literal <c>null</c>.</summary>
        public static JsonRpcRequestId Null => new JsonRpcRequestId(JsonRpcIdKind.Null, 0, null);

        public static JsonRpcRequestId FromInt64(long value) => new JsonRpcRequestId(JsonRpcIdKind.Integer, value, null);

        public static JsonRpcRequestId FromString(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new JsonRpcRequestId(JsonRpcIdKind.String, 0, value);
        }

        /// <summary>
        /// Builds the snapshot from the raw JSON of an id (what <see cref="JsonRpcRequestReader.IdRaw"/> yields:
        /// <c>12</c>, <c>"abc"</c>, <c>null</c>, or empty when absent). An integer that does not fit in Int64 keeps its
        /// digits in <see cref="GetIntegerText"/>; a value that is not a valid JSON-RPC id is <see cref="JsonRpcIdKind.Invalid"/>.
        /// </summary>
        public static JsonRpcRequestId FromRaw(ReadOnlySpan<byte> raw)
        {
            return FromRaw(raw, Utf8Json.ClassifyId(raw));
        }

        internal static JsonRpcRequestId FromRaw(ReadOnlySpan<byte> raw, JsonRpcIdKind kind)
        {
            switch (kind)
            {
                case JsonRpcIdKind.Integer:
                    if (Utf8Parser.TryParse(raw, out long value, out int consumed) && consumed == raw.Length) return FromInt64(value);
                    return new JsonRpcRequestId(JsonRpcIdKind.Integer, 0, Utf8Json.ToStringUtf8(raw));
                case JsonRpcIdKind.String:
                    return FromString(raw.Length >= 2 ? Utf8Json.DecodeString(raw.Slice(1, raw.Length - 2)) : string.Empty);
                case JsonRpcIdKind.Null:
                    return Null;
                case JsonRpcIdKind.Absent:
                    return Absent;
                default:
                    return new JsonRpcRequestId(JsonRpcIdKind.Invalid, 0, Utf8Json.ToStringUtf8(raw));
            }
        }

        /// <summary>The snapshot of a CLR id value as pre/post handlers see it on <see cref="JsonRequest.Id"/> (long, string or null).</summary>
        public static JsonRpcRequestId FromObject(object id)
        {
            switch (id)
            {
                case null: return Null;
                case string s: return FromString(s);
                case long l: return FromInt64(l);
                case int i: return FromInt64(i);
                case short sh: return FromInt64(sh);
                case byte b: return FromInt64(b);
                case sbyte sb: return FromInt64(sb);
                case ushort us: return FromInt64(us);
                case uint ui: return FromInt64(ui);
                case ulong ul:
                    return ul <= long.MaxValue ? FromInt64((long)ul) : new JsonRpcRequestId(JsonRpcIdKind.Integer, 0, ul.ToString(CultureInfo.InvariantCulture));
                case JsonRpcRequestId r: return r;
                default:
                    return new JsonRpcRequestId(JsonRpcIdKind.Invalid, 0, Convert.ToString(id, CultureInfo.InvariantCulture));
            }
        }

        public JsonRpcIdKind Kind => _kind;
        public bool IsAbsent => _kind == JsonRpcIdKind.Absent;
        public bool IsNull => _kind == JsonRpcIdKind.Null;
        public bool IsInteger => _kind == JsonRpcIdKind.Integer;
        public bool IsString => _kind == JsonRpcIdKind.String;

        /// <summary>True for an integer id that fits in Int64; false for a string id, null, an absent id, or an integer outside the Int64 range.</summary>
        public bool TryGetInt64(out long value)
        {
            if (_kind == JsonRpcIdKind.Integer && _text == null)
            {
                value = _integer;
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>The decoded text of a string id; null for every other kind.</summary>
        public string GetString() => _kind == JsonRpcIdKind.String ? _text : null;

        /// <summary>The exact digits of an integer id (also when it is outside the Int64 range); null for every other kind.</summary>
        public string GetIntegerText()
        {
            if (_kind != JsonRpcIdKind.Integer) return null;
            return _text ?? _integer.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The id as the boxed path carries it: a long, a string, null; the digits as a string for an oversized integer.</summary>
        public object ToObject()
        {
            switch (_kind)
            {
                case JsonRpcIdKind.Integer: return _text ?? (object)_integer;
                case JsonRpcIdKind.String: return _text;
                default: return null;
            }
        }

        /// <summary>Writes the id as JSON (<c>null</c> for null, absent and invalid ids).</summary>
        public void WriteTo(IBufferWriter<byte> output)
        {
            switch (_kind)
            {
                case JsonRpcIdKind.Integer:
                    if (_text == null) Utf8Json.WriteInt64(output, _integer);
                    else Utf8Json.WriteAscii(output, _text.AsSpan());
                    break;
                case JsonRpcIdKind.String:
                    Utf8Json.WriteString(output, _text);
                    break;
                default:
                    Utf8Json.WriteNull(output);
                    break;
            }
        }

        public bool Equals(JsonRpcRequestId other)
        {
            return _kind == other._kind && _integer == other._integer && string.Equals(_text, other._text, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => obj is JsonRpcRequestId other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)_kind * 397;
                h = (h * 31) ^ _integer.GetHashCode();
                if (_text != null) h = (h * 31) ^ StringComparer.Ordinal.GetHashCode(_text);
                return h;
            }
        }

        public static bool operator ==(JsonRpcRequestId left, JsonRpcRequestId right) => left.Equals(right);
        public static bool operator !=(JsonRpcRequestId left, JsonRpcRequestId right) => !left.Equals(right);

        /// <summary>The value as text: the digits, the decoded string, "null", or "" when absent.</summary>
        public override string ToString()
        {
            switch (_kind)
            {
                case JsonRpcIdKind.Integer: return GetIntegerText();
                case JsonRpcIdKind.String: return _text;
                case JsonRpcIdKind.Null: return "null";
                case JsonRpcIdKind.Invalid: return _text ?? string.Empty;
                default: return string.Empty;
            }
        }
    }
}
