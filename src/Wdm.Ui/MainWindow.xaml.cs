using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Wdm.Ui.ViewModels;

namespace Wdm.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Internet Download Manager (WDM Engine)\nVersion 6.43 Build 15\n\nHigh-speed multi-connection segmented download engine powered by .NET 10.",
            "About Internet Download Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}