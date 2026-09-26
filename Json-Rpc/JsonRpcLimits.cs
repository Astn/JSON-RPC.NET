using System;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Immutable bounds on the UTF-8 bytes in one JSON-RPC document and the number of top-level elements in a
    /// batch. A zero value disables that bound. A transport's own byte limit, such as Kestrel's
    /// <c>MaxRequestBytes</c>, is checked first when present.
    /// </summary>
    public sealed class JsonRpcLimits
    {
        /// <summary>Creates document and batch limits. Negative values are invalid; zero disables a limit.</summary>
        public JsonRpcLimits(long maxDocumentBytes = 4 * 1024 * 1024, int maxBatchCount = 1024)
        {
            if (maxDocumentBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxDocumentBytes));
            if (maxBatchCount < 0) throw new ArgumentOutOfRangeException(nameof(maxBatchCount));
            MaxDocumentBytes = maxDocumentBytes;
            MaxBatchCount = maxBatchCount;
        }

        /// <summary>The maximum UTF-8 bytes in one document, or zero for no byte limit.</summary>
        public long MaxDocumentBytes { get; }

        /// <summary>The maximum number of top-level batch elements, or zero for no batch limit.</summary>
        public int MaxBatchCount { get; }

        /// <summary>The default bounds: 4 MiB per document and 1024 elements per batch.</summary>
        public static JsonRpcLimits Default { get; } = new JsonRpcLimits();

        /// <summary>No core document-byte or batch-count bound.</summary>
        public static JsonRpcLimits Unlimited { get; } = new JsonRpcLimits(0, 0);
    }
}
