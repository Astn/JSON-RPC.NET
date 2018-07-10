using System;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc
{
    public class SMDResult
    {
        public SMDResult(Type type)
        {
            Type = SMDAdditionalParameters.GetTypeRecursive(type);
        }

        [JsonProperty("__type")]
        public int Type { get; private set; }
    }
}