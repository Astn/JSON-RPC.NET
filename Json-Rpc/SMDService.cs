using System;
using System.Collections.Generic;
using System.Linq;

namespace AustinHarris.JsonRpc
{
    public sealed class SMDService
    {
        public Delegate SmdServiceDelegate;

        /// <summary>
        ///     Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="transport">POST, GET, REST, JSONP, TCP/IP</param>
        /// <param name="envelope">URL, PATH, JSON, JSON-RPC-1.0, JSON-RPC-1.1, JSON-RPC-2.0</param>
        /// <param name="parameters"></param>
        /// <param name="defaultValues"></param>
        public SMDService(string transport, string envelope, Dictionary<string, Type> parameters,
            Dictionary<string, object> defaultValues, Delegate dele)
        {
            // TODO: Complete member initialization
            SmdServiceDelegate = dele;
            this.transport = transport;
            this.envelope = envelope;
            this.parameters =
                new SMDAdditionalParameters[parameters.Count - 1]; // last param is return type similar to Func<,>
            var ctr = 0;
            foreach (var item in parameters)
                if (ctr < parameters.Count - 1) // never the last one. last one is the return type.
                    this.parameters[ctr++] = new SMDAdditionalParameters(item.Key, item.Value);

            // create the default values storage for optional parameters.
            this.defaultValues = new ParameterDefaultValue[defaultValues.Count];
            var counter = 0;
            foreach (var item in defaultValues)
                this.defaultValues[counter++] = new ParameterDefaultValue(item.Key, item.Value);

            // this is getting the return type from the end of the param list
            returns = new SMDResult(parameters.Values.LastOrDefault());
        }

        public string transport { get; }
        public string envelope { get; }
        public SMDResult returns { get; }

        /// <summary>
        ///     This indicates what parameters may be supplied for the service calls.
        ///     A parameters value MUST be an Array. Each value in the parameters Array should describe a parameter
        ///     and follow the JSON Schema property definition. Each of parameters that are defined at the root level
        ///     are inherited by each of service definition's parameters. The parameter definition follows the
        ///     JSON Schema property definition with the additional properties:
        /// </summary>
        public SMDAdditionalParameters[] parameters { get; }

        /// <summary>
        ///     Stores default values for optional parameters.
        /// </summary>
        public ParameterDefaultValue[] defaultValues { get; }
    }
}