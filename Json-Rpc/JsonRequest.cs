namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Represents a JsonRpc request. Only materialised for pre/post-process handlers and <see cref="Handler.Handle"/>;
    /// the fast path binds parameters straight from the request bytes.
    /// </summary>
    public class JsonRequest
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

        public string JsonRpc
        {
            get { return "2.0"; }
        }

        public string Method { get; set; }

        /// <summary>The params value in the serializer's own object model (JArray/JObject for Json.NET, JsonElement for System.Text.Json, List/Dictionary for the built-in serializer).</summary>
        public object Params { get; set; }

        /// <summary>The id: a long, a string, or null.</summary>
        public object Id { get; set; }
    }
}
