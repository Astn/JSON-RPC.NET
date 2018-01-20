namespace AustinHarris.JsonRpc
{
    public interface IJsonResponse
    {
        IJsonRpcException Error { get; set; }
        object Id { get; set; }
        string JsonRpc { get; }
        object Result { get; set; }
    }

    public interface IJsonResponseFactory
    {
        IJsonResponse CreateJsonErrorResponse(IJsonRpcException Error);
        IJsonResponse CreateJsonSuccessResponse(object result);
        IJsonResponse CreateJsonResponse();
        string SerializeResponse(IJsonResponse response);
        string SerializeResponses(IJsonResponse[] response);
    }
}