using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc.Invocation;
using AustinHarris.JsonRpc.Jsmn;
using AustinHarris.JsonRpc.Serialization;

namespace AustinHarris.JsonRpc
{
    public sealed partial class Handler
    {
        [ThreadStatic] private static InvocationState __unflowedState;

        private static class AsyncAmbient
        {
            internal static readonly AsyncLocal<InvocationState> Current = new AsyncLocal<InvocationState>(Changed);

            private static void Changed(AsyncLocalValueChangedArgs<InvocationState> change)
            {
                if (change.PreviousValue == null && change.CurrentValue != null) __unflowedState = __state;
                __state = change.CurrentValue ?? __unflowedState;
                if (change.CurrentValue == null) __unflowedState = null;
            }
        }

        // Only the async dispatcher uses this scope. The thread frame is restored before returning
        // an incomplete operation; a flowing frame is owned by that operation until terminal cleanup.
        // A None scope is always disposed on the thread that created it. A Flow scope may be disposed on the
        // thread that completed the method (HandleBoxedAsync awaits with a live scope), so Dispose never
        // writes the captured thread frame back: restoring the ambient value brings back the frame the
        // current thread had before the flowing one arrived, whichever thread that is.
        private readonly struct AsyncScope : IDisposable
        {
            internal readonly InvocationState Frame;
            internal readonly InvocationState FlowFrame;
            private readonly InvocationState _parent;
            private readonly InvocationState _thread;
            private readonly object _context;
            private readonly JsonRpcException _exception;
            private readonly JsonRpcRequestReader _reader;

            internal AsyncScope(object context, JsonRpcRequestReader reader, bool flow)
            {
                _parent = AsyncAmbient.Current.Value;
                _thread = __state;
                if (flow)
                {
                    Frame = FlowFrame = new InvocationState { Context = context, Reader = reader };
                    _context = null; _exception = null; _reader = null;
                    AsyncAmbient.Current.Value = Frame;
                }
                else
                {
                    // Suppress an enclosing flowing invocation before the method captures its context.
                    if (_parent != null) AsyncAmbient.Current.Value = null;
                    Frame = State;
                    FlowFrame = null;
                    _context = Frame.Context; _exception = Frame.Exception; _reader = Frame.Reader;
                    Frame.Context = context; Frame.Exception = null; Frame.Reader = reader;
                }
            }

            public void Dispose()
            {
                if (AsyncAmbient.Current.Value != _parent) AsyncAmbient.Current.Value = _parent;
                if (FlowFrame != null) return;
                Frame.Context = _context; Frame.Exception = _exception; Frame.Reader = _reader;
                // A None scope may have created the reusable thread frame; keep it when there was no parent.
                if (_thread != null) __state = _thread;
            }
        }

        private static void ClearAsyncFrame(InvocationState frame)
        {
            if (frame == null) return;
            frame.Context = null;
            frame.Reader = null;
            frame.Exception = null;
        }

        internal ValueTask<bool> HandleRequestAsync(JsonRpcRequestReader reader, int index, JsonRpcSerializer serializer,
            PooledByteBufferWriter output, object context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int envelopeStart = output.WrittenCount;
            if (!reader.Select(index))
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Request must be an object.")), default);
                return new ValueTask<bool>(true);
            }

