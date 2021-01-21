
using System.Text.Json;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Represents a JsonRpc request
    /// </summary>
    public class JsonRequest
    {
        public JsonRequest()
        {
        }

        public JsonRequest(string method, JsonElement pars, JsonElement id)
        {
            Method = method;
            Params = pars;
            Id = id;
        }


        public string JsonRpc
        {
            get { return "2.0"; }
        }


        public string Method { get; set; }


        public JsonElement Params { get; set; }


        public JsonElement Id { get; set; }
    }
}
