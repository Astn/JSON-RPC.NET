using System;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    ///     Used to assign JsonRpc parameter name to method argument.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class JsonRpcParamAttribute : Attribute
    {
        /// <summary>
        ///     Used to assign JsonRpc parameter name to method argument.
        /// </summary>
        /// <param name="jsonParamName">Lets you specify the parameter name as it will be referred to by JsonRpc.</param>
        public JsonRpcParamAttribute(string jsonParamName = "")
        {
            JsonParamName = jsonParamName;
        }

        public string JsonParamName { get; }
    }
}