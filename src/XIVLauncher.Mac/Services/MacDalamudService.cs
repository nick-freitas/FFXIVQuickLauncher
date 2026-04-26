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

public sealed class MacDalamudService : IMacDalamudService
{
    private readonly IMacDalamudUpdaterFactory updaterFactory;
    private readonly string applicationSupportDirectory;

    public MacDalamudService()
        : this(
            new MacDalamudUpdaterFactory(new HttpClient()),
            MacSettingsService.DefaultApplicationSupportDirectory)
    {
    }

    public MacDalamudService(IMacDalamudUpdaterFactory updaterFactory)
        : this(updaterFactory, MacSettingsService.DefaultApplicationSupportDirectory)
    {
    }

    public MacDalamudService(IMacDalamudUpdaterFactory updaterFactory, string applicationSupportDirectory)
    {
        this.updaterFactory = updaterFactory;
        this.applicationSupportDirectory = applicationSupportDirectory;
    }

    public Task<MacDalamudPrepareResult> PrepareAsync(
        OfficialMacAppInstall install,
        DirectoryInfo gamePath,
        ClientLanguage language,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var paths = MacDalamudPaths.Create(this.applicationSupportDirectory);
            Directory.CreateDirectory(paths.ConfigDirectory.FullName);
            Directory.CreateDirectory(paths.PluginDirectory.FullName);
            Directory.CreateDirectory(paths.LogDirectory.FullName);

            var updater = this.updaterFactory.Create(paths);
            updater.Run(betaKind: null, betaKey: null);

            var launcher = new DalamudLauncher(
                new OfficialMacAppDalamudRunner(install),
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

            var state = launcher.HoldForUpdate(gamePath);
            if (state != DalamudLauncher.DalamudInstallState.Ok)
                return Task.FromResult(MacDalamudPrepareResult.Failed("Dalamud is unavailable for the game version."));

            return Task.FromResult(MacDalamudPrepareResult.Prepared(new OfficialMacDalamudGameRunner(launcher)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(MacDalamudPrepareResult.Failed($"Could not prepare Dalamud: {ex.Message}"));
        }
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
