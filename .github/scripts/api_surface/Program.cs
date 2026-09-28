using System.Reflection;
using System.Text;

// Dumps the public API surface of the four assemblies as sorted text, one member per line, so two versions diff.
var names = new[] { "AustinHarris.JsonRpc", "AustinHarris.JsonRpc.AspNetCore", "AustinHarris.JsonRpc.SystemTextJson", "AustinHarris.JsonRpc.Newtonsoft" };
var sb = new StringBuilder();
foreach (var name in names)
{
    var asm = Assembly.Load(name);
    var an = asm.GetName();
    sb.AppendLine($"## {an.Name} {an.Version} PublicKeyToken={Convert.ToHexString(an.GetPublicKeyToken() ?? Array.Empty<byte>()).ToLowerInvariant()}");
    var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
    sb.AppendLine($"   informational={informational.Split('+')[0]}");
    foreach (var t in asm.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
    {
        if (t.IsNested && !(t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem)) continue;
        string kind = t.IsInterface ? "interface" : t.IsEnum ? "enum" : t.IsValueType ? "struct" : t.IsSubclassOf(typeof(Delegate)) ? "delegate" : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
        var bases = new List<string>();
        if (t.BaseType != null && t.BaseType != typeof(object) && t.BaseType != typeof(ValueType) && t.BaseType != typeof(Enum) && t.BaseType != typeof(MulticastDelegate)) bases.Add(t.BaseType.FullName ?? t.BaseType.Name);
        bases.AddRange(t.GetInterfaces().Where(i => i.IsPublic || i.IsNestedPublic).Select(i => i.FullName ?? i.Name).OrderBy(x => x, StringComparer.Ordinal));
        sb.AppendLine($"{kind} {t.FullName}{(bases.Count > 0 ? " : " + string.Join(", ", bases) : "")}");
        if (t.IsEnum)
        {
            foreach (var v in Enum.GetNames(t)) sb.AppendLine($"    {v} = {Convert.ToInt64(Enum.Parse(t, v))}");
            continue;
        }
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var lines = new List<string>();
        foreach (var m in t.GetMembers(flags))
        {
            if (!Visible(m)) continue;
            if (m is MethodInfo mi && mi.IsSpecialName) continue;   // property/event accessors, operators listed via the property
            string prefix = m switch { ConstructorInfo => "ctor", MethodInfo => "method", PropertyInfo => "property", FieldInfo => "field", EventInfo => "event", Type => "nested", _ => m.MemberType.ToString() };
            string text = m switch
            {
                PropertyInfo p => $"{TypeName(p.PropertyType)} {p.Name}{Indexer(p)} {{ {(p.GetMethod != null && Visible(p.GetMethod) ? "get; " : "")}{(p.SetMethod != null && Visible(p.SetMethod) ? "set; " : "")}}}",
                FieldInfo f => $"{(f.IsStatic ? "static " : "")}{(f.IsInitOnly ? "readonly " : "")}{(f.IsLiteral ? "const " : "")}{TypeName(f.FieldType)} {f.Name}",
                EventInfo e => $"{TypeName(e.EventHandlerType)} {e.Name}",
                ConstructorInfo c => $"{(c.IsStatic ? "static " : "")}{t.Name}({Params(c.GetParameters())})",
                MethodInfo mm => $"{(mm.IsStatic ? "static " : "")}{(mm.IsAbstract ? "abstract " : mm.IsVirtual && !mm.IsFinal ? "virtual " : "")}{TypeName(mm.ReturnType)} {mm.Name}{Generic(mm)}({Params(mm.GetParameters())})",
                Type nt => nt.Name,
                _ => m.Name,
            };
            string access = Access(m);
            lines.Add($"    {access} {prefix} {text}");
        }
        foreach (var l in lines.OrderBy(x => x, StringComparer.Ordinal)) sb.AppendLine(l);
    }
}
var outPath = args.Length > 0 ? args[0] : "api.txt";
File.WriteAllText(outPath, sb.ToString());
Console.WriteLine("written " + outPath);

static bool Visible(MemberInfo m) => m switch
{
    MethodBase mb => mb.IsPublic || mb.IsFamily || mb.IsFamilyOrAssembly,
    FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
    PropertyInfo p => (p.GetMethod != null && Visible(p.GetMethod)) || (p.SetMethod != null && Visible(p.SetMethod)),
    EventInfo e => e.AddMethod != null && Visible(e.AddMethod),
    Type t => t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem,
    _ => false,
};
static string Access(MemberInfo m) => m switch
{
    MethodBase mb => mb.IsPublic ? "public" : "protected",
    FieldInfo f => f.IsPublic ? "public" : "protected",
    PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false) ? "public" : "protected",
    EventInfo e => e.AddMethod.IsPublic ? "public" : "protected",
    Type t => t.IsNestedPublic ? "public" : "protected",
    _ => "?",
};
static string TypeName(Type t)
{
    if (t.IsByRef) return "ref " + TypeName(t.GetElementType());
    if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
    if (t.IsGenericParameter) return t.Name;
    if (t.IsGenericType)
    {
        var def = t.GetGenericTypeDefinition();
        string baseName = (def.FullName ?? def.Name).Split('`')[0];
        return baseName + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
    }
    return t.FullName ?? t.Name;
}
static string Params(ParameterInfo[] ps) => string.Join(", ", ps.Select(p => $"{(p.IsOut ? "out " : p.ParameterType.IsByRef && p.IsIn ? "in " : "")}{TypeName(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = " + (p.DefaultValue ?? "null") : "")}"));
static string Generic(MethodInfo m) => m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "";
static string Indexer(PropertyInfo p) { var ps = p.GetIndexParameters(); return ps.Length == 0 ? "" : "[" + Params(ps) + "]"; }
