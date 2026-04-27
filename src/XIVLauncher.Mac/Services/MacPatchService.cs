using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.Patch;
using XIVLauncher.Common.Game.Patch.Acquisition;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.Util;
using XIVLauncher.Mac.Settings;
using XIVLauncher.PlatformAbstractions;

namespace XIVLauncher.Mac.Services;

public interface IMacPatchService
{
    Task<bool> PatchAsync(
        Repository repository,
        PatchListEntry[] patches,
        MacLaunchRequest request,
        string? sessionId,
        IProgress<MacLaunchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class MacPatchService : IMacPatchService
{
    private static readonly TimeSpan DefaultDownloadStallTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient httpClient;
    private readonly Launcher launcher;
    private readonly TimeSpan downloadStallTimeout;

    public MacPatchService()
        : this(new HttpClient())
    {
    }

    public MacPatchService(HttpClient httpClient)
        : this(httpClient, new Launcher((XIVLauncher.Common.PlatformAbstractions.ISteam?)null, new CommonUniqueIdCache(null), string.Empty, ApiHelpers.GenerateAcceptLanguage()))
    {
    }

    public MacPatchService(HttpClient httpClient, Launcher launcher)
        : this(httpClient, launcher, DefaultDownloadStallTimeout)
    {
    }

    public MacPatchService(HttpClient httpClient, Launcher launcher, TimeSpan downloadStallTimeout)
    {
        this.httpClient = httpClient;
        this.launcher = launcher;
        this.downloadStallTimeout = downloadStallTimeout;
    }

    public async Task<bool> PatchAsync(
        Repository repository,
        PatchListEntry[] patches,
        MacLaunchRequest request,
        string? sessionId,
        IProgress<MacLaunchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (patches.Length == 0)
            return true;

        var patchStore = new DirectoryInfo(Path.Combine(MacSettingsService.DefaultApplicationSupportDirectory, "patches"));
        var repoName = repository == Repository.Boot ? "Boot" : "Game";

        progress?.Report(new MacLaunchProgress(MacLaunchStage.Patching, $"{repoName} patching: preparing {patches.Length} patch(es)..."));

        using var installer = new PatchInstaller(request.Install.GameRoot, keepPatches: false);
        using var acquisition = new HttpPatchAcquisition(this.httpClient, this.downloadStallTimeout);
        var patcher = new PatchManager(acquisition, speedLimitBps: 0, repository, patches, request.Install.GameRoot, patchStore, installer, this.launcher, sessionId ?? string.Empty);

        patcher.OnFail += (patch, context) =>
            progress?.Report(new MacLaunchProgress(MacLaunchStage.Patching, $"Patch failed during {context}: {patch.VersionId}"));

        var progressReporter = new PatchProgressReporter(repository, patches, patcher, progress, this.downloadStallTimeout);
        await progressReporter.RunWhileAsync(() => patcher.PatchAsync(external: false), cancellationToken);

        return !patcher.IsCancelling;
    }

    private sealed class PatchProgressReporter
    {
        private readonly Repository repository;
        private readonly long totalBytes;
        private readonly PatchManager patcher;
        private readonly IProgress<MacLaunchProgress>? progress;
        private readonly TimeSpan downloadStallTimeout;
        private readonly Stopwatch stopwatch = new();
        private long lastCompletedBytes;
        private DateTimeOffset lastProgressAt = DateTimeOffset.UtcNow;
        private bool reportedStall;

        public PatchProgressReporter(
            Repository repository,
            PatchListEntry[] patches,
            PatchManager patcher,
            IProgress<MacLaunchProgress>? progress,
            TimeSpan downloadStallTimeout)
        {
            this.repository = repository;
            this.totalBytes = patches.Sum(x => x.Length);
            this.patcher = patcher;
            this.progress = progress;
            this.downloadStallTimeout = downloadStallTimeout;
        }

        public async Task RunWhileAsync(Func<Task<bool>> operation, CancellationToken cancellationToken)
        {
            this.stopwatch.Start();
            var operationTask = operation();

            while (!operationTask.IsCompleted)
            {
                this.ReportAndCancelIfStalled();
                await Task.Delay(500, cancellationToken);
            }

            this.ReportAndCancelIfStalled();
            await operationTask;
        }

        private void ReportAndCancelIfStalled()
        {
            var progressSnapshot = this.GetProgressSnapshot();

            if (progressSnapshot.CompletedBytes > this.lastCompletedBytes)
            {
                this.lastCompletedBytes = progressSnapshot.CompletedBytes;
                this.lastProgressAt = DateTimeOffset.UtcNow;
            }
            else if (!this.IsStallCandidate(progressSnapshot.Phase))
            {
                this.lastProgressAt = DateTimeOffset.UtcNow;
            }
            else if (!this.reportedStall && DateTimeOffset.UtcNow - this.lastProgressAt >= this.downloadStallTimeout)
            {
                this.reportedStall = true;
                var message = progressSnapshot.Phase == "Downloading"
                    ? $"Patch failed during Download: no data received for {FormatDuration(this.downloadStallTimeout)}."
                    : $"Patch failed before Download while {progressSnapshot.Phase}: no download started for {FormatDuration(this.downloadStallTimeout)}.";
                this.progress?.Report(new MacLaunchProgress(
                    MacLaunchStage.Patching,
                    message));
                this.patcher.StartCancellation();
                return;
            }

            if (this.progress is null)
                return;

            var remainingTimeout = this.IsStallCandidate(progressSnapshot.Phase)
                ? this.downloadStallTimeout - (DateTimeOffset.UtcNow - this.lastProgressAt)
                : TimeSpan.Zero;
            var timeoutText = progressSnapshot.CompletedBytes == 0 && remainingTimeout > TimeSpan.Zero
                ? $" - {GetTimeoutDescription(progressSnapshot.Phase)}, timeout in {FormatDuration(remainingTimeout)}"
                : string.Empty;

            this.progress.Report(new MacLaunchProgress(
                MacLaunchStage.Patching,
                $"{progressSnapshot.RepoName} patching: {progressSnapshot.Phase} {progressSnapshot.PercentText}{progressSnapshot.EtaText}{timeoutText}",
                progressSnapshot.PercentComplete,
                progressSnapshot.Eta));
        }

        private ProgressSnapshot GetProgressSnapshot()
        {
            var downloadedBytes = this.patcher.Progresses.Sum();
            var finishedBytes = this.patcher.Downloads
                .Where(x => x.State is PatchState.Downloaded or PatchState.IsInstalling or PatchState.Finished)
                .Sum(x => x.Patch.Length);
            var completedBytes = Math.Min(this.totalBytes, downloadedBytes + finishedBytes);
            double? percentComplete = this.totalBytes > 0
                ? Math.Round((double)completedBytes / this.totalBytes * 100, 1)
                : null;
            var bytesPerSecond = this.patcher.Speeds.Sum();
            TimeSpan? eta = bytesPerSecond > 0 && this.totalBytes > completedBytes
                ? TimeSpan.FromSeconds((this.totalBytes - completedBytes) / bytesPerSecond)
                : null;
            var phase = this.GetPhase();
            var repoName = this.repository == Repository.Boot ? "Boot" : "Game";
            var percentText = percentComplete.HasValue ? $"{percentComplete:0.#}%" : "preparing";
            var etaText = eta.HasValue ? $" - about {FormatDuration(eta.Value)} remaining" : string.Empty;

            return new ProgressSnapshot(completedBytes, phase, repoName, percentText, etaText, percentComplete, eta);
        }

        private readonly record struct ProgressSnapshot(
            long CompletedBytes,
            string Phase,
            string RepoName,
            string PercentText,
            string EtaText,
            double? PercentComplete,
            TimeSpan? Eta);

        private string GetPhase()
        {
            if (this.patcher.IsInstallerBusy || this.patcher.Downloads.Any(x => x.State == PatchState.IsInstalling))
                return "Installing";

            if (this.patcher.Slots.Any(x => x == PatchManager.SlotState.Checking))
                return "Validating";

            if (this.patcher.DownloadsDone)
                return "Finalizing";

            if (!this.patcher.Downloads.Any(x => x.State == PatchState.IsDownloading))
                return this.patcher.CurrentPhase;

            return "Downloading";
        }

        private bool IsStallCandidate(string phase)
            => phase is "Downloading" or "Starting installer" or "Waiting for installer" or "Starting downloader" or "Scheduling downloads";

        private static string GetTimeoutDescription(string phase)
            => phase == "Downloading" ? "waiting for server" : "waiting for download to start";

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";

            if (duration.TotalMinutes >= 1)
                return $"{duration.Minutes}m {duration.Seconds}s";

            return $"{Math.Max(1, duration.Seconds)}s";
        }
    }

    private sealed class HttpPatchAcquisition : IPatchAcquisition, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly TimeSpan downloadStallTimeout;

        public HttpPatchAcquisition(HttpClient httpClient, TimeSpan downloadStallTimeout)
        {
            this.httpClient = httpClient;
            this.downloadStallTimeout = downloadStallTimeout;
        }

        public Task StartIfNeededAsync(long speedLimitBps)
            => Task.CompletedTask;

        public PatchAcquisitionTask MakeTask(string url, FileInfo outFile)
            => new HttpPatchAcquisitionTask(this.httpClient, url, outFile, this.downloadStallTimeout);

        public Task SetGlobalSpeedLimitAsync(long speedLimitBps)
            => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class HttpPatchAcquisitionTask : PatchAcquisitionTask
    {
        private readonly HttpClient httpClient;
        private readonly string url;
        private readonly FileInfo outFile;
        private readonly TimeSpan downloadStallTimeout;
        private readonly CancellationTokenSource cancellationTokenSource = new();

        public HttpPatchAcquisitionTask(HttpClient httpClient, string url, FileInfo outFile, TimeSpan downloadStallTimeout)
        {
            this.httpClient = httpClient;
            this.url = url;
            this.outFile = outFile;
            this.downloadStallTimeout = downloadStallTimeout;
        }

        public override async Task StartAsync()
        {
            try
            {
                this.outFile.Directory!.Create();
                using var stallTokenSource = CancellationTokenSource.CreateLinkedTokenSource(this.cancellationTokenSource.Token);

                using var request = new HttpRequestMessage(HttpMethod.Get, this.url);
                request.Headers.UserAgent.ParseAdd(Constants.PatcherUserAgent);
                stallTokenSource.CancelAfter(this.downloadStallTimeout);
                using var response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stallTokenSource.Token);
                response.EnsureSuccessStatusCode();

                stallTokenSource.CancelAfter(this.downloadStallTimeout);
                await using var source = await response.Content.ReadAsStreamAsync(stallTokenSource.Token);
                await using var destination = this.outFile.Create();
                var buffer = new byte[128 * 1024];
                long downloaded = 0;
                var stopwatch = Stopwatch.StartNew();
                var lastReport = TimeSpan.Zero;

                while (true)
                {
                    stallTokenSource.CancelAfter(this.downloadStallTimeout);
                    var read = await source.ReadAsync(buffer, stallTokenSource.Token);
                    if (read == 0)
                        break;

                    await destination.WriteAsync(buffer.AsMemory(0, read), this.cancellationTokenSource.Token);
                    downloaded += read;
                    stallTokenSource.CancelAfter(this.downloadStallTimeout);

                    if (stopwatch.Elapsed - lastReport < TimeSpan.FromMilliseconds(500))
                        continue;

                    lastReport = stopwatch.Elapsed;
                    this.OnProgressChanged(new AcquisitionProgress
                    {
                        Progress = downloaded,
                        BytesPerSecondSpeed = stopwatch.Elapsed.TotalSeconds > 0 ? (long)(downloaded / stopwatch.Elapsed.TotalSeconds) : 0,
                    });
                }

                this.OnProgressChanged(new AcquisitionProgress
                {
                    Progress = downloaded,
                    BytesPerSecondSpeed = stopwatch.Elapsed.TotalSeconds > 0 ? (long)(downloaded / stopwatch.Elapsed.TotalSeconds) : 0,
                });
                this.OnComplete(AcquisitionResult.Success);
            }
            catch (OperationCanceledException ex)
            {
                if (this.cancellationTokenSource.IsCancellationRequested)
                {
                    this.OnComplete(AcquisitionResult.Cancelled);
                    return;
                }

                Serilog.Log.Error(ex, "Patch download stalled for {Url}", this.url);
                this.OnComplete(AcquisitionResult.Error);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Patch download failed for {Url}", this.url);
                this.OnComplete(AcquisitionResult.Error);
            }
        }

        public override Task CancelAsync()
        {
            this.cancellationTokenSource.Cancel();
            return Task.CompletedTask;
        }
    }
}
