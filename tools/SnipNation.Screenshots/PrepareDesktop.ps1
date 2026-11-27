$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ScreenshotDesktop
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetSysColors(int count, int[] elements, int[] colors);

    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr window, int command);

    public static bool IsSetupHost(IntPtr process)
    {
        uint length = 256;
        var name = new StringBuilder((int)length);
        return GetPackageFamilyName(process, ref length, name) == 0 &&
            name.ToString() == "Microsoft.Windows.CloudExperienceHost_cw5n1h2txyewy";
    }

    public static void HideDesktopIcons()
    {
        EnumWindows((window, _) =>
        {
            var view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
            {
                var list = FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
                if (list != IntPtr.Zero)
                    ShowWindowAsync(list, 0);
            }
            return true;
        }, IntPtr.Zero);
    }
}
'@

# Close first-run windows in the current GitHub-hosted test session.
$runnerSession = (Get-Process -Id $PID).SessionId
Get-Process -Name WWAHost -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $runnerSession -and [ScreenshotDesktop]::IsSetupHost($_.Handle) } |
    ForEach-Object { [void]$_.CloseMainWindow() }

Start-Sleep -Milliseconds 800
Get-Process -Name wsl -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $runnerSession -and $_.MainWindowTitle -like '*\wsl.exe' } |
    ForEach-Object { [void]$_.CloseMainWindow() }

# Use a plain desktop without third-party shortcuts or wallpaper.
$palettePath = Join-Path $PSScriptRoot '../../assets/branding/palette.json'
$hex = (Get-Content $palettePath -Raw | ConvertFrom-Json).lightMuted.TrimStart('#')
$red = [Convert]::ToInt32($hex.Substring(0, 2), 16)
$green = [Convert]::ToInt32($hex.Substring(2, 2), 16)
$blue = [Convert]::ToInt32($hex.Substring(4, 2), 16)
$desktopColor = $red -bor ($green -shl 8) -bor ($blue -shl 16)
$setWallpaper = 20
$saveAndBroadcast = 3
$desktopBackground = 1
Set-ItemProperty -Path 'HKCU:\Control Panel\Colors' -Name Background -Value "$red $green $blue"
if (![ScreenshotDesktop]::SystemParametersInfo($setWallpaper, 0, '', $saveAndBroadcast) -or
    ![ScreenshotDesktop]::SetSysColors(1, @($desktopBackground), @($desktopColor))) {
    throw 'Could not set the desktop background.'
}

(New-Object -ComObject Shell.Application).MinimizeAll()
$hideWindow = 0
Get-Process -Name StartMenuExperienceHost -ErrorAction SilentlyContinue |
    Where-Object { $_.SessionId -eq $runnerSession -and $_.MainWindowHandle -ne 0 } |
    ForEach-Object { [void][ScreenshotDesktop]::ShowWindowAsync($_.MainWindowHandle, $hideWindow) }
[ScreenshotDesktop]::HideDesktopIcons()
