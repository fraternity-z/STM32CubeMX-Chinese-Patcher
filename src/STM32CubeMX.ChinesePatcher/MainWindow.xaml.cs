using System.Text;
using System.Windows;
using System.IO;
using Microsoft.Win32;
using STM32CubeMX.ChinesePatcher.ViewModels;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace STM32CubeMX.ChinesePatcher;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanBrowse)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "选择 STM32CubeMX 安装目录",
            Multiselect = false
        };
        if (Directory.Exists(_viewModel.PathDisplay))
        {
            dialog.InitialDirectory = _viewModel.PathDisplay;
        }

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.SelectManualPathAsync(dialog.FolderName);
        }
    }
}
