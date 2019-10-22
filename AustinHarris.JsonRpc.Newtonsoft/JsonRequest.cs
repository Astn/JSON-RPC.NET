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

        public JsonRequest(string method, string raw)
        {
            Method = method;
            Raw = raw;
        }

        [JsonProperty("method")]
        public string Method { get; set; }

        public string Raw { get; set; }
    }
}
