using System;
using System.Buffers;

namespace AustinHarris.JsonRpc.Jsmn
{
    /// <summary>JSON token kinds, mirroring jsmn.</summary>
    public enum JsmnType : byte
    {
        Undefined = 0,
        Object = 1,
        Array = 2,
        String = 3,
        /// <summary>number, true, false, null (and bare-word keys in lenient mode)</summary>
        Primitive = 4
    }

    /// <summary>
    /// A token: type plus [Start, End) byte range in the source. For strings the range excludes the quotes.
    /// <see cref="Size"/> is the number of direct children (members for objects, elements for arrays,
    /// 1 for a key that has a value). <see cref="Parent"/> is the index of the enclosing token or -1.
    /// </summary>
    public struct JsmnToken
    {
        // The three byte-sized fields lead so the struct packs into 20 bytes instead of 24 (denser token arrays).
        public JsmnType Type;
        /// <summary>True when the string token contains a backslash escape and must be decoded.</summary>
        public bool Escaped;
        /// <summary>True when the token is an object member name (a string directly under an object).</summary>
        public bool IsKey;
        public int Start;
        public int End;
        public int Size;
        public int Parent;
    }

    /// <summary>
    /// A safe port of the jsmn tokenizer (https://github.com/zserge/jsmn) over <see cref="ReadOnlySpan{T}"/>.
    /// It records token boundaries without copying or decoding anything; a document of N tokens needs one
    /// pooled array of N tokens and no other allocation.
    /// <para>
    /// Unlike the original, the tokenizer validates the JSON grammar: exactly one root value, no trailing
    /// commas or trailing content, literals that are exactly <c>true</c>/<c>false</c>/<c>null</c>, RFC 8259
    /// number syntax, no unescaped control characters and only well-formed UTF-8 and escape sequences inside
    /// strings. Lenient mode additionally accepts single-quoted strings, unquoted (bare-word) member names and
    /// trailing commas; everything else is validated the same way. Nesting is limited to <see cref="MaxDepth"/>
    /// open containers, and closing a container is O(1) (the open containers are kept on an explicit stack).
    /// </para>
    /// <para>
    /// Upstream: ported from jsmn master as of commit 25647e6 (2021-10-14, the latest at the time of writing;
    /// v1.1.0 is from 2019). Open upstream pull requests were reviewed on 2026-09-23: #241 (memoise the parent per
    /// depth level) is what the explicit stack here already does; #242/#98/#197 (a next-sibling link per token for
    /// O(1) subtree skips) is not adopted because <see cref="Skip"/> only walks the few tokens of an envelope
    /// member; #248 (packed token fields) is worth revisiting for cache density; #194/#197 (RFC 8259 strict mode),
    /// #168 (unterminated strings) and #102 (primitive root) are covered by the validation above.
    /// </para>
    /// </summary>
    public sealed class JsmnTokenizer
    {
        public const int ErrorNoMemory = -1;   // never surfaced: the token array grows
        public const int ErrorInvalid = -2;    // invalid character or grammar inside the document
        public const int ErrorPartial = -3;    // the document ended early
        public const int ErrorDepth = -4;      // more than MaxDepth nested containers

        /// <summary>The default nesting limit (the same as System.Text.Json and Json.NET).</summary>
        public const int DefaultMaxDepth = 64;

        // grammar state, as flags: what the next significant character may be
        private const byte ExpectValue = 1;    // a value may start here
        private const byte ExpectKey = 2;      // a member name may start here
        private const byte AllowClose = 4;     // the enclosing container may close here
        private const byte ExpectComma = 8;    // a ',' may follow
        private const byte ExpectColon = 16;   // a ':' must follow (after a member name)
        private const byte ExpectEnd = 32;     // the root value is complete; only whitespace may follow

        private JsmnToken[] _tokens = ArrayPool<JsmnToken>.Shared.Rent(64);
        private int[] _stack = new int[16];     // indices of the open containers, innermost last
        private int _tokNext;
        private int _pos;
        private int _maxDepth = DefaultMaxDepth;
        public bool Lenient;

        public JsmnToken[] Tokens => _tokens;
        public int TokenCount => _tokNext;
        /// <summary>Where the last <see cref="Parse"/> stopped: the document length on success, the offending byte on an error.</summary>
        public int Position => _pos;

