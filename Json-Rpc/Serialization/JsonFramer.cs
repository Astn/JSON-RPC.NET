using System;
using System.Buffers;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// Finds complete top-level JSON documents in a byte stream. Intended for transports that deliver
    /// JSON-RPC over a raw connection (System.IO.Pipelines / Kestrel ConnectionHandler, sockets) where
    /// messages are concatenated or newline-separated and a read may end mid-document.
    /// </summary>
    public static class JsonFramer
    {
        /// <summary>
        /// Tries to slice one complete JSON value (object or array) from the start of <paramref name="buffer"/>,
        /// skipping leading whitespace. On success <paramref name="document"/> holds the value and
        /// <paramref name="buffer"/> is advanced past it. Returns false when more bytes are needed.
        /// </summary>
        public static bool TryReadDocument(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> document)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            bool started = false;
            long index = 0;
            SequencePosition? startPos = null;

            foreach (var segment in buffer)
            {
                var span = segment.Span;
                for (int i = 0; i < span.Length; i++, index++)
                {
                    byte b = span[i];
                    if (!started)
                    {
                        if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
                        if (b != (byte)'{' && b != (byte)'[')
                        {
                            // not the start of a structured document; let the caller drop the byte
                            document = buffer.Slice(0, index + 1);
                            buffer = buffer.Slice(index + 1);
                            return true;
                        }
                        started = true;
                        startPos = buffer.GetPosition(index);
                    }
                    if (inString)
                    {
                        if (escaped) escaped = false;
                        else if (b == (byte)'\\') escaped = true;
                        else if (b == (byte)'"') inString = false;
                        continue;
                    }
                    switch (b)
                    {
                        case (byte)'"': inString = true; break;
                        case (byte)'{':
                        case (byte)'[': depth++; break;
                        case (byte)'}':
                        case (byte)']':
                            depth--;
                            if (depth == 0)
                            {
                                var end = buffer.GetPosition(index + 1);
                                document = buffer.Slice(startPos.Value, end);
                                buffer = buffer.Slice(end);
                                return true;
                            }
                            break;
                    }
                }
            }
            document = default;
            return false;
        }

        /// <summary>Span variant: returns the length of the first complete document (after leading whitespace) or -1.</summary>
        public static int FindDocumentEnd(ReadOnlySpan<byte> buffer)
        {
            int depth = 0;
            bool inString = false, escaped = false, started = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                byte b = buffer[i];
                if (!started)
                {
                    if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n') continue;
                    if (b != (byte)'{' && b != (byte)'[') return -1;
                    started = true;
                }
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (b == (byte)'\\') escaped = true;
                    else if (b == (byte)'"') inString = false;
                    continue;
                }
                switch (b)
                {
                    case (byte)'"': inString = true; break;
                    case (byte)'{':
                    case (byte)'[': depth++; break;
                    case (byte)'}':
                    case (byte)']':
                        depth--;
                        if (depth == 0) return i + 1;
                        break;
                }
            }
            return -1;
        }
    }
}
