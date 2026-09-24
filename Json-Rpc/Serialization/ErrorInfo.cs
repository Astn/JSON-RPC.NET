using System;
using System.Buffers;
using System.Text;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// The <c>data</c> of a -32601 error: <c>{"method":"name"}</c>, the effective method name (decoded, and as
    /// replaced by a pre-process handler). Written the same way by every serializer. Nothing else is disclosed: the
    /// session and the registered methods are not the client's business.
    /// </summary>
    public sealed class MethodNotFoundInfo
    {
        private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("{\"method\":");

        public MethodNotFoundInfo(string method)
        {
            Method = method;
        }

        /// <summary>The method name the request asked for.</summary>
        public string Method { get; }

        public void WriteTo(IBufferWriter<byte> output)
        {
            Utf8Json.WriteRaw(output, Prefix);
            if (Method == null) Utf8Json.WriteNull(output);
            else Utf8Json.WriteString(output, Method);
            Utf8Json.WriteByte(output, (byte)'}');
        }

        public override string ToString() => "Method not found: " + Method;
    }

    /// <summary>
    /// The <c>data</c> of a -32602 error raised because one argument could not be converted to the parameter's type:
    /// <c>{"reason":"conversion","parameter":"name","index":0,"expectedType":"int32"}</c>, plus <c>"message"</c>
    /// (the serializer's description of the failure) when <see cref="Config.IncludeExceptionDetails"/> is on. The
    /// value the client sent is never echoed. Written the same way by every serializer.
    /// </summary>
    public sealed class ParameterErrorInfo
    {
        private static readonly byte[] Prefix = Encoding.ASCII.GetBytes("{\"reason\":\"conversion\",\"parameter\":");
        private static readonly byte[] IndexKey = Encoding.ASCII.GetBytes(",\"index\":");
        private static readonly byte[] ExpectedTypeKey = Encoding.ASCII.GetBytes(",\"expectedType\":");
        private static readonly byte[] MessageKey = Encoding.ASCII.GetBytes(",\"message\":");

        public ParameterErrorInfo(string parameter, int index, Type expectedType, Exception cause)
        {
            Parameter = parameter;
            Index = index;
            ExpectedType = Describe(expectedType);
            Cause = cause;
        }

        public ParameterErrorInfo(Invocation.RpcParameter parameter, int index, Exception cause)
            : this(parameter.Name, index, parameter.Type, cause)
        {
        }

        /// <summary>Always <c>"conversion"</c> in this release.</summary>
        public string Reason => "conversion";
        /// <summary>The JSON name of the parameter.</summary>
        public string Parameter { get; }
        /// <summary>The parameter's position in the method signature (0-based).</summary>
        public int Index { get; }
        /// <summary>The parameter's CLR type in a short spelling: <c>int32</c>, <c>string</c>, <c>guid</c>, <c>int32?</c>, <c>string[]</c>, <c>List&lt;Order&gt;</c>, <c>Order</c>.</summary>
        public string ExpectedType { get; }
        /// <summary>The serializer's exception; its message goes to the client only with <see cref="Config.IncludeExceptionDetails"/>.</summary>
        public Exception Cause { get; }
        public string Message => Cause?.Message;

        public void WriteTo(IBufferWriter<byte> output)
        {
            Utf8Json.WriteRaw(output, Prefix);
            Utf8Json.WriteString(output, Parameter);
            Utf8Json.WriteRaw(output, IndexKey);
            Utf8Json.WriteInt64(output, Index);
            Utf8Json.WriteRaw(output, ExpectedTypeKey);
            Utf8Json.WriteString(output, ExpectedType);
            if (Config.IncludeExceptionDetails && Message != null)
            {
                Utf8Json.WriteRaw(output, MessageKey);
                Utf8Json.WriteString(output, Message);
            }
            Utf8Json.WriteByte(output, (byte)'}');
        }

        public override string ToString()
        {
            return "Parameter '" + Parameter + "' (" + ExpectedType + ") could not be converted" + (Message != null ? ": " + Message : ".");
        }

        /// <summary>
        /// Whether <paramref name="cause"/> says a value could not be converted (the client's fault) rather than
        /// that the serializer cannot handle the type or failed internally: the serializers' own
        /// <see cref="JsonRpcBindException"/>, the BCL parse failures, and any <c>JsonException</c> family
        /// (System.Text.Json's and Json.NET's, matched by base type name so a serializer built on either qualifies).
        /// </summary>
        public static bool IsConversionFailure(Exception cause)
        {
            if (cause == null) return false;
            if (cause is JsonRpcBindException || cause is FormatException || cause is OverflowException || cause is InvalidCastException) return true;
            for (var t = cause.GetType(); t != null && t != typeof(Exception); t = t.BaseType)
            {
                if (t.Name == "JsonException") return true;
            }
            return false;
        }

        /// <summary>A short, language-neutral spelling of a CLR type for diagnostics.</summary>
        public static string Describe(Type type)
        {
            if (type == null) return null;
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return Describe(underlying) + "?";
            if (type.IsArray) return Describe(type.GetElementType()) + "[]";
            if (type.IsByRef) return Describe(type.GetElementType());
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.String: return "string";
                case TypeCode.Boolean: return "boolean";
                case TypeCode.Char: return "char";
                case TypeCode.SByte: return "int8";
                case TypeCode.Byte: return "uint8";
                case TypeCode.Int16: return "int16";
                case TypeCode.UInt16: return "uint16";
                case TypeCode.Int32: return "int32";
                case TypeCode.UInt32: return "uint32";
                case TypeCode.Int64: return "int64";
                case TypeCode.UInt64: return "uint64";
                case TypeCode.Single: return "single";
                case TypeCode.Double: return "double";
                case TypeCode.Decimal: return "decimal";
                case TypeCode.DateTime: return "datetime";
            }
            if (type == typeof(object)) return "object";
            if (type == typeof(Guid)) return "guid";
            if (type == typeof(DateTimeOffset)) return "datetimeoffset";
            if (type == typeof(TimeSpan)) return "timespan";
            if (type == typeof(Uri)) return "uri";
            if (type.IsGenericType)
            {
                var name = type.Name;
                int tick = name.IndexOf('`');
                if (tick > 0) name = name.Substring(0, tick);
                var args = type.GetGenericArguments();
                var sb = new StringBuilder(name).Append('<');
                for (int i = 0; i < args.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(Describe(args[i]));
                }
                return sb.Append('>').ToString();
            }
            return type.Name;
        }
    }
}
