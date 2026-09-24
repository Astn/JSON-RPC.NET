using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// A reusable <see cref="TextWriter"/> that UTF-8 encodes straight into an <see cref="IBufferWriter{T}"/>.
    /// Characters are encoded in place (GetSpan / Advance) with a stateful <see cref="Encoder"/>, so surrogate
    /// pairs split across writes stay intact and nothing is buffered on this side; <see cref="Flush"/> drains
    /// the encoder. No BOM is ever written.
    /// </summary>
    internal sealed class BufferWriterTextWriter : TextWriter
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

        private readonly Encoder _encoder = Utf8NoBom.GetEncoder();
        private IBufferWriter<byte> _output;
#if NETSTANDARD2_0
        private readonly char[] _one = new char[1];
        private char[] _charScratch;
        private byte[] _byteScratch;
#endif

        public BufferWriterTextWriter() : base(CultureInfo.InvariantCulture) { }

        public override Encoding Encoding => Utf8NoBom;

        /// <summary>Points the writer at a new destination and clears any encoder state.</summary>
        public void Reset(IBufferWriter<byte> output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _encoder.Reset();
        }

        /// <summary>Drops the reference to the destination once the value has been flushed.</summary>
        public void Detach() => _output = null;

        public override void Flush()
        {
            if (_output == null) return;
#if NETSTANDARD2_0
            Encode(Array.Empty<char>(), 0, 0, flush: true);
#else
            Encode(ReadOnlySpan<char>.Empty, flush: true);
#endif
        }

        // ------------------------------------------------------------------ overrides Json.NET uses

        public override void Write(char value)
        {
#if NETSTANDARD2_0
            _one[0] = value;
            Encode(_one, 0, 1, flush: false);
#else
            Span<char> one = stackalloc char[1];
            one[0] = value;
            Encode(one, flush: false);
#endif
        }

        public override void Write(char[] buffer) => Write(buffer, 0, buffer.Length);

        public override void Write(char[] buffer, int index, int count)
        {
            if (count == 0) return;
#if NETSTANDARD2_0
            Encode(buffer, index, count, flush: false);
#else
            Encode(new ReadOnlySpan<char>(buffer, index, count), flush: false);
#endif
        }

        public override void Write(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
#if NETSTANDARD2_0
            if (_charScratch == null || _charScratch.Length < value.Length)
            {
                if (_charScratch != null) ArrayPool<char>.Shared.Return(_charScratch);
                _charScratch = ArrayPool<char>.Shared.Rent(value.Length);
            }
            value.CopyTo(0, _charScratch, 0, value.Length);
            Encode(_charScratch, 0, value.Length, flush: false);
#else
            Encode(value.AsSpan(), flush: false);
#endif
        }

#if !NETSTANDARD2_0
        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (!buffer.IsEmpty) Encode(buffer, flush: false);
        }
#endif

        // ------------------------------------------------------------------ encoding

#if NETSTANDARD2_0
        private void Encode(char[] chars, int index, int count, bool flush)
        {
            var output = _output ?? throw new InvalidOperationException("The writer is not attached to an output.");
            do
            {
                var memory = output.GetMemory(SizeHint(count));
                if (MemoryMarshal.TryGetArray<byte>(memory, out var segment))
                {
                    _encoder.Convert(chars, index, count, segment.Array, segment.Offset, segment.Count, flush, out int charsUsed, out int bytesUsed, out _);
                    output.Advance(bytesUsed);
                    index += charsUsed;
                    count -= charsUsed;
                }
                else
                {
                    // not array-backed: encode through a pooled scratch buffer and copy
                    if (_byteScratch == null) _byteScratch = ArrayPool<byte>.Shared.Rent(4096);
                    _encoder.Convert(chars, index, count, _byteScratch, 0, _byteScratch.Length, flush, out int charsUsed, out int bytesUsed, out _);
                    var dest = output.GetSpan(bytesUsed);
                    new ReadOnlySpan<byte>(_byteScratch, 0, bytesUsed).CopyTo(dest);
                    output.Advance(bytesUsed);
                    index += charsUsed;
                    count -= charsUsed;
                }
            } while (count > 0);
        }
#else
        private void Encode(ReadOnlySpan<char> chars, bool flush)
        {
            var output = _output ?? throw new InvalidOperationException("The writer is not attached to an output.");
            do
            {
                var dest = output.GetSpan(SizeHint(chars.Length));
                _encoder.Convert(chars, dest, flush, out int charsUsed, out int bytesUsed, out _);
                output.Advance(bytesUsed);
                chars = chars.Slice(charsUsed);
            } while (!chars.IsEmpty);
        }
#endif

        /// <summary>Asks for the whole encoded size for short runs and a bounded chunk for long ones (the loop above handles the remainder).</summary>
        private static int SizeHint(int charCount)
        {
            const int Chunk = 4096;
            if (charCount >= Chunk) return Chunk;
            return Math.Max(16, (charCount + 1) * 3);
        }

        protected override void Dispose(bool disposing)
        {
            _output = null;
#if NETSTANDARD2_0
            if (_charScratch != null) { ArrayPool<char>.Shared.Return(_charScratch); _charScratch = null; }
            if (_byteScratch != null) { ArrayPool<byte>.Shared.Return(_byteScratch); _byteScratch = null; }
#endif
            base.Dispose(disposing);
        }
    }
}
