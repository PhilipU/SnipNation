using System.Diagnostics;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SnipNation;

internal enum UpdateChannel
{
    Stable,
    Beta
}

internal sealed class UpdateService(IVelopackLocator locator, UpdateChannel? preferred)
{
    private const string Repository = "https://github.com/PhilipU/SnipNation";
    private const string StableFeed = "win";
    private const string BetaFeed = "beta";
    private readonly SemaphoreSlim _checkRequested = new(0, 1);
    private readonly SemaphoreSlim _packageLock = new(1, 1);
    private volatile UpdateChannel _channel = ResolveChannel(locator, preferred);
    private bool _restarting;

    internal bool IsInstalled => locator.CurrentlyInstalledVersion is not null;
    internal UpdateChannel Channel => _channel;

    internal static bool CanAutoApply(IVelopackLocator locator, UpdateChannel? preferred) =>
        locator.CurrentlyInstalledVersion is { } installed && locator.GetLatestLocalFullPackage() is { } package &&
        package.Version > installed && IsForChannel(locator, package, ResolveChannel(locator, preferred));

    private static UpdateChannel ResolveChannel(IVelopackLocator locator, UpdateChannel? preferred) =>
        preferred ?? (locator.Channel == BetaFeed ? UpdateChannel.Beta : UpdateChannel.Stable);

    internal void SelectChannel(UpdateChannel channel)
    {
        _channel = channel;
        RequestCheck();
    }

    internal void RequestCheck()
    {
        if (_checkRequested.CurrentCount == 0)
            _checkRequested.Release();
    }

    internal UpdateManager CreateManager(UpdateChannel channel, IUpdateSource? source = null) => new(
        source ?? new GithubSource(Repository, accessToken: null, prerelease: channel == UpdateChannel.Beta),
        new UpdateOptions
        {
            ExplicitChannel = channel == UpdateChannel.Beta ? BetaFeed : StableFeed,
            AllowVersionDowngrade = channel == UpdateChannel.Stable && locator.Channel == BetaFeed
        }, locator);

    internal async Task RunAsync(Action<UpdateChannel, VelopackAsset> ready, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckAsync(Channel, ready, cancellationToken);
                await _checkRequested.WaitAsync(TimeSpan.FromDays(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckAsync(UpdateChannel channel, Action<UpdateChannel, VelopackAsset> ready,
        CancellationToken cancellationToken)
    {
        try
        {
            var manager = CreateManager(channel);
            var update = await manager.CheckForUpdatesAsync();
            if (update is null || !IsForChannel(locator, update.TargetFullRelease, channel))
                return;

            await _packageLock.WaitAsync(cancellationToken);
            try
            {
                if (_restarting || channel != Channel)
                    return;
                await manager.DownloadUpdatesAsync(update, cancelToken: cancellationToken);
                if (!cancellationToken.IsCancellationRequested && channel == Channel)
                    ready(channel, update.TargetFullRelease);
            }
            finally
            {
                _packageLock.Release();
            }
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Trace.TraceWarning("Update check: {0}", error.Message);
        }
    }

    internal async Task<bool> ScheduleRestartAsync(VelopackAsset package, CancellationToken cancellationToken)
    {
        await _packageLock.WaitAsync(cancellationToken);
        try
        {
            var channel = Channel;
            if (!IsForChannel(locator, package, channel) || locator.PackagesDir is not { } directory ||
                !File.Exists(Path.Combine(directory, Path.GetFileName(package.FileName))))
                return false;

            if (channel == UpdateChannel.Stable)
            {
                // Older stable builds would also auto-apply a newer cached beta on their next launch.
                foreach (var cached in locator.GetLocalPackages().Where(asset => IsForChannel(locator, asset, UpdateChannel.Beta)))
                    File.Delete(Path.Combine(directory, cached.FileName));
            }

            CreateManager(channel).WaitExitThenApplyUpdates(package);
            _restarting = true;
            return true;
        }
        finally
        {
            _packageLock.Release();
        }
    }

    private static bool IsForChannel(IVelopackLocator locator, VelopackAsset package, UpdateChannel channel) =>
        package.PackageId == locator.AppId && package.Type == VelopackAssetType.Full &&
        (channel == UpdateChannel.Beta
            ? package.Version.Release.StartsWith("beta.", StringComparison.Ordinal)
            : !package.Version.IsPrerelease);
}
