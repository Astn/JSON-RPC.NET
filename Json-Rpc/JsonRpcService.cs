namespace AustinHarris.JsonRpc
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using AustinHarris.JsonRpc;

    public abstract class JsonRpcService
    {
        protected JsonRpcService()
        {
             ServiceBinder.BindService(Handler.DefaultSessionId(), this);
        }

        protected JsonRpcService(string sessionID)
        {
            ServiceBinder.BindService(sessionID, this);
        }
    }
}
