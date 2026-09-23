using System;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>Maps a fixture name to a serializer instance. Add new serializers here to run the suite against them.</summary>
    public static class SerializerCatalog
    {
        public static JsonRpcSerializer Create(string name)
        {
            switch (name)
            {
                case "jsmn": return JsmnSerializer.Instance;
                case "newtonsoft": return new AustinHarris.JsonRpc.Newtonsoft.NewtonsoftJsonRpcSerializer();
                case "stj": return new AustinHarris.JsonRpc.SystemTextJson.SystemTextJsonRpcSerializer();
                default: throw new ArgumentException("Unknown serializer '" + name + "'.", nameof(name));
            }
        }
    }
}