        /// <summary>Maximum number of nested containers (objects/arrays, the root counts as one). At least 1.</summary>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value < 1 ? 1 : value;
        }

        public void Release()
        {
            if (_tokens != null && _tokens.Length > 4096)
            {
                ArrayPool<JsmnToken>.Shared.Return(_tokens);
                _tokens = ArrayPool<JsmnToken>.Shared.Rent(64);
            }
            if (_stack.Length > 4096) _stack = new int[16];
            _tokNext = 0;
        }

        /// <summary>
        /// Tokenizes the whole document. Returns the token count (0 for a document that is empty or whitespace)
        /// or a negative error code (<see cref="ErrorInvalid"/>, <see cref="ErrorPartial"/>, <see cref="ErrorDepth"/>).
        /// </summary>
        public int Parse(ReadOnlySpan<byte> js)
        {
            // The scanner state lives in locals for the whole loop (position, token count, current parent, depth
            // and the arrays) and is published to the fields once on exit, so the JIT can keep it in registers
            // instead of reloading object fields after every helper call. The helpers are static scanners that
            // return an end position or an error; every token is written here, fully, on allocation.
            bool lenient = Lenient;
            int maxDepth = _maxDepth;
            var tokens = _tokens;
            var stack = _stack;
            int tokNext = 0;
            int tokSuper = -1;
            int depth = 0;
            byte state = ExpectValue;
            int pos = 0;
            int result;

            for (; pos < js.Length; pos++)
            {
                byte c = js[pos];
                switch (c)
                {
                    case (byte)'{':
                    case (byte)'[':
                        {
                            if ((state & ExpectValue) == 0) { result = ErrorInvalid; goto Done; }
                            if (depth >= maxDepth) { result = ErrorDepth; goto Done; }
                            if (tokNext == tokens.Length) tokens = Grow(tokNext);
                            if (tokSuper != -1) tokens[tokSuper].Size++;
                            ref var tok = ref tokens[tokNext];
                            tok.Type = c == (byte)'{' ? JsmnType.Object : JsmnType.Array;
                            tok.Escaped = false;
                            tok.IsKey = false;
                            tok.Start = pos;
                            tok.End = -1;
                            tok.Size = 0;
                            tok.Parent = tokSuper;
                            tokSuper = tokNext++;
                            if (depth == stack.Length)
                            {
                                Array.Resize(ref stack, stack.Length * 2);
                                _stack = stack;
                            }
                            stack[depth++] = tokSuper;
                            state = c == (byte)'{' ? (byte)(ExpectKey | AllowClose) : (byte)(ExpectValue | AllowClose);
                            break;
                        }
                    case (byte)'}':
                    case (byte)']':
                        {
                            if ((state & AllowClose) == 0) { result = ErrorInvalid; goto Done; }   // implies depth > 0
                            ref var open = ref tokens[stack[depth - 1]];
                            if (open.Type != (c == (byte)'}' ? JsmnType.Object : JsmnType.Array)) { result = ErrorInvalid; goto Done; }
                            open.End = pos + 1;
                            depth--;
                            tokSuper = open.Parent;
                            state = depth == 0 ? ExpectEnd : (byte)(ExpectComma | AllowClose);
                            break;
                        }
                    case (byte)'"':
                    case (byte)'\'':
                        {
                            if (c == (byte)'\'' && !lenient) { result = ErrorInvalid; goto Done; }
                            bool isKey;
                            if ((state & ExpectValue) != 0) isKey = false;
                            else if ((state & ExpectKey) != 0) isKey = true;
                            else { result = ErrorInvalid; goto Done; }
                            int end = ScanString(js, pos, c, lenient, out bool escaped);
                            if (end < 0) { result = end; goto Done; }
                            if (tokNext == tokens.Length) tokens = Grow(tokNext);
                            if (tokSuper != -1) tokens[tokSuper].Size++;
                            ref var tok = ref tokens[tokNext++];
                            tok.Type = JsmnType.String;
                            tok.Escaped = escaped;
                            tok.IsKey = isKey;
                            tok.Start = pos + 1;
                            tok.End = end;
                            tok.Size = 0;
                            tok.Parent = tokSuper;
                            state = isKey ? ExpectColon : depth == 0 ? ExpectEnd : (byte)(ExpectComma | AllowClose);
                            pos = end;
                            break;
                        }
                    case (byte)'\t':
                    case (byte)'\r':
                    case (byte)'\n':
                    case (byte)' ':
                        break;
                    case (byte)':':
                        {
                            if ((state & ExpectColon) == 0) { result = ErrorInvalid; goto Done; }
                            tokSuper = tokNext - 1;   // the member name: its value becomes its child
                            state = ExpectValue;
                            break;
                        }
                    case (byte)',':
                        {
                            if ((state & ExpectComma) == 0) { result = ErrorInvalid; goto Done; }   // implies depth > 0
                            int container = stack[depth - 1];
                            tokSuper = container;
                            state = tokens[container].Type == JsmnType.Object ? ExpectKey : ExpectValue;
                            if (lenient) state |= AllowClose;   // trailing comma
                            break;
                        }
                    default:
                        {
                            int end;
                            bool isKey;
                            if ((state & ExpectValue) != 0)
                            {
                                end = ScanPrimitive(js, pos);
                                isKey = false;
                            }
                            else if ((state & ExpectKey) != 0 && lenient)
                            {
                                end = ScanBareKey(js, pos);
                                isKey = true;
                            }
                            else { result = ErrorInvalid; goto Done; }
                            if (end < 0) { result = end; goto Done; }
                            if (tokNext == tokens.Length) tokens = Grow(tokNext);
                            if (tokSuper != -1) tokens[tokSuper].Size++;
                            ref var tok = ref tokens[tokNext++];
                            tok.Type = JsmnType.Primitive;
                            tok.Escaped = false;
                            tok.IsKey = isKey;
                            tok.Start = pos;
                            tok.End = end;
                            tok.Size = 0;
                            tok.Parent = tokSuper;
                            state = isKey ? ExpectColon : depth == 0 ? ExpectEnd : (byte)(ExpectComma | AllowClose);
                            pos = end - 1;
                            break;
                        }
                }
            }

            if (depth != 0) result = ErrorPartial;
            else if (state == ExpectEnd) result = tokNext;
            else result = 0;   // only the initial state can remain at depth 0: nothing but whitespace was seen

        Done:
            _pos = pos;
            _tokNext = tokNext;
            return result;
        }

