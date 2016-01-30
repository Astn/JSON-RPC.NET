using System;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Required to expose a method to the JsonRpc service.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public sealed class JsonRpcMethodAttribute : Attribute
    {
        readonly string jsonMethodName;

        /// <summary>
        /// Required to expose a method to the JsonRpc service.
        /// </summary>
        /// <param name="jsonMethodName">Lets you specify the method name as it will be referred to by JsonRpc.</param>
        public JsonRpcMethodAttribute(string jsonMethodName = "")
        {
            this.jsonMethodName = jsonMethodName;
        }

        public string JsonMethodName
        {
            get { return jsonMethodName; }
        }
    }

    /// <summary>
    /// Used to assign JsonRpc parameter name to method argument.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class JsonRpcParamAttribute : Attribute
    {
        readonly string jsonParamName;

        /// <summary>
        /// Used to assign JsonRpc parameter name to method argument.
        /// </summary>
        /// <param name="jsonParamName">Lets you specify the parameter name as it will be referred to by JsonRpc.</param>
        public JsonRpcParamAttribute(string jsonParamName = "")
        {
            this.jsonParamName = jsonParamName;
        }

        public string JsonParamName
        {
            get { return jsonParamName; }
        }
    }
}
