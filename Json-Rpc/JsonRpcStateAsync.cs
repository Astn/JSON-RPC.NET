using System;
using System.Threading;

namespace AustinHarris.JsonRpc
{
    public class JsonRpcStateAsync : IAsyncResult
    {
        private readonly AsyncCallback cb;

        public JsonRpcStateAsync(AsyncCallback cb, object extraData)
        {
            this.cb = cb;
            AsyncState = extraData;
            IsCompleted = false;
        }

        public string JsonRpc { get; set; }
        public string Result { get; set; }

        public object AsyncState { get; }

        public bool CompletedSynchronously => false;

        // If this object was not being used solely with ASP.Net this
        // method would need an implementation. ASP.Net never uses the
        // event, so it is not implemented here.
        public WaitHandle AsyncWaitHandle => null;

        public bool IsCompleted { get; private set; }

        internal void SetCompleted()
        {
            IsCompleted = true;
            if (cb != null)
                cb(this);
        }
    }
}