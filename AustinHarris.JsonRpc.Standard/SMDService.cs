using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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

        public KeyValuePair<string, Type>[] parameters { get; }

        /// <summary>
        /// Stores default values for optional parameters.
        /// </summary>
        public KeyValuePair<string, Object>[] defaultValues { get; }
        public Type returns { get; }

        /// <summary>
        /// Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="defaultValues"></param>
        public SMDService(Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            // TODO: Complete member initialization
            this.dele = dele;
            this.parameters = parameters.Take(parameters.Count-1).ToArray(); // last param is return type similar to Func<,>

            // create the default values storage for optional parameters.
            this.defaultValues = defaultValues.ToArray();

            // this is getting the return type from the end of the param list
            this.returns = parameters.Values.LastOrDefault();
        }
    }
}
