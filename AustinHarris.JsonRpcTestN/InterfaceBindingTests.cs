using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AustinHarris.JsonRpc;
using AustinHarris.JsonRpc.Serialization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    [TestFixture]
    public sealed class InterfaceBindingTests
    {
        private const string Session = "interface-binding";
        private static readonly string[] Serializers = { "jsmn", "newtonsoft", "stj" };
        private static SMDServiceCollection Services => Handler.GetSessionHandler(Session).MetaData.Services;

        [TearDown]
        public void Clean() => Handler.DestroySession(Session);

        private static string Run(string json, JsonRpcSerializer serializer = null, object context = null)
            => JsonRpcProcessor.ProcessSync(Session, json, context, serializer);

        private static JObject Call(string method, string parameters = null, JsonRpcSerializer serializer = null)
            => JObject.Parse(Run("{\"method\":\"" + method + "\",\"id\":1" + (parameters == null ? "" : ",\"params\":" + parameters) + "}", serializer));

        private static JToken Result(string method, string parameters = null, JsonRpcSerializer serializer = null)
        {
            var response = Call(method, parameters, serializer);
            Assert.IsNull(response["error"], response.ToString());
            return response["result"];
        }

        private static void Absent(params string[] names)
        {
            foreach (string name in names)
            {
                Assert.IsFalse(Services.ContainsKey(name), name);
                Assert.AreEqual(-32601, (int)Call(name)["error"]["code"], name);
            }
        }

        private interface IWorld
        {
            ICharacter Character { get; }
            ISession Session { get; }
            IObserver Observer { get; }
            IAdmin Admin { get; }
        }
        private interface ICharacter
        {
            float MoveAndRotate(float distance, float roll = 0, int ticks = 1);
            string Use();
        }
        private interface ISession { void Reset(); }
        private interface IObserver { List<int> Observe(); }
        private interface IAdmin
        {
            IAdminCharacter Character { get; }
            IAdminObserver Observer { get; }
        }
        private interface IAdminCharacter
        {
            void Teleport(int x, int y);
            string Use(string item, int x, int y);
        }
        private interface IAdminObserver { Dictionary<string, string> Describe(); }
        private sealed class World : IWorld, ICharacter, ISession, IObserver, IAdmin, IAdminCharacter, IAdminObserver
        {
            public int Resets;
            public int Position;
            public ICharacter Character => this;
            public ISession Session => this;
            public IObserver Observer => this;
            public IAdmin Admin => this;
            IAdminCharacter IAdmin.Character => this;
            IAdminObserver IAdmin.Observer => this;
            public float MoveAndRotate(float distance, float roll = 99, int ticks = 99) => distance + roll + ticks;
            public string Use() => "ordinary";
            public void Reset() => Resets++;
            public List<int> Observe() => new List<int> { 1, 2, 3 };
            public void Teleport(int x, int y) => Position = x + y;
            public string Use(string item, int x, int y) => item + ":" + (x + y);
            public Dictionary<string, string> Describe() => new Dictionary<string, string> { ["role"] = "admin" };
        }

        [TestCaseSource(nameof(Serializers))]
        public void ThreeLevelTree_DefaultsVoidCollectionsAndDistinctPaths(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var world = new World();
            using var binding = ServiceBinder.BindInterface<IWorld>(Session, world);
            CollectionAssert.AreEquivalent(new[] { "Character.MoveAndRotate", "Character.Use", "Session.Reset", "Observer.Observe", "Admin.Character.Teleport", "Admin.Character.Use", "Admin.Observer.Describe" }, binding.Methods);
            Assert.AreEqual(3f, (float)Result("Character.MoveAndRotate", "[2]", serializer));
            Assert.AreEqual(3f, (float)Result("Character.MoveAndRotate", "{\"distance\":2}", serializer));
            Assert.AreEqual(9f, (float)Result("Character.MoveAndRotate", "{\"ticks\":4,\"roll\":3,\"distance\":2}", serializer));
            Assert.AreEqual(JTokenType.Null, Result("Session.Reset", null, serializer).Type);
            Assert.AreEqual(1, world.Resets);
            Assert.AreEqual(JTokenType.Null, Result("Admin.Character.Teleport", "[4,5]", serializer).Type);
            Assert.AreEqual(9, world.Position);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, Result("Observer.Observe", null, serializer).Values<int>());
            Assert.AreEqual("admin", (string)Result("Admin.Observer.Describe", null, serializer)["role"]);
            Assert.AreEqual("ordinary", (string)Result("Character.Use", null, serializer));
            Assert.AreEqual("key:7", (string)Result("Admin.Character.Use", "[\"key\",3,4]", serializer));
        }

        private interface IAdd { int Add(int left, int right); }
        private sealed class Adder : IAdd { public int Add(int a, int b) => a + b; }
        private sealed class ExplicitAdder : IAdd
        {
            int IAdd.Add(int a, int b) => a + b;
            [JsonRpcMethod("secret")] public int Secret() => 99;
        }

        private interface IRefError { int Check(int value, ref JsonRpcException error); }
        private sealed class RefError : IRefError
        {
            public int Check(int value, ref JsonRpcException error)
            {
                if (value < 0) error = new JsonRpcException(-32001, "negative", null);
                return value;
            }
        }

        [TestCaseSource(nameof(Serializers))]
        public void TrailingRefError_UsesCompiledInvokerWithoutLegacyDelegate(string name)
        {
            using var binding = ServiceBinder.BindInterface<IRefError>(Session, new RefError());
            var serializer = SerializerCatalog.Create(name);
            Assert.AreEqual(3, (int)Result("Check", "[3]", serializer));
            Assert.AreEqual(-32001, (int)Call("Check", "[-1]", serializer)["error"]["code"]);
            Assert.IsNull(Services["Check"].dele);
            Assert.AreEqual(1, Services["Check"].parameters.Length);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ExplicitImplementation_UsesOnlyContractMetadata(string name)
        {
            using var binding = ServiceBinder.BindInterface<IAdd>(Session, new ExplicitAdder());
            Assert.AreEqual(7, (int)Result("Add", "{\"right\":4,\"left\":3}", SerializerCatalog.Create(name)));
            CollectionAssert.AreEqual(new[] { "Add" }, binding.Methods);
            Absent("secret", "Secret", "ToString", "Equals", "GetHashCode", "GetType");
            var legacy = (Func<int, int, int>)Services["Add"].dele;
            Assert.AreEqual(9, legacy(4, 5));
        }

        private interface IBase { int Base(); }
        private interface ILeft : IBase { }
        private interface IRight : IBase { }
        private interface IDiamond : ILeft, IRight { int Leaf(); }
        private sealed class Diamond : IDiamond
        {
            public int Base() => 1;
            public int Leaf() => 2;
        }

        [Test]
        public void InheritedDiamond_VisitsDeclarationOnce()
        {
            using var binding = ServiceBinder.BindInterface<IDiamond>(Session, new Diamond());
            CollectionAssert.AreEquivalent(new[] { "Base", "Leaf" }, binding.Methods);
            Assert.AreEqual(1, (int)Result("Base"));
            Assert.AreEqual(2, (int)Result("Leaf"));
        }

        private interface IGeneric<T> { T Echo(T value); }
        private sealed class Generic<T> : IGeneric<T> { public T Echo(T item) => item; }

        private interface IGenericPair : IGeneric<int>, IGeneric<string> { }
        private sealed class GenericPair : IGenericPair
        {
            public int Echo(int value) => value;
            public string Echo(string value) => value;
        }

        [Test]
        public void DifferentClosedGenericDeclarations_AreNotDeduplicated()
        {
            using var binding = ServiceBinder.BindInterface<IGenericPair>(Session, new GenericPair(), new RpcInterfaceBindingOptions
            {
                NameRule = m => m.Interface.GetGenericArguments()[0].Name + "." + m.Leaf
            });
            Assert.AreEqual(4, (int)Result("Int32.Echo", "[4]"));
            Assert.AreEqual("four", (string)Result("String.Echo", "[\"four\"]"));
        }

        [TestCaseSource(nameof(Serializers))]
        public void ClosedGenericInterface(string name)
        {
            RpcInterfaceMethod seen = null;
            using var binding = ServiceBinder.BindInterface<IGeneric<int>>(Session, new Generic<int>(), new RpcInterfaceBindingOptions { Include = m => { seen = m; return true; } });
            Assert.AreEqual(typeof(IGeneric<int>), seen.Interface);
            Assert.AreEqual(12, (int)Result("Echo", "{\"value\":12}", SerializerCatalog.Create(name)));
        }

        [Test]
        public void OpenGenericInterface_IsRejectedByTypeValidation()
        {
            // The generic public API cannot be invoked with an open T; exercise the same validator directly.
            var validate = typeof(ServiceBinder).GetMethod("ValidateInterface", BindingFlags.NonPublic | BindingFlags.Static);
            var ex = Assert.Throws<TargetInvocationException>(() => validate.Invoke(null, new object[] { typeof(IGeneric<>) }));
            Assert.IsInstanceOf<ArgumentException>(ex.InnerException);
            StringAssert.Contains("closed interface", ex.InnerException.Message);
            Assert.AreEqual(0, Services.Count);
        }

        private interface IGenericMethod { int Good(); T Echo<T>(T value); }
        private sealed class GenericMethod : IGenericMethod
        {
            public int Good() => 1;
            public T Echo<T>(T value) => value;
        }

        [Test]
        public void GenericMethod_IsRejectedAtomically()
        {
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IGenericMethod>(Session, new GenericMethod()));
            StringAssert.Contains("Generic interface method", ex.Message);
            Absent("Good", "Echo");
        }

        [AttributeUsage(AttributeTargets.Method)]
        private sealed class ExportAttribute : Attribute { }
        private interface IAliases
        {
            [Export, JsonRpcMethod("ALIAS"), JsonRpcMethod("OtherAlias")]
            int Value([JsonRpcParam("n")] int number = 7);
            [JsonRpcMethod] int Plain();
        }
        private sealed class Aliases : IAliases
        {
            [JsonRpcMethod("implementation")]
            public int Value([JsonRpcParam("wrong")] int different = 99) => different;
            public int Plain() => 3;
        }

        [TestCaseSource(nameof(Serializers))]
        public void AliasesRenamesAndDefaults_ComeFromInterface(string name)
        {
            using var binding = ServiceBinder.BindInterface<IAliases>(Session, new Aliases());
            var serializer = SerializerCatalog.Create(name);
            Assert.AreEqual(7, (int)Result("ALIAS", "{}", serializer));
            Assert.AreEqual(8, (int)Result("OtherAlias", "{\"n\":8}", serializer));
            Assert.AreEqual(3, (int)Result("Plain", null, serializer));
            Assert.AreEqual(-32602, (int)Call("ALIAS", "{\"wrong\":8}", serializer)["error"]["code"]);
            Absent("Value", "implementation");
            var service = Services["ALIAS"];
            Assert.AreEqual("n", service.parameters.Single().Name);
            Assert.AreEqual(7, service.defaultValues.Single().Value);
            Assert.AreEqual(5, ((Func<int, int>)service.dele)(5));
        }

        [Test]
        public void Include_ReadsHostAttributeForEveryAlias()
        {
            var seen = new List<RpcInterfaceMethod>();
            using var binding = ServiceBinder.BindInterface<IAliases>(Session, new Aliases(), new RpcInterfaceBindingOptions
            {
                Include = method => { seen.Add(method); return method.MethodInfo.IsDefined(typeof(ExportAttribute), false); }
            });
            Assert.AreEqual(3, seen.Count);
            CollectionAssert.AreEquivalent(new[] { "ALIAS", "OtherAlias" }, binding.Methods);
            Assert.AreEqual(typeof(IAliases), seen[0].Interface);
            Assert.IsEmpty(seen[0].Path);
            Absent("Plain");
        }

        [Test]
        public void NameRule_ReceivesFullPathAndReplacesDefaultName()
        {
            var descriptions = new List<RpcInterfaceMethod>();
            using var binding = ServiceBinder.BindInterface<IWorld>(Session, new World(), new RpcInterfaceBindingOptions
            {
                Prefix = "unused:",
                NameRule = method => { descriptions.Add(method); return "v1/" + string.Join("/", method.Path.Concat(new[] { method.Leaf })).ToLowerInvariant(); }
            });
            var teleport = descriptions.Single(m => m.Leaf == "Teleport");
            CollectionAssert.AreEqual(new[] { "Admin", "Character" }, teleport.Path);
            Assert.AreEqual("unused:Admin.Character.Teleport", teleport.DefaultName);
            Assert.AreEqual(typeof(IAdminCharacter), teleport.Interface);
            Assert.AreEqual("ordinary", (string)Result("v1/character/use"));
            Assert.AreEqual(JTokenType.Null, Result("v1/admin/character/teleport", "[1,2]").Type);
        }

        [Test]
        public void CamelCasePrefixAndSeparator_LeaveAliasesLiteral()
        {
            using var tree = ServiceBinder.BindInterface<IWorld>(Session, new World(), new RpcInterfaceBindingOptions { Prefix = "V1:", Separator = "/", Casing = RpcNameCasing.CamelCase });
            Assert.AreEqual(3f, (float)Result("V1:character/moveAndRotate", "[2]"));
            using var aliases = ServiceBinder.BindInterface<IAliases>(Session, new Aliases(), new RpcInterfaceBindingOptions { Prefix = "V1:", Casing = RpcNameCasing.CamelCase });
            CollectionAssert.AreEquivalent(new[] { "V1:ALIAS", "V1:OtherAlias", "V1:plain" }, aliases.Methods);
        }

        private interface ICasing { int ID(); int URLValue(); }
        private sealed class Casing : ICasing
        {
            public int ID() => 1;
            public int URLValue() => 2;
        }

        [Test]
        public void CamelCase_IsInvariantAndHandlesAcronyms()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
                using var binding = ServiceBinder.BindInterface<ICasing>(Session, new Casing(), new RpcInterfaceBindingOptions { Casing = RpcNameCasing.CamelCase });
                CollectionAssert.AreEquivalent(new[] { "id", "urlValue" }, binding.Methods);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        private interface IMount { int Root(); IAdd Child { get; } }
        private sealed class Mount : IMount
        {
            public Func<IAdd> GetChild = () => new Adder();
            public int Reads;
            public int Root() => 4;
            public IAdd Child { get { Reads++; return GetChild(); } }
        }

        private interface IMountLeft : IMount { }
        private interface IMountRight : IMount { }
        private interface IMountDiamond : IMountLeft, IMountRight { }
        private sealed class MountDiamond : IMountDiamond
        {
            public int Reads;
            public int Root() => 1;
            public IAdd Child { get { Reads++; return new Adder(); } }
        }

        [Test]
        public void InheritedPropertyDiamond_EvaluatesGetterOnce()
        {
            var target = new MountDiamond();
            using var binding = ServiceBinder.BindInterface<IMountDiamond>(Session, target);
            Assert.AreEqual(1, target.Reads);
            CollectionAssert.AreEquivalent(new[] { "Root", "Child.Add" }, binding.Methods);
        }

        [Test]
        public void RecursionOff_DoesNotEvaluateChildren()
        {
            var mount = new Mount { GetChild = () => throw new InvalidOperationException() };
            using var binding = ServiceBinder.BindInterface<IMount>(Session, mount, new RpcInterfaceBindingOptions { Recursive = false });
            Assert.AreEqual(0, mount.Reads);
            Assert.AreEqual(4, (int)Result("Root"));
            Absent("Child.Add");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullOrThrowingChild_LeavesNoPartialTree(bool throws)
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var mount = new Mount { GetChild = () => throws ? throw new InvalidOperationException("getter failed") : null };
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IMount>(Session, mount));
            StringAssert.Contains("Child", ex.Message);
            if (throws) Assert.IsInstanceOf<InvalidOperationException>(ex.InnerException);
            Assert.AreEqual(1, mount.Reads);
            Absent("Root", "Child.Add");
            Assert.AreEqual(17, (int)Result("before"));
        }

        private interface IA { int Root(); IB B { get; } }
        private interface IB { IA A { get; } }
        private sealed class Cycle : IA, IB
        {
            public int Root() => 1;
            public IB B => this;
            public IA A => this;
        }

        [Test]
        public void Cycle_UsesReferenceAndClosedInterfaceOnActivePath()
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IA>(Session, new Cycle()));
            StringAssert.Contains("cycle", ex.Message);
            StringAssert.Contains("B.A", ex.Message);
            Absent("Root", "B.A.Root");
            Assert.AreEqual(17, (int)Result("before"));
        }

        private interface IChain { int Value(); IChain Next { get; } }
        private sealed class Chain : IChain
        {
            public int Value() => 1;
            public IChain Next => new Chain();
        }

        [Test]
        public void FreshObjectRecursion_StopsAtDepthLimit()
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IChain>(Session, new Chain()));
            StringAssert.Contains("depth exceeds 32", ex.Message);
            CollectionAssert.AreEqual(new[] { "before" }, Services.Keys);
            Absent("Value", "Next.Value");
            Assert.AreEqual(17, (int)Result("before"));
        }

        private interface ITwoMounts { IMount Left { get; } IMount Right { get; } }
        private sealed class TwoMounts : ITwoMounts
        {
            public readonly Mount Shared = new Mount();
            public int Reads;
            public IMount Left { get { Reads++; return Shared; } }
            public IMount Right { get { Reads++; return Shared; } }
        }

        [Test]
        public void SameInstanceAtTwoPaths_EvaluatesGettersOncePerMount()
        {
            var root = new TwoMounts();
            using var binding = ServiceBinder.BindInterface<ITwoMounts>(Session, root);
            Assert.AreEqual(2, root.Reads);
            Assert.AreEqual(2, root.Shared.Reads);
            root.Shared.GetChild = () => throw new InvalidOperationException("must never be read per request");
            Assert.AreEqual(3, (int)Result("Left.Child.Add", "[1,2]"));
            Assert.AreEqual(7, (int)Result("Right.Child.Add", "[3,4]"));
            Assert.AreEqual(2, root.Shared.Reads);
        }

        [Test]
        public void OptionsAndCallbackPaths_AreSnapshots()
        {
            var options = new RpcInterfaceBindingOptions { Prefix = "fixed:" };
            options.Include = method =>
            {
                options.Prefix = "changed:";
                options.Separator = "/";
                options.Recursive = false;
                options.NameRule = _ => "wrong";
                var path = method.Path;
                if (path.Length != 0) path[0] = "changed";
                return true;
            };
            using var binding = ServiceBinder.BindInterface<IMount>(Session, new Mount(), options);
            CollectionAssert.AreEquivalent(new[] { "fixed:Root", "fixed:Child.Add" }, binding.Methods);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThrowingCallbacks_LeaveNoPartialTree(bool naming)
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var options = new RpcInterfaceBindingOptions();
            if (naming) options.NameRule = m => m.Path.Length == 0 ? m.DefaultName : throw new InvalidOperationException("name failed");
            else options.Include = m => m.Path.Length == 0 ? true : throw new InvalidOperationException("include failed");
            Assert.Throws<InvalidOperationException>(() => ServiceBinder.BindInterface<IMount>(Session, new Mount(), options));
            Absent("Root", "Child.Add");
            Assert.AreEqual(17, (int)Result("before"));
        }

        [Test]
        public void ExistingCollision_RejectsWholeTree()
        {
            ServiceBinder.BindMethod(Session, "Child.Add", () => 17);
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IMount>(Session, new Mount()));
            StringAssert.Contains("already registered", ex.Message);
            Absent("Root");
            Assert.AreEqual(17, (int)Result("Child.Add"));
        }

        [Test]
        public void CollisionAddedDuringDiscovery_IsRecheckedAtPublication()
        {
            var mount = new Mount { GetChild = () => { ServiceBinder.BindMethod(Session, "Root", () => 17); return new Adder(); } };
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IMount>(Session, mount));
            Assert.AreEqual(17, (int)Result("Root"));
            Absent("Child.Add");
        }

        private interface IDuplicateAliases { [JsonRpcMethod("same"), JsonRpcMethod("same")] int Value(); }
        private sealed class DuplicateAliases : IDuplicateAliases { public int Value() => 1; }
        private interface IDuplicateParameters { int Value([JsonRpcParam("same")] int a, [JsonRpcParam("same")] int b); }
        private sealed class DuplicateParameters : IDuplicateParameters { public int Value(int a, int b) => a + b; }

        [Test]
        public void DuplicateParameterNames_AreRejected()
        {
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IDuplicateParameters>(Session, new DuplicateParameters()));
            StringAssert.Contains("duplicate parameter name", ex.Message);
            Absent("Value");
        }
        private interface IOverloads { int Same(); int Same(int x); }
        private sealed class Overloads : IOverloads
        {
            public int Same() => 1;
            public int Same(int x) => x;
        }

        [Test]
        public void DuplicateAliases_RejectWholeTree()
        {
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IDuplicateAliases>(Session, new DuplicateAliases()));
            Absent("same");
        }

        [Test]
        public void OverloadsWithSameLeaf_RejectWholeTree()
        {
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IOverloads>(Session, new Overloads()));
            Absent("Same");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("rpc.reserved")]
        public void InvalidNames_RejectWholeTree(string name)
        {
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IAdd>(Session, new Adder(), new RpcInterfaceBindingOptions { NameRule = _ => name }));
            Assert.AreEqual(0, Services.Count);
        }

        [Test]
        public void InvalidArguments_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindInterface<IAdd>(null, new Adder()));
            Assert.Throws<ArgumentNullException>(() => ServiceBinder.BindInterface<IAdd>(Session, null));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface(Session, new Adder()));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IAdd>(Session, new Adder(), new RpcInterfaceBindingOptions { Prefix = null }));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IAdd>(Session, new Adder(), new RpcInterfaceBindingOptions { Separator = null }));
            Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IAdd>(Session, new Adder(), new RpcInterfaceBindingOptions { Casing = (RpcNameCasing)99 }));
        }

        [Test]
        public void Dispose_RemovesOwnedNamesAndIsIdempotent()
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var binding = ServiceBinder.BindInterface<IMount>(Session, new Mount());
            Assert.AreEqual(Session, binding.SessionId);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)binding.Methods)[0] = "other");
            binding.Dispose();
            binding.Dispose();
            Absent("Root", "Child.Add");
            Assert.AreEqual(17, (int)Result("before"));
            Assert.AreEqual(2, binding.Methods.Count);
        }

        [Test]
        public void Dispose_LeavesLaterRegistrationAlone()
        {
            var binding = ServiceBinder.BindInterface<IMount>(Session, new Mount());
            ServiceBinder.UnbindMethod(Session, "Root");
            ServiceBinder.BindMethod(Session, "Root", () => 99);
            binding.Dispose();
            binding.Dispose();
            Assert.AreEqual(99, (int)Result("Root"));
            Absent("Child.Add");
        }

        private sealed class Replacement { [JsonRpcMethod("Add")] public int Add() => 99; }

        [Test]
        public void Dispose_LeavesLegacyReplacementAlone()
        {
            var binding = ServiceBinder.BindInterface<IAdd>(Session, new Adder());
            ServiceBinder.BindService(Session, new Replacement());
            binding.Dispose();
            Assert.AreEqual(99, (int)Result("Add"));
        }

        [Test]
        public void Dispose_DoesNotTouchRecreatedSession()
        {
            var binding = ServiceBinder.BindInterface<IAdd>(Session, new Adder());
            Handler.DestroySession(Session);
            ServiceBinder.BindMethod(Session, "Add", () => 99);
            binding.Dispose();
            Assert.AreEqual(99, (int)Result("Add"));
        }

        private interface IVirtual { int Value(); }
        private class VirtualBase : IVirtual { public virtual int Value() => 1; }
        private class VirtualDerived : VirtualBase { public override int Value() => 2; }
        private sealed class SealedValue : IVirtual { public int Value() => 3; }

        [TestCase(false)]
        [TestCase(true)]
        public void MappedCall_PreservesOverridesAndSealedTargets(bool sealedTarget)
        {
            IVirtual target = sealedTarget ? new SealedValue() : new VirtualDerived();
            using var binding = ServiceBinder.BindInterface(Session, target);
            Assert.AreEqual(sealedTarget ? 3 : 2, (int)Result("Value"));
        }

        private interface ITaskContract
        {
            Task<int> Value(int x);
            [JsonRpcMethod("delayed", ContextFlow = RpcContextFlow.Flow)] ValueTask<string> Delayed(string text);
            Task Fire();
        }
        private sealed class TaskContract : ITaskContract
        {
            public int Fired;
            public Task<int> Value(int x) => Task.FromResult(x + 1);
            public async ValueTask<string> Delayed(string text) { await Task.Yield(); return text + "!"; }
            public Task Fire() { Fired++; return Task.CompletedTask; }
        }
        private interface IVoidContract { void Fire(); }
        private sealed class AsyncVoidContract : IVoidContract { public async void Fire() { await Task.Yield(); } }

        [Test]
        public async Task TaskReturn_IsServedThroughProcessAsync()
        {
            var impl = new TaskContract();
            using (ServiceBinder.BindInterface<ITaskContract>(Session, impl))
            {
                Assert.IsTrue(Services["Value"].Method.IsAsync);
                Assert.AreEqual(RpcContextFlow.None, Services["Value"].Method.ContextFlow);
                Assert.AreEqual(RpcContextFlow.Flow, Services["delayed"].Method.ContextFlow);
                Assert.AreEqual(typeof(int), Services["Value"].Method.ResultType);
                Assert.AreEqual(typeof(void), Services["Fire"].Method.ResultType);

                var value = JObject.Parse(await JsonRpcProcessor.ProcessAsync(Session, "{\"method\":\"Value\",\"params\":[41],\"id\":1}"));
                Assert.AreEqual(42, (int)value["result"]);
                var delayed = JObject.Parse(await JsonRpcProcessor.ProcessAsync(Session, "{\"method\":\"delayed\",\"params\":{\"text\":\"hi\"},\"id\":2}"));
                Assert.AreEqual("hi!", (string)delayed["result"]);
                var fire = JObject.Parse(await JsonRpcProcessor.ProcessAsync(Session, "{\"method\":\"Fire\",\"id\":3}"));
                Assert.IsTrue(fire["result"].Type == JTokenType.Null, fire.ToString());
                Assert.AreEqual(1, impl.Fired);

                // The synchronous entry points refuse to run an asynchronous registration.
                var sync = Call("Value", "[1]");
                Assert.AreEqual(-32603, (int)sync["error"]["code"]);
                StringAssert.Contains("is asynchronous", (string)sync["error"]["message"]);
            }
            Absent("Value", "delayed", "Fire");
        }

        [Test]
        public void AsyncVoidImplementation_IsRejected()
        {
            var ex = Assert.Throws<NotSupportedException>(() => ServiceBinder.BindInterface<IVoidContract>(Session, new AsyncVoidContract()));
            StringAssert.Contains("async void", ex.Message);
            Absent("Fire");
        }

        private interface IDefault { int Default() => 1; }
        private sealed class DefaultBody : IDefault { }

        [Test]
        public void DefaultInterfaceBody_IsRejectedClearly()
        {
            var ex = Assert.Throws<ArgumentException>(() => ServiceBinder.BindInterface<IDefault>(Session, new DefaultBody()));
            StringAssert.Contains("Default interface member", ex.Message);
            Absent("Default");
        }

        [Test]
        public void BuiltInNumericInvocation_AllocatesNothing()
        {
            using var binding = ServiceBinder.BindInterface<IAdd>(Session, new Adder());
            var serializer = SerializerCatalog.Create("jsmn");
            const string request = "{\"jsonrpc\":\"2.0\",\"method\":\"Add\",\"params\":[1,2],\"id\":1}";
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":1}", Run(request, serializer));
            var memory = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(request));
            using var output = new PooledByteBufferWriter(256);
            for (int i = 0; i < 500; i++) { output.Clear(); JsonRpcProcessor.Process(Session, memory, output, null, serializer); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) { output.Clear(); JsonRpcProcessor.Process(Session, memory, output, null, serializer); }
            long total = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, total, "undivided allocation total across 2000 warmed requests");
        }

        private interface IProtocol : IAdd
        {
            int Throw(int value);
            void Notify();
            string Context();
        }
        private sealed class Protocol : IProtocol
        {
            public int Notifications;
            public int Add(int x, int y) => x + y;
            public int Throw(int value) => throw new FormatException("method failure");
            public void Notify() => Notifications++;
            public string Context() => Handler.RpcRequestId() + "/" + JsonRpcContext.Current().Value;
        }

        [TestCaseSource(nameof(Serializers))]
        public void BatchNotificationsAndErrors_UseExistingProtocol(string name)
        {
            var serializer = SerializerCatalog.Create(name);
            var target = new Protocol();
            using var binding = ServiceBinder.BindInterface<IProtocol>(Session, target);
            var batch = JArray.Parse(Run("[{\"method\":\"Add\",\"params\":[1,2],\"id\":1},{\"method\":\"Notify\"},{\"method\":\"Throw\",\"params\":[1],\"id\":2}]", serializer));
            Assert.AreEqual(2, batch.Count);
            Assert.AreEqual(3, (int)batch[0]["result"]);
            Assert.AreEqual(1, (int)batch[0]["id"]);
            Assert.AreEqual(-32603, (int)batch[1]["error"]["code"]);
            Assert.AreEqual(2, (int)batch[1]["id"]);
            Assert.AreEqual(1, target.Notifications);
            Assert.AreEqual("", Run("[{\"method\":\"Notify\"},{\"method\":\"Throw\",\"params\":[1]}]", serializer));
            Assert.AreEqual(2, target.Notifications);
            var error = Call("Add", "{\"left\":1,\"right\":\"bad\"}", serializer)["error"];
            Assert.AreEqual(-32602, (int)error["code"]);
            Assert.AreEqual("conversion", (string)error["data"]["reason"]);
            Assert.AreEqual("right", (string)error["data"]["parameter"]);
            Assert.AreEqual(1, (int)error["data"]["index"]);
            Assert.AreEqual("int32", (string)error["data"]["expectedType"]);
        }

        [TestCaseSource(nameof(Serializers))]
        public void ContextAndRequestId_AreAvailable(string name)
        {
            using var binding = ServiceBinder.BindInterface<IProtocol>(Session, new Protocol());
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":\"abc/ctx\",\"id\":\"abc\"}", Run("{\"method\":\"Context\",\"id\":\"abc\"}", SerializerCatalog.Create(name), "ctx"));
            Assert.IsTrue(Handler.RpcRequestId().IsAbsent);
            Assert.IsNull(Handler.RpcContext());
        }

        [TestCaseSource(nameof(Serializers))]
        public void HookedRequests_UseTheSameMappedInvoker(string name)
        {
            using var binding = ServiceBinder.BindInterface<IAdd>(Session, new ExplicitAdder());
            Handler.GetSessionHandler(Session).SetPreProcessHandler((request, context) => null);
            Assert.AreEqual(7, (int)Result("Add", "[3,4]", SerializerCatalog.Create(name)));
        }

        [Test]
        public void DefaultSessionOverload()
        {
            using var binding = ServiceBinder.BindInterface<IAdd>(new Adder(), new RpcInterfaceBindingOptions { Prefix = "iface.default." });
            Assert.AreEqual(Handler.DefaultSessionId(), binding.SessionId);
            Assert.AreEqual("{\"jsonrpc\":\"2.0\",\"result\":3,\"id\":1}", JsonRpcProcessor.ProcessSync("{\"method\":\"iface.default.Add\",\"params\":[1,2],\"id\":1}"));
        }

        private interface ISpecialMembers
        {
            int Value();
            IAdd this[int index] { get; }
            IAdd WriteOnly { set; }
            event Action Changed;
            static int Static() => 99;
            private int Hidden() => 99;
        }
        private sealed class SpecialMembers : ISpecialMembers
        {
            public int Value() => 1;
            public IAdd this[int index] => throw new InvalidOperationException();
            public IAdd WriteOnly { set => throw new InvalidOperationException(); }
            public event Action Changed { add { } remove { } }
        }

        [Test]
        public void AccessorsEventsStaticsAndPrivateHelpers_AreExcluded()
        {
            using var binding = ServiceBinder.BindInterface<ISpecialMembers>(Session, new SpecialMembers());
            CollectionAssert.AreEqual(new[] { "Value" }, binding.Methods);
        }

        [Test]
        public void ConcurrentPublicationAndDisposal_ExposeWholeSnapshots()
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            var services = Services;
            var table = typeof(SMDServiceCollection).GetField("_table", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(services);
            var snapshotField = table.GetType().GetField("_snapshot", BindingFlags.NonPublic | BindingFlags.Instance);
            var entriesField = snapshotField.FieldType.GetField("Entries");
            object firstSnapshot = snapshotField.GetValue(table);
            int done = 0;
            Exception failure = null;
            using var started = new ManualResetEventSlim();
            using var observed = new ManualResetEventSlim();
            var worker = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        using var binding = ServiceBinder.BindInterface<IWorld>(Session, new World());
                        if (i == 0)
                        {
                            started.Set();
                            if (!observed.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
                        }
                    }
                }
                catch (Exception ex) { failure = ex; }
                finally { Volatile.Write(ref done, 1); started.Set(); }
            });
            worker.Start();
            try
            {
                Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)));
                object published = snapshotField.GetValue(table);
                Assert.AreEqual(8, ((Array)entriesField.GetValue(published)).Length);
                observed.Set();
                while (Volatile.Read(ref done) == 0)
                {
                    var snapshot = snapshotField.GetValue(table);
                    int count = ((Array)entriesField.GetValue(snapshot)).Length;
                    Assert.That(count, Is.EqualTo(1).Or.EqualTo(8), "one immutable table contains either the old registry or the whole tree");
                    Assert.That(services.Count, Is.EqualTo(1).Or.EqualTo(8));
                }
                Assert.AreEqual(8, ((Array)entriesField.GetValue(published)).Length, "published snapshots stay immutable after disposal");
            }
            finally { observed.Set(); Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10))); }
            Assert.IsNull(failure);
            Assert.AreEqual(1, ((Array)entriesField.GetValue(firstSnapshot)).Length);
            Assert.AreEqual(1, services.Count);
            Assert.AreEqual(17, (int)Result("before"));
        }

        [Test]
        public void Discovery_IsInvisibleUntilAllGettersComplete()
        {
            ServiceBinder.BindMethod(Session, "before", () => 17);
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            RpcBinding binding = null;
            Exception failure = null;
            var mount = new Mount { GetChild = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); return new Adder(); } };
            var worker = new Thread(() => { try { binding = ServiceBinder.BindInterface<IMount>(Session, mount); } catch (Exception ex) { failure = ex; } });
            worker.Start();
            try
            {
                Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
                CollectionAssert.AreEqual(new[] { "before" }, Services.Keys);
                Absent("Root", "Child.Add");
                Assert.AreEqual(17, (int)Result("before"));
            }
            finally { release.Set(); Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(10))); }
            Assert.IsNull(failure);
            using (binding)
            {
                Assert.AreEqual(4, (int)Result("Root"));
                Assert.AreEqual(3, (int)Result("Child.Add", "[1,2]"));
            }
        }
    }
}
