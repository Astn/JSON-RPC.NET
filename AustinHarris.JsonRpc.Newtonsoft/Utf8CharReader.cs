using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// A reusable <see cref="TextReader"/> over the UTF-8 bytes of one JSON value. The bytes are decoded once
    /// into a pooled char buffer (no string, no MemoryStream) and served to <see cref="global::Newtonsoft.Json.JsonTextReader"/>.
    /// </summary>
    internal sealed class Utf8CharReader : TextReader
    {
        private char[] _chars = ArrayPool<char>.Shared.Rent(512);
        private int _length;
        private int _pos;

        /// <summary>Decodes <paramref name="utf8"/> into the buffer and rewinds.</summary>
        public void Reset(ReadOnlySpan<byte> utf8)
        {
            int max = Encoding.UTF8.GetMaxCharCount(utf8.Length);
            if (_chars.Length < max)
            {
                ArrayPool<char>.Shared.Return(_chars);
                _chars = ArrayPool<char>.Shared.Rent(max);
            }
#if NETSTANDARD2_0
            if (utf8.Length == 0)
            {
                _length = 0;
            }
            else
            {
                unsafe
                {
                    fixed (byte* src = utf8)
                    fixed (char* dst = _chars)
                    {
                        _length = Encoding.UTF8.GetChars(src, utf8.Length, dst, _chars.Length);
                    }
                }
            }
#else
            _length = Encoding.UTF8.GetChars(utf8, _chars);
#endif
            _pos = 0;
        }

        public override int Peek() => _pos < _length ? _chars[_pos] : -1;

        public override int Read() => _pos < _length ? _chars[_pos++] : -1;

        public override int Read(char[] buffer, int index, int count)
        {
            int n = Math.Min(count, _length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_chars, _pos, buffer, index, n);
            _pos += n;
            return n;
        }

        public override int ReadBlock(char[] buffer, int index, int count) => Read(buffer, index, count);

#if !NETSTANDARD2_0
        public override int Read(Span<char> buffer)
        {
            int n = Math.Min(buffer.Length, _length - _pos);
            if (n <= 0) return 0;
            new ReadOnlySpan<char>(_chars, _pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override int ReadBlock(Span<char> buffer) => Read(buffer);
#endif

        public override string ReadToEnd()
        {
            var s = _pos < _length ? new string(_chars, _pos, _length - _pos) : string.Empty;
            _pos = _length;
            return s;
        }

        protected override void Dispose(bool disposing)
        {
            var chars = _chars;
            if (chars != null)
            {
                _chars = null;
                _length = _pos = 0;
                ArrayPool<char>.Shared.Return(chars);
            }
            base.Dispose(disposing);
        }
    }
}
