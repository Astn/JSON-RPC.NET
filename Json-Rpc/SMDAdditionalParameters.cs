using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AustinHarris.JsonRpc
{
    public class SMDAdditionalParameters
    {
        public SMDAdditionalParameters(string parametername, Type type)
        {
            Name = parametername;
            Type = GetTypeRecursive(ObjectType = type);
        }

        [JsonIgnore]
        public Type ObjectType { get; set; }

        [JsonProperty("__name")]
        public string Name { get; set; }

        [JsonProperty("__type")]
        public int Type { get; set; }

        internal static int GetTypeRecursive(Type t)
        {
            var jo = new JObject
            {
                {"__name", t.Name.ToLower()}
            };

            if (IsSimpleType(t) || SMD.ContainsType(jo))
                return SMD.AddType(jo);

            var retVal = SMD.AddType(jo);

            var genArgs = t.GetGenericArguments();
            var properties = t.GetProperties();
            var fields = t.GetFields();

            if (genArgs.Length > 0)
            {
                var ja = new JArray();
                foreach (var item in genArgs)
                    if (item != t)
                    {
                        var jt = GetTypeRecursive(item);
                        ja.Add(jt);
                    }
                    else
                    {
                        // make a special case where -1 indicates this type
                        ja.Add(-1);
                    }
                jo.Add("__genericArguments", ja);
            }

            foreach (var item in properties)
                if (item.GetAccessors().Where(x => x.IsPublic).Count() > 0)
                    if (item.PropertyType != t)
                    {
                        var jt = GetTypeRecursive(item.PropertyType);
                        jo.Add(item.Name, jt);
                    }
                    else
                    {
                        // make a special case where -1 indicates this type
                        jo.Add(item.Name, -1);
                    }

            foreach (var item in fields)
                if (item.IsPublic)
                    if (item.FieldType != t)
                    {
                        var jt = GetTypeRecursive(item.FieldType);
                        jo.Add(item.Name, jt);
                    }
                    else
                    {
                        // make a special case where -1 indicates this type
                        jo.Add(item.Name, -1);
                    }

            return retVal;
        }

        internal static bool IsSimpleType(Type t)
        {
            var name = t.FullName.ToLower();

            if (name.Contains("newtonsoft")
                || name == "system.sbyte"
                || name == "system.byte"
                || name == "system.int16"
                || name == "system.uint16"
                || name == "system.int32"
                || name == "system.uint32"
                || name == "system.int64"
                || name == "system.uint64"
                || name == "system.char"
                || name == "system.single"
                || name == "system.double"
                || name == "system.boolean"
                || name == "system.decimal"
                || name == "system.float"
                || name == "system.numeric"
                || name == "system.money"
                || name == "system.string"
                || name == "system.object"
                || name == "system.type"
                // || name == "system.datetime"
                || name == "system.reflection.membertypes")
                return true;

            return false;
        }
    }
}