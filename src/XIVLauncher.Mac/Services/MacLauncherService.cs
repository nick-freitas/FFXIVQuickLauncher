using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Common.Unix;
using XIVLauncher.Common.Util;
using XIVLauncher.Mac.Settings;
using XIVLauncher.PlatformAbstractions;

namespace XIVLauncher.Mac.Services;

public interface IMacLauncherService
{
    Task<MacLaunchResult> LaunchAsync(MacLaunchRequest request, CancellationToken cancellationToken = default);
}

public interface IXivLauncherClientFactory
{
    Task<IXivLauncherClient> CreateAsync(CancellationToken cancellationToken = default);
}

public interface IXivLauncherClient
{
    Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default);

    Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default);

    bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request);
}

public sealed class MacLauncherService : IMacLauncherService
{
    private readonly IXivLauncherClientFactory clientFactory;

    public MacLauncherService()
        : this(new XivLauncherClientFactory(MacSettingsService.DefaultApplicationSupportDirectory))
    {
    }

    public MacLauncherService(IXivLauncherClientFactory clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public async Task<MacLaunchResult> LaunchAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
    {
        var client = await this.clientFactory.CreateAsync(cancellationToken);
        var bootPatches = await client.CheckBootVersionAsync(request.Install.GameRoot, cancellationToken);

        if (bootPatches.Length > 0)
            return MacLaunchResult.PatchingRequired(bootPatches.Length, "Boot");

        var loginResult = await client.LoginAsync(request, cancellationToken);

        if (loginResult.State == Launcher.LoginState.NoService)
            return MacLaunchResult.NoService();

        if (loginResult.State == Launcher.LoginState.NoTerms)
            return MacLaunchResult.NoTerms();

        if (loginResult.State == Launcher.LoginState.NeedsPatchBoot)
            return MacLaunchResult.PatchingRequired(Math.Max(1, loginResult.PendingPatches.Length), "Boot");

        if (loginResult.State == Launcher.LoginState.NeedsPatchGame || loginResult.PendingPatches.Length > 0)
            return MacLaunchResult.PatchingRequired(Math.Max(1, loginResult.PendingPatches.Length), "Game");

        if (loginResult.State != Launcher.LoginState.Ok)
            return MacLaunchResult.Failed($"Login returned unexpected state: {loginResult.State}.");

        if (string.IsNullOrWhiteSpace(loginResult.UniqueId) || loginResult.OauthLogin is null)
            return MacLaunchResult.Failed("Login succeeded but did not return launch session details.");

        return client.LaunchGame(loginResult, request)
            ? MacLaunchResult.Launched()
            : MacLaunchResult.Failed("Game process did not start.");
    }
}

public sealed class XivLauncherClientFactory : IXivLauncherClientFactory
{
    private readonly string applicationSupportDirectory;
    private readonly HttpClient httpClient;

    public XivLauncherClientFactory(string applicationSupportDirectory)
        : this(applicationSupportDirectory, new HttpClient())
    {
    }

    public XivLauncherClientFactory(string applicationSupportDirectory, HttpClient httpClient)
    {
        this.applicationSupportDirectory = applicationSupportDirectory;
        this.httpClient = httpClient;
    }

    public async Task<IXivLauncherClient> CreateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(this.applicationSupportDirectory);
        var frontierUrl = await DebugHelpers.GetFrontierUrlForDebugAsync(this.httpClient);
        var uidCachePath = new FileInfo(Path.Combine(this.applicationSupportDirectory, "uidCache.json"));
        var launcher = new Launcher((ISteam?)null, new CommonUniqueIdCache(uidCachePath), frontierUrl, ApiHelpers.GenerateAcceptLanguage());

        return new XivLauncherClient(launcher);
    }
}

public sealed class XivLauncherClient : IXivLauncherClient
{
    private readonly Launcher launcher;

    public XivLauncherClient(Launcher launcher)
    {
        this.launcher = launcher;
    }

    public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default)
        => this.launcher.CheckBootVersion(gamePath);

    public Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        => this.launcher.Login(
            request.Username,
            request.Password,
            request.Otp,
            request.IsSteam,
            useCache: true,
            request.Install.GameRoot,
            forceBaseVersion: false,
            request.IsFreeTrial,
            request.Language);

    public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request)
    {
        var process = this.launcher.LaunchGame(
            new OfficialMacAppGameRunner(request.Install),
            loginResult.UniqueId!,
            loginResult.OauthLogin!.Region,
            loginResult.OauthLogin.MaxExpansion,
            request.IsSteam,
            additionalArguments: string.Empty,
            request.Install.GameRoot,
            request.Language,
            encryptArguments: false,
            DpiAwareness.Unaware);

        return process is not null;
    }
}
