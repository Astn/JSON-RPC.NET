namespace AustinHarris.JsonRpc
{
    // JSONRPC0xxx is reserved for obsoletions; JSONRPC1xxx is reserved for generator diagnostics. IDs are never reused.
    internal static class Obsoletions
    {
        internal const string SharedUrlFormat = "https://astn.github.io/JSON-RPC.NET/obsoletions.html#{0}";

        internal const string SetBeforeProcessHandlerMessage = "Use SetPreProcessHandler(sessionId, handler).";
        internal const string SetBeforeProcessHandlerDiagId = "JSONRPC0001";

        internal const string RegisterFuctionMessage = "Use ServiceBinder.BindMethod; unlike RegisterFuction it throws when the name is already registered instead of replacing it.";
        internal const string RegisterFuctionDiagId = "JSONRPC0002";

        internal const string UnRegisterFunctionMessage = "Use ServiceBinder.UnbindMethod.";
        internal const string UnRegisterFunctionDiagId = "JSONRPC0003";
    }
}
