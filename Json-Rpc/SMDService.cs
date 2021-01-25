using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.Json;


namespace AustinHarris.JsonRpc
{
    public class SMD
    {
        public Dictionary<string, SMDService> Services { get; set; }

        public SMD ()
	    {
            Services = new Dictionary<string,SMDService>();
	    }

        internal void AddService(string method, Dictionary<string,Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            var newService = new SMDService(parameters, defaultValues, dele);
            Services.Add(method,newService);
        }
    }

    public class SMDService
    {
        public Delegate dele;
        /// <summary>
        /// Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="transport">POST, GET, REST, JSONP, TCP/IP</param>
        /// <param name="envelope">URL, PATH, JSON, JSON-RPC-1.0, JSON-RPC-1.1, JSON-RPC-2.0</param>
        /// <param name="parameters"></param>
        /// <param name="defaultValues"></param>
        public SMDService(Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            // TODO: Complete member initialization
            this.dele = dele;
            this.parameters = new SMDAdditionalParameters[parameters.Count-1]; // last param is return type similar to Func<,>
            int ctr=0;
            foreach (var item in parameters)
	        {
                if (ctr < parameters.Count -1)// never the last one. last one is the return type.
                {
                    this.parameters[ctr++] = new SMDAdditionalParameters(item.Key, item.Value);
                }
	        }

            // create the default values storage for optional parameters.
            this.defaultValues = new ParameterDefaultValue[defaultValues.Count];
            int counter = 0;
            foreach (var item in defaultValues)
            {
                this.defaultValues[counter++] = new ParameterDefaultValue(item.Key, item.Value);
            }

            // this is getting the return type from the end of the param list
            this.returns = parameters.Values.LastOrDefault();
        }
        public Type returns { get; private set; }

        /// <summary>
        /// This indicates what parameters may be supplied for the service calls. 
        /// A parameters value MUST be an Array. Each value in the parameters Array should describe a parameter 
        /// and follow the JSON Schema property definition. Each of parameters that are defined at the root level
        /// are inherited by each of service definition's parameters. The parameter definition follows the 
        /// JSON Schema property definition with the additional properties:
        /// </summary>
        public SMDAdditionalParameters[] parameters { get; private set; }

        /// <summary>
        /// Stores default values for optional parameters.
        /// </summary>
        public ParameterDefaultValue[] defaultValues { get; private set; }
    }

    /// <summary>
    /// Holds default value for parameters.
    /// </summary>
    public class ParameterDefaultValue
    {
        /// <summary>
        /// Name of the parameter.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Default value for the parameter.
        /// </summary>
        public object Value { get; private set; }

        public ParameterDefaultValue(string name, object value)
        {
            this.Name = name;
            this.Value = value;
        }
    }

    public class SMDAdditionalParameters
    {
        private Func<JsonElement,object> __extractValue;

        public  SMDAdditionalParameters(string parametername, System.Type type)
        {
            Name = parametername;
            ObjectType = type;
            // fallback... slow
            __extractValue = (je) => je.ToObject(type);//throw new NotImplementedException($"Convert to type {type} not implemented");

            if (type == typeof(string)) __extractValue = (je) => je.GetString();
            if (type == typeof(short)) __extractValue = (je) => je.GetInt16();
            if (type == typeof(int)) __extractValue = (je) => je.GetInt32();
            if (type == typeof(Int64)) __extractValue = (je) => je.GetInt64();
            if (type == typeof(float)) __extractValue = (je) => je.GetSingle();
            if (type == typeof(double)) __extractValue = (je) => je.GetDouble();
            if (type == typeof(decimal)) __extractValue = (je) => je.GetDecimal();
            if (type == typeof(float?)) __extractValue = (je) => je.ValueKind == JsonValueKind.Null ? new float?() : je.GetSingle();
            if (type == typeof(double?)) __extractValue = (je) => je.ValueKind == JsonValueKind.Null ? new double?() :je.GetDouble();
            if (type == typeof(decimal?)) __extractValue = (je) => je.ValueKind == JsonValueKind.Null ? new decimal?() :je.GetDecimal();
            if (type == typeof(Boolean)) __extractValue = (je) => je.GetBoolean();
            if (type == typeof(DateTime)) __extractValue = (je) => je.GetDateTime();
            if (type == typeof(DateTimeOffset)) __extractValue = (je) => je.GetDateTimeOffset();
            if (type == typeof(char)) __extractValue = (je) => (char) je.GetInt32();

        }

       
        public Type ObjectType { get; set; }
        public string Name { get; set; }

        public object ExtractValue(JsonElement je)
        {
            return this.__extractValue(je);
        }
        internal static bool isSimpleType(Type t)
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
            {
                return true;
            }

            return false;
        }
    }
}
