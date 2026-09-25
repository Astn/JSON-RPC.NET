using System;
using System.Reflection;

namespace AustinHarris.JsonRpc
{
    /// <summary>Casing of generated property and method name segments. Explicit aliases remain literal.</summary>
    public enum RpcNameCasing
    {
        /// <summary>Keep CLR spelling.</summary>
        Preserve,
        /// <summary>Lowercase the initial capital or acronym using invariant casing.</summary>
        CamelCase
    }

    /// <summary>Registration-time naming and selection for an interface tree. Values are captured before discovery.</summary>
    public sealed class RpcInterfaceBindingOptions
    {
        /// <summary>Text prepended verbatim to every default wire name.</summary>
        public string Prefix { get; set; } = "";

        /// <summary>Text joining property segments and the leaf. Defaults to a dot.</summary>
        public string Separator { get; set; } = ".";

        /// <summary>Casing applied to generated segments only; explicit aliases and the prefix are unchanged.</summary>
        public RpcNameCasing Casing { get; set; } = RpcNameCasing.Preserve;

        /// <summary>
        /// Walk readable, non-indexed instance properties declared as interfaces. Each getter runs once per
        /// mount at registration and may have side effects. Captured children do not follow later property changes.
        /// Null children, getter failures, cycles, and paths longer than 32 properties reject the whole tree.
        /// </summary>
        public bool Recursive { get; set; } = true;

        /// <summary>Optional predicate called once per leaf alias; null includes all methods. Reads interface metadata.</summary>
        public Func<RpcInterfaceMethod, bool> Include { get; set; }

        /// <summary>Optional rule called once per included alias, returning the complete wire name in place of default naming.</summary>
        public Func<RpcInterfaceMethod, string> NameRule { get; set; }
    }

    /// <summary>An interface declaration and mounted alias presented to registration callbacks.</summary>
    public sealed class RpcInterfaceMethod
    {
        private readonly string[] _path;

        internal RpcInterfaceMethod(MethodInfo method, string[] path, string leaf, string defaultName)
        {
            MethodInfo = method;
            Interface = method.DeclaringType;
            _path = (string[])path.Clone();
            Leaf = leaf;
            DefaultName = defaultName;
        }

        /// <summary>The MethodInfo for the interface declaration, including its parameter metadata and attributes.</summary>
        public MethodInfo MethodInfo { get; }

        /// <summary>The closed interface declaring <see cref="MethodInfo"/>.</summary>
        public Type Interface { get; }

        /// <summary>A copy of the CLR property names from the root; empty for root methods.</summary>
        public string[] Path => (string[])_path.Clone();

        /// <summary>The explicit alias, or the CLR method name when no nonempty alias was supplied.</summary>
        public string Leaf { get; }

        /// <summary>The prefix plus joined path and leaf, with generated segments cased as requested.</summary>
        public string DefaultName { get; }
    }
}
