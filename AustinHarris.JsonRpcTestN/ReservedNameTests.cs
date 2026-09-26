using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using AustinHarris.JsonRpc;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>
    /// Reserved method names (<c>rpc.</c>-prefixed and <c>$/cancelRequest</c>) are refused by
    /// <see cref="SMDServiceCollection"/> itself, so every registration path refuses them.
    /// </summary>
    [TestFixture]
    public sealed class ReservedNameTests
    {
        private const string Session = "reserved-names";
        private static readonly string[] Reserved = { "rpc.x", "$/cancelRequest" };
        private static readonly string[] Allowed = { "$/progress", "rpcx", "Rpc.x", "x.rpc.y", "$/cancelrequest" };
        private static SMDServiceCollection Services => Handler.GetSessionHandler(Session).MetaData.Services;

        [TearDown]
        public void Clean() => Handler.DestroySession(Session);

        private static SMDService NewService(int result)
        {
            return new SMDService("POST", "JSON-RPC-2.0", new Dictionary<string, Type> { ["returns"] = typeof(int) }, new Dictionary<string, object>(), new Func<int>(() => result));
        }

        private static SMDService Find(string name) => Services.Find(Encoding.UTF8.GetBytes(name));

        private static void AssertReserved(string name, TestDelegate register)
        {
            var ex = Assert.Throws<ArgumentException>(register, name);
            StringAssert.StartsWith("'" + name + "' is a reserved JSON-RPC method name.", ex.Message);
            Assert.IsFalse(Services.ContainsKey(name), name);
            Assert.IsNull(Find(name), name);
        }

        private static void AssertRegistered(string name)
        {
            Assert.IsTrue(Services.ContainsKey(name), name);
            Assert.IsNotNull(Find(name), name);
        }

        private interface IPair
        {
            int First();
            int Second();
        }

        private sealed class Pair : IPair
        {
            public int First() => 1;
            public int Second() => 2;
        }

        private sealed class ReservedAlias
        {
            [JsonRpcMethod("rpc.x")]
            public int M() => 1;
        }

        private sealed class ReservedCancelAlias
        {
            [JsonRpcMethod("$/cancelRequest")]
            public int M() => 1;
        }

        private sealed class AllowedAliases
        {
            [JsonRpcMethod("$/progress")]
            [JsonRpcMethod("rpcx")]
            [JsonRpcMethod("Rpc.x")]
            [JsonRpcMethod("x.rpc.y")]
            [JsonRpcMethod("$/cancelrequest")]
            public int M() => 1;
        }

        private sealed class NullKeyEntries : IReadOnlyDictionary<string, SMDService>
        {
            public int Count => 1;
            public IEnumerable<string> Keys => new string[] { null };
            public IEnumerable<SMDService> Values => new SMDService[] { null };
            public SMDService this[string key] => throw new NotImplementedException();
            public bool ContainsKey(string key) => false;
            public bool TryGetValue(string key, out SMDService value) { value = null; return false; }
            public IEnumerator<KeyValuePair<string, SMDService>> GetEnumerator()
            {
                yield return new KeyValuePair<string, SMDService>(null, null);
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        [Test]
        public void BindMethod_RefusesReserved_AcceptsTheRest()
        {
            foreach (var name in Reserved)
                AssertReserved(name, () => ServiceBinder.BindMethod(Session, name, () => 1));
            foreach (var name in Allowed)
            {
                ServiceBinder.BindMethod(Session, name, () => 7);
                Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":7,\"id\":1}",
                    JsonRpcProcessor.ProcessSync(Session, "{\"method\":\"" + name + "\",\"id\":1}", null), name);
            }
        }

        [Test]
        public void BindInterface_RefusesReserved_AcceptsTheRest()
        {
            foreach (var name in Reserved)
            {
                AssertReserved(name, () => ServiceBinder.BindInterface<IPair>(Session, new Pair(),
                    new RpcInterfaceBindingOptions { NameRule = m => m.Leaf == "First" ? name : "second" }));
                Assert.AreEqual(0, Services.Count);
            }
            foreach (var name in Allowed)
            {
                ServiceBinder.BindInterface<IPair>(Session, new Pair(),
                    new RpcInterfaceBindingOptions { NameRule = m => m.Leaf == "First" ? name : "second." + name });
                AssertRegistered(name);
                AssertRegistered("second." + name);
            }
        }

        [Test]
        public void AttributeBinder_RefusesReservedAliases_AcceptsTheRest()
        {
            AssertReserved("rpc.x", () => ServiceBinder.BindService(Session, new ReservedAlias()));
            AssertReserved("$/cancelRequest", () => ServiceBinder.BindService(Session, new ReservedCancelAlias()));
            Assert.AreEqual(0, Services.Count);
            ServiceBinder.BindService(Session, new AllowedAliases());
            foreach (var name in Allowed) AssertRegistered(name);
        }

        [Test]
        public void RegisterFuction_RefusesReserved_AcceptsTheRest()
        {
            var handler = Handler.GetSessionHandler(Session);
#pragma warning disable CS0618, JSONRPC0002
            foreach (var name in Reserved)
                AssertReserved(name, () => handler.RegisterFuction(name, new Dictionary<string, Type> { ["returns"] = typeof(int) }, null, new Func<int>(() => 1)));
            foreach (var name in Allowed)
            {
                handler.RegisterFuction(name, new Dictionary<string, Type> { ["returns"] = typeof(int) }, null, new Func<int>(() => 1));
                AssertRegistered(name);
            }
#pragma warning restore CS0618, JSONRPC0002
        }

        [Test]
        public void DirectCollectionAdds_RefuseReserved_AcceptTheRest()
        {
            var services = Services;
            foreach (var name in Reserved)
            {
                AssertReserved(name, () => services.Add(name, NewService(1)));
                AssertReserved(name, () => services.Add(new KeyValuePair<string, SMDService>(name, NewService(1))));
                AssertReserved(name, () => services[name] = NewService(1));
                Assert.AreEqual("key", Assert.Throws<ArgumentException>(() => services.Add(name, NewService(1))).ParamName);
                Assert.AreEqual("key", Assert.Throws<ArgumentException>(() => services[name] = NewService(1)).ParamName);
            }
            Assert.AreEqual(0, services.Count);

            foreach (var name in Allowed)
            {
                services.Add(name, NewService(1));
                AssertRegistered(name);
                Assert.IsTrue(services.Remove(name));
                services.Add(new KeyValuePair<string, SMDService>(name, NewService(2)));
                AssertRegistered(name);
                var replacement = NewService(3);
                services[name] = replacement;
                Assert.AreSame(replacement, Find(name));
            }

            Assert.Throws<ArgumentNullException>(() => services.Add(null, NewService(1)));
            Assert.Throws<ArgumentNullException>(() => services[null] = NewService(1));
        }

        [Test]
        public void Names_AreComparedAsGiven()
        {
            // no trimming and no case folding: these are ordinary names
            foreach (var name in new[] { " rpc.x", "RPC.x", "$/CancelRequest", "$/cancelRequest ", "rpc" })
            {
                Services.Add(name, NewService(1));
                AssertRegistered(name);
            }
            AssertReserved("rpc.", () => Services.Add("rpc.", NewService(1)));
        }

        [Test]
        public void AddBatch_WithOneReservedEntry_LeavesTheCollectionUnchanged()
        {
            ServiceBinder.BindMethod(Session, "before", () => 1);
            var before = Find("before");
            int count = Services.Count;

            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IPair>(Session, new Pair(),
                new RpcInterfaceBindingOptions { NameRule = m => m.Leaf == "First" ? "ok" : "$/cancelRequest" }));
            Assert.AreEqual("entries", ex.ParamName);

            Assert.AreEqual(count, Services.Count);
            Assert.AreSame(before, Find("before"));
            Assert.IsNull(Find("ok"));
            Assert.IsNull(Find("$/cancelRequest"));
            Assert.IsFalse(Services.ContainsKey("ok"));
            CollectionAssert.AreEqual(new[] { "before" }, Services.Keys);
        }

        [Test]
        public void AddBatch_NullName_RetainsArgumentNullException()
        {
            var ex = Assert.Throws<ArgumentNullException>(() => Services.AddBatch(new NullKeyEntries()));
            Assert.AreEqual(0, Services.Count);
        }

        [Test]
        public void AddReserved_AcceptsReservedOnce_AndKeepsTheDuplicateRule()
        {
            var first = NewService(1);
            Services.AddReserved("rpc.discover", first);
            Assert.AreSame(first, Find("rpc.discover"));
            Assert.AreSame(first, Services["rpc.discover"]);

            Assert.Throws<ArgumentException>(() => Services.AddReserved("rpc.discover", NewService(2)));
            Assert.AreSame(first, Find("rpc.discover"), "the first registration stands");
            Assert.AreEqual(1, Services.Count);

            Assert.Throws<ArgumentNullException>(() => Services.AddReserved(null, NewService(1)));
            Assert.Throws<ArgumentNullException>(() => Services.AddReserved("rpc.other", null));
        }
    }
}
