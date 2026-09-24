using System;
using AustinHarris.JsonRpc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Config.VersionPolicy / Config.SetVersionPolicy: how the "jsonrpc" member of a request is checked.
    /// Lenient (default): absent is fine, present must be "2.0". Ignore: never looked at. Strict: must be "2.0".
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class VersionPolicyTests
    {
        private const string Session = "version-policy";
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };

        private class PingService
        {
            [JsonRpcMethod("ping")]
            public int Ping() => 7;
        }

        [OneTimeSetUp]
        public void Bind() => ServiceBinder.BindService(Session, new PingService());

        [OneTimeTearDown]
        public void Destroy() => Handler.DestroySession(Session);

        [TearDown]
        public void ResetPolicies()
        {
            Config.VersionPolicy = JsonRpcVersionPolicy.Lenient;
            Config.SetVersionPolicy(Session, null);
        }

        private static string Run(string name, string json) => JsonRpcProcessor.ProcessSync(Session, json, null, SerializerCatalog.Create(name));

        private const string Ok = "{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}";

        private static void AssertInvalidRequest(string response, string detail, object id = null)
        {
            var o = JObject.Parse(response);
            Assert.AreEqual(-32600, (int)o["error"]["code"], response);
            StringAssert.Contains(detail, response);
            if (id == null) Assert.AreEqual(JTokenType.Null, o["id"].Type, response);
            else Assert.AreEqual(id, o["id"].ToObject(id.GetType()), response);
        }

        [Test]
        public void DefaultIsLenient()
        {
            Assert.AreEqual(JsonRpcVersionPolicy.Lenient, Config.VersionPolicy);
            Assert.IsNull(Handler.GetSessionHandler(Session).VersionPolicy);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Lenient_AcceptsMissingAndExact(string s)
        {
            Assert.AreEqual(Ok, Run(s, "{\"method\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok, Run(s, "{\"JSONRPC\":\"2.0\",\"method\":\"ping\",\"id\":1}"), "member names match case-insensitively like the others");
        }

        [TestCaseSource(nameof(Serializers))]
        public void Lenient_RejectsOtherVersions(string s)
        {
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":\"1.0\",\"method\":\"ping\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":2.0,\"method\":\"ping\",\"id\":\"a\"}"), "must be \\\"2.0\\\"", "a");
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":null,\"method\":\"ping\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":\"2.00\",\"method\":\"ping\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
            // an invalid request object is answered even without an id (it is not a valid notification)
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":\"1.0\",\"method\":\"ping\"}"), "must be \\\"2.0\\\"");
        }

        [TestCaseSource(nameof(Serializers))]
        public void Lenient_ChecksVersionBeforeMethod(string s)
        {
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":\"1.0\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
            AssertInvalidRequest(Run(s, "{\"id\":1}"), "Missing property 'method'", 1L);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Ignore_AcceptsAnything(string s)
        {
            Config.VersionPolicy = JsonRpcVersionPolicy.Ignore;
            Assert.AreEqual(Ok, Run(s, "{\"method\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":\"1.0\",\"method\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":2,\"method\":\"ping\",\"id\":1}"));
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}"));
        }

        [TestCaseSource(nameof(Serializers))]
        public void Strict_RequiresTheMember(string s)
        {
            Config.VersionPolicy = JsonRpcVersionPolicy.Strict;
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1}"));
            AssertInvalidRequest(Run(s, "{\"method\":\"ping\",\"id\":1}"), "Missing property 'jsonrpc'", 1L);
            AssertInvalidRequest(Run(s, "{\"jsonrpc\":\"1.0\",\"method\":\"ping\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
        }

        [TestCaseSource(nameof(Serializers))]
        public void SessionOverridesGlobal(string s)
        {
            const string missing = "{\"method\":\"ping\",\"id\":1}";
            Config.VersionPolicy = JsonRpcVersionPolicy.Strict;
            Config.SetVersionPolicy(Session, JsonRpcVersionPolicy.Lenient);
            Assert.AreEqual(Ok, Run(s, missing), "the session's Lenient wins over the global Strict");

            Config.VersionPolicy = JsonRpcVersionPolicy.Lenient;
            Config.SetVersionPolicy(Session, JsonRpcVersionPolicy.Strict);
            AssertInvalidRequest(Run(s, missing), "Missing property 'jsonrpc'", 1L);

            Config.SetVersionPolicy(Session, null);
            Assert.AreEqual(Ok, Run(s, missing), "null makes the session follow the global policy again");
        }

        [TestCaseSource(nameof(Serializers))]
        public void Batch_JudgesEachRequest(string s)
        {
            var r = JArray.Parse(Run(s, "[{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":1},{\"jsonrpc\":\"1.0\",\"method\":\"ping\",\"id\":2},{\"method\":\"ping\",\"id\":3}]"));
            Assert.AreEqual(3, r.Count);
            Assert.AreEqual(7, (int)r[0]["result"]);
            Assert.AreEqual(-32600, (int)r[1]["error"]["code"]);
            Assert.AreEqual(2, (int)r[1]["id"]);
            Assert.AreEqual(7, (int)r[2]["result"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void EscapedMemberNameIsStillTheVersion(string s)
        {
            AssertInvalidRequest(Run(s, "{\"json\\u0072pc\":\"1.0\",\"method\":\"ping\",\"id\":1}"), "must be \\\"2.0\\\"", 1L);
            // an escaped value is decoded before the comparison
            Assert.AreEqual(Ok, Run(s, "{\"jsonrpc\":\"2\\u002e0\",\"method\":\"ping\",\"id\":1}"));
        }
    }
}
