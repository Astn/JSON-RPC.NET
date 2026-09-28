using System.Collections.Concurrent;
using AustinHarris.JsonRpc;

namespace EmbeddedHost;

/// <summary>
/// A thread that owns the state: an editor loop, a desktop UI thread, a game's main thread. Requests arrive on
/// transport threads and are handed to this one; the method body, and every continuation after an await inside
/// it, runs here, because the thread installs itself as the <see cref="SynchronizationContext"/>. The core has no
/// dispatch context of its own (that is the 2.6 duplex work), so the host marshals whole documents.
/// </summary>
public sealed class UiThread : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback callback, object state)> _queue = new();
    private readonly Thread _thread;

    public int ManagedThreadId => _thread.ManagedThreadId;

    public UiThread()
    {
        _thread = new Thread(Loop) { Name = "ui", IsBackground = true };
        _thread.Start();
    }

    private void Loop()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
    }

    public override void Post(SendOrPostCallback d, object state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object state) => throw new NotSupportedException("Post only.");

    /// <summary>Runs <paramref name="work"/> on the thread and completes when the whole async chain has.</summary>
    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try { completion.SetResult(await work()); }
            catch (Exception ex) { completion.SetException(ex); }
        }, null);
        return completion.Task;
    }

    /// <summary>A <see cref="StreamHost.Dispatcher"/> that processes every document on this thread.</summary>
    public StreamHost.Dispatcher Dispatcher(string session) => (document, context, cancellationToken) =>
        RunAsync(async () =>
        {
            var output = new System.Buffers.ArrayBufferWriter<byte>();
            await JsonRpcProcessor.ProcessAsync(session, document, output, context: context, cancellationToken: cancellationToken);
            return (ReadOnlyMemory<byte>)output.WrittenMemory;
        });

    public void Dispose() => _queue.CompleteAdding();
}
