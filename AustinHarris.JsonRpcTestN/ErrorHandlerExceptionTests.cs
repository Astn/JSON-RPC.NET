using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// What an error handler receives in <c>error.data</c> when a method throws: the exception the method threw,
    /// with its own cause still attached. Only the wrappers the invocation machinery adds are stripped, a
    /// <see cref="TargetInvocationException"/> and an <see cref="AggregateException"/> holding one exception; an
    /// inner exception is never substituted for the outer one, and an inner <see cref="JsonRpcException"/> is not
    /// promoted. The same rule on the synchronous and the asynchronous path.
    /// </summary>
    [TestFixture]
    public sealed class ErrorHandlerExceptionTests
    {
        private string _session;
        private Exception _seen;

        [SetUp]
        public void SetUp()
        {
            _session = "error-handler-" + Guid.NewGuid().ToString("N");
            _seen = null;
            Handler.GetSessionHandler(_session).SetErrorHandler((request, error) => { _seen = error.data as Exception; return error; });
        }

        [TearDown]
        public void TearDown() => Handler.DestroySession(_session);

        private static string Request(string method, string id = "1") => "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\",\"id\":" + id + "}";

        private string Sync(string json)
        {
            var output = new ArrayBufferWriter<byte>();
            JsonRpcProcessor.Process(_session, Encoding.UTF8.GetBytes(json).AsSpan(), output);
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }

        private Task<string> Async(string json) => JsonRpcProcessor.ProcessAsync(_session, json);

        private static JObject Error(string json, int code)
        {
            var response = JObject.Parse(json);
            Assert.IsNotNull(response["error"], json);
            Assert.AreEqual(code, (int)response["error"]["code"], json);
            return response;
        }

        /// <summary>Runs <paramref name="thrower"/> as a synchronous method, then as a Task method that has already suspended.</summary>
        private void Bind(Func<Exception> thrower)
        {
            ServiceBinder.BindMethod(_session, "sync", new Func<int>(() => throw thrower()));
            ServiceBinder.BindMethod(_session, "async", new Func<Task<int>>(async () => { await Task.Yield(); throw thrower(); }));
        }

        [TestCase("sync")] [TestCase("async")]
        public async Task ThrownException_ReachesTheHandler_WithItsCause(string path)
        {
            Bind(() => new InvalidOperationException("outer", new IOException("cause")));
            string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
            var error = Error(response, -32603);
            Assert.AreEqual(JTokenType.Null, error["error"]["data"].Type, "redacted on the wire by default");
            Assert.IsInstanceOf<InvalidOperationException>(_seen, "the handler gets the exception that was thrown, not its cause");
            Assert.AreEqual("outer", _seen.Message);
            Assert.IsInstanceOf<IOException>(_seen.InnerException, "with its cause still attached");
        }

        [TestCase("sync")] [TestCase("async")]
        public async Task InnerJsonRpcException_IsNotPromoted(string path)
        {
            Bind(() => new InvalidOperationException("outer", new JsonRpcException(-32040, "authored inside", "data")));
            string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
            Error(response, -32603);
            Assert.IsInstanceOf<InvalidOperationException>(_seen);
            Assert.AreEqual("outer", _seen.Message);
        }

        [TestCase("sync")] [TestCase("async")]
        public async Task AuthoredException_IsPassedAsThrown(string path)
        {
            Bind(() => new JsonRpcException(-32040, "authored", "ticket 42"));
            string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
            var error = Error(response, -32040);
            Assert.AreEqual("authored", (string)error["error"]["message"]);
            Assert.AreEqual("ticket 42", (string)error["error"]["data"]);
        }

        [TestCase("sync")] [TestCase("async")]
        public async Task TargetInvocationWrapper_IsStripped(string path)
        {
            Bind(() => new TargetInvocationException(new JsonRpcException(-32040, "authored", null)));
            string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
            Error(response, -32040);
        }

        [TestCase("sync", false)] [TestCase("sync", true)] [TestCase("async", false)] [TestCase("async", true)]
        public async Task Aggregate_OnlyASingleInner_IsStripped(string path, bool several)
        {
            var authored = new JsonRpcException(-32040, "authored", null);
            Bind(() => several ? new AggregateException(authored, new Exception("second")) : new AggregateException(authored));
            string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
            Error(response, several ? -32603 : -32040);
            if (several) Assert.IsInstanceOf<AggregateException>(_seen, "an aggregate of several failures is passed whole");
        }

        [TestCase("sync")] [TestCase("async")]
        public async Task WithDetailsOn_TheWireCarriesTheOuterExceptionAndItsCause(string path)
        {
            Bind(() => new InvalidOperationException("outer", new IOException("cause")));
            Config.IncludeExceptionDetails = true;
            try
            {
                string response = path == "sync" ? Sync(Request(path)) : await Async(Request(path));
                var data = (JObject)Error(response, -32603)["error"]["data"];
                Assert.AreEqual("outer", (string)data["Message"], response);
                Assert.AreEqual("cause", (string)data["InnerException"]["Message"], response);
            }
            finally
            {
                Config.IncludeExceptionDetails = false;
            }
        }
    }
}
