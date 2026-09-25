namespace AustinHarris.JsonRpc
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using AustinHarris.JsonRpc.Invocation;

    public static partial class ServiceBinder
    {
        /// <summary>Compatibility overload preserving the original default-session registration signature.</summary>
        public static void BindMethod(string name, Delegate implementation, string[] parameterNames, IDictionary<string, object> defaults)
        {
            BindMethod(name, implementation, parameterNames, defaults, RpcContextFlow.None);
        }

        /// <summary>Compatibility overload preserving the original session registration signature.</summary>
        public static void BindMethod(string sessionId, string name, Delegate implementation, string[] parameterNames, IDictionary<string, object> defaults)
        {
            BindMethod(sessionId, name, implementation, parameterNames, defaults, RpcContextFlow.None);
        }

        /// <summary>Registers <paramref name="implementation"/> as method <paramref name="name"/> on the default session. See the session overload.</summary>
        public static void BindMethod(string name, Delegate implementation, string[] parameterNames = null, IDictionary<string, object> defaults = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            BindMethod(Handler.DefaultSessionId(), name, implementation, parameterNames, defaults, contextFlow);
        }

        /// <summary>
        /// Registers any delegate (a lambda, a method group, a closed instance method) as JSON-RPC method
        /// <paramref name="name"/> on session <paramref name="sessionId"/>, without attributes or a service class.
        /// Parameters bind by the delegate's signature: positional params by order, named params by
        /// <paramref name="parameterNames"/> when given (null entries keep the lambda's own name), else by the
        /// lambda's parameter names, else <c>arg1</c>, <c>arg2</c>... for a delegate whose names are not recoverable.
        /// <paramref name="defaults"/> (keyed by JSON name) make those parameters optional. The name must be free:
        /// re-registering a name is an error, unlike attribute binding; unbind it first with <see cref="UnbindMethod(string, string)"/>.
        /// Task and ValueTask delegates require ProcessAsync; async void is rejected.
        /// <paramref name="contextFlow"/> controls ambient context across awaits.
        /// </summary>
        public static void BindMethod(string sessionId, string name, Delegate implementation, string[] parameterNames = null, IDictionary<string, object> defaults = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            if (sessionId == null) throw new ArgumentNullException(nameof(sessionId));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A JSON-RPC method name is required.", nameof(name));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));

            var rpc = RpcMethod.FromDelegate(name, implementation, parameterNames, defaults, contextFlow);
            var handler = Handler.GetSessionHandler(sessionId);
            if (handler.MetaData.Services.ContainsKey(name))
            {
                throw new ArgumentException("JSON-RPC method '" + name + "' is already registered on session '" + sessionId + "'; unbind it first.", nameof(name));
            }

            var paras = new Dictionary<string, Type>();
            var defaultValues = new Dictionary<string, object>();
            foreach (var p in rpc.Parameters)
            {
                if (paras.ContainsKey(p.Name))
                {
                    throw new ArgumentException("JSON-RPC method '" + name + "': parameter name '" + p.Name + "' is used more than once.", nameof(parameterNames));
                }
                paras.Add(p.Name, p.Type);
                if (p.HasDefault) defaultValues.Add(p.Name, p.DefaultValue);
            }
            paras.Add("returns", rpc.ResultType);
            handler.MetaData.AddService(name, paras, defaultValues, implementation, rpc);
        }

        /// <summary>Removes method <paramref name="name"/> from session <paramref name="sessionId"/>; false when it was not registered.</summary>
        public static bool UnbindMethod(string sessionId, string name)
        {
            return Handler.GetSessionHandler(sessionId).MetaData.RemoveService(name);
        }

        /// <summary>Removes method <paramref name="name"/> from the default session; false when it was not registered.</summary>
        public static bool UnbindMethod(string name)
        {
            return UnbindMethod(Handler.DefaultSessionId(), name);
        }

        public static void BindService<T>() where T : new()
        {
            BindService<T>(Handler.DefaultSessionId());
        }
        public static void BindService<T>(string sessionId) where T : new()
        {
            BindService(sessionId, new T());
        }

        /// <summary>
        /// Registers every <c>[JsonRpcMethod]</c> of <paramref name="instance"/>'s type on session <paramref name="sessionId"/>,
        /// invoking them on that one instance from every thread; it must be thread-safe.
        /// </summary>
        public static void BindService(string sessionId, Object instance)
        {
            if (sessionId == null) throw new ArgumentNullException(nameof(sessionId));
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            Bind(sessionId, instance.GetType(), instance, null);
        }

        /// <summary>
        /// Registers every <c>[JsonRpcMethod]</c> of <paramref name="serviceType"/> on session <paramref name="sessionId"/>
        /// without an instance. Right before each call, <paramref name="resolve"/> is handed the RPC context of the
        /// request (what <see cref="Handler.RpcContext"/> returns) and returns the instance to invoke; it runs once per
        /// invocation, on the invoking thread. This is how a container's scoped and transient lifetimes reach a method:
        /// the resolver looks the request's scope up through the context and asks it for the service. The binder never
        /// constructs, caches or disposes anything itself, and the core takes no dependency on any container. Static
        /// methods never resolve. A resolver that returns null or another type fails the call with <c>-32603</c> (an
        /// <see cref="InvalidOperationException"/> naming the service type, visible to the error handler).
        /// </summary>
        public static void BindService(string sessionId, Type serviceType, Func<object, object> resolve)
        {
            if (sessionId == null) throw new ArgumentNullException(nameof(sessionId));
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            if (serviceType.ContainsGenericParameters)
                throw new ArgumentException("A closed type is required: '" + serviceType + "'.", nameof(serviceType));
            Bind(sessionId, serviceType, null, resolve);
        }

        /// <summary>Attribute discovery shared by the instance and the resolver overloads; exactly one of <paramref name="instance"/> and <paramref name="resolve"/> is set.</summary>
        private static void Bind(string sessionId, Type item, object instance, Func<object, object> resolve)
        {
            var methods = item.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(typeof(JsonRpcMethodAttribute), false).Length > 0);
            foreach (var meth in methods)
            {
                Dictionary<string, Type> paras = new Dictionary<string, Type>();
                Dictionary<string, object> defaultValues = new Dictionary<string, object>();

                var paramzs = meth.GetParameters();
                var jsonNames = new string[paramzs.Length];

                for (int i = 0; i < paramzs.Length; i++)
                {
                    string paramName;
                    var paramAttrs = paramzs[i].GetCustomAttributes(typeof(JsonRpcParamAttribute), false);
                    if (paramAttrs.Length > 0)
                    {
                        paramName = ((JsonRpcParamAttribute)paramAttrs[0]).JsonParamName;
                        if (string.IsNullOrEmpty(paramName))
                        {
                            paramName = paramzs[i].Name;
                        }
                    }
                    else
                    {
                        paramName = paramzs[i].Name;
                    }
                    jsonNames[i] = paramName;
                    paras.Add(paramName, paramzs[i].ParameterType);

                    if (paramzs[i].IsOptional)
                        defaultValues.Add(paramName, paramzs[i].DefaultValue);
                }

                var resType = meth.ReturnType;
                paras.Add("returns", resType); // the return type travels as the last entry, like Func<,>

                var atdata = meth.GetCustomAttributes(typeof(JsonRpcMethodAttribute), false);
                foreach (JsonRpcMethodAttribute handlerAttribute in atdata)
                {
                    var methodName = string.IsNullOrEmpty(handlerAttribute.JsonMethodName) ? meth.Name : handlerAttribute.JsonMethodName;
                    var rpc = resolve != null && !meth.IsStatic
                        ? RpcMethod.FromMethodInfo(methodName, meth, item, resolve, jsonNames, handlerAttribute.ContextFlow)
                        : RpcMethod.FromMethodInfo(methodName, meth, meth.IsStatic ? null : instance, jsonNames, handlerAttribute.ContextFlow);
                    Delegate legacy = null;
                    if (instance != null || meth.IsStatic)
                    {
                        try
                        {
                            legacy = Delegate.CreateDelegate(System.Linq.Expressions.Expression.GetDelegateType(paras.Values.ToArray()), meth.IsStatic ? null : instance, meth);
                        }
                        catch (ArgumentException)
                        {
                            // e.g. ref parameters: no Func<> shape exists; the compiled invoker still works
                        }
                    }
                    // a resolver-bound method has no instance to close a legacy delegate over; invocation uses rpc
                    var handlerSession = Handler.GetSessionHandler(sessionId);
                    handlerSession.MetaData.AddService(methodName, paras, defaultValues, legacy, rpc);
                }
            }
        }
    }
}
