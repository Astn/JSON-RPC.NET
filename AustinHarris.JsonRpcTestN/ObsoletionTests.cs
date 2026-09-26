using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AustinHarris.JsonRpc;
using NUnit.Framework;

namespace AustinHarris.JsonRpcTestN
{
    /// <summary>Verifies the stable diagnostics on obsolete public APIs.</summary>
    [TestFixture]
    public class ObsoletionTests
    {
        private const string UrlFormat = "https://astn.github.io/JSON-RPC.NET/obsoletions.html#{0}";

        /// <summary>Checks the session pre-process alias diagnostic.</summary>
        [Test]
        public void SetBeforeProcessHandler_HasDiagnosticAndMigrationMessage()
        {
            AssertObsolete(typeof(Config), "SetBeforeProcessHandler", "JSONRPC0001",
                "Use SetPreProcessHandler(sessionId, handler).");
        }

        /// <summary>Checks the legacy registration diagnostic.</summary>
        [Test]
        public void RegisterFuction_HasDiagnosticAndMigrationMessage()
        {
            AssertObsolete(typeof(Handler), "RegisterFuction", "JSONRPC0002",
                "Use ServiceBinder.BindMethod; unlike RegisterFuction it throws when the name is already registered instead of replacing it.");
        }

        /// <summary>Checks the legacy unregistration diagnostic.</summary>
        [Test]
        public void UnRegisterFunction_HasDiagnosticAndMigrationMessage()
        {
            AssertObsolete(typeof(Handler), "UnRegisterFunction", "JSONRPC0003",
                "Use ServiceBinder.UnbindMethod.");
        }

        /// <summary>Checks that core obsoletion IDs are reserved, unique and discoverable.</summary>
        [Test]
        public void DiagnosticIds_AreUniqueAndInTheObsoletionRange()
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var ids = typeof(Config).Assembly.GetTypes()
                .SelectMany(type => new MemberInfo[] { type }.Concat(type.GetMembers(flags)))
                .Select(member => member.GetCustomAttribute<ObsoleteAttribute>())
                .Where(attribute => attribute != null && attribute.DiagnosticId != null)
                .Select(attribute => attribute.DiagnosticId)
                .ToArray();

            Assert.GreaterOrEqual(ids.Length, 3, "The scan must find the three current obsoletions.");
            CollectionAssert.Contains(ids, "JSONRPC0001");
            CollectionAssert.Contains(ids, "JSONRPC0002");
            CollectionAssert.Contains(ids, "JSONRPC0003");
            Assert.AreEqual(ids.Length, ids.Distinct(StringComparer.Ordinal).Count(), "Diagnostic IDs must be unique.");
            foreach (string id in ids)
                Assert.IsTrue(Regex.IsMatch(id, @"^JSONRPC0\d{3}$"), $"Unexpected diagnostic ID: {id}");
        }

        private static void AssertObsolete(Type declaringType, string memberName, string diagnosticId, string message)
        {
            var method = declaringType.GetMethod(memberName);
            Assert.NotNull(method);
            var attribute = method.GetCustomAttribute<ObsoleteAttribute>();
            Assert.NotNull(attribute);
            Assert.IsFalse(attribute.IsError);
#if NET5_0_OR_GREATER
            Assert.AreEqual(diagnosticId, attribute.DiagnosticId);
            Assert.AreEqual(UrlFormat, attribute.UrlFormat);
#endif
            Assert.AreEqual(message, attribute.Message);
        }
    }
}
