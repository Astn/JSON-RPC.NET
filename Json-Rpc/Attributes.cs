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

        /// <summary>Whether the invocation context flows across awaits. Defaults to Flow.</summary>
        public RpcContextFlow ContextFlow { get; set; } = RpcContextFlow.None;

        public string JsonMethodName
        {
            get { return jsonMethodName; }
        }
    }

    /// <summary>Controls ambient context propagation for asynchronous methods. The default is <see cref="None"/>.</summary>
    public enum RpcContextFlow
    {
        /// <summary>Only the initial synchronous part of the method has ambient context; capture snapshots before awaiting. Allocation-free when the operation completes inline.</summary>
        None,
        /// <summary>Context, request id and authored exceptions flow across sequential awaits, at a per-invocation allocation.</summary>
        Flow
    }

    /// <summary>Injects the processor cancellation token instead of binding a JSON parameter.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
    public sealed class JsonRpcCancellationAttribute : Attribute { }

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
