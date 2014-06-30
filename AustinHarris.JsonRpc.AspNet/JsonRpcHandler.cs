using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Web;

namespace AustinHarris.JsonRpc.Handlers.AspNet
{
    public class JsonRpcHandler : IHttpAsyncHandler
    {
        #region Fields
        /// <summary>
        /// UTF8 Encoding without BOM.
        /// </summary>
        static Encoding UTF8Encoding = new UTF8Encoding(false);
        #endregion

        #region IHttpHandler Members

        public bool IsReusable
        {
            get { return true; }
        }

        public void ProcessRequest(HttpContext context)
        {
            // not used
        }

        #endregion

        #region IHttpAsyncHandler Members

        /// <summary>
        /// Initiates an asynchronous call to the HTTP handler.
        /// </summary>
        /// <param name="context">An <see cref="T:System.Web.HttpContext"/> object that provides references to intrinsic server objects (for example, Request, Response, Session, and Server) used to service HTTP requests.</param>
        /// <param name="cb">The <see cref="T:System.AsyncCallback"/> to call when the asynchronous method call is complete. If <paramref name="cb"/> is null, the delegate is not called.</param>
        /// <param name="extraData">Any extra data needed to process the request.</param>
        /// <returns>
        /// An <see cref="T:System.IAsyncResult"/> that contains information about the status of the process.
        /// </returns>
        public IAsyncResult BeginProcessRequest(HttpContext context, AsyncCallback cb, object extraData)
        {
            var async = new JsonRpcStateAsync(cb, context);
            async.JsonRpc = GetJsonRpcString(context.Request);
            JsonRpcProcessor.Process(async,context.Request);
            return async;
        }

        private static string GetJsonRpcString(System.Web.HttpRequest request)
        {
            string json = string.Empty;
            if (request.RequestType == "GET")
            {
                json = request.Params["jsonrpc"] ?? string.Empty;
            }
            else if (request.RequestType == "POST")
            {
                if (request.ContentType == "application/x-www-form-urlencoded")
                {
                    json = request.Params["jsonrpc"] ?? string.Empty;
                }
                else
                {
                    json = new StreamReader(request.InputStream).ReadToEnd();
                }
            }
            return json;
        }

        /// <summary>
        /// Provides an asynchronous process End method when the process ends.
        /// </summary>
        /// <param name="result">An <see cref="T:System.IAsyncResult"/> that contains information about the status of the process.</param>
        public void EndProcessRequest(IAsyncResult result)
        {
            var state = result as JsonRpcStateAsync;
            if (state != null)
            {
                var r = state.Result;
                var callback = ((HttpContext)state.AsyncState).Request.Params["callback"];
                if (!string.IsNullOrWhiteSpace(callback))
                {
                    r = string.Format("{0}({1})", callback, r);
                }

                // try to compress the response data.
                // fix me: compression filters in IHttpModule always failed for IHttpAsyncHandler
                CompressResponseIfPossible(((HttpContext)state.AsyncState).Request, ((HttpContext)state.AsyncState).Response, r, UTF8Encoding);
            }
        }

        #endregion

        #region Utility methods

        /// <summary>
        /// Transfer the result data compressed when the client accepts gzip.
        /// </summary>
        /// <param name="request">A HttpRequest object that represents the HTTP request.</param>
        /// <param name="response">A HttpResponse object that represents the HTTP response to be sent to the client.</param>
        /// <param name="result">The string data to be sent to the client.</param>
        /// <param name="encoding">The Encoding to be used to encode as the result.</param>
        static void CompressResponseIfPossible(HttpRequest request, HttpResponse response, String result, Encoding encoding)
        {
            string AcceptEncoding = request.Headers["Accept-Encoding"];
            if (AcceptEncoding != null && AcceptEncoding.Contains("gzip"))
            {
                //response.Headers.Remove("Content-Encoding");
                response.AddHeader("Content-Encoding", "gzip");

                using (var gstream = new GZipStream(response.OutputStream, CompressionMode.Compress))
                using (var writer = new StreamWriter(gstream, encoding))
                {
                    writer.Write(result);
                    writer.Flush();
                }
            }
            else
            {
                using (StreamWriter writer = new StreamWriter(response.OutputStream, encoding))
                {
                    writer.Write(result);
                    writer.Flush();
                }
            }

            response.End();
        }
            

        #endregion
    }
}
