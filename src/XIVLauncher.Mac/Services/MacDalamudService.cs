using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Common.Unix;
using XIVLauncher.Mac.Settings;

namespace XIVLauncher.Mac.Services;

public interface IMacDalamudService
{
    Task<MacDalamudPrepareResult> PrepareAsync(
        OfficialMacAppInstall install,
        DirectoryInfo gamePath,
        ClientLanguage language,
        CancellationToken cancellationToken);
}

public interface IMacDalamudUpdaterFactory
{
    DalamudUpdater Create(MacDalamudPaths paths);
}

public interface IMacDalamudLauncherAdapter
{
    void RunUpdater(string? betaKind, string? betaKey, CancellationToken cancellationToken);

    DalamudLauncher.DalamudInstallState HoldForUpdate(DirectoryInfo gamePath, CancellationToken cancellationToken);

    IGameRunner CreateGameRunner();
}

public interface IMacDalamudLauncherAdapterFactory
{
    IMacDalamudLauncherAdapter Create(
        OfficialMacAppInstall install,
        IMacDalamudUpdaterFactory updaterFactory,
        DirectoryInfo gamePath,
        MacDalamudPaths paths,
        ClientLanguage language);
}

public sealed class MacDalamudUpdaterFactory : IMacDalamudUpdaterFactory
{
    private readonly HttpClient httpClient;

    public MacDalamudUpdaterFactory(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public DalamudUpdater Create(MacDalamudPaths paths)
        => new(
            this.httpClient,
            paths.AddonDirectory,
            paths.RuntimeDirectory,
            paths.AssetRootDirectory,
            cache: null,
            dalamudRolloutBucket: null)
        {
            Overlay = new NullDalamudLoadingOverlay(),
        };
}

public sealed class MacDalamudLauncherAdapterFactory : IMacDalamudLauncherAdapterFactory
{
    public IMacDalamudLauncherAdapter Create(
        OfficialMacAppInstall install,
        IMacDalamudUpdaterFactory updaterFactory,
        DirectoryInfo gamePath,
        MacDalamudPaths paths,
        ClientLanguage language)
    {
        var updater = updaterFactory.Create(paths);
        var launcher = new DalamudLauncher(
            new OfficialMacAppDalamudRunner(install, paths.RuntimeDirectory),
            updater,
            DalamudLoadMethod.DllInject,
            gamePath,
            paths.ConfigDirectory,
            paths.LogDirectory,
            language,
            injectionDelay: 0,
            fakeLogin: false,
            noPlugin: false,
            noThirdPlugin: false,
            troubleshootingData: "{}");

        return new MacDalamudLauncherAdapter(updater, launcher);
    }
}

public sealed class MacDalamudLauncherAdapter : IMacDalamudLauncherAdapter
{
    private readonly DalamudUpdater updater;
    private readonly DalamudLauncher launcher;

    public MacDalamudLauncherAdapter(DalamudUpdater updater, DalamudLauncher launcher)
    {
        this.updater = updater;
        this.launcher = launcher;
    }

    public void RunUpdater(string? betaKind, string? betaKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.updater.Run(betaKind, betaKey);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public DalamudLauncher.DalamudInstallState HoldForUpdate(DirectoryInfo gamePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this.updater.State != DalamudUpdater.DownloadState.Done)
            this.updater.ShowOverlay();

        while (this.updater.State != DalamudUpdater.DownloadState.Done)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.updater.State == DalamudUpdater.DownloadState.NoIntegrity)
            {
                this.updater.CloseOverlay();

                if (this.updater.EnsurementException != null)
                    throw new DalamudRunnerException("Updater returned no integrity.", this.updater.EnsurementException);

                throw new DalamudRunnerException("Updater returned no integrity.");
            }

            Thread.Yield();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!this.updater.Runner.Exists)
            throw new DalamudRunnerException("Runner did not exist.");

        var applicable = this.updater.ReCheckVersion(gamePath) ?? throw new DalamudRunnerException("ReCheckVersion returned null.");
        if (!applicable)
        {
            this.updater.SetOverlayProgress(IDalamudLoadingOverlay.DalamudUpdateStep.Unavailable);
            this.updater.ShowOverlay();

            return DalamudLauncher.DalamudInstallState.OutOfDate;
        }

        return DalamudLauncher.DalamudInstallState.Ok;
    }

    public IGameRunner CreateGameRunner()
        => new OfficialMacDalamudGameRunner(this.launcher);
}

