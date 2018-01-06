namespace AustinHarris.JsonRpc
{
    /// <summary>
    ///     For routing use SessionId
    /// </summary>
    public abstract class JsonRpcService
    {
        protected JsonRpcService()
        {
            ServiceBinder.BindService(Handler.DefaultSessionId(), this);
        }

        /// <summary>
        ///     Routing by SessionId
        /// </summary>
        /// <param name="sessionID"></param>
        protected JsonRpcService(string sessionID)
        {
            ServiceBinder.BindService(sessionID, this);
        }
    }
}