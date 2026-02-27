using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Tech901.IdPhoto.App.Services;
using Tech901.IdPhoto.Core.Interfaces;
using Tech901.IdPhoto.Core.Services;
using Tech901.IdPhoto.Infrastructure.DependencyInjection;
using Tech901.IdPhoto.ViewModels;

namespace Tech901.IdPhoto.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddUserSecrets<App>(optional: true);
                config.AddEnvironmentVariables();
            })
            .UseSerilog((context, loggerConfig) =>
            {
                loggerConfig.ReadFrom.Configuration(context.Configuration);
            })
            .ConfigureServices((context, services) =>
            {
                // Core services
                services.AddSingleton<IDispatcher, WpfDispatcher>();
                services.AddSingleton<IRosterService, RosterService>();
                services.AddSingleton<IImageProcessingService, ImageProcessingService>();

                // Infrastructure services
                services.AddInfrastructure(context.Configuration);

                // ViewModels
                services.AddSingleton<KioskFlowViewModel>();
                services.AddTransient<IdleViewModel>();
                services.AddTransient<NameCaptureViewModel>();
                services.AddTransient<RosterMatchViewModel>();
                services.AddTransient<PositioningViewModel>();
                services.AddTransient<CaptureViewModel>();
                services.AddTransient<ReviewViewModel>();
                services.AddTransient<CompleteViewModel>();
                services.AddTransient<AdminViewModel>();
                services.AddTransient<BatchProcessViewModel>();
                services.AddTransient<ErrorViewModel>();

                // Main window
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var kioskVm = _host.Services.GetRequiredService<KioskFlowViewModel>();
        mainWindow.DataContext = kioskVm;
        mainWindow.Show();

        // Initialize the kiosk flow (starts camera, transitions to Idle)
        try
        {
            await kioskVm.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Camera initialization failed — starting in offline mode");
            kioskVm.TransitionToIdle();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI thread exception");

        MessageBox.Show(
            $"An unexpected error occurred:\n{e.Exception.Message}\n\nThe application will attempt to continue.",
            "Tech901 ID Photo — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("Unhandled non-CLR AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
