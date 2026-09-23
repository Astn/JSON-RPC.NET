using System;
using System.Collections.Generic;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// <see cref="ServiceBinder.BindMethod(string, string, Delegate, string[], IDictionary{string, object})"/> (issue #6):
    /// any delegate becomes a method without attributes or a service class.
    /// </summary>
    [TestFixture]
    public class DelegateBindingTests
    {
        private const string Session = "delegate-binding";
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };

        private sealed class Counter
        {
            public int Count;
            public int Next() => ++Count;
            public string Describe(string prefix, int n) => prefix + n;
            public static double Half(double x) => x / 2;
        }

        private static string Run(string json, JsonRpcSerializer serializer = null)
        {
            return JsonRpcProcessor.ProcessSync(Session, json, null, serializer);
        }

        [TearDown]
        public void Clean()
        {
            Handler.DestroySession(Session);
        }

        [TestCaseSource(nameof(Serializers))]
        public void Lambdas_KeepTheirParameterNames(string name)
        {
            var s = SerializerCatalog.Create(name);
            ServiceBinder.BindMethod(Session, "add", (int left, int right) => left + right);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":1}", Run("{\"method\":\"add\",\"params\":[1,2],\"id\":1}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":1}", Run("{\"method\":\"add\",\"params\":{\"right\":2,\"left\":1},\"id\":1}", s));
            var response = JObject.Parse(Run("{\"method\":\"add\",\"params\":{\"l\":1,\"r\":2},\"id\":1}", s));
            Assert.AreEqual(-32602, (int)response["error"]["code"]);
            StringAssert.Contains("'l'", (string)response["error"]["data"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void CapturingLambdas_MethodGroups_StaticAndInstance(string name)
        {
            var s = SerializerCatalog.Create(name);
            var counter = new Counter();
            ServiceBinder.BindMethod(Session, "next", () => counter.Next());
            ServiceBinder.BindMethod(Session, "describe", new Func<string, int, string>(counter.Describe));
            ServiceBinder.BindMethod(Session, "half", new Func<double, double>(Counter.Half));
            ServiceBinder.BindMethod(Session, "greet", (string who) => "hi " + who);

            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}", Run("{\"method\":\"next\",\"id\":1}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":2,\"id\":2}", Run("{\"method\":\"next\",\"id\":2}", s));
            Assert.AreEqual(2, counter.Count);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"n=7\",\"id\":1}", Run("{\"method\":\"describe\",\"params\":{\"prefix\":\"n=\",\"n\":7},\"id\":1}", s), "a method group keeps the target's parameter names");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":2.5,\"id\":1}", Run("{\"method\":\"half\",\"params\":[5],\"id\":1}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"hi you\",\"id\":1}", Run("{\"method\":\"greet\",\"params\":[\"you\"],\"id\":1}", s));
        }

        [TestCaseSource(nameof(Serializers))]
        public void ExplicitNames_AndDefaults(string name)
        {
            var s = SerializerCatalog.Create(name);
            ServiceBinder.BindMethod(Session, "scale", (double value, double factor) => value * factor,
                parameterNames: new[] { "v", null }, defaults: new Dictionary<string, object> { ["factor"] = 10 });
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":20.0,\"id\":1}", Run("{\"method\":\"scale\",\"params\":{\"v\":2},\"id\":1}", s), "null keeps the lambda's name; the default is converted to the parameter type");
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":6.0,\"id\":1}", Run("{\"method\":\"scale\",\"params\":{\"v\":2,\"factor\":3},\"id\":1}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":20.0,\"id\":1}", Run("{\"method\":\"scale\",\"params\":[2],\"id\":1}", s));
            var response = JObject.Parse(Run("{\"method\":\"scale\",\"params\":{\"value\":2},\"id\":1}", s));
            Assert.AreEqual(-32602, (int)response["error"]["code"], "the renamed parameter is not reachable by its CLR name");

            var smd = Handler.GetSessionHandler(Session).MetaData.Services["scale"];
            Assert.AreEqual("v", smd.Method.Parameters[0].Name);
            Assert.AreEqual("factor", smd.Method.Parameters[1].Name);
            Assert.IsTrue(smd.Method.Parameters[1].HasDefault);
            Assert.AreEqual(typeof(double), smd.Method.ReturnType);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ClosedDelegate_WithoutRecoverableNames_UsesArgN(string name)
        {
            var s = SerializerCatalog.Create(name);
            // a delegate closed over its first argument: the target's parameter list no longer describes the delegate
            var closed = (Func<int, string>)Delegate.CreateDelegate(typeof(Func<int, string>), "prefix-", typeof(DelegateBindingTests).GetMethod(nameof(Concat), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
            ServiceBinder.BindMethod(Session, "closed", closed);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"prefix-4\",\"id\":1}", Run("{\"method\":\"closed\",\"params\":[4],\"id\":1}", s));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"prefix-4\",\"id\":1}", Run("{\"method\":\"closed\",\"params\":{\"arg\":4},\"id\":1}", s));
            Assert.AreEqual("arg", Handler.GetSessionHandler(Session).MetaData.Services["closed"].Method.Parameters[0].Name);

            ServiceBinder.BindMethod(Session, "closedNamed", closed, new[] { "n" });
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"prefix-5\",\"id\":1}", Run("{\"method\":\"closedNamed\",\"params\":{\"n\":5},\"id\":1}", s));
        }

        private static string Concat(string prefix, int n) => prefix + n;

        [TestCaseSource(nameof(Serializers))]
        public void VoidDelegates_AnswerNull(string name)
        {
            var s = SerializerCatalog.Create(name);
            int calls = 0;
            ServiceBinder.BindMethod(Session, "fire", (int n) => { calls += n; });
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":null,\"id\":1}", Run("{\"method\":\"fire\",\"params\":[3],\"id\":1}", s));
            Assert.AreEqual("", Run("{\"method\":\"fire\",\"params\":[4]}", s));
            Assert.AreEqual(7, calls);
        }

        [Test]
        public void Names_MustBeFree_AndUnbindFreesThem()
        {
            ServiceBinder.BindMethod(Session, "m", () => 1);
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindMethod(Session, "m", () => 2));
            StringAssert.Contains("already registered", ex.Message);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":1,\"id\":1}", Run("{\"method\":\"m\",\"id\":1}"), "the first registration stands");

            Assert.IsTrue(ServiceBinder.UnbindMethod(Session, "m"));
            Assert.IsFalse(ServiceBinder.UnbindMethod(Session, "m"));
            Assert.AreEqual(-32601, (int)JObject.Parse(Run("{\"method\":\"m\",\"id\":1}"))["error"]["code"]);
            ServiceBinder.BindMethod(Session, "m", () => 2);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":2,\"id\":1}", Run("{\"method\":\"m\",\"id\":1}"));

            // the legacy surface keeps replacing silently
            Handler.GetSessionHandler(Session).RegisterFuction("m", new Dictionary<string, Type> { ["returns"] = typeof(int) }, null, new Func<int>(() => 3));
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":1}", Run("{\"method\":\"m\",\"id\":1}"));
        }

        [Test]
        public void Rejections()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindMethod(Session, "x", null));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindMethod(Session, "", () => 1));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindMethod(Session, " ", () => 1));
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindMethod(null, "x", () => 1));

            Func<int> multicast = () => 1;
            multicast += () => 2;
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindMethod(Session, "multi", multicast));
            StringAssert.Contains("multicast", ex.Message);

            ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindMethod(Session, "dup", (int a, int b) => a + b, new[] { "x", "x" }));
            StringAssert.Contains("'x'", ex.Message);

            Assert.IsFalse(Handler.GetSessionHandler(Session).MetaData.Services.ContainsKey("multi"));
            Assert.IsFalse(Handler.GetSessionHandler(Session).MetaData.Services.ContainsKey("dup"));
        }

        [Test]
        public void DefaultSessionOverloads()
        {
            try
            {
                ServiceBinder.BindMethod("db.default", (string s) => s + "!");
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"a!\",\"id\":1}", JsonRpcProcessor.ProcessSync("{\"method\":\"db.default\",\"params\":[\"a\"],\"id\":1}"));
            }
            finally
            {
                Assert.IsTrue(ServiceBinder.UnbindMethod("db.default"));
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void RequestIdAndContext_AreAvailableToDelegates(string name)
        {
            var s = SerializerCatalog.Create(name);
            ServiceBinder.BindMethod(Session, "who", () => Handler.RpcRequestId() + "/" + Handler.RpcContext());
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"abc/ctx\",\"id\":\"abc\"}", JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"who\",\"id\":\"abc\"}", "ctx", s));
        }
    }
}
