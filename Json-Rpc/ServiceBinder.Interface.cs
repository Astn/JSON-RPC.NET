using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AustinHarris.JsonRpc.Invocation;

namespace AustinHarris.JsonRpc
{
    public static partial class ServiceBinder
    {
        /// <summary>Registers a closed interface contract and its children on the default session. See the session overload.</summary>
        public static RpcBinding BindInterface<TInterface>(TInterface implementation, RpcInterfaceBindingOptions options = null)
            where TInterface : class
        {
            return BindInterface(Handler.DefaultSessionId(), implementation, options);
        }

        /// <summary>
        /// Discovers and compiles a closed interface tree, then publishes all methods atomically on the session.
        /// Only public instance methods declared by the selected interfaces are exported; names, attributes,
        /// and optional defaults come from those declarations, including explicit implementations.
        /// Recursive getters run once per mount at registration and may have side effects. A failure publishes
        /// nothing; getter side effects cannot be undone. Empty, reserved (<c>rpc.</c>-prefixed or <c>$/cancelRequest</c>),
        /// duplicate, and occupied names are rejected. Generic methods and default interface bodies are unsupported.
        /// The returned handle owns the registrations, not the lifetime of the implementation objects.
        /// </summary>
        public static RpcBinding BindInterface<TInterface>(string sessionId, TInterface implementation,
            RpcInterfaceBindingOptions options = null) where TInterface : class
        {
            if (sessionId == null) throw new ArgumentNullException(nameof(sessionId));
            ValidateInterface(typeof(TInterface));
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            var builder = new InterfaceTreeBuilder(options ?? new RpcInterfaceBindingOptions());
            var metadata = Handler.GetSessionHandler(sessionId).MetaData;
            builder.Discover(typeof(TInterface), implementation, Array.Empty<string>(), metadata.transport);
            var binding = new RpcBinding(sessionId, metadata.Services, builder.Entries);
            metadata.Services.AddBatch(builder.Entries);
            return binding;
        }

        private static void ValidateInterface(Type type)
        {
            if (!type.IsInterface || type.ContainsGenericParameters)
                throw new ArgumentException("Interface binding requires a closed interface type: '" + type + "'.", "TInterface");
        }

        private sealed class InterfaceTreeBuilder
        {
            private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            private readonly string _prefix;
            private readonly string _separator;
            private readonly RpcNameCasing _casing;
            private readonly bool _recursive;
            private readonly Func<RpcInterfaceMethod, bool> _include;
            private readonly Func<RpcInterfaceMethod, string> _nameRule;
            private readonly List<(object Target, Type Interface)> _active = new List<(object, Type)>();
            internal readonly Dictionary<string, SMDService> Entries = new Dictionary<string, SMDService>(StringComparer.Ordinal);

            internal InterfaceTreeBuilder(RpcInterfaceBindingOptions options)
            {
                _prefix = options.Prefix ?? throw new ArgumentException("Prefix cannot be null.", nameof(options));
                _separator = options.Separator ?? throw new ArgumentException("Separator cannot be null.", nameof(options));
                _casing = options.Casing;
                _recursive = options.Recursive;
                _include = options.Include;
                _nameRule = options.NameRule;
                if (_casing != RpcNameCasing.Preserve && _casing != RpcNameCasing.CamelCase)
                    throw new ArgumentException("Unknown interface name casing.", nameof(options));
            }

            internal void Discover(Type type, object target, string[] path, string transport)
            {
                ValidateInterface(type);
                if (path.Length > 32) throw new ArgumentException("Interface tree depth exceeds 32 at '" + string.Join(".", path) + "'.");
                if (target == null) throw new ArgumentException("Interface child '" + string.Join(".", path) + "' is null.");
                if (_active.Any(item => ReferenceEquals(item.Target, target) && item.Interface == type))
                    throw new ArgumentException("Interface tree cycle at '" + string.Join(".", path) + "'.");
                _active.Add((target, type));
                try
                {
                    // GetInterfaces includes the transitive closure; each closed declaration is visited once per mount.
                    foreach (var contract in new[] { type }.Concat(type.GetInterfaces()).Distinct())
                    {
                        foreach (var method in contract.GetMethods(Declared))
                        {
                            if (method.IsSpecialName) continue;
                            var attributes = method.GetCustomAttributes(typeof(JsonRpcMethodAttribute), false)
                                .Cast<JsonRpcMethodAttribute>().ToArray();
                            if (attributes.Length == 0) AddMethod(method, target, path, null, RpcContextFlow.None, transport);
                            foreach (var attribute in attributes) AddMethod(method, target, path, attribute.JsonMethodName, attribute.ContextFlow, transport);
                        }
                        if (!_recursive) continue;
                        foreach (var property in contract.GetProperties(Declared))
                        {
                            var getter = property.GetGetMethod();
                            if (getter == null || getter.IsStatic || !property.PropertyType.IsInterface || property.GetIndexParameters().Length != 0) continue;
                            var childPath = path.Concat(new[] { property.Name }).ToArray();
                            var mapped = MapMethod(getter, target);
                            object child;
                            try { child = mapped.Invoke(target, null); }
                            catch (TargetInvocationException ex)
                            {
                                throw new ArgumentException("Interface getter '" + string.Join(".", childPath) + "' threw during registration.", ex.InnerException ?? ex);
                            }
                            Discover(property.PropertyType, child, childPath, transport);
                        }
                    }
                }
                finally { _active.RemoveAt(_active.Count - 1); }
            }

