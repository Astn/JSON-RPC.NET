using System;
using System.Buffers;
using System.Text;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// A growable UTF-8 output buffer backed by <see cref="ArrayPool{T}"/>. Supports rewinding so a
    /// partially written response can be discarded when a method throws. The processor keeps one per thread.
    /// </summary>
    public sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer;
        private int _written;

        public PooledByteBufferWriter(int initialCapacity = 4096)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        public int WrittenCount => _written;
        public ReadOnlySpan<byte> WrittenSpan => new ReadOnlySpan<byte>(_buffer, 0, _written);
        public ReadOnlyMemory<byte> WrittenMemory => new ReadOnlyMemory<byte>(_buffer, 0, _written);
        public ArraySegment<byte> WrittenSegment => new ArraySegment<byte>(_buffer, 0, _written);

        public void Clear() => _written = 0;

        /// <summary>Discards everything written after <paramref name="position"/>.</summary>
        public void Rewind(int position)
        {
            if (position < 0 || position > _written) throw new ArgumentOutOfRangeException(nameof(position));
            _written = position;
        }

        public void Advance(int count)
        {
            if (count < 0 || _written + count > _buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return new Memory<byte>(_buffer, _written, _buffer.Length - _written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return new Span<byte>(_buffer, _written, _buffer.Length - _written);
        }

        public void Write(byte b)
        {
            if (_written == _buffer.Length) Grow(1);
            _buffer[_written++] = b;
        }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            Ensure(bytes.Length);
            bytes.CopyTo(new Span<byte>(_buffer, _written, bytes.Length));
            _written += bytes.Length;
        }

        /// <summary>Removes the byte at <paramref name="position"/>, shifting the rest left.</summary>
        public void RemoveAt(int position)
        {
            if (position < 0 || position >= _written) throw new ArgumentOutOfRangeException(nameof(position));
            Buffer.BlockCopy(_buffer, position + 1, _buffer, position, _written - position - 1);
            _written--;
        }

        /// <summary>Decodes the written bytes as a UTF-8 string.</summary>
        public override string ToString() => _written == 0 ? string.Empty : Encoding.UTF8.GetString(_buffer, 0, _written);

        public void CopyTo(IBufferWriter<byte> destination)
        {
            var span = destination.GetSpan(_written);
            new ReadOnlySpan<byte>(_buffer, 0, _written).CopyTo(span);
            destination.Advance(_written);
        }

        public byte[] ToArray()
        {
            var copy = new byte[_written];
            Buffer.BlockCopy(_buffer, 0, copy, 0, _written);
            return copy;
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint < 1) sizeHint = 1;
            if (_buffer.Length - _written < sizeHint) Grow(sizeHint);
        }

        private void Grow(int sizeHint)
        {
            int newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
            var next = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(_buffer, 0, next, 0, _written);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = next;
        }

        public void Dispose()
        {
            var b = _buffer;
            if (b != null)
            {
                _buffer = null;
                _written = 0;
                ArrayPool<byte>.Shared.Return(b);
            }
        }
    }
}
