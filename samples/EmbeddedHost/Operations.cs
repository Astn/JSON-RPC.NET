using AustinHarris.JsonRpc;

namespace EmbeddedHost;

/// <summary>
/// The methods every host in this sample exposes. One instance is bound to one named session, so nothing else
/// in the process (another plugin, a library that also uses the default session) can reach or replace them.
/// </summary>
public sealed class Operations
{
    private readonly Dictionary<string, string> _notes = new();
    private readonly int? _ownerThread;

    /// <summary>The session this instance is bound to.</summary>
    public string Session { get; }

    /// <summary>Raised after <c>notes/set</c>; a stream host turns it into an outbound notification.</summary>
    public event Action<string> NoteChanged;

    /// <param name="session">A session name of your own: not the default session, which is shared process-wide.</param>
    /// <param name="ownerThread">When set, the state may only be touched on that thread (a UI or editor thread).</param>
    public Operations(string session, int? ownerThread = null)
    {
        Session = session;
        _ownerThread = ownerThread;
        ServiceBinder.BindService(session, this);
    }

    /// <summary>Unbinds everything: the session is gone, later requests for it answer -32601.</summary>
    public void Dispose() => Handler.DestroySession(Session);

    [JsonRpcMethod("add")]
    public double Add(double l, double r) => l + r;

    [JsonRpcMethod("notes/set")]
    public int Set(string key, string text)
    {
        Guard();
        _notes[key] = text;
        NoteChanged?.Invoke(key);
        return _notes.Count;
    }

    [JsonRpcMethod("notes/get")]
    public string Get(string key)
    {
        Guard();
        // An application error: a code outside -32768..-32000, the range JSON-RPC 2.0 reserves, with authored data.
        return _notes.TryGetValue(key, out var text) ? text : throw new JsonRpcException(1001, "Note not found", new NoteError { Key = key });
    }

    /// <summary>
    /// The cancellation token sits between the JSON parameters: C# wants optional parameters last, and the token
    /// is never bound from JSON, so <c>["wait", 50]</c> and <c>{"milliseconds":50,"result":9}</c> both work.
    /// </summary>
    [JsonRpcMethod("wait")]
    public async Task<int> Wait(int milliseconds, [JsonRpcCancellation] CancellationToken cancellationToken, int result = 7)
    {
        await Task.Delay(milliseconds, cancellationToken);
        return result;
    }

    /// <summary>The managed thread the method body runs on: how the check mode proves the UI-thread host works.</summary>
    [JsonRpcMethod("thread")]
    public int CurrentThread() => Environment.CurrentManagedThreadId;

    private void Guard()
    {
        if (_ownerThread is int owner && owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("The notes belong to thread " + owner + "; this call runs on " + Environment.CurrentManagedThreadId + ".");
    }

    public sealed class NoteError
    {
        public string Key { get; set; }
    }
}
