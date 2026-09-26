using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// The request path never creates a session; registration paths still do. Per-session pre- and post-process
    /// handlers are set through Config with names symmetric to the default-session setters. A JsonRpcService
    /// subclass can opt out of binding itself.
    /// </summary>
    [TestFixture]
    public class SessionAndConfigTests
    {
        private const string Session = "session-config";

        private class Service
        {
            [JsonRpcMethod("sc.ping")]
            public int Ping() => 7;
        }

        [OneTimeSetUp]
        public void Bind()
        {
            ServiceBinder.BindService(Session, new Service());
        }

        [OneTimeTearDown]
        public void Destroy()
        {
            Handler.DestroySession(Session);
        }

        // ------------------------------------------------------------------ unknown session ids

        [Test]
        public void UnknownSession_AnswersMethodNotFound_AndIsNotCreated()
        {
            string id = "never-registered-" + Guid.NewGuid().ToString("N");

            var response = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null));
            Assert.AreEqual(-32601, (int)response["error"]["code"]);
            Assert.AreEqual("sc.ping", (string)response["error"]["data"]["method"]);
            Assert.AreEqual(1, (int)response["id"]);

            // a request with the unknown id did not create the session: the lookup is still a miss
            Assert.IsFalse(Handler.TryGetSessionHandler(id, out _), "the request path must not create sessions");

            // the default session's methods are not reachable through an unknown id either
            var viaDefault = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"add\",\"params\":[1,2],\"id\":2}", null));
            Assert.AreEqual(-32601, (int)viaDefault["error"]["code"], "unknown ids do not fall back to the default session");
        }

        [Test]
        public async Task UnknownSession_Async_AnswersMethodNotFound_AndIsNotCreated()
        {
            string id = "never-registered-async-" + Guid.NewGuid().ToString("N");
            var response = JObject.Parse(await JsonRpcProcessor.ProcessAsync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}"));
            Assert.AreEqual(-32601, (int)response["error"]["code"]);
            Assert.IsFalse(Handler.TryGetSessionHandler(id, out _));
        }

        [Test]
        public void UnknownSession_ParseErrorsBatchesAndNotifications_BehaveAsUsual()
        {
            string id = "never-registered-shapes-" + Guid.NewGuid().ToString("N");
            var parse = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",", null));
            Assert.AreEqual(-32700, (int)parse["error"]["code"]);

            var batch = JArray.Parse(JsonRpcProcessor.ProcessSync(id, "[{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1},{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\"}]", null));
            Assert.AreEqual(1, batch.Count, "the notification produces nothing, the call answers -32601");
            Assert.AreEqual(-32601, (int)batch[0]["error"]["code"]);

            Assert.AreEqual("", JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\"}", null));
            Assert.IsFalse(Handler.TryGetSessionHandler(id, out _));
        }

        [Test]
        public void RegistrationAfterAMiss_IsFoundByTheThreadThatMissed()
        {
            string id = "registered-after-miss-" + Guid.NewGuid().ToString("N");
            // the miss puts nothing in this thread's snapshot or last-hit cache
            var miss = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null));
            Assert.AreEqual(-32601, (int)miss["error"]["code"]);
            try
            {
                ServiceBinder.BindService(id, new Service());
                var hit = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":2}", null));
                Assert.AreEqual(7, (int)hit["result"]);
            }
            finally
            {
                Handler.DestroySession(id);
            }
            var gone = JObject.Parse(JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":3}", null));
            Assert.AreEqual(-32601, (int)gone["error"]["code"], "a destroyed session is unknown again");
        }

        [Test]
        public void RegistrationRacingRequests_IsSeenByEveryThread()
        {
            // Threads hammer an id with requests while another thread registers it. After the registration no
            // thread may keep answering -32601: the snapshot miss consults the master registry.
            string id = "race-" + Guid.NewGuid().ToString("N");
            var registered = new ManualResetEventSlim();
            var stop = new ManualResetEventSlim();
            var errorsAfterRegistration = 0;
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                while (!stop.IsSet)
                {
                    // read the flag before sending: a request that started before the registration finished may
                    // legitimately answer -32601
                    bool wasRegistered = registered.IsSet;
                    var r = JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null);
                    if (wasRegistered && r.Contains("-32601")) Interlocked.Increment(ref errorsAfterRegistration);
                }
            })).ToArray();
            try
            {
                Thread.Sleep(20);
                ServiceBinder.BindService(id, new Service());
                registered.Set();
                Thread.Sleep(50);
                // one more request from each worker after registration
                stop.Set();
                Task.WaitAll(workers);
                for (int i = 0; i < 4; i++)
                {
                    var r = JsonRpcProcessor.ProcessSync(id, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null);
                    Assert.IsTrue(r.Contains("\"result\":7"), r);
                }
                Assert.AreEqual(0, errorsAfterRegistration, "no thread answered -32601 once the session existed");
            }
            finally
            {
                stop.Set();
                Handler.DestroySession(id);
            }
        }

        [Test]
        public void GetSessionHandler_StillCreates_ForRegistrationPaths()
        {
            string id = "created-by-config-" + Guid.NewGuid().ToString("N");
            try
            {
                Config.SetSerializer(id, null);
                Assert.IsTrue(Handler.TryGetSessionHandler(id, out var handler));
                Assert.AreSame(handler, Handler.GetSessionHandler(id));
            }
            finally
            {
                Handler.DestroySession(id);
            }
        }

        // ------------------------------------------------------------------ per-session handler setters

        [Test]
        public void Config_SetsPreAndPostProcessHandlers_PerSession()
        {
            int pre = 0, post = 0, defaultPre = 0;
            try
            {
                Config.SetPreProcessHandler((request, context) => { defaultPre++; return null; });
                Config.SetPreProcessHandler(Session, (request, context) => { pre++; return null; });
                Config.SetPostProcessHandler(Session, (request, response, context) => { post++; return null; });

                var response = JObject.Parse(JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null));
                Assert.AreEqual(7, (int)response["result"]);
                Assert.AreEqual(1, pre);
                Assert.AreEqual(1, post);
                Assert.AreEqual(0, defaultPre, "the default session's handler does not run for another session");

                // null clears only that session's handler
                Config.SetPreProcessHandler(Session, null);
                Config.SetPostProcessHandler(Session, null);
                JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":2}", null);
                Assert.AreEqual(1, pre);
                Assert.AreEqual(1, post);

                JsonRpcProcessor.ProcessSync("{\"jsonrpc\":\"2.0\",\"method\":\"add\",\"params\":[1,2],\"id\":3}", context: null);
                Assert.AreEqual(1, defaultPre, "the default session's own handler still runs");
            }
            finally
            {
                Config.SetPreProcessHandler(null);
                Config.SetPreProcessHandler(Session, null);
                Config.SetPostProcessHandler(Session, null);
            }
        }

        [Test]
        public void Config_SetBeforeProcessHandler_IsAnAliasOfSetPreProcessHandler()
        {
            int pre = 0;
            try
            {
#pragma warning disable CS0618, JSONRPC0001
                Config.SetBeforeProcessHandler(Session, (request, context) => { pre++; return null; });
#pragma warning restore CS0618, JSONRPC0001
                JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":1}", null);
                Assert.AreEqual(1, pre);
                Config.SetPreProcessHandler(Session, null);
                JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.ping\",\"id\":2}", null);
                Assert.AreEqual(1, pre, "the new name clears what the old name set");
            }
            finally
            {
                Config.SetPreProcessHandler(Session, null);
            }
        }

        // ------------------------------------------------------------------ JsonRpcService(bool autoBind)

        private sealed class BoundService : JsonRpcService
        {
            public BoundService() : base(true) { }
            [JsonRpcMethod("sc.bound")] public int Bound() => 1;
        }

        private sealed class UnboundService : JsonRpcService
        {
            public UnboundService() : base(false) { }
            [JsonRpcMethod("sc.unbound")] public int Unbound() => 2;
        }

        [Test]
        public void JsonRpcService_AutoBindFalse_BindsNowhere_UntilBoundExplicitly()
        {
            var unbound = new UnboundService();
            Assert.IsFalse(Handler.DefaultHandler.MetaData.Services.ContainsKey("sc.unbound"), "base(false) does not touch the default session");

            _ = new BoundService();
            try
            {
                Assert.IsTrue(Handler.DefaultHandler.MetaData.Services.ContainsKey("sc.bound"), "base(true) is the parameterless behaviour");

                ServiceBinder.BindService(Session, unbound);
                var response = JObject.Parse(JsonRpcProcessor.ProcessSync(Session, "{\"jsonrpc\":\"2.0\",\"method\":\"sc.unbound\",\"id\":1}", null));
                Assert.AreEqual(2, (int)response["result"]);
                Assert.IsFalse(Handler.DefaultHandler.MetaData.Services.ContainsKey("sc.unbound"), "explicit binding to another session leaves the default session alone");
            }
            finally
            {
#pragma warning disable CS0618, JSONRPC0003
                Handler.DefaultHandler.UnRegisterFunction("sc.bound");
                Handler.GetSessionHandler(Session).UnRegisterFunction("sc.unbound");
#pragma warning restore CS0618, JSONRPC0003
            }
        }
    }
}
