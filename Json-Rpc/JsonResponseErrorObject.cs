using System;

namespace AustinHarris.JsonRpc
{
    /// <summary>
    ///  5.1 Error object
    ///
    ///  When a rpc call encounters an error, the Response Object MUST contain the error member with a value that is a Object with the following members:
    ///  code    A Number that indicates the error type that occurred. This MUST be an integer.
    ///  message A String providing a short description of the error. The message SHOULD be limited to a concise single sentence.
    ///  data    A Primitive or Structured value that contains additional information about the error. This may be omitted.
    ///          The value of this member is defined by the Server (e.g. detailed error information, nested errors etc.).
    ///
    ///  The error codes from and including -32768 to -32000 are reserved for pre-defined errors.
    ///
    ///  code        message             meaning
    ///  -32700      Parse error         Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text.
    ///  -32600      Invalid Request     The JSON sent is not a valid Request object.
    ///  -32601      Method not found    The method does not exist / is not available.
    ///  -32602      Invalid params      Invalid method parameter(s).
    ///  -32603      Internal error      Internal JSON-RPC error.
    ///  -32000 to -32099 Server error   Reserved for implementation-defined server-errors.
    ///
    ///  The remainder of the space is available for application defined errors.
    ///
    ///  On the wire the object is always written as {"code":..,"message":..,"data":..}; when <see cref="data"/> is an
    ///  <see cref="Exception"/> it is written as a <see cref="Serialization.ExceptionInfo"/>.
    /// </summary>
    [Serializable]
    public class JsonRpcException : System.ApplicationException
    {
        public int code { get; set; }

        public string message { get; set; }

        public object data { get; set; }

        public JsonRpcException(int code, string message, object data)
            : base(message)
        {
            this.code = code;
            this.message = message;
            this.data = data;
        }
    }
}
