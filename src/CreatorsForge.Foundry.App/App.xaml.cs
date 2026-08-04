using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CreatorsForge.Foundry.Workspaces;
using Microsoft.Win32;

namespace CreatorsForge.Foundry.App;

public partial class App : Application
{
    private static Mutex? runningMutex;
    private AppServices? services;
    private FoundryThemePreference themePreference = FoundryThemePreference.System;

    internal event EventHandler? ThemeChanged;

    internal FoundryThemePreference EffectiveTheme { get; private set; } =
        FoundryThemePreference.Dark;

    internal static bool IsHighContrast => SystemParameters.HighContrast;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        runningMutex = new Mutex(initiallyOwned: true, "CreatorsForge.Foundry", out _);

        Resources["CreatorForgeLogoImage"] = FoundrySvgLogo.Load();
        services = AppServices.CreateDefault();
        var settings = await services.Settings.LoadAsync(CancellationToken.None);
        themePreference = settings.Value.Theme;
        ApplyTheme(themePreference);
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        var isSmokeTest = e.Args.Contains(
            "--smoke-test",
            StringComparer.OrdinalIgnoreCase);
        try
        {
            if (isSmokeTest)
            {
                var smokeProjectPath = e.Args.FirstOrDefault(argument =>
                    argument.EndsWith(
                        ".foundryproj",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.EndsWith(
                        ".foundryworkspace",
                        StringComparison.OrdinalIgnoreCase));
                var smokeWindow = new MainWindow(
                    services,
                    smokeProjectPath,
                    isSmokeTest: true);
                MainWindow = smokeWindow;
                var succeeded = await smokeWindow.RunSmokeTestAsync(
                    CancellationToken.None);
                Shutdown(succeeded ? 0 : 1);
                return;
            }

            var window = new MainWindow(
                services,
                e.Args.FirstOrDefault(argument =>
                    argument.EndsWith(
                        ".foundryproj",
                        StringComparison.OrdinalIgnoreCase) ||
                    argument.EndsWith(
                        ".foundryworkspace",
                        StringComparison.OrdinalIgnoreCase)));
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            if (!isSmokeTest)
            {
                MessageBox.Show(
                    $"Creators Forge Foundry could not start.\n\n{exception.Message}",
                    "Creators Forge Foundry",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        runningMutex?.Dispose();
        base.OnExit(e);
    }

    private async void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var report = await WriteFailureReportAsync(e.Exception, "WPF dispatcher");
        MessageBox.Show($"Foundry encountered an unexpected error and will close to protect your work. Recovery snapshots remain available.\n\n{e.Exception.Message}\n\nA local report was written to:\n{report}",
            "Creators Forge Foundry", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteFailureReportAsync(exception, "Application domain").GetAwaiter().GetResult();
    }

    private async Task<string> WriteFailureReportAsync(Exception exception, string context)
    {
        try { return services is null ? "Unavailable" : await services.FailureReports.WriteAsync(exception, context); }
        catch { return "The report could not be written."; }
    }

    internal void ApplyTheme(FoundryThemePreference preference)
    {
        themePreference = preference;
        EffectiveTheme = FoundryThemeManager.Resolve(preference);
        FoundryThemeManager.Apply(Resources, preference, SystemParameters.HighContrast);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SystemParameters_StaticPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            ApplyTheme(themePreference);
        }
    }

    private void SystemEvents_UserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (themePreference == FoundryThemePreference.System)
        {
            Dispatcher.BeginInvoke(() => ApplyTheme(themePreference));
        }
    }
}
