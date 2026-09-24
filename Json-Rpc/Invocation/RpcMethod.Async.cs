using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc.Invocation
{
    internal delegate Task AsyncJsmnInvoker(JsmnRequestReader reader, int[] map, PooledByteBufferWriter output, CancellationToken cancellationToken);
    internal delegate Task AsyncStreamingInvoker(JsonRpcRequestReader reader, int[] map, JsonRpcSerializer serializer, IBufferWriter<byte> output, CancellationToken cancellationToken);
    internal delegate ValueTask<object> AsyncBoxedInvoker(JsonRpcRequestReader reader, int[] map, CancellationToken cancellationToken);

    public sealed partial class RpcMethod
    {
        /// <summary>The eventual result type, or void for a non-generic Task or ValueTask.</summary>
        public Type ResultType { get; private set; }
        /// <summary>Whether this registration requires asynchronous processing.</summary>
        public bool IsAsync { get; private set; }
        /// <summary>The ambient context policy selected at registration.</summary>
        public RpcContextFlow ContextFlow { get; private set; }
        internal bool HasCancellation { get; private set; }
        internal AsyncJsmnInvoker InvokeJsmnAsync { get; private set; }
        internal AsyncStreamingInvoker InvokeAsync { get; private set; }
        internal AsyncBoxedInvoker InvokeBoxedAsync { get; private set; }

        private enum AsyncReturnShape { Sync, Task, TaskResult, ValueTask, ValueTaskResult }

        private static AsyncReturnShape ClassifyReturn(string name, Type type, out Type resultType)
        {
            resultType = type;
            if (type == typeof(Task)) { resultType = typeof(void); return AsyncReturnShape.Task; }
            if (type == typeof(ValueTask)) { resultType = typeof(void); return AsyncReturnShape.ValueTask; }
            if (type.IsGenericType)
            {
                var definition = type.GetGenericTypeDefinition();
                if (definition == typeof(Task<>) || definition == typeof(ValueTask<>))
                {
                    resultType = type.GetGenericArguments()[0];
                    // Nested awaitables and async streams are not JSON result values.
                    if (ClassifyReturn(name, resultType, out _) != AsyncReturnShape.Sync)
                        throw new NotSupportedException("JSON-RPC method '" + name + "' returns a nested awaitable.");
                    return definition == typeof(Task<>) ? AsyncReturnShape.TaskResult : AsyncReturnShape.ValueTaskResult;
                }
            }
            if (typeof(Task).IsAssignableFrom(type) || type.GetMethod("GetAwaiter", Type.EmptyTypes) != null ||
                type.IsByRef || type.IsPointer || type.ContainsGenericParameters ||
                type.GetInterfaces().Concat(new[] { type }).Any(t => t.IsGenericType && t.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IAsyncEnumerable`1"))
                throw new NotSupportedException("JSON-RPC method '" + name + "' has unsupported return type " + type + "; use Task, Task<T>, ValueTask or ValueTask<T>.");
            return AsyncReturnShape.Sync;
        }

        private static void ValidateCancellationParameters(string name, ParameterInfo[] parameters, AsyncReturnShape shape)
        {
            foreach (var p in parameters)
            {
                bool marked = p.IsDefined(typeof(JsonRpcCancellationAttribute), false);
                if (p.ParameterType == typeof(CancellationToken) && !marked)
                    throw new NotSupportedException("JSON-RPC method '" + name + "': CancellationToken parameter '" + p.Name + "' requires [JsonRpcCancellation].");
                if (marked && p.ParameterType != typeof(CancellationToken))
                    throw new NotSupportedException("[JsonRpcCancellation] requires a CancellationToken parameter.");
                if (shape != AsyncReturnShape.Sync && p.ParameterType.IsByRef)
                    throw new NotSupportedException("JSON-RPC method '" + name + "': asynchronous registrations cannot have by-ref parameters, including ref JsonRpcException.");
            }
        }

        private static RpcMethod BuildCancellableSync(string name, ParameterInfo[] ps, Type returnType,
            string[] parameterNames, Func<Expression[], Expression> makeCall, IDictionary<string, object> defaults)
        {
            var wire = ps.Where(p => !p.IsDefined(typeof(JsonRpcCancellationAttribute), false)).ToArray();
            var names = parameterNames == null ? null : ps.Select((p, i) => new { p, i })
                .Where(v => !v.p.IsDefined(typeof(JsonRpcCancellationAttribute), false))
                .Select(v => v.i < parameterNames.Length ? parameterNames[v.i] : null).ToArray();
            var sync = Build(name, wire, returnType, names, args =>
            {
                int index = 0;
                return makeCall(ps.Select(p => p.IsDefined(typeof(JsonRpcCancellationAttribute), false)
                    ? (Expression)Expression.Default(typeof(CancellationToken)) : args[index++]).ToArray());
            }, defaults);
            var plan = BuildAsync(name, ps, returnType, returnType, AsyncReturnShape.Sync, parameterNames, makeCall, defaults, RpcContextFlow.None);
            plan.IsAsync = false;
            plan.Invoke = sync.Invoke;
            plan.InvokeBoxed = sync.InvokeBoxed;
            plan.InvokeJsmn = sync.InvokeJsmn;
            plan.ExpectsRefException = sync.ExpectsRefException;
            return plan;
        }

        private static RpcMethod BuildAsync(string name, ParameterInfo[] ps, Type returnType, Type resultType,
            AsyncReturnShape shape, string[] parameterNames, Func<Expression[], Expression> makeCall,
            IDictionary<string, object> defaults, RpcContextFlow contextFlow)
        {
            if (contextFlow != RpcContextFlow.Flow && contextFlow != RpcContextFlow.None)
                throw new ArgumentOutOfRangeException(nameof(contextFlow));
            var reader = Expression.Parameter(typeof(JsonRpcRequestReader), "reader");
            var jsmn = Expression.Parameter(typeof(JsmnRequestReader), "reader");
            var map = Expression.Parameter(typeof(int[]), "map");
            var serializer = Expression.Parameter(typeof(JsonRpcSerializer), "serializer");
            var output = Expression.Parameter(typeof(IBufferWriter<byte>), "output");
            var pooled = Expression.Parameter(typeof(PooledByteBufferWriter), "output");
            var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
            var args = new Expression[ps.Length];
            var jsmnArgs = new Expression[ps.Length];
            var parameters = new List<RpcParameter>();
            bool expectsRef = shape == AsyncReturnShape.Sync && ps.Length > 0 && ps[ps.Length - 1].ParameterType == typeof(JsonRpcException).MakeByRefType();
            var refError = Expression.Variable(typeof(JsonRpcException), "refError");
            for (int i = 0; i < ps.Length; i++)
            {
                var p = ps[i];
                if (expectsRef && i == ps.Length - 1) { args[i] = jsmnArgs[i] = refError; continue; }
                if (p.IsDefined(typeof(JsonRpcCancellationAttribute), false))
                {
                    args[i] = jsmnArgs[i] = token;
                    continue;
                }
                string jsonName = parameterNames != null && i < parameterNames.Length && parameterNames[i] != null ? parameterNames[i] : p.Name;
                bool hasDefault = p.IsOptional || (defaults != null && defaults.ContainsKey(jsonName));
                object defaultValue = null;
                if (defaults != null && defaults.TryGetValue(jsonName, out var dv)) defaultValue = dv;
                else if (p.IsOptional && p.DefaultValue != DBNull.Value && p.DefaultValue != Type.Missing) defaultValue = p.DefaultValue;
                var index = Expression.ArrayIndex(map, Expression.Constant(parameters.Count));
                parameters.Add(new RpcParameter(jsonName, p.ParameterType, hasDefault, defaultValue));
                var supplied = Expression.GreaterThanOrEqual(index, Expression.Constant(0));
                var fallback = MakeDefault(p.ParameterType, hasDefault, defaultValue);
                args[i] = Expression.Condition(supplied, Expression.Call(reader, ReadParamGeneric.MakeGenericMethod(p.ParameterType), index), fallback);
                jsmnArgs[i] = Expression.Condition(supplied, MakeJsmnRead(jsmn, index, p.ParameterType), fallback);
            }
            Expression MakeCall(Expression[] arguments)
            {
                var call = makeCall(arguments);
                if (!expectsRef) return call;
                var throwIfError = Expression.IfThen(Expression.NotEqual(refError, Expression.Constant(null, typeof(JsonRpcException))), Expression.Throw(refError));
                if (returnType == typeof(void)) return Expression.Block(new[] { refError }, call, throwIfError);
                var result = Expression.Variable(returnType, "result");
                return Expression.Block(new[] { refError, result }, Expression.Assign(result, call), throwIfError, result);
            }
            return new RpcMethod
            {
                Name = name,
                Parameters = parameters.ToArray(),
                ReturnType = returnType,
                ResultType = resultType,
                IsAsync = true,
                HasCancellation = ps.Any(p => p.IsDefined(typeof(JsonRpcCancellationAttribute), false)),
                ContextFlow = contextFlow,
                DefaultCount = parameters.Count(p => p.HasDefault),
                IdentityMap = Enumerable.Range(0, parameters.Count).ToArray(),
                HasUniqueNames = parameters.Select(p => p.Name).Distinct().Count() == parameters.Count,
                Invoke = (r, m, s, w) => throw SynchronousAsyncError(name, r),
                InvokeBoxed = (r, m) => throw SynchronousAsyncError(name, r),
                InvokeJsmn = (r, m, w) => throw SynchronousAsyncError(name, r),
                InvokeAsync = Expression.Lambda<AsyncStreamingInvoker>(MakeAsyncBody(name, MakeCall(args), returnType, resultType, shape, serializer, output, false), reader, map, serializer, output, token).Compile(),
                InvokeJsmnAsync = Expression.Lambda<AsyncJsmnInvoker>(MakeAsyncBody(name, MakeCall(jsmnArgs), returnType, resultType, shape, null, pooled, false), jsmn, map, pooled, token).Compile(),
                InvokeBoxedAsync = Expression.Lambda<AsyncBoxedInvoker>(MakeAsyncBody(name, MakeCall(args), returnType, resultType, shape, null, null, true), reader, map, token).Compile()
            };
        }

        private static Expression MakeAsyncBody(string name, Expression call, Type returnType, Type resultType,
            AsyncReturnShape shape, ParameterExpression serializer, ParameterExpression output, bool boxed)
        {
            if (shape == AsyncReturnShape.Sync)
            {
                if (boxed)
                {
                    var ctor = typeof(ValueTask<object>).GetConstructor(new[] { typeof(object) });
                    return returnType == typeof(void) ? Expression.Block(call, Expression.New(ctor, Expression.Constant(null, typeof(object))))
                        : (Expression)Expression.New(ctor, Expression.Convert(call, typeof(object)));
                }
                if (returnType == typeof(void))
                    return Expression.Block(call, Expression.Call(serializer != null ? WriteNullMethod : WriteNullPooled, output), Expression.Constant(Task.CompletedTask));
                var value = Expression.Variable(returnType, "value");
                return Expression.Block(new[] { value }, Expression.Assign(value, call),
                    serializer != null ? (Expression)Expression.Call(serializer, WriteGeneric.MakeGenericMethod(returnType), output, value) : MakeJsmnWrite(output, value, returnType),
                    Expression.Constant(Task.CompletedTask));
            }
            var operation = Expression.Variable(returnType, "operation");
            bool hasResult = resultType != typeof(void);
            bool isTask = shape == AsyncReturnShape.Task || shape == AsyncReturnShape.TaskResult;
            var awaiter = Expression.Call(operation, returnType.GetMethod("GetAwaiter", Type.EmptyTypes));
            var getResult = Expression.Call(awaiter, awaiter.Type.GetMethod("GetResult", Type.EmptyTypes));
            var valueTaskType = hasResult ? typeof(ValueTask<>).MakeGenericType(resultType) : typeof(ValueTask);
            Expression slowOperation = isTask ? Expression.New(valueTaskType.GetConstructor(new[] { returnType }), operation) : (Expression)operation;
            Expression completed;
            Expression slow;
            if (boxed)
            {
                var ctor = typeof(ValueTask<object>).GetConstructor(new[] { typeof(object) });
                completed = hasResult ? (Expression)Expression.New(ctor, Expression.Convert(getResult, typeof(object)))
                    : Expression.Block(getResult, Expression.New(ctor, Expression.Constant(null, typeof(object))));
                slow = Expression.Call(AsyncHelper(hasResult ? nameof(AwaitBoxed) : nameof(AwaitVoidBoxed), hasResult ? resultType : null), slowOperation);
            }
            else
            {
                var done = Expression.Constant(Task.CompletedTask, typeof(Task));
                if (hasResult)
                {
                    var result = Expression.Variable(resultType, "result");
                    var write = serializer != null ? (Expression)Expression.Call(serializer, WriteGeneric.MakeGenericMethod(resultType), output, result) : MakeJsmnWrite(output, result, resultType);
                    completed = Expression.Block(new[] { result }, Expression.Assign(result, getResult), write, done);
                    slow = serializer != null
                        ? Expression.Call(AsyncHelper(nameof(AwaitAndWrite), resultType), slowOperation, serializer, output)
                        : Expression.Call(JsmnAsyncHelper(resultType), slowOperation, output);
                }
                else
                {
                    completed = Expression.Block(getResult, Expression.Call(serializer != null ? WriteNullMethod : WriteNullPooled, output), done);
                    slow = Expression.Call(AsyncHelper(serializer != null ? nameof(AwaitVoidAndWrite) : nameof(AwaitVoidAndWritePooled), null), slowOperation, output);
                }
            }
            var expressions = new List<Expression> { Expression.Assign(operation, call) };
            if (isTask)
                expressions.Add(Expression.IfThen(Expression.Equal(operation, Expression.Constant(null, returnType)),
                    Expression.Throw(Expression.Call(AsyncHelper(nameof(NullTaskReturned), null), Expression.Constant(name)))));
            expressions.Add(Expression.Condition(Expression.Property(operation, "IsCompleted"), completed, slow));
            return Expression.Block(new[] { operation }, expressions);
        }

        private static MethodInfo AsyncHelper(string name, Type type)
        {
            var method = typeof(RpcMethod).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            return type == null ? method : method.MakeGenericMethod(type);
        }

        private static JsonRpcException SynchronousAsyncError(string name, JsonRpcRequestReader reader) => new JsonRpcException(-32603,
            "Method '" + (string.IsNullOrEmpty(name) ? reader.Method : name) + "' is asynchronous; process the request with JsonRpcProcessor.ProcessAsync", null);

        private static JsonRpcException NullTaskReturned(string name) => new JsonRpcException(-32603, "Method '" + name + "' returned a null Task.", null);

        private static MethodInfo JsmnAsyncHelper(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type);
            var primitive = underlying ?? type;
            if (Primitives.ContainsKey(primitive))
                return AsyncHelper("Await" + primitive.Name + (underlying != null ? "Nullable" : "") + "AndWrite", null);
            return AsyncHelper(nameof(AwaitAndWritePooled), type);
        }

        private static async Task AwaitAndWrite<T>(ValueTask<T> operation, JsonRpcSerializer serializer, IBufferWriter<byte> output)
        {
            T result = await operation.ConfigureAwait(false);
            serializer.Write(output, result);
        }

        private static async Task AwaitAndWritePooled<T>(ValueTask<T> operation, PooledByteBufferWriter output)
        {
            T result = await operation.ConfigureAwait(false);
            JsmnWriter<T>.Write(output, result);
        }

        private static async ValueTask<object> AwaitBoxed<T>(ValueTask<T> operation) => await operation.ConfigureAwait(false);
        private static async ValueTask<object> AwaitVoidBoxed(ValueTask operation) { await operation.ConfigureAwait(false); return null; }
        private static async Task AwaitVoidAndWrite(ValueTask operation, IBufferWriter<byte> output) { await operation.ConfigureAwait(false); Utf8Json.WriteNull(output); }
        private static async Task AwaitVoidAndWritePooled(ValueTask operation, PooledByteBufferWriter output) { await operation.ConfigureAwait(false); Utf8Json.WriteNull(output); }

        private static async Task AwaitStringAndWrite(ValueTask<string> operation, PooledByteBufferWriter output)
        {
            string result = await operation.ConfigureAwait(false);
            Utf8Json.WriteString(output, result);
        }

        private static async Task AwaitInt32AndWrite(ValueTask<int> operation, PooledByteBufferWriter output)
        {
            int result = await operation.ConfigureAwait(false);
            Utf8Json.WriteInt64(output, result);
        }

        private static async Task AwaitInt32NullableAndWrite(ValueTask<int?> operation, PooledByteBufferWriter output)
        {
            int? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteInt64(output, result.Value); else Utf8Json.WriteNull(output);
        }

        private static async Task AwaitInt64AndWrite(ValueTask<long> operation, PooledByteBufferWriter output)
        {
            long result = await operation.ConfigureAwait(false);
            Utf8Json.WriteInt64(output, result);
        }

        private static async Task AwaitInt64NullableAndWrite(ValueTask<long?> operation, PooledByteBufferWriter output)
        {
            long? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteInt64(output, result.Value); else Utf8Json.WriteNull(output);
        }

        private static async Task AwaitDoubleAndWrite(ValueTask<double> operation, PooledByteBufferWriter output)
        {
            double result = await operation.ConfigureAwait(false);
            Utf8Json.WriteDouble(output, result);
        }

        private static async Task AwaitDoubleNullableAndWrite(ValueTask<double?> operation, PooledByteBufferWriter output)
        {
            double? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteDouble(output, result.Value); else Utf8Json.WriteNull(output);
        }

        private static async Task AwaitSingleAndWrite(ValueTask<float> operation, PooledByteBufferWriter output)
        {
            float result = await operation.ConfigureAwait(false);
            Utf8Json.WriteSingle(output, result);
        }

        private static async Task AwaitSingleNullableAndWrite(ValueTask<float?> operation, PooledByteBufferWriter output)
        {
            float? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteSingle(output, result.Value); else Utf8Json.WriteNull(output);
        }

        private static async Task AwaitBooleanAndWrite(ValueTask<bool> operation, PooledByteBufferWriter output)
        {
            bool result = await operation.ConfigureAwait(false);
            Utf8Json.WriteBool(output, result);
        }

        private static async Task AwaitBooleanNullableAndWrite(ValueTask<bool?> operation, PooledByteBufferWriter output)
        {
            bool? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteBool(output, result.Value); else Utf8Json.WriteNull(output);
        }

        private static async Task AwaitDecimalAndWrite(ValueTask<decimal> operation, PooledByteBufferWriter output)
        {
            decimal result = await operation.ConfigureAwait(false);
            Utf8Json.WriteDecimal(output, result);
        }

        private static async Task AwaitDecimalNullableAndWrite(ValueTask<decimal?> operation, PooledByteBufferWriter output)
        {
            decimal? result = await operation.ConfigureAwait(false);
            if (result.HasValue) Utf8Json.WriteDecimal(output, result.Value); else Utf8Json.WriteNull(output);
        }
    }
}