            private void AddMethod(MethodInfo method, object target, string[] path, string alias, RpcContextFlow contextFlow, string transport)
            {
                bool literal = !string.IsNullOrEmpty(alias);
                string leaf = literal ? alias : method.Name;
                string defaultName = _prefix + string.Join(_separator, path.Select(Case).Concat(new[] { literal ? leaf : Case(leaf) }));
                var description = new RpcInterfaceMethod(method, path, leaf, defaultName);
                if (_include != null && !_include(description)) return;
                string name = _nameRule == null ? defaultName : _nameRule(description);
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Invalid JSON-RPC interface method name: '" + name + "'.");
                if (Entries.ContainsKey(name)) throw new ArgumentException("Duplicate JSON-RPC interface method name '" + name + "'.");
                if (method.ContainsGenericParameters)
                    throw new ArgumentException("Generic interface method '" + method.Name + "' is not supported.");

                var mapped = MapMethod(method, target);
                var parameters = method.GetParameters();
                var names = parameters.Select(p =>
                {
                    var rename = p.GetCustomAttribute<JsonRpcParamAttribute>()?.JsonParamName;
                    return string.IsNullOrEmpty(rename) ? p.Name : rename;
                }).ToArray();
                var rpc = RpcMethod.FromMappedMethod(name, method, mapped, target, names, contextFlow);
                var types = new Dictionary<string, Type>();
                var defaults = new Dictionary<string, object>();
                foreach (var parameter in rpc.Parameters)
                {
                    if (types.ContainsKey(parameter.Name))
                        throw new ArgumentException("JSON-RPC method '" + name + "': duplicate parameter name '" + parameter.Name + "'.");
                    types.Add(parameter.Name, parameter.Type);
                    if (parameter.HasDefault) defaults.Add(parameter.Name, parameter.DefaultValue);
                }
                // The last entry is only a return-type marker, so it need not reserve an application parameter name.
                string returnKey = "returns";
                while (types.ContainsKey(returnKey)) returnKey += "_";
                types.Add(returnKey, rpc.ResultType);
                Delegate legacy = null;
                var shape = parameters.Select(p => p.ParameterType).ToArray();
                Type delegateType;
                bool hasShape = method.ReturnType == typeof(void)
                    ? Expression.TryGetActionType(shape, out delegateType)
                    : Expression.TryGetFuncType(shape.Concat(new[] { method.ReturnType }).ToArray(), out delegateType);
                if (hasShape) legacy = Delegate.CreateDelegate(delegateType, target, mapped);
                Entries.Add(name, new SMDService(transport, "JSON-RPC-2.0", types, defaults, legacy, rpc));
            }

            private static MethodInfo MapMethod(MethodInfo method, object target)
            {
                if (!method.IsAbstract)
                    throw new ArgumentException("Default interface member '" + method.Name + "' is not supported by interface binding.");
                var mapping = target.GetType().GetInterfaceMap(method.DeclaringType);
                int index = Array.IndexOf(mapping.InterfaceMethods, method);
                if (index < 0 || mapping.TargetMethods[index].DeclaringType.IsInterface)
                    throw new ArgumentException("Interface member '" + method.Name + "' has no concrete implementation mapping.");
                return mapping.TargetMethods[index];
            }

            private string Case(string segment)
            {
                if (_casing == RpcNameCasing.Preserve || segment.Length == 0 || !char.IsUpper(segment[0])) return segment;
                var chars = segment.ToCharArray();
                for (int i = 0; i < chars.Length && char.IsUpper(chars[i]); i++)
                {
                    if (i > 0 && i + 1 < chars.Length && !char.IsUpper(chars[i + 1])) break;
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
                return new string(chars);
            }
        }
    }
}
