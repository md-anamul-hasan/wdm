using Wdm.Core.Ipc;

namespace Wdm.Tests;

/// <summary>
/// A WDM IPC listener on a pipe name of its own, so tests cannot interfere with each other (or with a real WDM), and one that
/// is always stopped afterwards: a listener leaked by a failing test would otherwise swallow the next test's requests.
/// </summary>
internal sealed class PipeFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _listener;

    public string Name { get; } = $"WDM.Test.{Guid.NewGuid():N}";

    public PipeFixture(Action<ExternalDownloadRequest> onRequest, TimeSpan? readTimeout = null)
    {
        _listener = WdmPipe.ListenAsync(onRequest, _cts.Token, readTimeout, Name);
    }

    /// <summary>Sends until <paramref name="done"/> completes: a send may be accepted by a pipe instance that is just closing.</summary>
    public async Task SendUntilAsync(ExternalDownloadRequest request, Task done, int attempts = 60)
    {
        for (int i = 0; i < attempts && !done.IsCompleted; i++)
        {
            await WdmPipe.TrySendAsync(request, 300, Name);
            await Task.Delay(100);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _listener.WaitAsync(TimeSpan.FromSeconds(10)); } catch (Exception) { /* cancelled, or already faulted: nothing left to stop */ }
        _cts.Dispose();
    }
}
