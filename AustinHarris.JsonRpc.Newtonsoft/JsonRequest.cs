using Newtonsoft.Json;

namespace AustinHarris.JsonRpc.Newtonsoft
{
    /// <summary>
    /// Represents a JsonRpc request
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class JsonRequest : IJsonRequest
    {
        public JsonRequest()
        {
        }

        public JsonRequest(string method, object pars, object id)
        {
            Method = method;
            Params = pars;
            Id = id;
        }

        [JsonProperty("jsonrpc")]
        public string JsonRpc => "2.0";

        [JsonProperty("method")]
        public string Method { get; set; }

        [JsonProperty("params")]
        public object Params { get; set; }

        [JsonProperty("id")]
        public object Id { get; set; }
    }
}
