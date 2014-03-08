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
        /// 
        [Obsolete("You can now use JsonRpcProcessor.Process directly")]
        public static Task<string> Invoke(string jsonrpc)
        {
            return JsonRpcProcessor.Process(jsonrpc);
        }
    }
}
