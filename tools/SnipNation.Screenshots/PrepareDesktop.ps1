$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class ScreenshotDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint
    {
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetSysColors(int count, int[] elements, int[] colors);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);

    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(ScreenPoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static IntPtr WindowAt(int x, int y) => GetAncestor(WindowFromPoint(new ScreenPoint { X = x, Y = y }), 2);

    public static int Owner(IntPtr window)
    {
        GetWindowThreadProcessId(window, out int processId);
        return processId;
    }

    public static bool CloseWindow(IntPtr window) => PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);

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
$screenWidth = 0
$screenHeight = 1
$centerX = [int]([ScreenshotDesktop]::GetSystemMetrics($screenWidth) / 2)
$centerY = [int]([ScreenshotDesktop]::GetSystemMetrics($screenHeight) / 2)
$topWindow = [ScreenshotDesktop]::WindowAt($centerX, $centerY)
$topProcess = Get-Process -Id ([ScreenshotDesktop]::Owner($topWindow)) -ErrorAction SilentlyContinue
if ($topProcess.ProcessName -eq 'WWAHost' -and [ScreenshotDesktop]::IsSetupHost($topProcess.Handle)) {
    $setupPolicy = 'HKCU:\Software\Policies\Microsoft\Windows\OOBE'
    New-Item -Path $setupPolicy -Force | Out-Null
    New-ItemProperty -Path $setupPolicy -Name DisablePrivacyExperience -Value 1 -PropertyType DWord -Force | Out-Null
    [void][ScreenshotDesktop]::CloseWindow($topWindow)
    Start-Sleep -Seconds 2
}

$topWindow = [ScreenshotDesktop]::WindowAt($centerX, $centerY)
$topProcess = Get-Process -Id ([ScreenshotDesktop]::Owner($topWindow)) -ErrorAction SilentlyContinue
if ($topProcess.ProcessName -eq 'wsl') {
    [void][ScreenshotDesktop]::CloseWindow($topWindow)
}

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
Start-Sleep -Milliseconds 350
$topWindow = [ScreenshotDesktop]::WindowAt($centerX, $centerY)
$topProcess = Get-Process -Id ([ScreenshotDesktop]::Owner($topWindow)) -ErrorAction SilentlyContinue
$hideWindow = 0
if ($topProcess.ProcessName -eq 'StartMenuExperienceHost') {
    [void][ScreenshotDesktop]::ShowWindowAsync($topWindow, $hideWindow)
}
[ScreenshotDesktop]::HideDesktopIcons()
