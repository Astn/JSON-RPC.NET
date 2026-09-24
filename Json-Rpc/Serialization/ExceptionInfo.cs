using System;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// The serializer-neutral shape used when an <see cref="Exception"/> is placed in a JSON-RPC error's
    /// <c>data</c> member. Every serializer emits the same members in this order.
    /// </summary>
    public sealed class ExceptionInfo
    {
        public string ClassName { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public string StackTraceString { get; set; }
        public int HResult { get; set; }
        public ExceptionInfo InnerException { get; set; }

        /// <summary>The full description of <paramref name="ex"/>, diagnostics included.</summary>
        public static ExceptionInfo From(Exception ex)
        {
            return From(ex, true);
        }

        /// <summary>
        /// Describes <paramref name="ex"/>. With <paramref name="includeDetails"/> false only the type name and
        /// message are carried; Source, StackTraceString and the InnerException chain are null and HResult is 0.
        /// </summary>
        public static ExceptionInfo From(Exception ex, bool includeDetails)
        {
            if (ex == null) return null;
            if (!includeDetails)
            {
                return new ExceptionInfo { ClassName = ex.GetType().FullName, Message = ex.Message };
            }
            return new ExceptionInfo
            {
                ClassName = ex.GetType().FullName,
                Message = ex.Message,
                Source = ex.Source,
                StackTraceString = ex.StackTrace,
                HResult = ex.HResult,
                InnerException = From(ex.InnerException, true)
            };
        }

        /// <summary>The description sent to a client in <c>error.data</c>, governed by <see cref="Config.IncludeExceptionDetails"/>.</summary>
        public static ExceptionInfo ForResponse(Exception ex)
        {
            return From(ex, Config.IncludeExceptionDetails);
        }
    }
}
