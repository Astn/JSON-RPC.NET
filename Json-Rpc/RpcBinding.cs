using System;
using System.Collections.Generic;
using System.Linq;

namespace AustinHarris.JsonRpc
{
    /// <summary>Owns a published interface tree. Disposing it unbinds methods without disposing application objects.</summary>
    public sealed class RpcBinding : IDisposable
    {
        private readonly object _sync = new object();
        private readonly SMDServiceCollection _services;
        private IReadOnlyDictionary<string, SMDService> _entries;

        internal RpcBinding(string sessionId, SMDServiceCollection services, IReadOnlyDictionary<string, SMDService> entries)
        {
            SessionId = sessionId;
            Methods = Array.AsReadOnly(entries.Keys.ToArray());
            _services = services;
            _entries = entries;
        }

        /// <summary>The session on which this tree was registered.</summary>
        public string SessionId { get; }

        /// <summary>The immutable list of wire names originally published by this binding.</summary>
        public IReadOnlyList<string> Methods { get; }

        /// <summary>
        /// Removes, in one batch, entries still owned by this binding. Later replacements are left alone.
        /// Repeated calls do nothing. Already resolved calls can finish on their captured implementation.
        /// </summary>
        public void Dispose()
        {
            lock (_sync)
            {
                if (_entries == null) return;
                _services.RemoveBatch(_entries);
                _entries = null;
            }
        }
    }
}