public sealed class MacDalamudService : IMacDalamudService
{
    private readonly IMacDalamudUpdaterFactory updaterFactory;
    private readonly IMacDalamudLauncherAdapterFactory launcherAdapterFactory;
    private readonly string applicationSupportDirectory;

    public MacDalamudService()
        : this(
            new MacDalamudUpdaterFactory(new HttpClient()),
            new MacDalamudLauncherAdapterFactory(),
            MacSettingsService.DefaultApplicationSupportDirectory)
    {
    }

    public MacDalamudService(IMacDalamudUpdaterFactory updaterFactory)
        : this(
            updaterFactory,
            new MacDalamudLauncherAdapterFactory(),
            MacSettingsService.DefaultApplicationSupportDirectory)
    {
    }

    public MacDalamudService(
        IMacDalamudUpdaterFactory updaterFactory,
        IMacDalamudLauncherAdapterFactory launcherAdapterFactory,
        string applicationSupportDirectory)
    {
        this.updaterFactory = updaterFactory;
        this.launcherAdapterFactory = launcherAdapterFactory;
        this.applicationSupportDirectory = applicationSupportDirectory;
    }

    public async Task<MacDalamudPrepareResult> PrepareAsync(
        OfficialMacAppInstall install,
        DirectoryInfo gamePath,
        ClientLanguage language,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                () => this.Prepare(install, gamePath, language, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MacDalamudPrepareResult.Failed($"Could not prepare Dalamud: {ex.Message}");
        }
    }

    private MacDalamudPrepareResult Prepare(
        OfficialMacAppInstall install,
        DirectoryInfo gamePath,
        ClientLanguage language,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var paths = MacDalamudPaths.Create(this.applicationSupportDirectory);
        Directory.CreateDirectory(paths.ConfigDirectory.FullName);
        Directory.CreateDirectory(paths.PluginDirectory.FullName);
        Directory.CreateDirectory(paths.LogDirectory.FullName);

        var launcherAdapter = this.launcherAdapterFactory.Create(
            install,
            this.updaterFactory,
            gamePath,
            paths,
            language);

        launcherAdapter.RunUpdater(betaKind: null, betaKey: null, cancellationToken);

        var state = launcherAdapter.HoldForUpdate(gamePath, cancellationToken);
        if (state != DalamudLauncher.DalamudInstallState.Ok)
            return MacDalamudPrepareResult.Failed("Dalamud is unavailable for the game version.");

        return MacDalamudPrepareResult.Prepared(launcherAdapter.CreateGameRunner());
    }
}

public sealed record MacDalamudPaths(
    DirectoryInfo AddonDirectory,
    DirectoryInfo RuntimeDirectory,
    DirectoryInfo AssetRootDirectory,
    DirectoryInfo PluginDirectory,
    DirectoryInfo ConfigDirectory,
    DirectoryInfo LogDirectory)
{
    public static MacDalamudPaths Create(string applicationSupportDirectory)
        => new(
            new DirectoryInfo(Path.Combine(applicationSupportDirectory, "addon")),
            new DirectoryInfo(Path.Combine(applicationSupportDirectory, "runtime")),
            new DirectoryInfo(Path.Combine(applicationSupportDirectory, "dalamudAssets")),
            new DirectoryInfo(Path.Combine(applicationSupportDirectory, "installedPlugins")),
            new DirectoryInfo(applicationSupportDirectory),
            new DirectoryInfo(Path.Combine(applicationSupportDirectory, "logs")));
}

public sealed record MacDalamudPrepareResult(
    bool IsSuccess,
    IGameRunner? GameRunner,
    string? ErrorMessage)
{
    public static MacDalamudPrepareResult Prepared(IGameRunner gameRunner)
        => new(true, gameRunner, null);

    public static MacDalamudPrepareResult Failed(string message)
        => new(false, null, message);
}

public sealed class OfficialMacDalamudGameRunner : IGameRunner
{
    private readonly DalamudLauncher launcher;

    public OfficialMacDalamudGameRunner(DalamudLauncher launcher)
    {
        this.launcher = launcher;
    }

    public Process? Start(
        string path,
        string workingDirectory,
        string arguments,
        IDictionary<string, string> environment,
        DpiAwareness dpiAwareness)
        => this.launcher.Run(new FileInfo(path), arguments, environment);
}
