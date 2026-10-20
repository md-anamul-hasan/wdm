using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Wdm.Core.Ipc;
using Wdm.Ui.ViewModels;

namespace Wdm.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Theme initialization (title bar is tinted once the window handle exists)
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
            new RelayCommand(_ => OnDeleteKey()),
            Key.Delete, ModifierKeys.None));

        // Clipboard sniffer on window focus
        Activated += (_, _) => ViewModel.CheckClipboardForDownloadableLink();

        // Drag & drop support
        AllowDrop = true;
        DragOver += OnWindowDragOver;
        Drop += OnWindowDrop;

        // Download passed on this process's own command line (/d url /p path /f name /n /a, or a bare URL)
        Loaded += (_, _) =>
        {
            if ((Application.Current as App)?.StartupRequest is { } request)
            {
                ViewModel.HandleExternalRequest(request);
            }
        };

        Closing += (_, _) => ViewModel.Shutdown();

        PreviewKeyDown += OnWindowPreviewKeyDown;
        InputBindings.Add(new KeyBinding(new RelayCommand(_ => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.F, ModifierKeys.Control));

        FocusAfterOpen(nameof(ViewModels.MainViewModel.IsAddModalOpen), () => AddUrlBox);
        FocusAfterOpen(nameof(ViewModels.MainViewModel.IsBatchModalOpen), () => BatchBox);
        FocusAfterOpen(nameof(ViewModels.MainViewModel.IsGrabberModalOpen), () => GrabberBox);
    }

    public void BringToFront()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        // Topmost toggle is the reliable way to defeat foreground-lock without P/Invoke
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private bool ConfirmDelete(int count)
    {
        if (!ViewModel.Settings.ConfirmBeforeDelete || count == 0) return true;

        var what = count == 1 ? "this download" : $"these {count} downloads";
        return MessageBox.Show(
            $"Remove {what} from the list? Unfinished downloads also lose their partial file.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    /// <summary>The Delete key removes a download only when it is not being used to edit text or inside a dialog.</summary>
    private void OnDeleteKey()
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox) return;
        if (ViewModel.IsAddModalOpen || ViewModel.IsSettingsModalOpen || ViewModel.IsSchedulerModalOpen ||
            ViewModel.IsGrabberModalOpen || ViewModel.IsPropertiesModalOpen || ViewModel.IsBatchModalOpen) return;

        DeleteSelectedWithConfirm();
    }

    private void DeleteSelectedWithConfirm()
    {
        if (ViewModel.SelectedDownload != null && ConfirmDelete(1))
        {
            ViewModel.DeleteSelected();
        }
    }

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["ThemeWindowBg"] = "#F4F6FA", ["ThemeSidebarBg"] = "#EDF0F6", ["ThemeSurfaceBg"] = "#FFFFFF", ["ThemeSurfaceAltBg"] = "#F1F4F9",
        ["ThemeInputBg"] = "#FFFFFF", ["ThemeHeaderBg"] = "#F8FAFC", ["ThemeCardBg"] = "#FFFFFF", ["ThemeBorder"] = "#E3E8F0",
        ["ThemeBorderStrong"] = "#C5CEDB", ["ThemeTextPrimary"] = "#0F172A", ["ThemeTextSecondary"] = "#334155", ["ThemeTextMuted"] = "#6B7A90",
        ["ThemeSubtleHover"] = "#E8EDF5", ["ThemeRowHover"] = "#F5F8FC", ["ThemeRowSelected"] = "#E6EEFF", ["ThemeRowSelectedText"] = "#0F172A",
        ["ThemeAccent"] = "#2563EB", ["ThemeAccentHover"] = "#1D4FD8", ["ThemeAccentPressed"] = "#1A44BA", ["ThemeAccentSoft"] = "#E0EAFF",
        ["ThemeOnAccent"] = "#FFFFFF", ["ThemeDanger"] = "#DC2626", ["ThemeDangerSoft"] = "#FEE2E2", ["ThemeSuccess"] = "#16A34A",
        ["ThemeWarning"] = "#D97706", ["ThemeOverlay"] = "#99000000",
    };

    private static readonly Dictionary<string, string> DarkPalette = new()
    {
        ["ThemeWindowBg"] = "#0E1117", ["ThemeSidebarBg"] = "#12161D", ["ThemeSurfaceBg"] = "#161B22", ["ThemeSurfaceAltBg"] = "#1C222B",
        ["ThemeInputBg"] = "#0F1319", ["ThemeHeaderBg"] = "#12171E", ["ThemeCardBg"] = "#1C222B", ["ThemeBorder"] = "#262D38",
        ["ThemeBorderStrong"] = "#3A4452", ["ThemeTextPrimary"] = "#E6EAF0", ["ThemeTextSecondary"] = "#B4BECC", ["ThemeTextMuted"] = "#8091A6",
        ["ThemeSubtleHover"] = "#222A35", ["ThemeRowHover"] = "#1A2029", ["ThemeRowSelected"] = "#1E2F52", ["ThemeRowSelectedText"] = "#E6EAF0",
        ["ThemeAccent"] = "#4C8DFF", ["ThemeAccentHover"] = "#6BA0FF", ["ThemeAccentPressed"] = "#3B7AEB", ["ThemeAccentSoft"] = "#1B2B4B",
        ["ThemeOnAccent"] = "#0B1220", ["ThemeDanger"] = "#F87171", ["ThemeDangerSoft"] = "#3B1818", ["ThemeSuccess"] = "#4ADE80",
        ["ThemeWarning"] = "#FBBF24", ["ThemeOverlay"] = "#B3000000",
    };

    /// <summary>Swaps the palette at application level so popups (menus, tooltips) follow the theme too.</summary>
    private void ApplyTheme(bool isDark)
    {
        var palette = isDark ? DarkPalette : LightPalette;
        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }

        ApplyTitleBarTheme(isDark, palette["ThemeSurfaceBg"], palette["ThemeTextPrimary"]);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Tints the native title bar to match the theme (Windows 11; harmlessly ignored on older builds).</summary>
    private void ApplyTitleBarTheme(bool isDark, string captionHex, string textHex)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE

        static int ToColorRef(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            return c.R | (c.G << 8) | (c.B << 16);
        }

        int caption = ToColorRef(captionHex);
        int text = ToColorRef(textHex);
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTheme(ViewModel.IsDarkMode);
    }

    private void FocusAfterOpen(string flagName, Func<Control> target)
    {
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != flagName) return;
            bool open = (bool)typeof(ViewModels.MainViewModel).GetProperty(flagName)!.GetValue(ViewModel)!;
            if (!open) return;

            Dispatcher.BeginInvoke(() =>
            {
                var control = target();
                control.Focus();
                if (control is TextBox box) box.SelectAll();
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    /// <summary>Esc closes whichever dialog is open.</summary>
    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;

        if (ViewModel.IsAddModalOpen) ViewModel.CloseAddModal();
        else if (ViewModel.IsSettingsModalOpen) ViewModel.CloseSettingsModal();
        else if (ViewModel.IsSchedulerModalOpen) ViewModel.CloseSchedulerModal();
        else if (ViewModel.IsGrabberModalOpen) ViewModel.CloseGrabberModal();
        else if (ViewModel.IsPropertiesModalOpen) ViewModel.ClosePropertiesModal();
        else if (ViewModel.IsBatchModalOpen) ViewModel.CloseBatchModal();
        else if (!string.IsNullOrEmpty(ViewModel.SearchText)) ViewModel.SearchText = string.Empty;
        else return;

        e.Handled = true;
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = ViewModel;
            menu.IsOpen = true;
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
                else if (ExternalDownloadRequest.IsHttpUrl(trimmed))
                {
                    ViewModel.OpenAddModal(trimmed);
                }
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
                if (ExternalDownloadRequest.IsHttpUrl(txt))
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
            ViewModel.SetDefaultDownloadDirectory(dialog.FolderName);
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
        DeleteSelectedWithConfirm();
    }

    private void OnDeleteCompletedClick(object sender, RoutedEventArgs e)
    {
        if (ConfirmDelete(ViewModel.CompletedCount))
        {
            ViewModel.DeleteCompleted();
        }
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

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = string.Empty;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
        MessageBox.Show(
            $"WDM — Windows Download Manager\nVersion {version}\n\nHigh-speed multi-connection segmented download manager.\nDynamic middle-split engine with zero-merge direct file writes.\nBuilt with .NET 10 and WPF.",
            "About WDM",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}