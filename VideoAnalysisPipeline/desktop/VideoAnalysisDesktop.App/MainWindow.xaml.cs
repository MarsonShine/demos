using System.Windows;
using VideoAnalysisDesktop.App.ViewModels;

namespace VideoAnalysisDesktop.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch
        {
            MessageBox.Show(
                "The local job database could not be initialized. Verify that this account can access the application data directory, then contact support if the problem persists.",
                "Video Analysis Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = !_viewModel.CanCloseWindow();
    }
}
