namespace AustinHarris.JsonRpc
{
    public interface IJsonRequest
    {
        string Raw { get; set; }
        string Method { get; set; }
    }

    public interface IJsonRequestFactory
    {
        IJsonRequest CreateRequest();
        IJsonRequest[] DeserializeRequests(string requests);
    }
}