using SnipNation.Windows;
using Velopack;

namespace SnipNation;

internal static class Program
{
    [STAThread]
    public static int Main()
    {
        using var instance = new Mutex(false, @"Local\SnipNation", out var primary);

        // Only the primary process applies a pending update.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(primary)
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
