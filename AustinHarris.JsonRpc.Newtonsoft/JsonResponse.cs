using Newtonsoft.Json;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// Represents a Json Rpc Response
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonResponse : IJsonResponse
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "jsonrpc")]
        public string JsonRpc { get { return "2.0"; } }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "result")]
        public object Result { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "error")]
        public IJsonRpcException Error { get; set; }

        [JsonProperty(PropertyName = "id")]
        public object Id { get; set; }
    }
}
