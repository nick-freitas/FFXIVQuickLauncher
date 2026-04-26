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

    bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null);
}

public interface IMacPatchService
{
}

public sealed class MacPatchService : IMacPatchService
{
}

public sealed class MacLauncherService : IMacLauncherService
{
    private readonly IXivLauncherClientFactory clientFactory;
    private readonly MacLaunchOptions launchOptions;
    private readonly IMacDalamudService dalamudService;

    public MacLauncherService()
        : this(new XivLauncherClientFactory(), new MacPatchService(), new MacLaunchOptions(), new MacDalamudService())
    {
    }

    public MacLauncherService(MacLaunchOptions launchOptions)
        : this(new XivLauncherClientFactory(), new MacPatchService(), launchOptions, new MacDalamudService())
    {
    }

    public MacLauncherService(IXivLauncherClientFactory clientFactory)
        : this(clientFactory, new MacPatchService(), new MacLaunchOptions(), new MacDalamudService())
    {
    }

    public MacLauncherService(IXivLauncherClientFactory clientFactory, IMacPatchService patchService)
        : this(clientFactory, patchService, new MacLaunchOptions(), new MacDalamudService())
    {
    }

    public MacLauncherService(
        IXivLauncherClientFactory clientFactory,
        IMacPatchService patchService,
        MacLaunchOptions launchOptions,
        IMacDalamudService dalamudService)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(patchService);
        ArgumentNullException.ThrowIfNull(launchOptions);
        ArgumentNullException.ThrowIfNull(dalamudService);

        this.clientFactory = clientFactory;
        this.launchOptions = launchOptions;
        this.dalamudService = dalamudService;
    }

    public async Task<MacLaunchResult> LaunchAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
    {
        if (request.IsSteam)
            return MacLaunchResult.Failed("Steam login is not supported in the first Mac launcher pass.");

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

        if (this.launchOptions.ExperimentalDalamud)
        {
            var dalamud = await this.dalamudService.PrepareAsync(
                request.Install,
                request.Install.GameRoot,
                request.Language,
                cancellationToken);

            if (!dalamud.IsSuccess || dalamud.GameRunner is null)
                return MacLaunchResult.Failed(dalamud.ErrorMessage ?? "Could not prepare Dalamud.");

            return client.LaunchGame(loginResult, request, dalamud.GameRunner)
                ? MacLaunchResult.Launched()
                : MacLaunchResult.Failed("Game process did not start through experimental Dalamud.");
        }

        return client.LaunchGame(loginResult, request)
            ? MacLaunchResult.Launched()
            : MacLaunchResult.Failed("Game process did not start.");
    }
}

public sealed class XivLauncherClientFactory : IXivLauncherClientFactory
{
    private readonly HttpClient httpClient;

    public XivLauncherClientFactory()
        : this(new HttpClient())
    {
    }

    public XivLauncherClientFactory(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<IXivLauncherClient> CreateAsync(CancellationToken cancellationToken = default)
    {
        var frontierUrl = await DebugHelpers.GetFrontierUrlForDebugAsync(this.httpClient);
        var launcher = new Launcher((ISteam?)null, new CommonUniqueIdCache(null), frontierUrl, ApiHelpers.GenerateAcceptLanguage());

        return new XivLauncherClient(launcher);
    }
}

public interface IXivLauncherCore
{
    Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath);

    Task<Launcher.LoginResult> LoginAsync(
        string username,
        string password,
        string otp,
        bool isSteam,
        bool useCache,
        DirectoryInfo gamePath,
        bool forceBaseVersion,
        bool isFreeTrial,
        ClientLanguage language);

    bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null);
}

public sealed class XivLauncherClient : IXivLauncherClient
{
    private readonly IXivLauncherCore launcher;

    public XivLauncherClient(Launcher launcher)
        : this(new XivLauncherCore(launcher))
    {
    }

    public XivLauncherClient(IXivLauncherCore launcher)
    {
        this.launcher = launcher;
    }

    public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default)
        => this.launcher.CheckBootVersionAsync(gamePath);

    public Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        => this.launcher.LoginAsync(
            request.Username,
            request.Password,
            request.Otp,
            request.IsSteam,
            useCache: false,
            request.Install.GameRoot,
            forceBaseVersion: false,
            request.IsFreeTrial,
            request.Language);

    public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
        => this.launcher.LaunchGame(loginResult, request, runner);
}

public sealed class XivLauncherCore : IXivLauncherCore
{
    private readonly Launcher launcher;

    public XivLauncherCore(Launcher launcher)
    {
        this.launcher = launcher;
    }

    public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath)
        => this.launcher.CheckBootVersion(gamePath);

    public Task<Launcher.LoginResult> LoginAsync(
        string username,
        string password,
        string otp,
        bool isSteam,
        bool useCache,
        DirectoryInfo gamePath,
        bool forceBaseVersion,
        bool isFreeTrial,
        ClientLanguage language)
        => this.launcher.Login(
            username,
            password,
            otp,
            isSteam,
            useCache,
            gamePath,
            forceBaseVersion,
            isFreeTrial,
            language);

    public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
    {
        var process = this.launcher.LaunchGame(
            runner ?? new OfficialMacAppGameRunner(request.Install),
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
