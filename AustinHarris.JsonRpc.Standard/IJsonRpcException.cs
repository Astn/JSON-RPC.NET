namespace AustinHarris.JsonRpc
{
    public interface IJsonRpcException
    {
        int code { get; set; }
        object data { get; set; }
        string message { get; set; }
    }
    public interface IJsonRpcExceptionFactory
    {
        IJsonRpcException CreateException(int code, string message, object data);
    }
}