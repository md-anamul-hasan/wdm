using Wdm.Core.Engine;

namespace Wdm.Ui.ViewModels;

public partial class MainViewModel
{
    // --- Queue Priority Reordering ---
    public void MoveUpSelected()
    {
        if (SelectedDownload == null) return;
        int idx = AllDownloads.IndexOf(SelectedDownload);
        if (idx > 0)
        {
            AllDownloads.Move(idx, idx - 1);
            ApplyCategoryFilter();
            ScheduleSave();
        }
    }

    public void MoveDownSelected()
    {
        if (SelectedDownload == null) return;
        int idx = AllDownloads.IndexOf(SelectedDownload);
        if (idx >= 0 && idx < AllDownloads.Count - 1)
        {
            AllDownloads.Move(idx, idx + 1);
            ApplyCategoryFilter();
            ScheduleSave();
        }
    }

    public void ToggleSpeedLimiter()
    {
        IsSpeedLimiterEnabled = !IsSpeedLimiterEnabled;
    }

    /// <summary>
    /// Re-evaluates the filters and reconciles <see cref="FilteredDownloads"/> in place, so rows are not
    /// recreated (no flicker, selection and scroll position survive status updates).
    /// </summary>
    public void ApplyCategoryFilter()
    {
        var wanted = AllDownloads.Where(MatchesFilters).ToList();

        for (int i = FilteredDownloads.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(FilteredDownloads[i])) FilteredDownloads.RemoveAt(i);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < FilteredDownloads.Count && ReferenceEquals(FilteredDownloads[i], wanted[i])) continue;

            int existing = FilteredDownloads.IndexOf(wanted[i]);
            if (existing >= 0) FilteredDownloads.Move(existing, i);
            else FilteredDownloads.Insert(i, wanted[i]);
        }

        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(FreeDiskSpaceFormatted));
    }

    private bool MatchesFilters(DownloadItemViewModel item)
    {
        if (!MatchesCategory(item, SelectedCategory)) return false;

        bool matchesFilter = ActiveFilterTag switch
        {
            "Active" => item.IsActive || item.IsQueued,
            "Completed" => item.IsCompleted,
            "Paused" => item.IsPaused,
            _ => true
        };
        if (!matchesFilter) return false;

        return string.IsNullOrWhiteSpace(SearchText) ||
               item.FileName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Url.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.CategoryName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               item.Status.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(DownloadingCount));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(PausedCount));
        OnPropertyChanged(nameof(UnfinishedCount));
        OnPropertyChanged(nameof(FinishedCount));
        OnPropertyChanged(nameof(CompressedCount));
        OnPropertyChanged(nameof(DocumentsCount));
        OnPropertyChanged(nameof(MusicCount));
        OnPropertyChanged(nameof(ProgramsCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(HasNoDownloads));
        OnPropertyChanged(nameof(IsLibraryEmpty));
        NotifySelectionState();
    }

    private static bool MatchesCategory(DownloadItemViewModel item, string category)
    {
        if (category == "All Downloads") return true;
        if (category == "Downloading" || category == "Unfinished") return !item.IsCompleted;
        if (category == "Completed" || category == "Finished") return item.IsCompleted;
        if (category == "Paused") return item.IsPaused;

        return string.Equals(item.CategoryName, category, StringComparison.Ordinal);
    }
}
