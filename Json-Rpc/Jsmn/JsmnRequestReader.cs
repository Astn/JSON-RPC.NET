using System;
using System.Buffers;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.Jsmn
{
    /// <summary>
    /// The default envelope reader: tokenizes the document with <see cref="JsmnTokenizer"/> and exposes the
    /// method / params / id of each request as slices of the original bytes. Values are converted by the
    /// owning serializer (directly from the tokens when it is the built-in <see cref="JsmnSerializer"/>).
    /// </summary>
    public sealed class JsmnRequestReader : JsonRpcRequestReader
    {
        private static readonly byte[] KeyMethod = { (byte)'m', (byte)'e', (byte)'t', (byte)'h', (byte)'o', (byte)'d' };
        private static readonly byte[] KeyParams = { (byte)'p', (byte)'a', (byte)'r', (byte)'a', (byte)'m', (byte)'s' };
        private static readonly byte[] KeyId = { (byte)'i', (byte)'d' };
        private static readonly byte[] Version2 = { (byte)'2', (byte)'.', (byte)'0' };
        private static readonly byte[] KeyJsonRpc = { (byte)'j', (byte)'s', (byte)'o', (byte)'n', (byte)'r', (byte)'p', (byte)'c' };

        private readonly JsonRpcSerializer _serializer;
        private readonly JsmnSerializer _jsmn;
        private readonly JsmnTokenizer _tok = new JsmnTokenizer();
        private ReadOnlyMemory<byte> _doc;

        private bool _isBatch;
        private int _count;
        private int[] _requests = new int[8];

        private int _methodTok = -1, _paramsTok = -1, _idTok = -1, _versionTok = -1;
        private JsonRpcParamsKind _paramsKind;
        private int _paramCount;
        private int[] _paramVals = new int[8];
        private int[] _paramKeys = new int[8];
        // Transient decode buffer for escaped names (member names, method, parameter names). Each caller
        // consumes the returned span before the next reader call, so one buffer serves them all.
        private byte[] _scratch;
        // Storage for a normalized (lenient, single-quoted) id. The handler keeps the IdRaw span alive across
        // MethodUtf8 / ParamNameUtf8 calls, so it must never share a buffer with the name decoding above.
        private byte[] _idScratch;

        public JsmnRequestReader(JsonRpcSerializer serializer)
        {
            _serializer = serializer;
            _jsmn = serializer as JsmnSerializer;
            _tok.MaxDepth = serializer != null ? serializer.MaxDepth : JsmnTokenizer.DefaultMaxDepth;
        }

        public JsmnTokenizer Tokenizer => _tok;
        public override ReadOnlyMemory<byte> Document => _doc;

        /// <summary>True when the owning serializer is the built-in <see cref="JsmnSerializer"/>, so parameters bind straight from the tokens.</summary>
        internal bool IsBuiltIn => _jsmn != null;

        // Typed parameter reads for the compiled built-in invoker (see RpcMethod): direct static calls, no cursor
        // construction through a virtual generic method and no delegate in between.
        internal JsmnCursor CursorAt(int i) => new JsmnCursor(_doc.Span, _tok.Tokens, _paramVals[i]);
        internal static string ReadStringParam(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadString(ref c); }
        internal static int ReadInt32Param(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadInt32(ref c); }
        internal static long ReadInt64Param(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadInt64(ref c); }
        internal static double ReadDoubleParam(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadDouble(ref c); }
        internal static float ReadSingleParam(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadSingle(ref c); }
        internal static bool ReadBooleanParam(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadBoolean(ref c); }
        internal static decimal ReadDecimalParam(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnMapper.ReadDecimal(ref c); }
        internal static T ReadTypedParam<T>(JsmnRequestReader r, int i) { var c = r.CursorAt(i); return JsmnReader<T>.Read(ref c); }
        /// <summary>Nullable primitives: the JSON null literal binds to null, anything else through the value reader.</summary>
        internal static bool ParamIsNullLiteral(JsmnRequestReader r, int i) => JsmnTokenizer.IsNull(r._doc.Span, r._tok.Tokens[r._paramVals[i]]);

        public override bool TryParse(ReadOnlyMemory<byte> utf8Document, out string error)
        {
            _doc = utf8Document;
            _tok.Lenient = _serializer.Lenient;
            _methodTok = _paramsTok = _idTok = _versionTok = -1;
            _paramCount = 0;
            _paramsKind = JsonRpcParamsKind.Absent;

            int n = _tok.Parse(utf8Document.Span);
            if (n < 0)
            {
                switch (n)
                {
                    case JsmnTokenizer.ErrorPartial:
                        error = "Unexpected end of JSON input.";
                        break;
                    case JsmnTokenizer.ErrorDepth:
                        error = "Invalid JSON was received by the server. The maximum nesting depth of " + _tok.MaxDepth + " was exceeded.";
                        break;
                    default:
                        error = "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text.";
                        break;
                }
                return false;
            }
            if (n == 0)
            {
                error = "Empty request.";
                return false;
            }
            var tokens = _tok.Tokens;
            ref var root = ref tokens[0];
            if (root.Type == JsmnType.Array)
            {
                _isBatch = true;
                _count = root.Size;
                if (_requests.Length < _count) _requests = new int[Math.Max(_count, _requests.Length * 2)];
                int idx = 1;
                for (int i = 0; i < _count; i++)
                {
                    _requests[i] = idx;
                    idx = _tok.Skip(idx);
                }
            }
            else if (root.Type == JsmnType.Object)
            {
                _isBatch = false;
                _count = 1;
                _requests[0] = 0;
            }
            else
            {
                error = "A JSON-RPC request must be an object or an array of objects.";
                return false;
            }
            error = null;
            return true;
        }

        public override bool IsBatch => _isBatch;
        public override int Count => _count;

        public override bool Select(int index)
        {
            _methodTok = _paramsTok = _idTok = _versionTok = -1;
            _paramCount = 0;
            _paramsKind = JsonRpcParamsKind.Absent;

            var tokens = _tok.Tokens;
            var doc = _doc.Span;
            int obj = _requests[index];
            if (tokens[obj].Type != JsmnType.Object) return false;

            int k = obj + 1;
            for (int m = 0; m < tokens[obj].Size; m++)
            {
                ref var key = ref tokens[k];
                if (key.Size == 0) { k = _tok.Skip(k); continue; }   // key without a value; ignore
                int val = k + 1;
                var name = JsmnTokenizer.Slice(doc, key);
                if (key.Escaped)
                {
                    // A name written with escapes (m\u0065thod) is the same member as "method": decode before matching. The tokenizer has
                    // already validated the escapes, so Unescape cannot throw here.
                    if (_scratch == null || _scratch.Length < name.Length) _scratch = new byte[Math.Max(64, name.Length)];
                    name = new ReadOnlySpan<byte>(_scratch, 0, Utf8Json.Unescape(name, _scratch));
                }
                // the vocabulary is four names of three distinct lengths: select by length, then compare
                switch (name.Length)
                {
                    case 2:
                        if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyId)) _idTok = val;
                        break;
                    case 6:
                        if ((name[0] | 0x20) == (byte)'m') { if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyMethod)) _methodTok = val; }
                        else if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyParams)) _paramsTok = val;
                        break;
                    case 7:
                        if (Utf8Json.EqualsIgnoreAsciiCase(name, KeyJsonRpc)) _versionTok = val;
                        break;
                }
                k = _tok.Skip(k);
            }

            if (_paramsTok >= 0)
            {
                ref var p = ref tokens[_paramsTok];
                if (p.Type == JsmnType.Array)
                {
                    _paramsKind = JsonRpcParamsKind.Array;
                    _paramCount = p.Size;
                    EnsureParamCapacity(_paramCount);
                    int idx = _paramsTok + 1;
                    for (int i = 0; i < _paramCount; i++)
                    {
                        _paramVals[i] = idx;
                        _paramKeys[i] = -1;
                        idx = _tok.Skip(idx);
                    }
                }
                else if (p.Type == JsmnType.Object)
                {
                    _paramsKind = JsonRpcParamsKind.Object;
                    EnsureParamCapacity(p.Size);
                    int idx = _paramsTok + 1;
                    int n = 0;
                    for (int i = 0; i < p.Size; i++)
                    {
                        if (tokens[idx].Size > 0)
                        {
                            _paramKeys[n] = idx;
                            _paramVals[n] = idx + 1;
                            n++;
                        }
                        idx = _tok.Skip(idx);
                    }
                    _paramCount = n;
                }
                else if (JsmnTokenizer.IsNull(doc, p))
                {
                    _paramsKind = JsonRpcParamsKind.Absent;
                }
                else
                {
                    _paramsKind = JsonRpcParamsKind.Invalid;
                }
            }
            return true;
        }

        private void EnsureParamCapacity(int n)
        {
            if (_paramVals.Length < n)
            {
                _paramVals = new int[Math.Max(n, _paramVals.Length * 2)];
                _paramKeys = new int[_paramVals.Length];
            }
        }

        public override bool HasMethod => _methodTok >= 0 && _tok.Tokens[_methodTok].Type == JsmnType.String;

        public override JsonRpcVersionKind VersionKind
        {
            get
            {
                if (_versionTok < 0) return JsonRpcVersionKind.Absent;
                ref var t = ref _tok.Tokens[_versionTok];
                if (t.Type != JsmnType.String) return JsonRpcVersionKind.Other;
                var raw = JsmnTokenizer.Slice(_doc.Span, t);
                if (t.Escaped)
                {
                    if (_scratch == null || _scratch.Length < raw.Length) _scratch = new byte[Math.Max(64, raw.Length)];
                    raw = new ReadOnlySpan<byte>(_scratch, 0, Utf8Json.Unescape(raw, _scratch));
                }
                return raw.SequenceEqual(Version2) ? JsonRpcVersionKind.V2 : JsonRpcVersionKind.Other;
            }
        }

        public override ReadOnlySpan<byte> MethodUtf8
        {
            get
            {
                if (_methodTok < 0) return default;
                ref var t = ref _tok.Tokens[_methodTok];
                var raw = JsmnTokenizer.Slice(_doc.Span, t);
                if (!t.Escaped) return raw;
                if (_scratch == null || _scratch.Length < raw.Length) _scratch = new byte[Math.Max(64, raw.Length)];
                int n = Utf8Json.Unescape(raw, _scratch);
                return new ReadOnlySpan<byte>(_scratch, 0, n);
            }
        }

        public override string Method
        {
            get
            {
                if (_methodTok < 0) return null;
                ref var t = ref _tok.Tokens[_methodTok];
                if (t.Type != JsmnType.String) return null;
                return Utf8Json.DecodeString(JsmnTokenizer.Slice(_doc.Span, t));
            }
        }

        public override JsonRpcIdKind IdKind
        {
            get
            {
                if (_idTok < 0) return JsonRpcIdKind.Absent;
                ref var t = ref _tok.Tokens[_idTok];
                if (t.Type == JsmnType.String) return JsonRpcIdKind.String;
                if (t.Type != JsmnType.Primitive) return JsonRpcIdKind.Invalid;
                return Utf8Json.ClassifyId(JsmnTokenizer.Slice(_doc.Span, t));
            }
        }

        public override ReadOnlySpan<byte> IdRaw
        {
            get
            {
                if (_idTok < 0) return default;
                ref var t = ref _tok.Tokens[_idTok];
                var doc = _doc.Span;
                if (t.Type == JsmnType.String && doc[t.Start - 1] != (byte)'"')
                {
                    // single-quoted (lenient) string: re-encode as a proper JSON string into the id's own buffer
                    using (var w = new PooledByteBufferWriter(t.End - t.Start + 8))
                    {
                        Utf8Json.WriteString(w, Utf8Json.DecodeString(JsmnTokenizer.Slice(doc, t)));
                        var bytes = w.WrittenSpan;
                        if (_idScratch == null || _idScratch.Length < bytes.Length) _idScratch = new byte[Math.Max(64, bytes.Length)];
                        bytes.CopyTo(_idScratch);
                        return new ReadOnlySpan<byte>(_idScratch, 0, bytes.Length);
                    }
                }
                return JsmnTokenizer.RawJson(doc, t);
            }
        }

        public override object IdValue => _idTok < 0 ? null : Utf8Json.IdToObject(IdRaw, IdKind);

        public override JsonRpcParamsKind ParamsKind => _paramsKind;
        public override int ParamCount => _paramCount;

        public override ReadOnlySpan<byte> ParamNameUtf8(int i)
        {
            int k = _paramKeys[i];
            if (k < 0) return default;
            ref var t = ref _tok.Tokens[k];
            var raw = JsmnTokenizer.Slice(_doc.Span, t);
            if (!t.Escaped) return raw;
            if (_scratch == null || _scratch.Length < raw.Length) _scratch = new byte[Math.Max(64, raw.Length)];
            int n = Utf8Json.Unescape(raw, _scratch);
            return new ReadOnlySpan<byte>(_scratch, 0, n);
        }

        public override ReadOnlySpan<byte> ParamRaw(int i) => JsmnTokenizer.RawJson(_doc.Span, _tok.Tokens[_paramVals[i]]);

        public override bool ParamIsNull(int i) => JsmnTokenizer.IsNull(_doc.Span, _tok.Tokens[_paramVals[i]]);

        public override T ReadParam<T>(int i)
        {
            if (_jsmn != null)
            {
                var c = new JsmnCursor(_doc.Span, _tok.Tokens, _paramVals[i]);
                return JsmnReader<T>.Read(ref c);
            }
            return _serializer.Read<T>(ParamRaw(i));
        }

        public override object ReadParam(int i, Type type)
        {
            if (_jsmn != null)
            {
                var c = new JsmnCursor(_doc.Span, _tok.Tokens, _paramVals[i]);
                return JsmnMapper.ReadObject(ref c, type);
            }
            return _serializer.Read(ParamRaw(i), type);
        }

        public override object ParamsValue
        {
            get
            {
                if (_paramsTok < 0) return null;
                if (_jsmn != null)
                {
                    var c = new JsmnCursor(_doc.Span, _tok.Tokens, _paramsTok);
                    return JsmnMapper.ReadObject(ref c, typeof(object));
                }
                return _serializer.Read(JsmnTokenizer.RawJson(_doc.Span, _tok.Tokens[_paramsTok]), typeof(object));
            }
        }

        public override void Release()
        {
            _tok.Release();
            _doc = default;
        }
    }
}
