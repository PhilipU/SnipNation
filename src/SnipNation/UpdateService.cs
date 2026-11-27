using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace SnipNation;

internal sealed class UpdateService
{
    private readonly UpdateManager _manager =
        new(new GithubSource("https://github.com/PhilipU/SnipNation", accessToken: null, prerelease: false));

    internal bool IsInstalled => _manager.IsInstalled;

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        try
        {
            do
            {
                await CheckAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            if (update is not null && _manager.UpdatePendingRestart?.Version != update.TargetFullRelease.Version)
                await _manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Trace.TraceWarning("Update check: {0}", error.Message);
        }
    }
}
