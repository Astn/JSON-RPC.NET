using AustinHarris.JsonRpc.AspNet;

namespace AustinHarris.JsonRpc.Handlers.AspNet
{
    /// <summary>
    ///     Used default SessionId
    ///     For routing use JsonRpcHandlerBase
    /// </summary>
    public class JsonRpcHandler : JsonRpcHandlerBase
    {
        protected override string GetSessionId()
        {
            return Handler.DefaultSessionId();
        }
    }
}