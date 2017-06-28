using AustinHarris.JsonRpc;

namespace TestServer_AspNet.JsonRpcServiceClasses
{
    public class HelloWorldService : JsonRpcService
    {
        [JsonRpcMethod]
        private string helloWorld(string message)
        {
            return "Hello World " + message;
        }
    }
}