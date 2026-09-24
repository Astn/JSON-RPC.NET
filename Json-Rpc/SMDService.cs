using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AustinHarris.JsonRpc.Invocation;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// Service Mapping Description for one session: the registered methods plus a serializer-neutral
    /// description of their parameter and return types (http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html).
    /// </summary>
    public class SMD
    {
        public string transport { get; set; }
        public string envelope { get; set; }
        public string target { get; set; }
        public bool additonalParameters { get; set; }
        public SMDAdditionalParameters[] parameters { get; set; }

        private static readonly List<string> _typeHashes = new List<string>();
        private static readonly Dictionary<int, Dictionary<string, object>> _types = new Dictionary<int, Dictionary<string, object>>();

        /// <summary>Process-wide registry of described types (shared by every session).</summary>
        public static Dictionary<int, Dictionary<string, object>> Types => _types;

        /// <summary>
        /// The registered services by JSON method name. <see cref="Handler.RegisterFuction"/> and
        /// <see cref="Handler.UnRegisterFunction"/> go through it; editing it directly (Add, Remove, the indexer,
        /// Clear) is supported and takes effect for the next request, because every mutation also updates the
        /// dispatch table.
        /// </summary>
        public SMDServiceCollection Services { get; }

        public SMD()
        {
            transport = "POST";
            envelope = "URL";
            target = "/json.rpc";
            additonalParameters = false;
            parameters = new SMDAdditionalParameters[0];
            Services = new SMDServiceCollection();
        }

        internal void AddService(string method, Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
        {
            var names = parameters.Keys.Take(Math.Max(0, parameters.Count - 1)).ToArray();
            var rpc = RpcMethod.FromDelegate(method, dele, names, defaultValues);
            AddService(method, parameters, defaultValues, dele, rpc);
        }

        internal void AddService(string method, Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele, RpcMethod rpc)
        {
            Services[method] = new SMDService(transport, "JSON-RPC-2.0", parameters, defaultValues, dele, rpc);
        }

        internal bool RemoveService(string method)
        {
            return Services.Remove(method);
        }

        internal void Clear()
        {
            Services.Clear();
        }

        /// <summary>Span-keyed lookup used by the request path (lock-free).</summary>
        internal SMDService Find(ReadOnlySpan<byte> methodUtf8)
        {
            return Services.Find(methodUtf8);
        }

        internal SMDService Find(string method)
        {
            if (method == null) return null;
            return Services.TryGetValue(method, out var s) ? s : null;
        }

        public static int AddType(Dictionary<string, object> jo)
        {
            var hash = TypeHash(jo);
            lock (_typeHashes)
            {
                var existing = _typeHashes.IndexOf(hash);
                if (existing >= 0) return existing;
                _typeHashes.Add(hash);
                var idx = _typeHashes.Count - 1;
                _types.Add(idx, jo);
                return idx;
            }
        }

        public static bool ContainsType(Dictionary<string, object> jo)
        {
            lock (_typeHashes)
            {
                return _typeHashes.Contains(TypeHash(jo));
            }
        }

        private static string TypeHash(Dictionary<string, object> jo)
        {
            return "t_" + Jsmn.JsmnSerializer.Instance.Serialize(jo, typeof(Dictionary<string, object>)).GetHashCode();
        }
    }

    /// <summary>
    /// The services of one session keyed by JSON method name. A dictionary for callers; underneath, every
    /// mutation also replaces the lock-free UTF-8 dispatch table the request path resolves methods from, so
    /// an added, removed or replaced service is visible to the next request. Reads of the dictionary take a
    /// lock; the request path never does.
    /// </summary>
    public sealed class SMDServiceCollection : IDictionary<string, SMDService>, IReadOnlyDictionary<string, SMDService>
    {
        private Dictionary<string, SMDService> _services = new Dictionary<string, SMDService>();
        private readonly Utf8KeyTable<SMDService> _table = new Utf8KeyTable<SMDService>();
        private readonly object _sync = new object();

        internal void AddBatch(IReadOnlyDictionary<string, SMDService> entries)
        {
            if (entries.Count == 0) return;
            lock (_sync)
            {
                var next = new Dictionary<string, SMDService>(_services);
                foreach (var entry in entries)
                {
                    if (next.ContainsKey(entry.Key))
                        throw new ArgumentException("JSON-RPC method '" + entry.Key + "' is already registered; unbind it first.", nameof(entries));
                    next.Add(entry.Key, entry.Value);
                }
                // No fallible work remains after the table publishes; dictionary readers hold this same lock.
                _table.ReplaceAll(next);
                _services = next;
            }
        }

        internal void RemoveBatch(IReadOnlyDictionary<string, SMDService> entries)
        {
            lock (_sync)
            {
                Dictionary<string, SMDService> next = null;
                foreach (var entry in entries)
                {
                    if (_services.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry.Value))
                    {
                        if (next == null) next = new Dictionary<string, SMDService>(_services);
                        next.Remove(entry.Key);
                    }
                }
                if (next == null) return;
                _table.ReplaceAll(next);
                _services = next;
            }
        }

        /// <summary>Lock-free span-keyed lookup used by the request path.</summary>
        internal SMDService Find(ReadOnlySpan<byte> methodUtf8)
        {
            return _table.Find(methodUtf8);
        }

        public SMDService this[string key]
        {
            get
            {
                lock (_sync)
                {
                    return _services[key];
                }
            }
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                if (value == null) throw new ArgumentNullException(nameof(value));
                lock (_sync)
                {
                    _services[key] = value;
                    _table.Set(key, value);
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _services.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        /// <summary>A snapshot of the method names.</summary>
        public ICollection<string> Keys
        {
            get
            {
                lock (_sync)
                {
                    return new List<string>(_services.Keys);
                }
            }
        }

        /// <summary>A snapshot of the services.</summary>
        public ICollection<SMDService> Values
        {
            get
            {
                lock (_sync)
                {
                    return new List<SMDService>(_services.Values);
                }
            }
        }

        IEnumerable<string> IReadOnlyDictionary<string, SMDService>.Keys => Keys;
        IEnumerable<SMDService> IReadOnlyDictionary<string, SMDService>.Values => Values;

        public void Add(string key, SMDService value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            lock (_sync)
            {
                _services.Add(key, value);
                _table.Set(key, value);
            }
        }

        public void Add(KeyValuePair<string, SMDService> item)
        {
            Add(item.Key, item.Value);
        }

        public bool Remove(string key)
        {
            if (key == null) return false;
            lock (_sync)
            {
                if (!_services.Remove(key)) return false;
                _table.Remove(key);
                return true;
            }
        }

        public bool Remove(KeyValuePair<string, SMDService> item)
        {
            lock (_sync)
            {
                if (!_services.TryGetValue(item.Key, out var existing) || !ReferenceEquals(existing, item.Value)) return false;
                _services.Remove(item.Key);
                _table.Remove(item.Key);
                return true;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _services.Clear();
                _table.Clear();
            }
        }

        public bool ContainsKey(string key)
        {
            if (key == null) return false;
            lock (_sync)
            {
                return _services.ContainsKey(key);
            }
        }

        public bool Contains(KeyValuePair<string, SMDService> item)
        {
            lock (_sync)
            {
                return _services.TryGetValue(item.Key, out var existing) && ReferenceEquals(existing, item.Value);
            }
        }

        public bool TryGetValue(string key, out SMDService value)
        {
            if (key == null)
            {
                value = null;
                return false;
            }
            lock (_sync)
            {
                return _services.TryGetValue(key, out value);
            }
        }

        public void CopyTo(KeyValuePair<string, SMDService>[] array, int arrayIndex)
        {
            lock (_sync)
            {
                ((ICollection<KeyValuePair<string, SMDService>>)_services).CopyTo(array, arrayIndex);
            }
        }

        /// <summary>Enumerates a snapshot, so the collection may be edited while it is enumerated.</summary>
        public IEnumerator<KeyValuePair<string, SMDService>> GetEnumerator()
        {
            KeyValuePair<string, SMDService>[] snapshot;
            lock (_sync)
            {
                snapshot = new KeyValuePair<string, SMDService>[_services.Count];
                ((ICollection<KeyValuePair<string, SMDService>>)_services).CopyTo(snapshot, 0);
            }
            return ((IEnumerable<KeyValuePair<string, SMDService>>)snapshot).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class SMDService
    {
        /// <summary>The registered delegate (kept for backwards compatibility; invocation uses <see cref="Method"/>).</summary>
        public Delegate dele;

        /// <summary>The compiled invokers for this service method.</summary>
        public RpcMethod Method { get; private set; }

        /// <summary>
        /// Defines a service method http://dojotoolkit.org/reference-guide/1.8/dojox/rpc/smd.html
        /// </summary>
        /// <param name="transport">POST, GET, REST, JSONP, TCP/IP</param>
        /// <param name="envelope">URL, PATH, JSON, JSON-RPC-1.0, JSON-RPC-1.1, JSON-RPC-2.0</param>
        /// <param name="parameters">parameter names and types; the last entry is the return type</param>
        /// <param name="defaultValues">default values for optional parameters</param>
        /// <param name="dele">the implementation</param>
        public SMDService(string transport, string envelope, Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele)
            : this(transport, envelope, parameters, defaultValues, dele,
                  RpcMethod.FromDelegate("", dele, parameters.Keys.Take(Math.Max(0, parameters.Count - 1)).ToArray(), defaultValues))
        {
        }

        internal SMDService(string transport, string envelope, Dictionary<string, Type> parameters, Dictionary<string, object> defaultValues, Delegate dele, RpcMethod method)
        {
            this.dele = dele;
            this.Method = method;
            this.transport = transport;
            this.envelope = envelope;
            // Async metadata describes only wire parameters and the eventual result.
            if (method.IsAsync || method.HasCancellation)
            {
                parameters = method.Parameters.ToDictionary(p => p.Name, p => p.Type);
                parameters.Add("returns", method.ResultType);
                defaultValues = method.Parameters.Where(p => p.HasDefault).ToDictionary(p => p.Name, p => p.DefaultValue);
            }
            this.parameters = new SMDAdditionalParameters[Math.Max(0, parameters.Count - 1)]; // last param is return type similar to Func<,>
            int ctr = 0;
            foreach (var item in parameters)
            {
                if (ctr < parameters.Count - 1)// never the last one. last one is the return type.
                {
                    this.parameters[ctr++] = new SMDAdditionalParameters(item.Key, item.Value);
                }
            }

            this.defaultValues = new ParameterDefaultValue[defaultValues.Count];
            int counter = 0;
            foreach (var item in defaultValues)
            {
                this.defaultValues[counter++] = new ParameterDefaultValue(item.Key, item.Value);
            }

            this.returns = new SMDResult(parameters.Values.LastOrDefault());
        }
        public string transport { get; private set; }
        public string envelope { get; private set; }
        public SMDResult returns { get; private set; }

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

    public class SMDResult
    {
        public int __type { get; private set; }

        public int Type => __type;

        public SMDResult(System.Type type)
        {
            __type = type == null ? -1 : SMDAdditionalParameters.GetTypeRecursive(type);
        }
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
        public SMDAdditionalParameters(string parametername, System.Type type)
        {
            Name = parametername;
            Type = GetTypeRecursive(ObjectType = type);
        }

        public Type ObjectType { get; set; }
        public string __name { get { return Name; } }
        public string Name { get; set; }
        public int __type { get { return Type; } }
        public int Type { get; set; }

        internal static int GetTypeRecursive(Type t)
        {
            var jo = new Dictionary<string, object>();
            jo.Add("__name", t.Name.ToLower());

            if (isSimpleType(t) || SMD.ContainsType(jo))
            {
                return SMD.AddType(jo);
            }

            var retVal = SMD.AddType(jo);

            var genArgs = t.GetGenericArguments();
            PropertyInfo[] properties = t.GetProperties();
            FieldInfo[] fields = t.GetFields();

            if (genArgs.Length > 0)
            {
                var ja = new List<object>();
                foreach (var item in genArgs)
                {
                    // -1 marks a reference back to this type
                    ja.Add(item != t ? GetTypeRecursive(item) : -1);
                }
                jo.Add("__genericArguments", ja);
            }

            foreach (var item in properties)
            {
                if (item.GetAccessors().Any(x => x.IsPublic) && !jo.ContainsKey(item.Name))
                {
                    jo.Add(item.Name, item.PropertyType != t ? GetTypeRecursive(item.PropertyType) : -1);
                }
            }

            foreach (var item in fields)
            {
                if (item.IsPublic && !jo.ContainsKey(item.Name))
                {
                    jo.Add(item.Name, item.FieldType != t ? GetTypeRecursive(item.FieldType) : -1);
                }
            }

            return retVal;
        }

        internal static bool isSimpleType(Type t)
        {
            var name = t.FullName.ToLower();

            if (name.Contains("newtonsoft")
                || name.Contains("system.text.json")
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
                || name == "system.void"
                || name == "system.reflection.membertypes")
            {
                return true;
            }

            return false;
        }
    }
}
