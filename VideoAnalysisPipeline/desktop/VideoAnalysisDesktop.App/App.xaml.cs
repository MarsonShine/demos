using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using VideoAnalysisDesktop.Application.Services;
using VideoAnalysisDesktop.Infrastructure.Data;
using VideoAnalysisDesktop.Infrastructure.Security;

namespace VideoAnalysisDesktop.App;

public partial class App : System.Windows.Application
{
    private const string ConfigureArgument = "--configure";
    public static IServiceProvider Services { get; private set; } = null!;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Credential configuration has a separate elevated entry point. The
        // normal client deliberately remains non-elevated while it processes
        // operator-selected media files.
        if (e.Args.Any(argument => string.Equals(argument, ConfigureArgument, StringComparison.Ordinal)))
        {
            base.OnStartup(e);
            var settingsWindow = new Views.AdminSettingsWindow(new SecretManager());
            settingsWindow.ShowDialog();
            Shutdown();
            return;
        }

        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "Global\\Company.VideoAnalysisDesktop",
                createdNew: out _ownsSingleInstanceMutex);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(
                "The application could not acquire its single-instance lock. Contact support.",
                "Video Analysis Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        if (!_ownsSingleInstanceMutex)
        {
            MessageBox.Show(
                "Video Analysis Desktop is already running. Use the existing window to monitor or cancel the active job.",
                "Video Analysis Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        base.OnStartup(e);

        var services = new ServiceCollection();

        // Infrastructure
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<JobRepository>();
        services.AddSingleton<SecretManager>();

        // Do not fall back to a machine-wide Python. The staged engine is part of
        // the product contract and avoids executing an arbitrary local environment.
        var runtime = RuntimePaths.TryResolveCurrent();
        if (runtime is null)
        {
            MessageBox.Show(
                "The bundled processing engine could not be found. " +
                "A deployed installation requires engine\\python\\python.exe and engine.manifest.json. " +
                "Reinstall the application or contact support.",
                "Video Analysis Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        // Source checkout debugging must not depend on an installer having
        // created ProgramData ACLs. Production engines retain their shared,
        // installer-managed ProgramData location.
        if (!runtime.IsStagedReleaseEngine)
        {
            Environment.SetEnvironmentVariable(
                DesktopDataPaths.DataRootEnvironmentVariable,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Company",
                    "VideoAnalysisDesktop"),
                EnvironmentVariableTarget.Process);
        }

        // Application services
        services.AddSingleton(new CommandBuilder(runtime.PythonExecutable, runtime.WorkingDirectory));
        services.AddSingleton<PreflightService>();
        services.AddSingleton<JobStateMachine>();

        services.AddTransient<ViewModels.MainViewModel>();
        services.AddTransient<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();
        Services = _serviceProvider;

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The OS already released it during an abnormal shutdown.
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _serviceProvider?.Dispose();
        _serviceProvider = null;
        base.OnExit(e);
    }
}
