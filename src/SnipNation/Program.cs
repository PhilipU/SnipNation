using SnipNation.Windows;
using Velopack;
using Velopack.Locators;

namespace SnipNation;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        using var instance = new Mutex(false, @"Local\SnipNation", out var primary);
        var locator = VelopackLocator.CreateDefaultForPlatform();

        VelopackApp.Build()
            .SetLocator(locator)
            .SetAutoApplyOnStartup(primary && UpdateService.CanAutoApply(locator, UserSettings.LoadPreferences().UpdateChannel))
            .OnAfterInstallFastCallback(_ => UserSettings.SetLogonStartup(true))
            .OnBeforeUninstallFastCallback(_ => UserSettings.SetLogonStartup(false))
            .Run();

        if (!primary)
            return 0;

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
