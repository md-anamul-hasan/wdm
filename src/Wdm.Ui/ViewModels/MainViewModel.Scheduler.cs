using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public partial class MainViewModel
{
    // --- Queue Management ---
    public void StartQueue()
    {
        IsQueueActive = true;
        _shutdownPending = false;
        foreach (var item in AllDownloads.Where(d => !d.IsCompleted && !d.IsRunning).Reverse().ToList())
        {
            ResumeItem(item);
        }
    }

    public void StopQueue()
    {
        IsQueueActive = false;
        StopAll();
    }

    // --- Scheduler Feature ---
    public void OpenSchedulerModal()
    {
        SchedulerEnabled = Settings.SchedulerEnabled;
        SchedulerStartTime = Settings.SchedulerStartTime;
        SchedulerStopTime = Settings.SchedulerStopTime;
        SchedulerShutdownOnComplete = Settings.SchedulerShutdownOnComplete;
        IsSchedulerModalOpen = true;
    }

    public void CloseSchedulerModal()
    {
        IsSchedulerModalOpen = false;
    }

    public void SaveSchedulerSettings()
    {
        // Free-text times are normalised to HH:mm; anything that is not a time of day keeps the previous value
        SchedulerStartTime = AppSettings.NormalizeTime(SchedulerStartTime, Settings.SchedulerStartTime);
        SchedulerStopTime = AppSettings.NormalizeTime(SchedulerStopTime, Settings.SchedulerStopTime);

        Settings.SchedulerEnabled = SchedulerEnabled;
        Settings.SchedulerStartTime = SchedulerStartTime;
        Settings.SchedulerStopTime = SchedulerStopTime;
        Settings.SchedulerShutdownOnComplete = SchedulerShutdownOnComplete;
        Settings.Save();
        IsSchedulerModalOpen = false;
    }

    private void StartSchedulerHeartbeat()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) => OnSchedulerTick();
        timer.Start();
    }

    private void OnSchedulerTick()
    {
        if (Settings.SchedulerEnabled)
        {
            var now = DateTime.Now.ToString("HH:mm");
            if (now == Settings.SchedulerStartTime && !IsQueueActive)
            {
                StartQueue();
            }
            else if (now == Settings.SchedulerStopTime && IsQueueActive && Settings.SchedulerStopQueueAtEndTime)
            {
                StopQueue();
            }
        }

        // While the queue runs, failed downloads are retried every 30 s (up to 5 attempts), like IDM's scheduler
        if (IsQueueActive)
        {
            foreach (var item in AllDownloads.Where(d => d.IsError && !d.IsRunning && d.RetryCount < 5 &&
                                                         (DateTime.Now - d.LastFailureAt).TotalSeconds >= 30).ToList())
            {
                ResumeItem(item);
            }
        }
    }

    /// <summary>When the active queue has nothing left to do and shutdown-on-complete is set, offers to power off.</summary>
    private void CheckQueueFinished()
    {
        if (!IsQueueActive) return;
        if (AllDownloads.Any(d => !d.IsCompleted && !d.IsPaused && !(d.IsError && d.RetryCount >= 5))) return;
        if (!AllDownloads.Any(d => d.IsCompleted)) return;

        IsQueueActive = false;
        if (!Settings.SchedulerShutdownOnComplete || _shutdownPending) return;

        _shutdownPending = true;
        SaveNow();
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 60 /c \"WDM: all downloads completed\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { return; }

        var answer = MessageBox.Show(
            "All downloads are complete. Windows will shut down in 60 seconds.\n\nClick Cancel to abort the shutdown.",
            "WDM — Shutdown", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Cancel)
        {
            try { Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); }
            catch { }
        }
    }
}
