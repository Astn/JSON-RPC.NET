using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AustinHarris.JsonRpc.Client
{
    public class InProcessClient
    {
        /// <summary>
        /// Simple wrapper around JsonRpcProcessor.Process
        /// </summary>
        /// <param name="jsonrpc"></param>
        /// <returns></returns>
        public static Task<string> Invoke(string jsonrpc)
        {
            var request = Newtonsoft.Json.JsonConvert.DeserializeObject<AustinHarris.JsonRpc.JsonRequest>(jsonrpc);
            var taskSource = new TaskCompletionSource<string>();

            var mc = new IAsyncWrapper(taskSource);
            
            var async = new JsonRpcStateAsync(mc.AsyncCallback, null);
            async.JsonRpc = jsonrpc;
            JsonRpcProcessor.Process(async);

            return taskSource.Task;
        }

        private class IAsyncWrapper
        {
            TaskCompletionSource<string> foo;
            public IAsyncWrapper(TaskCompletionSource<string> item)
            {
                foo = item;
            }

            public void AsyncCallback(IAsyncResult ar)
            {
                foo.SetResult(((JsonRpcStateAsync)ar).Result);
            }
        }
    }
}
