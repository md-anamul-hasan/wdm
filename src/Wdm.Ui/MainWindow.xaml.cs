using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Wdm.Ui.ViewModels;

namespace Wdm.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Theme initialization
        ViewModel.OnThemeChanged = ApplyTheme;
        ApplyTheme(ViewModel.IsDarkMode);

        // Keyboard shortcuts
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.New,
            (_, _) => OnAddUrlClick(this, new RoutedEventArgs())));
        InputBindings.Add(new KeyBinding(ApplicationCommands.New, Key.N, ModifierKeys.Control));

        // Ctrl+B for Batch Download
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => OnBatchDownloadClick(this, new RoutedEventArgs())),
            Key.B, ModifierKeys.Control));

        // Alt+Up / Alt+Down for queue reordering
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ViewModel.MoveUpSelected()),
            Key.Up, ModifierKeys.Alt));
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ViewModel.MoveDownSelected()),
            Key.Down, ModifierKeys.Alt));

        // Delete key to delete download
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => ViewModel.DeleteSelected()),
            Key.Delete, ModifierKeys.None));

        // Clipboard sniffer on window focus
        Activated += (_, _) => ViewModel.CheckClipboardForDownloadableLink();

        // Drag & drop support
        AllowDrop = true;
        DragOver += OnWindowDragOver;
        Drop += OnWindowDrop;

        // Auto-handle download URL if passed via command line (e.g. from browser extension or NativeHost)
        Loaded += (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
            {
                var arg = args[i].Trim('"', ' ');
                if (arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    ViewModel.OpenAddModal(arg);
                    break;
                }
            }
        };
    }

    private void ApplyTheme(bool isDark)
    {
        if (isDark)
        {
            Resources["ThemeWindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19"));
            Resources["ThemeSurfaceBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
            Resources["ThemeSurfaceAltBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            Resources["ThemeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
            Resources["ThemeTextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F9FAFB"));
            Resources["ThemeTextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1D5DB"));
            Resources["ThemeTextMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
            Resources["ThemeSubtleHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
            Resources["ThemeHeaderBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111827"));
            Resources["ThemeRowSelected"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
            Resources["ThemeRowSelectedText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#93C5FD"));
            Resources["ThemeCardBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F2937"));
        }
        else
        {
            Resources["ThemeWindowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            Resources["ThemeSurfaceBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
            Resources["ThemeSurfaceAltBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            Resources["ThemeBorder"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
            Resources["ThemeTextPrimary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
            Resources["ThemeTextSecondary"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#334155"));
            Resources["ThemeTextMuted"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
            Resources["ThemeSubtleHover"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            Resources["ThemeHeaderBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
            Resources["ThemeRowSelected"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E0F2FE"));
            Resources["ThemeRowSelectedText"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0369A1"));
            Resources["ThemeCardBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
        }
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.Text))
        {
            var text = e.Data.GetData(DataFormats.Text) as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                var trimmed = text.Trim();
                if (trimmed.Contains('\n') || trimmed.Contains('\r'))
                {
                    ViewModel.OpenBatchModal(trimmed);
                }
                else if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    ViewModel.OpenAddModal(trimmed);
                }
            }
        }
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToolBar toolBar)
        {
            if (toolBar.Template.FindName("OverflowGrid", toolBar) is FrameworkElement overflowGrid)
            {
                overflowGrid.Visibility = Visibility.Collapsed;
            }
            if (toolBar.Template.FindName("MainPanelBorder", toolBar) is FrameworkElement mainPanelBorder)
            {
                mainPanelBorder.Margin = new Thickness(0);
            }
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAddUrlClick(object sender, RoutedEventArgs e)
    {
        string? clipText = null;
        try
        {
            if (Clipboard.ContainsText())
            {
                var txt = Clipboard.GetText().Trim();
                if (txt.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    txt.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    clipText = txt;
                }
            }
        }
        catch { }

        ViewModel.OpenAddModal(clipText);
    }

    private void OnConfirmModalClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmAddDownload();
    }

    private void OnCancelModalClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseAddModal();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSettingsModal();
    }

    private void OnCancelSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseSettingsModal();
    }

    private void OnSaveSettingsClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveSettings();
    }

    private void OnBrowseSettingsFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Default Download Directory",
            InitialDirectory = ViewModel.Settings.DefaultDownloadDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.Settings.DefaultDownloadDirectory = dialog.FolderName;
            ViewModel.NewDownloadDir = dialog.FolderName;
        }
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder",
            InitialDirectory = ViewModel.NewDownloadDir
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.NewDownloadDir = dialog.FolderName;
        }
    }

    private void OnToggleSegmentPanelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSegmentPanel();
    }

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ResumeSelected();
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        ViewModel.PauseSelected();
    }

    private void OnStopAllClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StopAll();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteSelected();
    }

    private void OnDeleteCompletedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteCompleted();
    }

    private void OnOpenFileLocationClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenFileLocation(ViewModel.SelectedDownload);
    }

    private void OnOpenFileClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenFile(ViewModel.SelectedDownload);
    }

    private void OnCopyUrlClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedDownload != null && !string.IsNullOrWhiteSpace(ViewModel.SelectedDownload.Url))
        {
            try
            {
                Clipboard.SetText(ViewModel.SelectedDownload.Url);
            }
            catch { }
        }
    }

    private void OnCategoriesTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is string tag)
        {
            ViewModel.SelectedCategory = tag;
        }
    }

    private void OnStartQueueClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StartQueue();
    }

    private void OnStopQueueClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StopQueue();
    }

    private void OnSchedulerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenSchedulerModal();
    }

    private void OnSaveSchedulerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveSchedulerSettings();
    }

    private void OnCancelSchedulerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseSchedulerModal();
    }

    private void OnGrabberClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenGrabberModal();
    }

    private async void OnAnalyzeGrabberClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExecuteGrabberAnalyzeAsync();
    }

    private void OnAddGrabbedClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddGrabbedDownloads();
    }

    private void OnCancelGrabberClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseGrabberModal();
    }

    private void OnPropertiesClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenPropertiesModal(null);
    }

    private void OnClosePropertiesClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ClosePropertiesModal();
    }

    private void OnRedownloadClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RedownloadItem(null);
    }

    private void OnDeleteFileFromDiskClick(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Are you sure you want to permanently delete this downloaded file from disk?",
            "Confirm Delete from Disk",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            ViewModel.DeleteFileFromDisk(null);
        }
    }

    private void OnDownloadsGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedDownload != null)
        {
            if (ViewModel.SelectedDownload.IsCompleted)
            {
                ViewModel.OpenFile(ViewModel.SelectedDownload);
            }
            else
            {
                ViewModel.ResumeSelected();
            }
        }
    }

    // Batch Download Handlers
    private void OnBatchDownloadClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenBatchModal();
    }

    private void OnBatchFromClipboardClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenBatchModal();
        ViewModel.ExtractClipboardUrls();
    }

    private void OnConfirmBatchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ConfirmBatchDownload();
    }

    private void OnCancelBatchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CloseBatchModal();
    }

    private void OnExtractBatchClipboardClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ExtractClipboardUrls();
    }

    // Clipboard Sniffer Banner Handlers
    private void OnDownloadClipboardBannerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DownloadClipboardBanner();
    }

    private void OnDismissClipboardBannerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DismissClipboardBanner();
    }

    // Queue Reordering Handlers
    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveUpSelected();
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        ViewModel.MoveDownSelected();
    }

    // Speed Limiter & Theme Handlers
    private void OnToggleSpeedLimiterClick(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleSpeedLimiter();
    }

    private void OnToggleThemeClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDarkMode = !ViewModel.IsDarkMode;
    }

    // Quick Filter Chips & Search Handlers
    private void OnFilterTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            ViewModel.ActiveFilterTag = tag;
        }
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = string.Empty;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "WDM — Windows Download Manager\nVersion 1.0\n\nHigh-speed multi-connection segmented download manager.\nDynamic middle-split engine with zero-merge direct file writes.\nBuilt with .NET 10 and WPF.",
            "About WDM",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}