using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AustinHarris.JsonRpc
{
    public class SMD
    {
        public SMD()
        {
            transport = "POST";
            envelope = "URL";
            target = "/json.rpc";
            additonalParameters = false;
            parameters = new SMDAdditionalParameters[0];
            Services = new Dictionary<string, SMDService>();
            Types = new Dictionary<int, JObject>();
            TypeHashes = new List<string>();
        }

        public string transport { get; set; }
        public string envelope { get; set; }
        public string target { get; set; }
        public bool additonalParameters { get; set; }
        public SMDAdditionalParameters[] parameters { get; set; }

        [JsonIgnore]
        public static List<string> TypeHashes { get; set; }

        [JsonProperty("types")]
        public static Dictionary<int, JObject> Types { get; set; }

        [JsonProperty("services")]
        public Dictionary<string, SMDService> Services { get; set; }

        internal void AddService(string method, Dictionary<string, Type> parameters,
            Dictionary<string, object> defaultValues, Delegate dele)
        {
            var newService = new SMDService(transport, "JSON-RPC-2.0", parameters, defaultValues, dele);
            Services.Add(method, newService);
        }

        public static int AddType(JObject jo)
        {
            var hash = string.Format("t_{0}", jo.ToString().GetHashCode());

            lock (TypeHashes)
            {
                if (TypeHashes.Contains(hash)) return TypeHashes.IndexOf(hash);

                TypeHashes.Add(hash);
                var idx = TypeHashes.IndexOf(hash);
                Types.Add(idx, jo);
            }

            return TypeHashes.IndexOf(hash);
        }

        public static bool ContainsType(JObject jo)
        {
            return TypeHashes.Contains(string.Format("t_{0}", jo.ToString().GetHashCode()));
        }
    }
}