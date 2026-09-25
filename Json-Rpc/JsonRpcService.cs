namespace AustinHarris.JsonRpc
{
    /// <summary>
    /// A base class whose constructor binds the instance's <c>[JsonRpcMethod]</c> members. Any class can be bound
    /// with <see cref="ServiceBinder.BindService(string, object)"/>; deriving from this class only saves that call.
    /// </summary>
    public abstract class JsonRpcService
    {
        /// <summary>Binds this instance to the default session.</summary>
        protected JsonRpcService() : this(true)
        {
        }

        /// <summary>
        /// Binds this instance to the default session when <paramref name="autoBind"/> is true. Pass false for a
        /// service that something else binds: the AspNetCore package binds every registered service to its
        /// effective session, and <see cref="ServiceBinder.BindService(string, object)"/> binds to any session.
        /// </summary>
        protected JsonRpcService(bool autoBind)
        {
            if (autoBind) ServiceBinder.BindService(Handler.DefaultSessionId(), this);
        }

        /// <summary>Binds this instance to session <paramref name="sessionID"/>, creating it when needed.</summary>
        protected JsonRpcService(string sessionID)
        {
            ServiceBinder.BindService(sessionID, this);
        }
    }
}
