using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace SnipNation.Windows;

internal static class UserSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string SettingsKey = @"Software\SnipNation";
    private const string Name = "SnipNation";
    private const string ShortcutName = "CaptureShortcut";
    private const string ScreenshotFolderName = "ScreenshotFolder";

    internal static bool StartsAtLogon()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(Name) is string;
    }

    internal static bool SetLogonStartup(bool enabled)
    {
        try
        {
            if (enabled)
            {
                // Velopack replaces files in the same current directory on update.
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(Name, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            Trace.TraceWarning("Startup: {0}", error.Message);
            return false;
        }
    }

    internal static CaptureShortcut PreferredShortcut()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return key?.GetValue(ShortcutName) is (int)CaptureShortcut.Alternate
            ? CaptureShortcut.Alternate
            : CaptureShortcut.Print;
    }

    internal static bool SaveShortcut(CaptureShortcut shortcut) => SaveValue(ShortcutName, (int)shortcut);

    internal static string? LoadScreenshotFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(ScreenshotFolderName) is string path && Path.IsPathFullyQualified(path) ? path : null;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            Trace.TraceWarning("Read screenshot folder: {0}", error.Message);
            return null;
        }
    }

    internal static bool SaveScreenshotFolder(string folder) => SaveValue(ScreenshotFolderName, folder);

    private static bool SaveValue(string name, object value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
            key.SetValue(name, value);
            return true;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or IOException)
        {
            Trace.TraceWarning("Save setting {0}: {1}", name, error.Message);
            return false;
        }
    }
}
