namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Represents a Json Rpc Response. Materialised only for post-process handlers and <see cref="Handler.Handle"/>.
    /// </summary>
    public class JsonResponse
    {
        public string JsonRpc { get; set; } = "2.0";

        public object Result { get; set; }

        public JsonRpcException Error { get; set; }

        public object Id { get; set; }
    }

    /// <summary>
    /// Represents a Json Rpc Response with a typed result (used by clients).
    /// </summary>
    public class JsonResponse<T>
    {
        public string JsonRpc { get; set; } = "2.0";

        public T Result { get; set; }

        public JsonRpcException Error { get; set; }

        public object Id { get; set; }
    }
}
