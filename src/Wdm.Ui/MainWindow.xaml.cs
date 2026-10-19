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
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeRestoreIcon != null && MaximizeRestoreBtn != null)
        {
            if (WindowState == WindowState.Maximized)
            {
                MaximizeRestoreIcon.Data = (Geometry)FindResource("GeoWinRestore");
                MaximizeRestoreBtn.ToolTip = "Restore";
            }
            else
            {
                MaximizeRestoreIcon.Data = (Geometry)FindResource("GeoWinMaximize");
                MaximizeRestoreBtn.ToolTip = "Maximize";
            }
        }
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2)
            {
                OnMaximizeClick(sender, e);
            }
            else
            {
                DragMove();
            }
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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

    private void OnPresetStreamClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int streams))
        {
            ViewModel.SetConnectionsPreset(streams);
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

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        ViewModel.DeleteSelected();
    }

    private void OnOpenFileLocationClick(object sender, RoutedEventArgs e)
    {
        ViewModel.OpenFileLocation(ViewModel.SelectedDownload);
    }

    // Row-level actions
    private void OnItemTogglePauseResume(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItemViewModel item)
        {
            if (item.IsActive)
            {
                ViewModel.PauseItem(item);
            }
            else
            {
                ViewModel.ResumeItem(item);
            }
        }
    }

    private void OnItemOpenFile(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItemViewModel item)
        {
            ViewModel.OpenFile(item);
        }
    }

    private void OnItemOpenFolder(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItemViewModel item)
        {
            ViewModel.OpenFileLocation(item);
        }
    }

    private void OnItemDelete(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItemViewModel item)
        {
            ViewModel.DeleteItem(item);
        }
    }

    private void OnItemCopyUrl(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is DownloadItemViewModel item && !string.IsNullOrWhiteSpace(item.Url))
        {
            try
            {
                Clipboard.SetText(item.Url);
            }
            catch { }
        }
    }
}