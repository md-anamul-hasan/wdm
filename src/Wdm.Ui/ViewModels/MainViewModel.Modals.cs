using System.Windows;
using Wdm.Core.Engine;
using Wdm.Core.Ipc;
using Wdm.Core.Models;

namespace Wdm.Ui.ViewModels;

public partial class MainViewModel
{
    public void OpenAddModal(string? initialUrl = null, bool keepPending = false)
    {
        if (!keepPending) PendingOptions = null;
        NewDownloadUrl = initialUrl ?? string.Empty;
        IsAddModalOpen = true;
    }

    public void CloseAddModal()
    {
        IsAddModalOpen = false;
        NewDownloadUrl = string.Empty;
        PendingOptions = null;
    }

    public void OpenSettingsModal()
    {
        _settingsSnapshot = Settings.Clone();
        IsSettingsModalOpen = true;
    }

    /// <summary>Cancel: discards every edit made since the dialog was opened.</summary>
    public void CloseSettingsModal()
    {
        if (_settingsSnapshot != null)
        {
            Settings.CopyFrom(_settingsSnapshot);
            _settingsSnapshot = null;
            SyncFromSettings();
        }

        IsSettingsModalOpen = false;
    }

    public void SaveSettings()
    {
        Settings.Normalize();
        Settings.Save();
        _settingsSnapshot = null;
        SyncFromSettings();
        RebuildSemaphore();
        IsSettingsModalOpen = false;
    }

    public void SetDefaultDownloadDirectory(string folder)
    {
        Settings.DefaultDownloadDirectory = folder;
        NewDownloadDir = folder;
        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Refreshes everything that mirrors <see cref="Settings"/> after it changed behind the bindings' back.</summary>
    private void SyncFromSettings()
    {
        NewDownloadDir = Settings.DefaultDownloadDirectory;
        NewDownloadConnections = Settings.DefaultConnections;

        _isSpeedLimiterEnabled = Settings.IsSpeedLimiterEnabled;
        _speedLimitKb = Settings.DefaultSpeedLimitKb;
        OnPropertyChanged(nameof(IsSpeedLimiterEnabled));
        OnPropertyChanged(nameof(SpeedLimitKb));
        UpdateSpeedLimits();

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(FreeDiskSpaceFormatted));
    }

    public void SetConnectionsPreset(int streams)
    {
        NewDownloadConnections = streams;
    }

    public void ToggleSegmentPanel()
    {
        IsSegmentPanelVisible = !IsSegmentPanelVisible;
    }

    // --- Site / Media Grabber Feature ---
    public void OpenGrabberModal()
    {
        GrabberUrl = string.Empty;
        GrabberStatusText = "Enter any webpage URL to crawl for downloadable media and files.";
        GrabberItems.Clear();
        IsGrabberModalOpen = true;
    }

    public void CloseGrabberModal()
    {
        IsGrabberModalOpen = false;
    }

    public async Task ExecuteGrabberAnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(GrabberUrl)) return;

        IsGrabberBusy = true;
        GrabberStatusText = "Analyzing URL and discovering media streams...";
        GrabberItems.Clear();

        try
        {
            var items = await _grabberService.GrabLinksAsync(GrabberUrl.Trim(), Settings.UserAgent);
            foreach (var it in items)
            {
                GrabberItems.Add(it);
            }

            GrabberStatusText = items.Count > 0
                ? $"Found {items.Count} downloadable files/streams."
                : "No downloadable media or direct links found on this page.";
        }
        catch (Exception ex)
        {
            GrabberStatusText = "Error during analysis: " + ex.Message;
        }
        finally
        {
            IsGrabberBusy = false;
        }
    }

    public void AddGrabbedDownloads()
    {
        var selected = GrabberItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        foreach (var item in selected)
        {
            AddDownload(item.Url, Settings.DefaultDownloadDirectory, Settings.DefaultConnections,
                new DownloadRequestOptions { Referer = GrabberUrl.Trim() });
        }

        CloseGrabberModal();
    }

    // --- Properties Modal ---
    public void OpenPropertiesModal(DownloadItemViewModel? item)
    {
        item ??= SelectedDownload;
        if (item == null) return;

        PropertiesItem = item;
        IsPropertiesModalOpen = true;
    }

    public void ClosePropertiesModal()
    {
        IsPropertiesModalOpen = false;
        PropertiesItem = null;
    }

    // --- Batch Download Feature ---
    public void OpenBatchModal(string? initialText = null, DownloadRequestOptions? options = null)
    {
        _batchOptions = options;
        BatchUrlsText = initialText ?? string.Empty;
        IsBatchModalOpen = true;
    }

    public void CloseBatchModal()
    {
        IsBatchModalOpen = false;
        _batchOptions = null;
    }

    public void ConfirmBatchDownload()
    {
        var dir = string.IsNullOrWhiteSpace(NewDownloadDir) ? Settings.DefaultDownloadDirectory : NewDownloadDir.Trim();
        foreach (var url in ParseUrlLines(BatchUrlsText))
        {
            AddDownload(url, dir, Settings.DefaultConnections, _batchOptions == null ? null : CloneSharedOptions(_batchOptions));
        }

        CloseBatchModal();
    }

    public void ExtractClipboardUrls()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"https?://[^\s""'<>]+");
                var urls = matches.Select(m => m.Value).Distinct().ToList();
                if (urls.Count > 0)
                {
                    var existing = string.IsNullOrWhiteSpace(BatchUrlsText) ? "" : BatchUrlsText + Environment.NewLine;
                    BatchUrlsText = existing + string.Join(Environment.NewLine, urls);
                }
            }
        }
        catch { }
    }

    // --- Clipboard Sniffer ---
    public void CheckClipboardForDownloadableLink()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var txt = Clipboard.GetText().Trim();
            if (ExternalDownloadRequest.IsHttpUrl(txt) &&
                !AllDownloads.Any(d => d.Url.Equals(txt, StringComparison.OrdinalIgnoreCase)))
            {
                if (txt != ClipboardBannerUrl)
                {
                    ClipboardBannerUrl = txt;
                    IsClipboardBannerVisible = true;
                }
            }
        }
        catch { }
    }

    public void DismissClipboardBanner()
    {
        IsClipboardBannerVisible = false;
    }

    public void DownloadClipboardBanner()
    {
        if (!string.IsNullOrWhiteSpace(ClipboardBannerUrl))
        {
            AddDownload(ClipboardBannerUrl, Settings.DefaultDownloadDirectory, Settings.DefaultConnections);
            DismissClipboardBanner();
        }
    }
}