        /// <summary>Doubles the token array (cold path); the caller reloads its local reference from the return value.</summary>
        private JsmnToken[] Grow(int count)
        {
            var old = _tokens;
            var bigger = ArrayPool<JsmnToken>.Shared.Rent(old.Length * 2);
            Array.Copy(old, bigger, count);
            ArrayPool<JsmnToken>.Shared.Return(old);
            _tokens = bigger;
            return bigger;
        }

        /// <summary>True for the characters that may follow a literal or number.</summary>
        private static bool IsDelimiter(byte c)
        {
            switch (c)
            {
                case (byte)' ': case (byte)'\t': case (byte)'\r': case (byte)'\n':
                case (byte)',': case (byte)']': case (byte)'}': case (byte)':':
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A literal (true/false/null) or a number starting at <paramref name="start"/>, validated against the JSON
        /// grammar and followed by a delimiter or the end. Returns the end (exclusive) or an error.
        /// </summary>
        private static int ScanPrimitive(ReadOnlySpan<byte> js, int start)
        {
            int end;
            switch (js[start])
            {
                case (byte)'t': end = MatchLiteral(js, start, (byte)'r', (byte)'u', (byte)'e', 0); break;
                case (byte)'f': end = MatchLiteral(js, start, (byte)'a', (byte)'l', (byte)'s', (byte)'e'); break;
                case (byte)'n': end = MatchLiteral(js, start, (byte)'u', (byte)'l', (byte)'l', 0); break;
                case (byte)'-':
                case (byte)'0': case (byte)'1': case (byte)'2': case (byte)'3': case (byte)'4':
                case (byte)'5': case (byte)'6': case (byte)'7': case (byte)'8': case (byte)'9':
                    end = ScanNumber(js, start);
                    break;
                default:
                    return ErrorInvalid;
            }
            if (end < 0) return end;
            if (end < js.Length && !IsDelimiter(js[end])) return ErrorInvalid;   // e.g. truX, 01, 1x
            return end;
        }

        /// <summary>Matches the rest of a literal after its first byte (c4 == 0 for a 4-byte literal). Returns the end or an error.</summary>
        private static int MatchLiteral(ReadOnlySpan<byte> js, int start, byte c1, byte c2, byte c3, byte c4)
        {
            int len = c4 == 0 ? 4 : 5;
            int avail = js.Length - start;
            if (avail < len)
            {
                // the document ends inside the literal: partial if what is there matches, invalid otherwise
                if (avail >= 2 && js[start + 1] != c1) return ErrorInvalid;
                if (avail >= 3 && js[start + 2] != c2) return ErrorInvalid;
                if (avail >= 4 && js[start + 3] != c3) return ErrorInvalid;
                return ErrorPartial;
            }
            if (js[start + 1] != c1 || js[start + 2] != c2 || js[start + 3] != c3) return ErrorInvalid;
            if (len == 5 && js[start + 4] != c4) return ErrorInvalid;
            return start + len;
        }

        /// <summary>-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)? — returns the end index or an error.</summary>
        private static int ScanNumber(ReadOnlySpan<byte> js, int start)
        {
            int i = start;
            int len = js.Length;
            if (js[i] == (byte)'-')
            {
                i++;
                if (i >= len) return ErrorPartial;
            }
            uint d = (uint)(js[i] - (byte)'0');
            if (d == 0)
            {
                i++;
            }
            else if (d <= 9)
            {
                i++;
                while (i < len && (uint)(js[i] - (byte)'0') <= 9) i++;
            }
            else
            {
                return ErrorInvalid;
            }
            if (i < len && js[i] == (byte)'.')
            {
                i++;
                if (i >= len) return ErrorPartial;
                if ((uint)(js[i] - (byte)'0') > 9) return ErrorInvalid;
                while (i < len && (uint)(js[i] - (byte)'0') <= 9) i++;
            }
            if (i < len && (js[i] == (byte)'e' || js[i] == (byte)'E'))
            {
                i++;
                if (i >= len) return ErrorPartial;
                if (js[i] == (byte)'+' || js[i] == (byte)'-')
                {
                    i++;
                    if (i >= len) return ErrorPartial;
                }
                if ((uint)(js[i] - (byte)'0') > 9) return ErrorInvalid;
                while (i < len && (uint)(js[i] - (byte)'0') <= 9) i++;
            }
            return i;
        }

        /// <summary>Lenient mode only: an unquoted member name, read up to the next delimiter (printable ASCII). Returns the end (exclusive) or an error.</summary>
        private static int ScanBareKey(ReadOnlySpan<byte> js, int start)
        {
            int i = start;
            for (; i < js.Length; i++)
            {
                byte c = js[i];
                if (IsDelimiter(c)) break;
                if (c < 32 || c >= 127) return ErrorInvalid;
            }
            return i;
        }

        /// <summary>
        /// A quoted string whose opening quote is at <paramref name="start"/>. Validates escapes (including
        /// surrogate pairs), rejects unescaped control characters and malformed UTF-8. Returns the index of the
        /// closing quote or an error; <paramref name="escaped"/> reports whether the contents need decoding.
        /// </summary>
        private static int ScanString(ReadOnlySpan<byte> js, int start, byte quote, bool lenient, out bool escaped)
        {
            bool seenEscape = false;
            for (int i = start + 1; i < js.Length; i++)
            {
                byte c = js[i];
                if (c == quote)
                {
                    escaped = seenEscape;
                    return i;
                }
                if (c == (byte)'\\')
                {
                    seenEscape = true;
                    i++;
                    if (i >= js.Length) { escaped = true; return ErrorPartial; }
                    switch (js[i])
                    {
                        case (byte)'"': case (byte)'/': case (byte)'\\': case (byte)'b':
                        case (byte)'f': case (byte)'r': case (byte)'n': case (byte)'t':
                            break;
                        case (byte)'\'':
                            if (!lenient) { escaped = true; return ErrorInvalid; }
                            break;
                        case (byte)'u':
                            {
                                if (i + 4 >= js.Length) { escaped = true; return ErrorPartial; }
                                int cp = Hex4(js, i + 1);
                                if (cp < 0) { escaped = true; return ErrorInvalid; }
                                i += 4;
                                if (cp >= 0xD800 && cp <= 0xDBFF)
                                {
                                    // a high surrogate must be followed by an escaped low surrogate
                                    if (i + 6 >= js.Length) { escaped = true; return ErrorPartial; }
                                    if (js[i + 1] != (byte)'\\' || js[i + 2] != (byte)'u') { escaped = true; return ErrorInvalid; }
                                    int low = Hex4(js, i + 3);
                                    if (low < 0xDC00 || low > 0xDFFF) { escaped = true; return ErrorInvalid; }
                                    i += 6;
                                }
                                else if (cp >= 0xDC00 && cp <= 0xDFFF)
                                {
                                    escaped = true;
                                    return ErrorInvalid;   // lone low surrogate
                                }
                                break;
                            }
                        default:
                            escaped = true;
                            return ErrorInvalid;
                    }
                    continue;
                }
                if ((uint)(c - 0x20) < 0x60) continue;   // printable ASCII: the common case
                if (c < 0x20) { escaped = seenEscape; return ErrorInvalid; }   // unescaped control character
                int n = Utf8SequenceLength(js, i);       // c >= 0x80: validate the multi-byte sequence
                if (n < 0) { escaped = seenEscape; return n; }
                i += n - 1;
            }
            escaped = seenEscape;
            return ErrorPartial;
        }

        /// <summary>Four hex digits at <paramref name="at"/> (caller guarantees they exist), or -1.</summary>
        private static int Hex4(ReadOnlySpan<byte> js, int at)
        {
            int v = 0;
            for (int k = 0; k < 4; k++)
            {
                int b = js[at + k];
                int d = b >= '0' && b <= '9' ? b - '0' : b >= 'a' && b <= 'f' ? b - 'a' + 10 : b >= 'A' && b <= 'F' ? b - 'A' + 10 : -1;
                if (d < 0) return -1;
                v = (v << 4) | d;
            }
            return v;
        }

        /// <summary>
        /// Length of the well-formed UTF-8 sequence starting at <paramref name="i"/> (a lead byte >= 0x80), per
        /// RFC 3629 (no overlongs, no surrogates, nothing above U+10FFFF); <see cref="ErrorInvalid"/> when it is
        /// malformed, <see cref="ErrorPartial"/> when the document ends inside it.
        /// </summary>
        private static int Utf8SequenceLength(ReadOnlySpan<byte> js, int i)
        {
            byte b0 = js[i];
            int need;
            byte lo = 0x80, hi = 0xBF;
            if (b0 >= 0xC2 && b0 <= 0xDF) need = 1;
            else if (b0 == 0xE0) { need = 2; lo = 0xA0; }
            else if ((b0 >= 0xE1 && b0 <= 0xEC) || b0 == 0xEE || b0 == 0xEF) need = 2;
            else if (b0 == 0xED) { need = 2; hi = 0x9F; }
            else if (b0 == 0xF0) { need = 3; lo = 0x90; }
            else if (b0 >= 0xF1 && b0 <= 0xF3) need = 3;
            else if (b0 == 0xF4) { need = 3; hi = 0x8F; }
            else return ErrorInvalid;   // continuation byte, overlong lead (C0/C1) or out of range (F5..FF)

            if (i + need >= js.Length) return ErrorPartial;
            byte b1 = js[i + 1];
            if (b1 < lo || b1 > hi) return ErrorInvalid;
            for (int k = 2; k <= need; k++)
            {
                byte b = js[i + k];
                if (b < 0x80 || b > 0xBF) return ErrorInvalid;
            }
            return need + 1;
        }

        /// <summary>Index of the first token after the subtree rooted at <paramref name="index"/>.</summary>
        public int Skip(int index)
        {
            int need = 1;
            int i = index;
            while (need > 0)
            {
                need += _tokens[i].Size;
                need--;
                i++;
            }
            return i;
        }

        /// <summary>Raw bytes of a token: for strings the contents without quotes, otherwise the literal text.</summary>
        public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> js, in JsmnToken tok) => js.Slice(tok.Start, tok.End - tok.Start);

        /// <summary>Raw JSON of a token including quotes for strings.</summary>
        public static ReadOnlySpan<byte> RawJson(ReadOnlySpan<byte> js, in JsmnToken tok)
        {
            if (tok.Type == JsmnType.String) return js.Slice(tok.Start - 1, tok.End - tok.Start + 2);
            return js.Slice(tok.Start, tok.End - tok.Start);
        }

        public static bool IsNull(ReadOnlySpan<byte> js, in JsmnToken tok)
        {
            return tok.Type == JsmnType.Primitive && tok.End - tok.Start == 4 && js[tok.Start] == (byte)'n';
        }
    }
}
