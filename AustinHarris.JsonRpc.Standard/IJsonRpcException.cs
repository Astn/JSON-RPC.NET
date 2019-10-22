namespace AustinHarris.JsonRpc
{
    public interface IJsonRpcException
    {
        int Code { get; set; }
        object Data { get; set; }
        string Message { get; set; }
    }
    public interface IJsonRpcExceptionFactory
    {
        IJsonRpcException CreateException(int code, string message, object data);
    }
}