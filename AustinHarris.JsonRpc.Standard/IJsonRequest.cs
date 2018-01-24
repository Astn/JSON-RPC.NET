namespace AustinHarris.JsonRpc
{
    public interface IJsonRequest
    {
        string Raw { get; set; }
        object Id { get; set; }
        string JsonRpc { get; }
        string Method { get; set; }
        object Params { get; set; }
    }

    public interface IJsonRequestFactory
    {
        IJsonRequest CreateRequest();
        IJsonRequest DeserializeRequest(string request);
        IJsonRequest[] DeserializeRequests(string requests);
    }
}