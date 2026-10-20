using System.Windows;
using Wdm.Core.Ipc;

namespace Wdm.Ui;

/// <summary>
/// WDM application startup. Enforces a single running instance: later launches (browser extension,
/// command line) forward their download request to the first instance over a named pipe and exit.
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private readonly CancellationTokenSource _pipeCts = new();

    /// <summary>Request supplied on this process's own command line, applied once the window is up.</summary>
    public ExternalDownloadRequest? StartupRequest { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var request = LaunchArguments.Parse(e.Args);

        _instanceMutex = new Mutex(initiallyOwned: true, $@"Local\WDM.SingleInstance.{Environment.UserName}", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Hand the request to the running instance (or just bring it forward) and quit
            Task.Run(() => WdmPipe.TrySendAsync(request.IsEmpty ? new ExternalDownloadRequest { ActivateOnly = true } : request, 3000)).GetAwaiter().GetResult();
            Shutdown();
            return;
        }

        StartupRequest = request.IsEmpty ? null : request;
        base.OnStartup(e);

        _ = WdmPipe.ListenAsync(OnExternalRequest, _pipeCts.Token);
    }

    private void OnExternalRequest(ExternalDownloadRequest request)
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is not MainWindow window) return;

            window.BringToFront();
            if (request.ActivateOnly) return;

            window.ViewModel.HandleExternalRequest(request);
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts.Cancel();
        try { _instanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
