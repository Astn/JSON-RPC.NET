using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.Jsmn
{
    /// <summary>A position inside a tokenized document.</summary>
    public ref struct JsmnCursor
    {
        public ReadOnlySpan<byte> Doc;
        public JsmnToken[] Tokens;
        public int Index;

        public JsmnCursor(ReadOnlySpan<byte> doc, JsmnToken[] tokens, int index)
        {
            Doc = doc;
            Tokens = tokens;
            Index = index;
        }

        public ref JsmnToken Token => ref Tokens[Index];
        public ReadOnlySpan<byte> Text => JsmnTokenizer.Slice(Doc, Tokens[Index]);
        public bool IsNull => JsmnTokenizer.IsNull(Doc, Tokens[Index]);

        /// <summary>Index of the token following the current subtree.</summary>
        public int Next()
        {
            int need = 1, i = Index;
            while (need > 0) { need += Tokens[i].Size; need--; i++; }
            return i;
        }
    }

    public delegate T JsmnTokenReader<T>(ref JsmnCursor cursor);

    /// <summary>Typed reader cache: primitives bind without boxing, everything else goes through the boxed mapper.</summary>
    public static class JsmnReader<T>
    {
        public static readonly JsmnTokenReader<T> Read = Build();

        private static JsmnTokenReader<T> Build()
        {
            var t = typeof(T);
            object r = null;
            if (t == typeof(string)) r = new JsmnTokenReader<string>(JsmnMapper.ReadString);
            else if (t == typeof(int)) r = new JsmnTokenReader<int>(JsmnMapper.ReadInt32);
            else if (t == typeof(long)) r = new JsmnTokenReader<long>(JsmnMapper.ReadInt64);
            else if (t == typeof(double)) r = new JsmnTokenReader<double>(JsmnMapper.ReadDouble);
            else if (t == typeof(float)) r = new JsmnTokenReader<float>(JsmnMapper.ReadSingle);
            else if (t == typeof(bool)) r = new JsmnTokenReader<bool>(JsmnMapper.ReadBoolean);
            else if (t == typeof(decimal)) r = new JsmnTokenReader<decimal>(JsmnMapper.ReadDecimal);
            else if (t == typeof(short)) r = new JsmnTokenReader<short>((ref JsmnCursor c) => checked((short)JsmnMapper.ReadInt64(ref c)));
            else if (t == typeof(ushort)) r = new JsmnTokenReader<ushort>((ref JsmnCursor c) => checked((ushort)JsmnMapper.ReadInt64(ref c)));
            else if (t == typeof(byte)) r = new JsmnTokenReader<byte>((ref JsmnCursor c) => checked((byte)JsmnMapper.ReadInt64(ref c)));
            else if (t == typeof(sbyte)) r = new JsmnTokenReader<sbyte>((ref JsmnCursor c) => checked((sbyte)JsmnMapper.ReadInt64(ref c)));
            else if (t == typeof(uint)) r = new JsmnTokenReader<uint>((ref JsmnCursor c) => checked((uint)JsmnMapper.ReadInt64(ref c)));
            else if (t == typeof(ulong)) r = new JsmnTokenReader<ulong>(JsmnMapper.ReadUInt64);
            else if (t == typeof(char)) r = new JsmnTokenReader<char>(JsmnMapper.ReadChar);
            else if (t == typeof(DateTime)) r = new JsmnTokenReader<DateTime>(JsmnMapper.ReadDateTime);
            if (r != null) return (JsmnTokenReader<T>)r;
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null)
            {
                // Nullable<U>: null token -> null, otherwise the unboxed U reader
                var factory = typeof(JsmnReader<T>).GetMethod(nameof(MakeNullable), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).MakeGenericMethod(underlying);
                return (JsmnTokenReader<T>)factory.Invoke(null, null);
            }
            return (ref JsmnCursor c) => (T)JsmnMapper.ReadObject(ref c, typeof(T));
        }

        private static JsmnTokenReader<U?> MakeNullable<U>() where U : struct
        {
            var inner = JsmnReader<U>.Read;
            return (ref JsmnCursor c) => c.IsNull ? (U?)null : inner(ref c);
        }
    }

    /// <summary>Typed writer cache, mirror of <see cref="JsmnReader{T}"/>.</summary>
    public static class JsmnWriter<T>
    {
        public static readonly Action<IBufferWriter<byte>, T> Write = Build();

        private static Action<IBufferWriter<byte>, T> Build()
        {
            var t = typeof(T);
            object w = null;
            if (t == typeof(string)) w = new Action<IBufferWriter<byte>, string>(Utf8Json.WriteString);
            else if (t == typeof(int)) w = new Action<IBufferWriter<byte>, int>((o, v) => Utf8Json.WriteInt64(o, v));
            else if (t == typeof(long)) w = new Action<IBufferWriter<byte>, long>(Utf8Json.WriteInt64);
            else if (t == typeof(double)) w = new Action<IBufferWriter<byte>, double>(Utf8Json.WriteDouble);
            else if (t == typeof(float)) w = new Action<IBufferWriter<byte>, float>(Utf8Json.WriteSingle);
            else if (t == typeof(bool)) w = new Action<IBufferWriter<byte>, bool>(Utf8Json.WriteBool);
            else if (t == typeof(decimal)) w = new Action<IBufferWriter<byte>, decimal>(Utf8Json.WriteDecimal);
            else if (t == typeof(short)) w = new Action<IBufferWriter<byte>, short>((o, v) => Utf8Json.WriteInt64(o, v));
            else if (t == typeof(ushort)) w = new Action<IBufferWriter<byte>, ushort>((o, v) => Utf8Json.WriteInt64(o, v));
            else if (t == typeof(byte)) w = new Action<IBufferWriter<byte>, byte>((o, v) => Utf8Json.WriteInt64(o, v));
            else if (t == typeof(sbyte)) w = new Action<IBufferWriter<byte>, sbyte>((o, v) => Utf8Json.WriteInt64(o, v));
            else if (t == typeof(uint)) w = new Action<IBufferWriter<byte>, uint>((o, v) => Utf8Json.WriteUInt64(o, v));
            else if (t == typeof(ulong)) w = new Action<IBufferWriter<byte>, ulong>(Utf8Json.WriteUInt64);
            else if (t == typeof(char)) w = new Action<IBufferWriter<byte>, char>(Utf8Json.WriteChar);
            else if (t == typeof(DateTime)) w = new Action<IBufferWriter<byte>, DateTime>(Utf8Json.WriteDateTime);
            if (w != null) return (Action<IBufferWriter<byte>, T>)w;
            var underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null)
            {
                var factory = typeof(JsmnWriter<T>).GetMethod(nameof(MakeNullable), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).MakeGenericMethod(underlying);
                return (Action<IBufferWriter<byte>, T>)factory.Invoke(null, null);
            }
            return (o, v) => JsmnMapper.WriteObject(o, v, typeof(T), 0);
        }

        private static Action<IBufferWriter<byte>, U?> MakeNullable<U>() where U : struct
        {
            var inner = JsmnWriter<U>.Write;
            return (o, v) => { if (v.HasValue) inner(o, v.Value); else Utf8Json.WriteNull(o); };
        }
    }

    /// <summary>
    /// Converts between jsmn tokens and CLR values. Coercions follow what the library has always accepted
    /// through Json.NET: numbers to bool/char, integers to floating types, ISO strings to DateTime,
    /// case-insensitive member names, public fields and properties in declaration order, nulls written out.
    /// </summary>
    public static class JsmnMapper
    {
        private const int MaxDepth = 64;

        // ------------------------------------------------------------------ primitives

        public static string ReadString(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            if (t.Type == JsmnType.String) return Utf8Json.DecodeString(c.Text);
            if (t.Type == JsmnType.Primitive)
            {
                if (c.IsNull) return null;
                return Utf8Json.ToStringUtf8(c.Text);
            }
            throw Bind("string", ref c);
        }

        public static long ReadInt64(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            var text = c.Text;
            if (t.Type == JsmnType.Primitive || t.Type == JsmnType.String)
            {
                if (Utf8Parser.TryParse(text, out long v, out int consumed) && consumed == text.Length) return v;
                if (text.Length == 4 && text[0] == (byte)'t') return 1;
                if (text.Length == 5 && text[0] == (byte)'f') return 0;
                if (Utf8Parser.TryParse(text, out double d, out consumed) && consumed == text.Length) return checked((long)Math.Round(d, MidpointRounding.ToEven));
            }
            throw Bind("integer", ref c);
        }

        public static ulong ReadUInt64(ref JsmnCursor c)
        {
            var text = c.Text;
            if (Utf8Parser.TryParse(text, out ulong v, out int consumed) && consumed == text.Length) return v;
            return checked((ulong)ReadInt64(ref c));
        }

        public static int ReadInt32(ref JsmnCursor c)
        {
            var text = c.Text;
            if (c.Token.Type == JsmnType.Primitive && Utf8Parser.TryParse(text, out int v, out int consumed) && consumed == text.Length) return v;
            return checked((int)ReadInt64(ref c));
        }

        public static double ReadDouble(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            var text = c.Text;
            if (t.Type == JsmnType.Primitive || t.Type == JsmnType.String)
            {
                if (Utf8Parser.TryParse(text, out double v, out int consumed) && consumed == text.Length) return v;
                if (text.Length == 4 && text[0] == (byte)'t') return 1;
                if (text.Length == 5 && text[0] == (byte)'f') return 0;
                // "NaN", "Infinity", "-Infinity": what Json.NET (and Utf8Json.WriteDouble) write for non-finite values
                if (Utf8Json.TryParseNonFinite(text, out v)) return v;
                if (t.Type == JsmnType.String)
                {
                    var s = Utf8Json.DecodeString(text);
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
                }
            }
            throw Bind("number", ref c);
        }

        public static float ReadSingle(ref JsmnCursor c)
        {
            var text = c.Text;
            if (c.Token.Type == JsmnType.Primitive && Utf8Parser.TryParse(text, out float v, out int consumed) && consumed == text.Length) return v;
            return (float)ReadDouble(ref c);
        }

        public static decimal ReadDecimal(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            var text = c.Text;
            if (t.Type == JsmnType.Primitive || t.Type == JsmnType.String)
            {
                if (Utf8Parser.TryParse(text, out decimal v, out int consumed) && consumed == text.Length) return v;
                if (text.Length == 4 && text[0] == (byte)'t') return 1;
                if (text.Length == 5 && text[0] == (byte)'f') return 0;
                if (Utf8Parser.TryParse(text, out double d, out consumed) && consumed == text.Length) return (decimal)d;
            }
            throw Bind("decimal", ref c);
        }

        public static bool ReadBoolean(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            var text = c.Text;
            if (text.Length == 4 && text[0] == (byte)'t' && text[1] == (byte)'r' && text[2] == (byte)'u' && text[3] == (byte)'e') return true;
            if (text.Length == 5 && text[0] == (byte)'f' && text[1] == (byte)'a') return false;
            if (t.Type == JsmnType.Primitive || t.Type == JsmnType.String)
            {
                if (Utf8Parser.TryParse(text, out double d, out int consumed) && consumed == text.Length) return d != 0;
                if (t.Type == JsmnType.String && bool.TryParse(Utf8Json.DecodeString(text), out bool b)) return b;
            }
            throw Bind("boolean", ref c);
        }

        public static char ReadChar(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            if (t.Type == JsmnType.String)
            {
                var s = Utf8Json.DecodeString(c.Text);
                if (s.Length == 1) return s[0];
                throw Bind("single character", ref c);
            }
            return checked((char)ReadInt64(ref c));
        }

        public static DateTime ReadDateTime(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            if (t.Type == JsmnType.String)
            {
                var s = Utf8Json.DecodeString(c.Text);
                if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)) return dt;
            }
            throw Bind("DateTime", ref c);
        }

        private static JsonRpcBindException Bind(string expected, ref JsmnCursor c)
        {
            var raw = JsmnTokenizer.RawJson(c.Doc, c.Token);
            string text = raw.Length > 64 ? Utf8Json.ToStringUtf8(raw.Slice(0, 64)) + "..." : Utf8Json.ToStringUtf8(raw);
            return new JsonRpcBindException("Could not convert " + text + " to " + expected + ".");
        }

        // ------------------------------------------------------------------ boxed read

        public static object ReadObject(ref JsmnCursor c, Type type)
        {
            if (type == typeof(object)) return ReadDynamic(ref c);
            var underlying = Nullable.GetUnderlyingType(type);
            if (c.IsNull)
            {
                if (type.IsValueType && underlying == null) throw new JsonRpcBindException("Cannot convert null to " + type.Name + ".");
                return null;
            }
            if (underlying != null) type = underlying;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String: return ReadString(ref c);
                case TypeCode.Int32: return ReadInt32(ref c);
                case TypeCode.Int64: return ReadInt64(ref c);
                case TypeCode.Double: return ReadDouble(ref c);
                case TypeCode.Single: return ReadSingle(ref c);
                case TypeCode.Boolean: return ReadBoolean(ref c);
                case TypeCode.Decimal: return ReadDecimal(ref c);
                case TypeCode.Int16: return checked((short)ReadInt64(ref c));
                case TypeCode.UInt16: return checked((ushort)ReadInt64(ref c));
                case TypeCode.Byte: return checked((byte)ReadInt64(ref c));
                case TypeCode.SByte: return checked((sbyte)ReadInt64(ref c));
                case TypeCode.UInt32: return checked((uint)ReadInt64(ref c));
                case TypeCode.UInt64: return ReadUInt64(ref c);
                case TypeCode.Char: return ReadChar(ref c);
                case TypeCode.DateTime: return ReadDateTime(ref c);
            }
            if (type.IsEnum)
            {
                if (c.Token.Type == JsmnType.String)
                {
                    var s = Utf8Json.DecodeString(c.Text);
                    try { return Enum.Parse(type, s, true); }
                    catch (Exception ex) { throw new JsonRpcBindException("Could not convert \"" + s + "\" to " + type.Name + ".", ex); }
                }
                return Enum.ToObject(type, ReadInt64(ref c));
            }
            if (type == typeof(DateTimeOffset))
            {
                var s = ReadString(ref c);
                if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)) return dto;
                throw Bind("DateTimeOffset", ref c);
            }
            if (type == typeof(TimeSpan))
            {
                var s = ReadString(ref c);
                if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts)) return ts;
                throw Bind("TimeSpan", ref c);
            }
            if (type == typeof(Guid))
            {
                var s = ReadString(ref c);
                if (Guid.TryParse(s, out var g)) return g;
                throw Bind("Guid", ref c);
            }
            if (type == typeof(Uri))
            {
                var s = ReadString(ref c);
                if (Uri.TryCreate(s, UriKind.RelativeOrAbsolute, out var u)) return u;
                throw Bind("Uri", ref c);
            }
            if (type == typeof(byte[]))
            {
                if (c.Token.Type == JsmnType.String)
                {
                    try { return Convert.FromBase64String(Utf8Json.DecodeString(c.Text)); }
                    catch (FormatException ex) { throw new JsonRpcBindException("Invalid base64 string.", ex); }
                }
                return ReadArray(ref c, typeof(byte));
            }
            if (type.IsArray)
            {
                return ReadArray(ref c, type.GetElementType());
            }
            var plan = TypePlan.For(type);
            switch (plan.Kind)
            {
                case PlanKind.List:
                    {
                        var list = ReadList(ref c, plan.ElementType, plan);
                        return list;
                    }
                case PlanKind.Dictionary:
                    return ReadDictionary(ref c, plan);
                case PlanKind.Poco:
                    return ReadPoco(ref c, plan);
                default:
                    throw new NotSupportedException("Type " + type.FullName + " is not supported by the built-in serializer.");
            }
        }

        private static object ReadDynamic(ref JsmnCursor c)
        {
            ref var t = ref c.Token;
            switch (t.Type)
            {
                case JsmnType.String:
                    return Utf8Json.DecodeString(c.Text);
                case JsmnType.Primitive:
                    {
                        var text = c.Text;
                        if (c.IsNull) return null;
                        if (text.Length == 4 && text[0] == (byte)'t') return true;
                        if (text.Length == 5 && text[0] == (byte)'f') return false;
                        if (Utf8Parser.TryParse(text, out long l, out int consumed) && consumed == text.Length) return l;
                        if (Utf8Parser.TryParse(text, out double d, out consumed) && consumed == text.Length) return d;
                        return Utf8Json.ToStringUtf8(text);
                    }
                case JsmnType.Array:
                    {
                        var list = new List<object>(t.Size);
                        int idx = c.Index + 1;
                        for (int i = 0; i < t.Size; i++)
                        {
                            var child = new JsmnCursor(c.Doc, c.Tokens, idx);
                            list.Add(ReadDynamic(ref child));
                            idx = child.Next();
                        }
                        return list;
                    }
                case JsmnType.Object:
                    {
                        var dict = new Dictionary<string, object>(t.Size);
                        int idx = c.Index + 1;
                        for (int i = 0; i < t.Size; i++)
                        {
                            var key = new JsmnCursor(c.Doc, c.Tokens, idx);
                            string name = Utf8Json.DecodeString(key.Text);
                            if (key.Token.Size > 0)
                            {
                                var val = new JsmnCursor(c.Doc, c.Tokens, idx + 1);
                                dict[name] = ReadDynamic(ref val);
                            }
                            idx = key.Next();
                        }
                        return dict;
                    }
            }
            throw Bind("value", ref c);
        }

        private static Array ReadArray(ref JsmnCursor c, Type elementType)
        {
            ref var t = ref c.Token;
            if (t.Type != JsmnType.Array) throw Bind("array", ref c);
            var arr = Array.CreateInstance(elementType, t.Size);
            int idx = c.Index + 1;
            for (int i = 0; i < t.Size; i++)
            {
                var child = new JsmnCursor(c.Doc, c.Tokens, idx);
                arr.SetValue(ReadObject(ref child, elementType), i);
                idx = child.Next();
            }
            return arr;
        }

        private static object ReadList(ref JsmnCursor c, Type elementType, TypePlan plan)
        {
            ref var t = ref c.Token;
            if (t.Type != JsmnType.Array) throw Bind("array", ref c);
            var list = plan.Create();
            int idx = c.Index + 1;
            for (int i = 0; i < t.Size; i++)
            {
                var child = new JsmnCursor(c.Doc, c.Tokens, idx);
                plan.Add(list, ReadObject(ref child, elementType));
                idx = child.Next();
            }
            return list;
        }

        private static object ReadDictionary(ref JsmnCursor c, TypePlan plan)
        {
            ref var t = ref c.Token;
            if (t.Type != JsmnType.Object) throw Bind("object", ref c);
            var dict = plan.Create();
            int idx = c.Index + 1;
            for (int i = 0; i < t.Size; i++)
            {
                var key = new JsmnCursor(c.Doc, c.Tokens, idx);
                if (key.Token.Size > 0)
                {
                    string name = Utf8Json.DecodeString(key.Text);
                    var val = new JsmnCursor(c.Doc, c.Tokens, idx + 1);
                    object k = plan.KeyType == typeof(string) ? name : Convert.ChangeType(name, plan.KeyType, CultureInfo.InvariantCulture);
                    plan.DictionaryAdd(dict, k, ReadObject(ref val, plan.ElementType));
                }
                idx = key.Next();
            }
            return dict;
        }

        private static object ReadPoco(ref JsmnCursor c, TypePlan plan)
        {
            ref var t = ref c.Token;
            if (t.Type != JsmnType.Object) throw Bind("object", ref c);
            var obj = plan.Create();
            int idx = c.Index + 1;
            for (int i = 0; i < t.Size; i++)
            {
                var key = new JsmnCursor(c.Doc, c.Tokens, idx);
                if (key.Token.Size > 0)
                {
                    var member = plan.FindMember(key.Text, key.Token.Escaped);
                    if (member != null && member.Set != null)
                    {
                        var val = new JsmnCursor(c.Doc, c.Tokens, idx + 1);
                        member.Set(obj, ReadObject(ref val, member.Type));
                    }
                }
                idx = key.Next();
            }
            return obj;
        }

        // ------------------------------------------------------------------ write

        public static void WriteObject(IBufferWriter<byte> w, object value, Type declaredType, int depth)
        {
            if (value == null) { Utf8Json.WriteNull(w); return; }
            if (depth > MaxDepth) throw new JsonRpcBindException("Object graph is too deep (possible cycle).");
            var type = value.GetType();
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String: Utf8Json.WriteString(w, (string)value); return;
                case TypeCode.Int32: Utf8Json.WriteInt64(w, (int)value); return;
                case TypeCode.Int64: Utf8Json.WriteInt64(w, (long)value); return;
                case TypeCode.Double: Utf8Json.WriteDouble(w, (double)value); return;
                case TypeCode.Single: Utf8Json.WriteSingle(w, (float)value); return;
                case TypeCode.Boolean: Utf8Json.WriteBool(w, (bool)value); return;
                case TypeCode.Decimal: Utf8Json.WriteDecimal(w, (decimal)value); return;
                case TypeCode.Int16: Utf8Json.WriteInt64(w, (short)value); return;
                case TypeCode.UInt16: Utf8Json.WriteInt64(w, (ushort)value); return;
                case TypeCode.Byte: Utf8Json.WriteInt64(w, (byte)value); return;
                case TypeCode.SByte: Utf8Json.WriteInt64(w, (sbyte)value); return;
                case TypeCode.UInt32: Utf8Json.WriteInt64(w, (uint)value); return;
                case TypeCode.UInt64: Utf8Json.WriteUInt64(w, (ulong)value); return;
                case TypeCode.Char: Utf8Json.WriteChar(w, (char)value); return;
                case TypeCode.DateTime: Utf8Json.WriteDateTime(w, (DateTime)value); return;
            }
            if (type.IsEnum)
            {
                Utf8Json.WriteInt64(w, Convert.ToInt64(value, CultureInfo.InvariantCulture));
                return;
            }
            if (value is DateTimeOffset dto) { Utf8Json.WriteDateTimeOffset(w, dto); return; }
            if (value is TimeSpan ts) { Utf8Json.WriteQuotedAscii(w, ts.ToString("c", CultureInfo.InvariantCulture)); return; }
            if (value is Guid g) { Utf8Json.WriteQuotedAscii(w, g.ToString("D")); return; }
            if (value is Uri u) { Utf8Json.WriteString(w, u.OriginalString); return; }
            if (value is byte[] bytes) { Utf8Json.WriteString(w, Convert.ToBase64String(bytes)); return; }
            if (value is Exception ex && !(value is JsonRpcException)) { WriteObject(w, ExceptionInfo.From(ex), typeof(ExceptionInfo), depth + 1); return; }
            if (value is IDictionary dict)
            {
                Utf8Json.WriteByte(w, (byte)'{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) Utf8Json.WriteByte(w, (byte)',');
                    first = false;
                    Utf8Json.WritePropertyName(w, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    WriteObject(w, e.Value, typeof(object), depth + 1);
                }
                Utf8Json.WriteByte(w, (byte)'}');
                return;
            }
            var plan = TypePlan.For(type);
            if (plan.Kind == PlanKind.Dictionary)
            {
                Utf8Json.WriteByte(w, (byte)'{');
                bool first = true;
                foreach (var kv in plan.Enumerate(value))
                {
                    if (!first) Utf8Json.WriteByte(w, (byte)',');
                    first = false;
                    Utf8Json.WritePropertyName(w, Convert.ToString(kv.Key, CultureInfo.InvariantCulture));
                    WriteObject(w, kv.Value, plan.ElementType, depth + 1);
                }
                Utf8Json.WriteByte(w, (byte)'}');
                return;
            }
            if (value is IEnumerable seq)
            {
                Utf8Json.WriteByte(w, (byte)'[');
                bool first = true;
                foreach (var item in seq)
                {
                    if (!first) Utf8Json.WriteByte(w, (byte)',');
                    first = false;
                    WriteObject(w, item, typeof(object), depth + 1);
                }
                Utf8Json.WriteByte(w, (byte)']');
                return;
            }
            if (plan.Kind != PlanKind.Poco) throw new NotSupportedException("Type " + type.FullName + " is not supported by the built-in serializer.");
            Utf8Json.WriteByte(w, (byte)'{');
            // Only readable members (write-only properties are skipped at plan time), so the comma follows the
            // members actually emitted rather than their declaration index.
            var members = plan.ReadableMembers;
            for (int i = 0; i < members.Length; i++)
            {
                var m = members[i];
                if (i > 0) Utf8Json.WriteByte(w, (byte)',');
                Utf8Json.WriteRaw(w, m.NameJson);
                WriteObject(w, m.Get(value), m.Type, depth + 1);
            }
            Utf8Json.WriteByte(w, (byte)'}');
        }

        // ------------------------------------------------------------------ type plans

        internal enum PlanKind { Poco, List, Dictionary, Unsupported }

        internal sealed class MemberPlan
        {
            public string Name;
            public byte[] NameUtf8;
            public byte[] NameJson;   // "name":
            public Type Type;
            public Func<object, object> Get;
            public Action<object, object> Set;
        }

        internal sealed class TypePlan
        {
            private static readonly ConcurrentDictionary<Type, TypePlan> Cache = new ConcurrentDictionary<Type, TypePlan>();

            public PlanKind Kind;
            public Type ElementType;
            public Type KeyType;
            public MemberPlan[] Members = Array.Empty<MemberPlan>();
            /// <summary>The subset of <see cref="Members"/> with a getter, in the same order: what the writer emits.</summary>
            public MemberPlan[] ReadableMembers = Array.Empty<MemberPlan>();
            public Func<object> Create;
            public Action<object, object> Add;
            public Action<object, object, object> DictionaryAdd;
            public Func<object, IEnumerable<KeyValuePair<object, object>>> Enumerate;

            public static TypePlan For(Type type) => Cache.GetOrAdd(type, Build);

            public MemberPlan FindMember(ReadOnlySpan<byte> name, bool escaped)
            {
                if (escaped)
                {
                    string decoded = Utf8Json.DecodeString(name);
                    foreach (var m in Members) if (string.Equals(m.Name, decoded, StringComparison.OrdinalIgnoreCase)) return m;
                    return null;
                }
                // exact first, then case-insensitive (Json.NET semantics)
                foreach (var m in Members) if (name.SequenceEqual(m.NameUtf8)) return m;
                foreach (var m in Members) if (Utf8Json.EqualsIgnoreAsciiCase(name, m.NameUtf8)) return m;
                return null;
            }

            private static TypePlan Build(Type type)
            {
                var plan = new TypePlan();

                // dictionaries: Dictionary<K,V>, IDictionary<K,V>, IReadOnlyDictionary<K,V>
                var dictIface = FindGenericInterface(type, typeof(IDictionary<,>));
                if (dictIface != null)
                {
                    var args = dictIface.GetGenericArguments();
                    plan.Kind = PlanKind.Dictionary;
                    plan.KeyType = args[0];
                    plan.ElementType = args[1];
                    var concrete = type.IsInterface ? typeof(Dictionary<,>).MakeGenericType(args) : type;
                    plan.Create = MakeCreator(concrete);
                    plan.DictionaryAdd = CompileAdd(dictIface.GetMethod("Add"), args[0], args[1]);
                    plan.Enumerate = d => EnumerateDictionary((IEnumerable)d, args[0], args[1]);
                    return plan;
                }
                var roDictIface = FindGenericInterface(type, typeof(IReadOnlyDictionary<,>));
                if (roDictIface != null)
                {
                    var args = roDictIface.GetGenericArguments();
                    plan.Kind = PlanKind.Dictionary;
                    plan.KeyType = args[0];
                    plan.ElementType = args[1];
                    var concrete = typeof(Dictionary<,>).MakeGenericType(args);
                    plan.Create = MakeCreator(concrete);
                    plan.DictionaryAdd = CompileAdd(concrete.GetMethod("Add"), args[0], args[1]);
                    plan.Enumerate = d => EnumerateDictionary((IEnumerable)d, args[0], args[1]);
                    return plan;
                }

                // lists: T[] handled by caller; List<T>, IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, ISet<T>, HashSet<T>, ...
                var enumIface = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? type : FindGenericInterface(type, typeof(IEnumerable<>));
                if (enumIface != null && type != typeof(string))
                {
                    var elem = enumIface.GetGenericArguments()[0];
                    plan.Kind = PlanKind.List;
                    plan.ElementType = elem;
                    Type concrete = type;
                    if (type.IsInterface)
                    {
                        var def = type.GetGenericTypeDefinition();
                        if (def == typeof(ISet<>)) concrete = typeof(HashSet<>).MakeGenericType(elem);
                        else concrete = typeof(List<>).MakeGenericType(elem);
                    }
                    var addMethod = concrete.GetMethod("Add", new[] { elem }) ?? FindGenericInterface(concrete, typeof(ICollection<>))?.GetMethod("Add");
                    if (addMethod == null || concrete.IsAbstract || concrete.GetConstructor(Type.EmptyTypes) == null)
                    {
                        plan.Kind = PlanKind.Unsupported;
                        return plan;
                    }
                    plan.Create = MakeCreator(concrete);
                    var target = Expression.Parameter(typeof(object), "list");
                    var item = Expression.Parameter(typeof(object), "item");
                    plan.Add = Expression.Lambda<Action<object, object>>(
                        Expression.Call(Expression.Convert(target, addMethod.DeclaringType), addMethod, Expression.Convert(item, elem)), target, item).Compile();
                    return plan;
                }

                if (type.IsInterface || type.IsAbstract || type.IsPrimitive || type == typeof(string))
                {
                    plan.Kind = PlanKind.Unsupported;
                    return plan;
                }

                plan.Kind = PlanKind.Poco;
                // Structs always have a default value (MakeCreator boxes `default(T)`; the boxed setters update the
                // box in place), so only classes need an explicit parameterless constructor.
                plan.Create = type.IsValueType || type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null) != null
                    ? MakeCreator(type)
                    : () => throw new NotSupportedException("Type " + type.FullName + " has no parameterless constructor.");
                plan.Members = BuildMembers(type);
                plan.ReadableMembers = plan.Members.Where(m => m.Get != null).ToArray();
                return plan;
            }

            private static IEnumerable<KeyValuePair<object, object>> EnumerateDictionary(IEnumerable dict, Type keyType, Type valueType)
            {
                var kvType = typeof(KeyValuePair<,>).MakeGenericType(keyType, valueType);
                var keyProp = kvType.GetProperty("Key");
                var valueProp = kvType.GetProperty("Value");
                foreach (var kv in dict)
                {
                    yield return new KeyValuePair<object, object>(keyProp.GetValue(kv), valueProp.GetValue(kv));
                }
            }

            private static Type FindGenericInterface(Type type, Type definition)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == definition) return type;
                foreach (var i in type.GetInterfaces())
                {
                    if (i.IsGenericType && i.GetGenericTypeDefinition() == definition) return i;
                }
                return null;
            }

            private static Func<object> MakeCreator(Type type)
            {
                var ctor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (ctor == null && type.IsValueType) return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(type), typeof(object))).Compile();
                return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ctor), typeof(object))).Compile();
            }

            private static MemberPlan[] BuildMembers(Type type)
            {
                var result = new List<MemberPlan>();
                var chain = new List<Type>();
                for (var t = type; t != null && t != typeof(object); t = t.BaseType) chain.Add(t);
                chain.Reverse();   // base first, like Json.NET

                var seen = new HashSet<string>();
                foreach (var t in chain)
                {
                    var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .OrderBy(f => f.MetadataToken);
                    foreach (var f in fields)
                    {
                        if (f.IsDefined(typeof(NonSerializedAttribute), false) || !seen.Add(f.Name)) continue;
                        result.Add(MakeMember(f.Name, f.FieldType,
                            get: MakeGetter(type, f),
                            set: f.IsInitOnly ? null : MakeSetter(type, f)));
                    }
                    var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .Where(p => p.GetIndexParameters().Length == 0)
                        .OrderBy(p => p.MetadataToken);
                    foreach (var p in props)
                    {
                        if (!seen.Add(p.Name)) continue;
                        var getter = p.GetGetMethod(false);
                        var setter = p.GetSetMethod(true);
                        result.Add(MakeMember(p.Name, p.PropertyType,
                            get: getter == null ? null : MakeGetter(type, p),
                            set: setter == null ? null : MakeSetter(type, p)));
                    }
                }
                return result.ToArray();
            }

            private static MemberPlan MakeMember(string name, Type memberType, Func<object, object> get, Action<object, object> set)
            {
                var json = new PooledByteBufferWriter(name.Length * 6 + 4);
                Utf8Json.WritePropertyName(json, name);
                var m = new MemberPlan
                {
                    Name = name,
                    NameUtf8 = System.Text.Encoding.UTF8.GetBytes(name),
                    NameJson = json.ToArray(),
                    Type = memberType,
                    Get = get,
                    Set = set
                };
                json.Dispose();
                return m;
            }

            /// <summary>A compiled <c>((TDict)d).Add((TKey)k, (TValue)v)</c>: no MethodInfo.Invoke and no argument array per entry.</summary>
            private static Action<object, object, object> CompileAdd(MethodInfo add, Type keyType, Type valueType)
            {
                var d = Expression.Parameter(typeof(object), "d");
                var k = Expression.Parameter(typeof(object), "k");
                var v = Expression.Parameter(typeof(object), "v");
                var call = Expression.Call(Expression.Convert(d, add.DeclaringType), add, Expression.Convert(k, keyType), Expression.Convert(v, valueType));
                return Expression.Lambda<Action<object, object, object>>(call, d, k, v).Compile();
            }

            private static Func<object, object> MakeGetter(Type owner, MemberInfo member)
            {
                var obj = Expression.Parameter(typeof(object), "obj");
                var access = Expression.MakeMemberAccess(Expression.Convert(obj, owner), member);
                return Expression.Lambda<Func<object, object>>(Expression.Convert(access, typeof(object)), obj).Compile();
            }

            private static Action<object, object> MakeSetter(Type owner, MemberInfo member)
            {
                var obj = Expression.Parameter(typeof(object), "obj");
                var val = Expression.Parameter(typeof(object), "val");
                var memberType = member is FieldInfo f ? f.FieldType : ((PropertyInfo)member).PropertyType;
                if (owner.IsValueType)
                {
                    // boxed struct: set through reflection so the box itself is updated
                    return member is FieldInfo fi
                        ? new Action<object, object>((o, v) => fi.SetValue(o, v))
                        : (o, v) => ((PropertyInfo)member).SetValue(o, v);
                }
                var access = Expression.MakeMemberAccess(Expression.Convert(obj, owner), member);
                return Expression.Lambda<Action<object, object>>(Expression.Assign(access, Expression.Convert(val, memberType)), obj, val).Compile();
            }
        }
    }
}