            var idKind = reader.IdKind;
            if (idKind == JsonRpcIdKind.Invalid)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Id property must be either null or string or integer.")), default);
                return new ValueTask<bool>(true);
            }
            var policy = VersionPolicy ?? Config.VersionPolicy;
            if (policy != JsonRpcVersionPolicy.Ignore)
            {
                var version = reader.VersionKind;
                if (version == JsonRpcVersionKind.Other)
                {
                    WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "The 'jsonrpc' member must be \"2.0\".")), reader.IdRaw);
                    return new ValueTask<bool>(true);
                }
                if (version == JsonRpcVersionKind.Absent && policy == JsonRpcVersionPolicy.Strict)
                {
                    WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Missing property 'jsonrpc'")), reader.IdRaw);
                    return new ValueTask<bool>(true);
                }
            }
            if (!reader.HasMethod)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'")), reader.IdRaw);
                return new ValueTask<bool>(true);
            }
            if (reader.ParamsKind == JsonRpcParamsKind.Invalid)
            {
                WriteErrorEnvelope(output, serializer, ProcessParseException(reader, new JsonRpcException(-32600, "Invalid Request", "The 'params' member must be an array or an object.")), reader.IdRaw);
                return new ValueTask<bool>(true);
            }

            // From here on the request object is valid: without an id it is a notification and gets no response.
            bool notification = idKind == JsonRpcIdKind.Absent;


            if (externalPreProcessingHandler != null || externalPostProcessingHandler != null)
                return HandleHookRequestAsync(reader, serializer, output, context, cancellationToken, notification, envelopeStart);

            var service = Resolve(reader);
            if (service == null)
            {
                if (notification && externalErrorHandler == null) return new ValueTask<bool>(false);
                var notFound = ProcessException(reader, MethodNotFound(reader.Method));
                if (notification) return new ValueTask<bool>(false);
                WriteErrorEnvelope(output, serializer, notFound, reader.IdRaw);
                return new ValueTask<bool>(true);
            }
            var method = service.Method;
            var map = BindMap(reader, method, out var bindError);
            if (bindError != null)
            {
                bindError = ProcessException(reader, bindError);
                if (notification) return new ValueTask<bool>(false);
                WriteErrorEnvelope(output, serializer, bindError, reader.IdRaw);
                return new ValueTask<bool>(true);
            }

            var scope = new AsyncScope(context, reader, method.IsAsync && method.ContextFlow == RpcContextFlow.Flow);
            bool transferred = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                output.Write(ResultPrefix);
                Task operation;
                if (method.IsAsync || method.HasCancellation)
                    operation = reader is JsmnRequestReader jsmn && jsmn.IsBuiltIn
                        ? method.InvokeJsmnAsync(jsmn, map, output, cancellationToken)
                        : method.InvokeAsync(reader, map, serializer, output, cancellationToken);
                else
                {
                    if (reader is JsmnRequestReader syncJsmn && syncJsmn.IsBuiltIn) method.InvokeJsmn(syncJsmn, map, output);
                    else method.Invoke(reader, map, serializer, output);
                    operation = Task.CompletedTask;
                }
                if (!operation.IsCompleted)
                {
                    var pending = AwaitStreaming(operation, reader, method, map, serializer, output, scope.FlowFrame,
                        scope.Frame.Exception, notification, envelopeStart);
                    transferred = true;
                    return pending;
                }
                operation.GetAwaiter().GetResult();
                return new ValueTask<bool>(FinishStreaming(reader, serializer, output, scope.Frame.Exception, notification, envelopeStart));
            }
            catch (Exception ex)
            {
                return new ValueTask<bool>(StreamingFailure(reader, method, map, serializer, output, ex, notification, envelopeStart));
            }
            finally
            {
                scope.Dispose();
                if (!transferred) { ClearAsyncFrame(scope.FlowFrame); ReturnMap(method, map); }
            }
        }

        private async ValueTask<bool> AwaitStreaming(Task operation, JsonRpcRequestReader reader, RpcMethod method, int[] map,
            JsonRpcSerializer serializer, PooledByteBufferWriter output, InvocationState frame, JsonRpcException initialError,
            bool notification, int envelopeStart)
        {
            try
            {
                await operation.ConfigureAwait(false);
                return FinishStreaming(reader, serializer, output, frame != null ? frame.Exception : initialError, notification, envelopeStart);
            }
            catch (Exception ex)
            {
                return StreamingFailure(reader, method, map, serializer, output, ex, notification, envelopeStart);
            }
            finally
            {
                ClearAsyncFrame(frame);
                ReturnMap(method, map);
            }
        }

        private bool FinishStreaming(JsonRpcRequestReader reader, JsonRpcSerializer serializer, PooledByteBufferWriter output,
            JsonRpcException error, bool notification, int envelopeStart)
        {
            if (error != null)
            {
                output.Rewind(envelopeStart);
                error = ProcessException(reader, error);
                if (notification) return false;
                WriteErrorEnvelope(output, serializer, error, reader.IdRaw);
            }
            else if (notification)
            {
                output.Rewind(envelopeStart);
                return false;
            }
            else
            {
                output.Write(IdInfix);
                WriteIdRaw(output, reader.IdRaw);
                output.Write((byte)'}');
            }
            return true;
        }

        private bool StreamingFailure(JsonRpcRequestReader reader, RpcMethod method, int[] map, JsonRpcSerializer serializer,
            PooledByteBufferWriter output, Exception ex, bool notification, int envelopeStart)
        {
            output.Rewind(envelopeStart);
            ex = UnwrapAsyncException(ex);
            var error = BindingFailure(reader, method, map, ex);
            error = error != null ? ProcessException(reader, error) : MapAsyncException(reader, ex);
            if (notification) return false;
            WriteErrorEnvelope(output, serializer, error, reader.IdRaw);
            return true;
        }

        private static Exception UnwrapAsyncException(Exception ex)
        {
            while (true)
            {
                if (ex is TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
                else if (ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1) ex = aggregate.InnerExceptions[0];
                else return ex;
            }
        }

        private JsonRpcException MapAsyncException(JsonRpcRequestReader reader, Exception ex)
        {
            ex = UnwrapAsyncException(ex);
            return ex is AggregateException
                ? ProcessException(reader, new JsonRpcException(-32603, "Internal Error", ex)) : MapException(reader, ex);
        }

        private JsonRpcException MapAsyncException(JsonRequest request, Exception ex)
        {
            ex = UnwrapAsyncException(ex);
            return ex is AggregateException
                ? ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex)) : MapException(request, ex);
        }

        private async ValueTask<bool> HandleHookRequestAsync(JsonRpcRequestReader reader, JsonRpcSerializer serializer,
            PooledByteBufferWriter output, object context, CancellationToken token, bool notification, int envelopeStart)
        {
            object originalId = reader.IdValue;
            var response = await HandleBoxedAsync(reader, serializer, context, token).ConfigureAwait(false);
            if (notification) return false;
            if (Equals(response.Id, originalId)) WriteResponse(output, serializer, response, reader.IdRaw, envelopeStart);
            else
            {
                using (var idBuffer = new PooledByteBufferWriter(64))
                {
                    WriteIdValue(idBuffer, serializer, response.Id);
                    WriteResponse(output, serializer, response, idBuffer.WrittenSpan, envelopeStart);
                }
            }
            return true;
        }

        private async ValueTask<JsonResponse> HandleBoxedAsync(JsonRpcRequestReader reader, JsonRpcSerializer serializer, object context, CancellationToken token)
        {
            // This async boundary restores its execution context when it returns to its caller, including
            // when it suspends. The frame itself remains owned until all hooks have finished.
            var scope = new AsyncScope(context, reader, true);
            try
            {
                string method = reader.Method;
                object id = reader.IdValue;
                object parameters;
                try { parameters = reader.ParamsValue; }
                catch (Exception ex)
                {
                    var failed = new JsonRequest(method, null, id);
                    return PostProcess(failed, new JsonResponse { Error = MapAsyncException(failed, ex), Id = id }, context);
                }
                var request = new JsonRequest(method, parameters, id);
                JsonRpcException preError;
                try { preError = PreProcess(request, context); }
                catch (Exception ex) { preError = ProcessException(request, new JsonRpcException(-32603, "Internal Error", ex)); }
                if (preError != null) return PostProcess(request, new JsonResponse { Error = preError, Id = request.Id }, context);
                JsonResponse response;
                if (request.Method == method && ReferenceEquals(request.Params, parameters) && Equals(request.Id, id))
                    response = await InvokeBoxedAsync(reader, request, context, token, scope.Frame).ConfigureAwait(false);
                else response = await InvokeModifiedAsync(request, serializer, context, token, scope.Frame).ConfigureAwait(false);
                return PostProcess(request, response, context);
            }
            catch (Exception ex)
            {
                return new JsonResponse { Error = new JsonRpcException(-32603, "Internal Error", ex), Id = SafeIdValue(reader) };
            }
            finally { scope.Dispose(); ClearAsyncFrame(scope.FlowFrame); }
        }

        private async ValueTask<JsonResponse> InvokeModifiedAsync(JsonRequest request, JsonRpcSerializer serializer, object context, CancellationToken token, InvocationState hookFrame)
        {
            JsonRpcRequestReader reader = null;
            using (var buffer = new PooledByteBufferWriter(512))
            {
                try
                {
                    serializer.Write(buffer, request, typeof(JsonRequest));
                    reader = serializer.CreateReader();
                    string message = null;
                    if (!reader.TryParse(buffer.WrittenMemory, out var error) || !reader.Select(0)) message = error ?? "Request must be an object.";
                    else if (!reader.HasMethod) message = "Missing property 'method'";
                    else if (reader.IdKind == JsonRpcIdKind.Invalid) message = "Id property must be either null or string or integer.";
                    else if (reader.ParamsKind == JsonRpcParamsKind.Invalid) message = "The 'params' member must be an array or an object.";
                    if (message != null) return new JsonResponse { Error = ProcessException(request, new JsonRpcException(-32600, "Invalid Request", message)), Id = request.Id };
                    return await InvokeBoxedAsync(reader, request, context, token, hookFrame).ConfigureAwait(false);
                }
                catch (Exception ex) { return new JsonResponse { Error = MapAsyncException(request, ex), Id = request.Id }; }
                finally { reader?.Release(); }
            }
        }

        private ValueTask<JsonResponse> InvokeBoxedAsync(JsonRpcRequestReader reader, JsonRequest request, object context, CancellationToken token, InvocationState hookFrame)
        {
            var service = Resolve(reader);
            if (service == null) return new ValueTask<JsonResponse>(new JsonResponse { Error = ProcessException(request, MethodNotFound(reader.Method)), Id = request.Id });
            var method = service.Method;
            var map = BindMap(reader, method, out var error);
            if (error != null) return new ValueTask<JsonResponse>(new JsonResponse { Error = ProcessException(request, error), Id = request.Id });
            var scope = new AsyncScope(context, reader, method.IsAsync && method.ContextFlow == RpcContextFlow.Flow);
            scope.Frame.Exception = hookFrame.Exception;
            hookFrame.Exception = null;
            bool transferred = false;
            try
            {
                token.ThrowIfCancellationRequested();
                var operation = method.IsAsync || method.HasCancellation ? method.InvokeBoxedAsync(reader, map, token) : new ValueTask<object>(method.InvokeBoxed(reader, map));
                if (!operation.IsCompleted)
                {
                    var pending = AwaitBoxedInvocation(operation, reader, request, method, map, scope.FlowFrame, scope.Frame.Exception);
                    transferred = true;
                    return pending;
                }
                var result = operation.GetAwaiter().GetResult();
                return new ValueTask<JsonResponse>(BoxedResponse(request, result, scope.Frame.Exception));
            }
            catch (Exception ex) { return new ValueTask<JsonResponse>(BoxedFailure(reader, request, method, map, ex)); }
            finally
            {
                scope.Dispose();
                if (!transferred) { ClearAsyncFrame(scope.FlowFrame); ReturnMap(method, map); }
            }
        }

        private async ValueTask<JsonResponse> AwaitBoxedInvocation(ValueTask<object> operation, JsonRpcRequestReader reader, JsonRequest request,
            RpcMethod method, int[] map, InvocationState frame, JsonRpcException initialError)
        {
            try
            {
                var result = await operation.ConfigureAwait(false);
                return BoxedResponse(request, result, frame != null ? frame.Exception : initialError);
            }
            catch (Exception ex) { return BoxedFailure(reader, request, method, map, ex); }
            finally { ClearAsyncFrame(frame); ReturnMap(method, map); }
        }

        private JsonResponse BoxedResponse(JsonRequest request, object result, JsonRpcException error) => error != null
            ? new JsonResponse { Error = ProcessException(request, error), Id = request.Id }
            : new JsonResponse { Result = result, Id = request.Id };

        private JsonResponse BoxedFailure(JsonRpcRequestReader reader, JsonRequest request, RpcMethod method, int[] map, Exception ex)
        {
            ex = UnwrapAsyncException(ex);
            var error = BindingFailure(reader, method, map, ex);
            return new JsonResponse { Error = error != null ? ProcessException(request, error) : MapAsyncException(request, ex), Id = request.Id };
        }
    }
}
