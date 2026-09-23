using System;
using System.Collections.Generic;
using System.Text;

namespace AustinHarris.JsonRpc.Serialization
{
    /// <summary>
    /// A lookup keyed by UTF-8 bytes so a method can be resolved from the request span without
    /// allocating a string. Copy-on-write: readers are lock-free over one immutable snapshot (bucket heads
    /// indexing a contiguous entry array, no per-entry objects); writers build and publish a new snapshot.
    /// </summary>
    internal sealed class Utf8KeyTable<TValue> where TValue : class
    {
        private readonly struct Entry
        {
            public readonly byte[] Key;
            public readonly int Hash;
            /// <summary>Index of the next entry in the same bucket, or -1.</summary>
            public readonly int Next;
            public readonly TValue Value;

            public Entry(byte[] key, int hash, int next, TValue value)
            {
                Key = key;
                Hash = hash;
                Next = next;
                Value = value;
            }
        }

        private sealed class Snapshot
        {
            public readonly int[] Buckets;
            public readonly Entry[] Entries;

            public Snapshot(int[] buckets, Entry[] entries)
            {
                Buckets = buckets;
                Entries = entries;
            }
        }

        private static readonly Snapshot Empty = Build(Array.Empty<KeyValuePair<byte[], TValue>>());

        private Snapshot _snapshot = Empty;
        private readonly object _writeLock = new object();

        public TValue Find(ReadOnlySpan<byte> key)
        {
            var snapshot = _snapshot;
            var entries = snapshot.Entries;
            int hash = Utf8Json.Hash(key);
            for (int i = snapshot.Buckets[hash & (snapshot.Buckets.Length - 1)]; i >= 0; i = entries[i].Next)
            {
                ref readonly var e = ref entries[i];
                if (e.Hash == hash && key.SequenceEqual(e.Key)) return e.Value;
            }
            return null;
        }

        public TValue Find(string key) => Find(Encoding.UTF8.GetBytes(key));

        public void Set(string key, TValue value)
        {
            lock (_writeLock)
            {
                _snapshot = Rebuild(_snapshot, Encoding.UTF8.GetBytes(key), value, true);
            }
        }

        public bool Remove(string key)
        {
            lock (_writeLock)
            {
                var bytes = Encoding.UTF8.GetBytes(key);
                if (Find(bytes) == null) return false;
                _snapshot = Rebuild(_snapshot, bytes, null, false);
                return true;
            }
        }

        public void Clear()
        {
            lock (_writeLock)
            {
                _snapshot = Empty;
            }
        }

        // The caller holds the collection mutation lock. Build before publishing so a failed batch is invisible.
        internal void ReplaceAll(IReadOnlyDictionary<string, TValue> values)
        {
            lock (_writeLock)
            {
                var items = new List<KeyValuePair<byte[], TValue>>(values.Count);
                foreach (var item in values)
                    items.Add(new KeyValuePair<byte[], TValue>(Encoding.UTF8.GetBytes(item.Key), item.Value));
                var snapshot = Build(items);
                System.Threading.Volatile.Write(ref _snapshot, snapshot);
            }
        }

        /// <summary>The entries of <paramref name="source"/> without <paramref name="key"/>, plus (key, value) when <paramref name="add"/>.</summary>
        private static Snapshot Rebuild(Snapshot source, byte[] key, TValue value, bool add)
        {
            var items = new List<KeyValuePair<byte[], TValue>>(source.Entries.Length + 1);
            foreach (var e in source.Entries)
            {
                if (!new ReadOnlySpan<byte>(e.Key).SequenceEqual(key)) items.Add(new KeyValuePair<byte[], TValue>(e.Key, e.Value));
            }
            if (add) items.Add(new KeyValuePair<byte[], TValue>(key, value));
            return Build(items);
        }

        private static Snapshot Build(IReadOnlyList<KeyValuePair<byte[], TValue>> items)
        {
            int size = 16;
            while (size < items.Count) size *= 2;   // load factor at most 1
            var buckets = new int[size];
            for (int i = 0; i < size; i++) buckets[i] = -1;
            var entries = new Entry[items.Count];
            for (int i = 0; i < entries.Length; i++)
            {
                var kv = items[i];
                int hash = Utf8Json.Hash(kv.Key);
                int b = hash & (size - 1);
                entries[i] = new Entry(kv.Key, hash, buckets[b], kv.Value);
                buckets[b] = i;
            }
            return new Snapshot(buckets, entries);
        }
    }
}
