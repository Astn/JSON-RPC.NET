using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.Invocation
{
    /// <summary>
    /// Binds parameters straight from the request reader, invokes the target and streams the result
    /// into <paramref name="output"/> through the serializer. No boxing, no object[] and no DynamicInvoke.
    /// <paramref name="map"/>[p] is the reader parameter index for method parameter p, or -1 to use its default.
    /// </summary>
    public delegate void StreamingInvoker(JsonRpcRequestReader reader, int[] map, JsonRpcSerializer serializer, IBufferWriter<byte> output);

    /// <summary>Same binding, but returns the boxed result. Used when pre/post handlers need a <see cref="JsonResponse"/>.</summary>
    public delegate object BoxedInvoker(JsonRpcRequestReader reader, int[] map);

    /// <summary>
    /// The invoker specialised for the built-in serializer: parameters are read from the tokens by direct static
    /// calls and the result is formatted into the concrete pooled writer, so a primitive request goes from bytes to
    /// bytes without a virtual, delegate or interface call around the service method.
    /// </summary>
    internal delegate void JsmnInvoker(JsmnRequestReader reader, int[] map, PooledByteBufferWriter output);

    public sealed class RpcParameter
    {
        internal RpcParameter(string name, Type type, bool hasDefault, object defaultValue)
        {
            Name = name;
            Type = type;
            HasDefault = hasDefault;
            DefaultValue = defaultValue;
            NameUtf8 = System.Text.Encoding.UTF8.GetBytes(name);
        }

        public string Name { get; }
        public Type Type { get; }
        public bool HasDefault { get; }
        public object DefaultValue { get; }
        internal readonly byte[] NameUtf8;
    }

    /// <summary>
    /// A registered JSON-RPC method: its parameter shape plus two compiled invokers built with expression trees.
    /// </summary>
    public sealed partial class RpcMethod
    {
        public string Name { get; private set; }
        /// <summary>Bindable parameters, in order. A trailing <c>ref JsonRpcException</c> parameter is not included.</summary>
        public RpcParameter[] Parameters { get; private set; }
        public Type ReturnType { get; private set; }
        public bool ExpectsRefException { get; private set; }
        public int DefaultCount { get; private set; }
        public StreamingInvoker Invoke { get; private set; }
        public BoxedInvoker InvokeBoxed { get; private set; }
        internal JsmnInvoker InvokeJsmn { get; private set; }
        /// <summary>The identity map (0,1,2,...) used when positional params match exactly.</summary>
        internal int[] IdentityMap { get; private set; }
        /// <summary>True when no two parameters share a JSON name (named params in declaration order can then use <see cref="IdentityMap"/>).</summary>
        internal bool HasUniqueNames { get; private set; }

        private static readonly MethodInfo ReadParamGeneric = typeof(JsonRpcRequestReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == nameof(JsonRpcRequestReader.ReadParam) && m.IsGenericMethodDefinition);

        private static readonly MethodInfo WriteGeneric = typeof(JsonRpcSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == nameof(JsonRpcSerializer.Write) && m.IsGenericMethodDefinition);

        private static readonly MethodInfo WriteNullMethod = typeof(Utf8Json).GetMethod(nameof(Utf8Json.WriteNull), new[] { typeof(IBufferWriter<byte>) });
        private static readonly MethodInfo WriteNullPooled = typeof(Utf8Json).GetMethod(nameof(Utf8Json.WriteNull), new[] { typeof(PooledByteBufferWriter) });
        private static readonly MethodInfo ReadTypedParamGeneric = typeof(JsmnRequestReader).GetMethod(nameof(JsmnRequestReader.ReadTypedParam), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ParamIsNullLiteral = typeof(JsmnRequestReader).GetMethod(nameof(JsmnRequestReader.ParamIsNullLiteral), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo WriteValuePooledGeneric = typeof(RpcMethod).GetMethod(nameof(WriteValuePooled), BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>The built-in typed writer for a non-primitive result (POCO, collection, nullable of a non-primitive).</summary>
        private static void WriteValuePooled<T>(PooledByteBufferWriter output, T value) => JsmnWriter<T>.Write(output, value);

        // primitive type -> (static reader on JsmnRequestReader, static writer on Utf8Json taking the pooled writer)
        private static readonly Dictionary<Type, (MethodInfo Read, MethodInfo Write)> Primitives = BuildPrimitiveTable();

        private static Dictionary<Type, (MethodInfo, MethodInfo)> BuildPrimitiveTable()
        {
            var reader = typeof(JsmnRequestReader);
            MethodInfo Read(string name) => reader.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo Write(string name, Type t) => typeof(Utf8Json).GetMethod(name, new[] { typeof(PooledByteBufferWriter), t });
            return new Dictionary<Type, (MethodInfo, MethodInfo)>
            {
                [typeof(string)] = (Read(nameof(JsmnRequestReader.ReadStringParam)), Write(nameof(Utf8Json.WriteString), typeof(string))),
                [typeof(int)] = (Read(nameof(JsmnRequestReader.ReadInt32Param)), Write(nameof(Utf8Json.WriteInt64), typeof(long))),
                [typeof(long)] = (Read(nameof(JsmnRequestReader.ReadInt64Param)), Write(nameof(Utf8Json.WriteInt64), typeof(long))),
                [typeof(double)] = (Read(nameof(JsmnRequestReader.ReadDoubleParam)), Write(nameof(Utf8Json.WriteDouble), typeof(double))),
                [typeof(float)] = (Read(nameof(JsmnRequestReader.ReadSingleParam)), Write(nameof(Utf8Json.WriteSingle), typeof(float))),
                [typeof(bool)] = (Read(nameof(JsmnRequestReader.ReadBooleanParam)), Write(nameof(Utf8Json.WriteBool), typeof(bool))),
                [typeof(decimal)] = (Read(nameof(JsmnRequestReader.ReadDecimalParam)), Write(nameof(Utf8Json.WriteDecimal), typeof(decimal))),
            };
        }

        /// <summary>Compatibility overload preserving the original MethodInfo registration signature.</summary>
        public static RpcMethod FromMethodInfo(string name, MethodInfo method, object target, string[] parameterNames)
        {
            return FromMethodInfo(name, method, target, parameterNames, RpcContextFlow.None);
        }

        /// <summary>Compatibility overload preserving the original delegate registration signature.</summary>
        public static RpcMethod FromDelegate(string name, Delegate implementation, string[] parameterNames, IDictionary<string, object> defaults)
        {
            return FromDelegate(name, implementation, parameterNames, defaults, RpcContextFlow.None);
        }

        /// <summary>Builds the invokers from a MethodInfo for an instance (or static) implementation; <paramref name="parameterNames"/> are the JSON names (null = CLR names).</summary>
        public static RpcMethod FromMethodInfo(string name, MethodInfo method, object target, string[] parameterNames = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            RejectAsyncReturnType(name, method);
            var ps = method.GetParameters();
            Expression instance = method.IsStatic ? null : Expression.Constant(target, method.DeclaringType);
            return Build(name, ps, method.ReturnType, parameterNames, args => Expression.Call(instance, method, args), contextFlow: contextFlow);
        }

        /// <summary>
        /// Builds the invokers from a MethodInfo for an instance implementation whose receiver is produced per invocation. Right before the
        /// implementation body runs, <paramref name="resolve"/> is called once with the RPC context of the request being
        /// served (what <see cref="Handler.RpcContext"/> returns) and must answer with an instance of
        /// <paramref name="serviceType"/>. This is the seam for container-managed lifetimes: the resolver can look
        /// the request's scope up through the context and return a scoped or transient service. Nothing is cached
        /// or disposed here. A static implementation keeps a null receiver and never resolves. A null result or another
        /// type is an <see cref="InvalidOperationException"/> naming the service type, answered as <c>-32603</c>.
        /// </summary>
        public static RpcMethod FromMethodInfo(string name, MethodInfo method, Type serviceType, Func<object, object> resolve, string[] parameterNames = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            if (serviceType.ContainsGenericParameters)
                throw new ArgumentException("JSON-RPC method '" + name + "': the service type '" + serviceType + "' is not a closed type.", nameof(serviceType));
            if (!method.DeclaringType.IsAssignableFrom(serviceType))
                throw new ArgumentException("JSON-RPC method '" + name + "' is declared by '" + method.DeclaringType + "', which '" + serviceType + "' is not.", nameof(serviceType));
            if (method.IsStatic) return FromMethodInfo(name, method, null, parameterNames, contextFlow);
            RejectAsyncReturnType(name, method);
            var ps = method.GetParameters();
            // (TService)ResolveReceiver(resolve, name): evaluated once per invocation. The arguments are read into
            // locals first, so a request the serializer refuses (-32602) never resolves a service.
            var receiver = Expression.Call(ResolveReceiverGeneric.MakeGenericMethod(serviceType), Expression.Constant(resolve), Expression.Constant(name));
            return Build(name, ps, method.ReturnType, parameterNames, args => CallAfterArguments(receiver, method, args), contextFlow: contextFlow);
        }

        private static Expression CallAfterArguments(Expression receiver, MethodInfo method, Expression[] args)
        {
            var locals = new List<ParameterExpression>();
            var body = new List<Expression>();
            var passed = new Expression[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                // a variable (ref JsonRpcException, the cancellation token) is passed through as it is
                if (args[i] is ParameterExpression) { passed[i] = args[i]; continue; }
                var local = Expression.Variable(args[i].Type, "arg" + i);
                locals.Add(local);
                body.Add(Expression.Assign(local, args[i]));
                passed[i] = local;
            }
            body.Add(Expression.Call(receiver, method, passed));
            return locals.Count == 0 ? body[0] : Expression.Block(locals, body);
        }

        private static readonly MethodInfo ResolveReceiverGeneric = typeof(RpcMethod).GetMethod(nameof(ResolveReceiver), BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary>The per-invocation receiver of a factory-bound method: the resolver gets the ambient RPC context and must answer with a <typeparamref name="T"/>.</summary>
        private static T ResolveReceiver<T>(Func<object, object> resolve, string name)
        {
            var instance = resolve(Handler.RpcContext());
            if (instance is T typed) return typed;
            throw new InvalidOperationException(instance == null
                ? "JSON-RPC method '" + name + "': the service resolver returned null instead of an instance of '" + typeof(T) + "'."
                : "JSON-RPC method '" + name + "': the service resolver returned a '" + instance.GetType() + "', not an instance of '" + typeof(T) + "'.");
        }

        /// <summary>
        /// Builds the invokers for an interface contract method dispatched to its implementation on <paramref name="target"/>.
        /// The contract supplies the parameter list, names, defaults and return type; the call is compiled against the
        /// implementation method with the receiver typed as the exact implementation type.
        /// </summary>
        internal static RpcMethod FromMappedMethod(string name, MethodInfo contractMethod, MethodInfo targetMethod,
            object target, string[] parameterNames, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            RejectAsyncReturnType(name, contractMethod);
            RejectAsyncReturnType(name, targetMethod, contractMethod.ReturnType);
            var instance = Expression.Constant(target, target.GetType());
            return Build(name, contractMethod.GetParameters(), contractMethod.ReturnType, parameterNames,
                args => Expression.Call(instance, targetMethod, args), contextFlow: contextFlow);
        }

        /// <summary>
        /// Builds the invokers for any delegate (lambda, closed instance method, ...). The parameter list is the
        /// delegate type's <c>Invoke</c> signature; the names come from <paramref name="parameterNames"/>, else from
        /// the target method when it has the same shape (a lambda's own parameter names), else <c>arg1</c>, <c>arg2</c>...
        /// </summary>
        public static RpcMethod FromDelegate(string name, Delegate implementation, string[] parameterNames = null, IDictionary<string, object> defaults = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            if (implementation.GetInvocationList().Length != 1)
            {
                throw new ArgumentException("JSON-RPC method '" + name + "': a multicast delegate cannot be registered; it has no single return value.", nameof(implementation));
            }
            var invoke = implementation.GetType().GetMethod("Invoke");
            var shape = invoke.GetParameters();
            var target = implementation.Method;
            RejectAsyncReturnType(name, target, invoke.ReturnType);

            // the target's own ParameterInfo carries names and optional-parameter defaults, but only describes the
            // delegate when it has the same shape (not for a closed static method or an extension-method delegate)
            var ps = shape;
            var targetPs = target.GetParameters();
            if (targetPs.Length == shape.Length)
            {
                bool same = true;
                for (int i = 0; i < shape.Length && same; i++) same = targetPs[i].ParameterType == shape[i].ParameterType;
                if (same) ps = targetPs;
            }
            var del = Expression.Constant(implementation);
            return Build(name, ps, invoke.ReturnType, parameterNames, args => Expression.Invoke(del, args), defaults, contextFlow);
        }

        private static void RejectAsyncReturnType(string name, MethodInfo method)
        {
            RejectAsyncReturnType(name, method, method.ReturnType);
        }

        private static void RejectAsyncReturnType(string name, MethodInfo method, Type returnType)
        {
            if (returnType == typeof(void) && method.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false))
                throw new NotSupportedException("JSON-RPC method '" + name + "' is declared async void; return Task or ValueTask instead.");
        }

        private static RpcMethod Build(string name, ParameterInfo[] ps, Type returnType, string[] parameterNames,
            Func<Expression[], Expression> makeCall, IDictionary<string, object> defaults = null, RpcContextFlow contextFlow = RpcContextFlow.None)
        {
            var shape = ClassifyReturn(name, returnType, out var resultType);
            ValidateCancellationParameters(name, ps, shape);
            if (shape == AsyncReturnShape.Sync && ps.Any(p => p.IsDefined(typeof(JsonRpcCancellationAttribute), false)))
                return BuildCancellableSync(name, ps, returnType, parameterNames, makeCall, defaults);
            if (shape != AsyncReturnShape.Sync)
                return BuildAsync(name, ps, returnType, resultType, shape, parameterNames, makeCall, defaults, contextFlow);

            var reader = Expression.Parameter(typeof(JsonRpcRequestReader), "reader");
            var map = Expression.Parameter(typeof(int[]), "map");
            var serializer = Expression.Parameter(typeof(JsonRpcSerializer), "serializer");
            var output = Expression.Parameter(typeof(IBufferWriter<byte>), "output");
            var refEx = Expression.Variable(typeof(JsonRpcException), "refException");

            bool expectsRef = ps.Length > 0 && ps[ps.Length - 1].ParameterType == typeof(JsonRpcException).MakeByRefType();
            int bindable = expectsRef ? ps.Length - 1 : ps.Length;

            // the built-in invoker binds the same parameters through direct static reads
            var jsmnReader = Expression.Parameter(typeof(JsmnRequestReader), "reader");
            var pooled = Expression.Parameter(typeof(PooledByteBufferWriter), "output");

            // The invokers carry no error handling of their own: an argument the serializer refuses throws straight
            // out, and the dispatcher then re-reads the arguments to tell a conversion failure (-32602, naming the
            // parameter) from a failure inside the method (-32603). That keeps the non-throwing path free of any
            // try region or bookkeeping store.
            var parameters = new RpcParameter[bindable];
            var args = new Expression[ps.Length];
            var jsmnArgs = new Expression[ps.Length];
            int defaultCount = 0;
            for (int i = 0; i < bindable; i++)
            {
                var p = ps[i];
                string jsonName = parameterNames != null && i < parameterNames.Length && parameterNames[i] != null ? parameterNames[i] : p.Name;
                bool hasDefault = p.IsOptional || (defaults != null && defaults.ContainsKey(jsonName));
                object defaultValue = null;
                if (defaults != null && defaults.TryGetValue(jsonName, out var dv)) defaultValue = dv;
                else if (p.IsOptional && p.DefaultValue != DBNull.Value && p.DefaultValue != Type.Missing) defaultValue = p.DefaultValue;
                if (hasDefault) defaultCount++;

                parameters[i] = new RpcParameter(jsonName, p.ParameterType, hasDefault, defaultValue);

                var index = Expression.ArrayIndex(map, Expression.Constant(i));
                var read = Expression.Call(reader, ReadParamGeneric.MakeGenericMethod(p.ParameterType), index);
                Expression fallback = MakeDefault(p.ParameterType, hasDefault, defaultValue);
                var supplied = Expression.GreaterThanOrEqual(index, Expression.Constant(0));
                args[i] = Expression.Condition(supplied, read, fallback);
                jsmnArgs[i] = Expression.Condition(supplied, MakeJsmnRead(jsmnReader, index, p.ParameterType), fallback);
            }
            if (expectsRef) args[ps.Length - 1] = jsmnArgs[ps.Length - 1] = refEx;

            var call = makeCall(args);
            var jsmnCall = makeCall(jsmnArgs);
            var throwIfRef = expectsRef
                ? (Expression)Expression.IfThen(Expression.NotEqual(refEx, Expression.Constant(null, typeof(JsonRpcException))), Expression.Throw(refEx))
                : Expression.Empty();
            var clearRef = Expression.Assign(refEx, Expression.Constant(null, typeof(JsonRpcException)));

            // streaming: result = call(); if (refEx != null) throw; serializer.Write<TRet>(output, result)
            Expression streamingBody;
            Expression boxedBody;
            Expression jsmnBody;
            if (returnType == typeof(void))
            {
                streamingBody = Expression.Block(new[] { refEx }, clearRef, call, throwIfRef, Expression.Call(WriteNullMethod, output));
                boxedBody = Expression.Block(typeof(object), new[] { refEx }, clearRef, call, throwIfRef, Expression.Constant(null, typeof(object)));
                jsmnBody = Expression.Block(new[] { refEx }, clearRef, jsmnCall, throwIfRef, Expression.Call(WriteNullPooled, pooled));
            }
            else
            {
                var result = Expression.Variable(returnType, "result");
                streamingBody = Expression.Block(new[] { refEx, result },
                    clearRef,
                    Expression.Assign(result, call),
                    throwIfRef,
                    Expression.Call(serializer, WriteGeneric.MakeGenericMethod(returnType), output, result));
                boxedBody = Expression.Block(typeof(object), new[] { refEx, result },
                    clearRef,
                    Expression.Assign(result, call),
                    throwIfRef,
                    Expression.Convert(result, typeof(object)));
                jsmnBody = Expression.Block(new[] { refEx, result },
                    clearRef,
                    Expression.Assign(result, jsmnCall),
                    throwIfRef,
                    MakeJsmnWrite(pooled, result, returnType));
            }

            var streaming = Expression.Lambda<StreamingInvoker>(streamingBody, reader, map, serializer, output).Compile();
            var boxed = Expression.Lambda<BoxedInvoker>(boxedBody, reader, map).Compile();
            var jsmnInvoker = Expression.Lambda<JsmnInvoker>(jsmnBody, jsmnReader, map, pooled).Compile();

            var identity = new int[bindable];
            for (int i = 0; i < bindable; i++) identity[i] = i;
            var names = new HashSet<string>();
            bool unique = true;
            foreach (var parameter in parameters) unique &= names.Add(parameter.Name);

            return new RpcMethod
            {
                Name = name,
                Parameters = parameters,
                ReturnType = returnType,
                ResultType = returnType,
                ExpectsRefException = expectsRef,
                DefaultCount = defaultCount,
                Invoke = streaming,
                InvokeBoxed = boxed,
                InvokeJsmn = jsmnInvoker,
                IdentityMap = identity,
                HasUniqueNames = unique
            };
        }

        /// <summary>
        /// The built-in read of parameter <paramref name="index"/> as <paramref name="type"/>: a direct static call
        /// for the primitives, a null test plus the value read for their nullables, the typed reader cache for the rest.
        /// </summary>
        private static Expression MakeJsmnRead(ParameterExpression reader, Expression index, Type type)
        {
            if (Primitives.TryGetValue(type, out var primitive)) return Expression.Call(primitive.Read, reader, index);
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null && Primitives.TryGetValue(underlying, out var inner))
            {
                return Expression.Condition(
                    Expression.Call(ParamIsNullLiteral, reader, index),
                    Expression.Constant(null, type),
                    Expression.Convert(Expression.Call(inner.Read, reader, index), type));
            }
            return Expression.Call(ReadTypedParamGeneric.MakeGenericMethod(type), reader, index);
        }

        /// <summary>The built-in write of <paramref name="result"/>: the concrete-writer formatter for primitives (and their nullables), the typed writer cache otherwise.</summary>
        private static Expression MakeJsmnWrite(ParameterExpression output, ParameterExpression result, Type type)
        {
            if (Primitives.TryGetValue(type, out var primitive))
            {
                Expression value = result;
                if (type == typeof(int)) value = Expression.Convert(result, typeof(long));
                return Expression.Call(primitive.Write, output, value);
            }
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null && Primitives.TryGetValue(underlying, out var inner))
            {
                Expression value = Expression.Property(result, "Value");
                if (underlying == typeof(int)) value = Expression.Convert(value, typeof(long));
                return Expression.IfThenElse(
                    Expression.Property(result, "HasValue"),
                    Expression.Call(inner.Write, output, value),
                    Expression.Call(WriteNullPooled, output));
            }
            return Expression.Call(WriteValuePooledGeneric.MakeGenericMethod(type), output, result);
        }

        private static Expression MakeDefault(Type type, bool hasDefault, object value)
        {
            if (!hasDefault || value == null) return Expression.Default(type);
            if (type.IsInstanceOfType(value)) return Expression.Constant(value, type);
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            object converted;
            try
            {
                converted = underlying.IsEnum ? Enum.ToObject(underlying, value) : Convert.ChangeType(value, underlying, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return Expression.Default(type);
            }
            return Expression.Convert(Expression.Constant(converted, underlying), type);
        }
    }
}
