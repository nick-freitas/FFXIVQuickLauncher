using System.Diagnostics;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacLauncherServiceTests
{
    [TestMethod]
    public async Task LaunchAsyncReportsBootPatchesAndDoesNotLoginOrLaunch()
    {
        var client = new FakeXivLauncherClient
        {
            BootPatches = [CreatePatch("boot", "2026.04.26.0000.0000")],
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client));

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.PatchingRequired, result.Kind);
        Assert.AreEqual(1, result.PendingPatchCount);
        Assert.AreEqual(0, client.LoginCalls);
        Assert.AreEqual(0, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncReportsGamePatchesAndDoesNotLaunch()
    {
        var client = new FakeXivLauncherClient
        {
            LoginResult = new Launcher.LoginResult
            {
                State = Launcher.LoginState.NeedsPatchGame,
                PendingPatches = [CreatePatch("game", "2026.04.26.0000.0001")],
            },
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client));

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.PatchingRequired, result.Kind);
        Assert.AreEqual(1, result.PendingPatchCount);
        Assert.AreEqual(1, client.LoginCalls);
        Assert.AreEqual(0, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncLaunchesGameAfterSuccessfulLoginWithoutPatches()
    {
        var client = new FakeXivLauncherClient
        {
            LoginResult = new Launcher.LoginResult
            {
                State = Launcher.LoginState.Ok,
                UniqueId = "session-id",
                OauthLogin = new Launcher.OauthLoginResult
                {
                    Region = 3,
                    MaxExpansion = 5,
                    Playable = true,
                    TermsAccepted = true,
                    SessionId = "oauth-session",
                },
            },
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client));

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(1, client.LoginCalls);
        Assert.AreEqual(1, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncUsesNormalLaunchPathWhenExperimentalDalamudIsDisabled()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService();
        IMacLauncherService service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { ExperimentalDalamud = false },
            dalamud);
        var progress = new CollectingProgress<MacLaunchProgress>();

        var result = await service.LaunchAsync(CreateRequest(), progress);

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(1, client.LaunchCalls);
        Assert.AreEqual(0, client.DalamudLaunchCalls);
        Assert.AreEqual(0, dalamud.PrepareCalls);
        CollectionAssert.AreEqual(
            new[] { "Starting game..." },
            progress.Reports.Select(report => report.Message).ToArray());
    }

    [TestMethod]
    public async Task LaunchAsyncUsesDalamudLaunchPathWhenExperimentalDalamudIsEnabled()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Prepared(new FakeGameRunner()) };
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { ExperimentalDalamud = true },
            dalamud);
        var progress = new CollectingProgress<MacLaunchProgress>();

        var result = await service.LaunchAsync(CreateRequest(), progress);

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(0, client.LaunchCalls);
        Assert.AreEqual(1, client.DalamudLaunchCalls);
        Assert.AreEqual(1, dalamud.PrepareCalls);
        CollectionAssert.AreEqual(
            new[]
            {
                "Preparing Dalamud...",
                "Starting game with experimental Dalamud...",
            },
            progress.Reports.Select(report => report.Message).ToArray());
    }

    [TestMethod]
    public async Task LaunchAsyncReportsDalamudPreparationFailure()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Failed("Could not prepare Dalamud: network failed") };
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { ExperimentalDalamud = true },
            dalamud);

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.Failed, result.Kind);
        StringAssert.Contains(result.Message, "Could not prepare Dalamud");
        Assert.AreEqual(0, client.LaunchCalls);
        Assert.AreEqual(0, client.DalamudLaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncRejectsSteamBeforeLogin()
    {
        var client = new FakeXivLauncherClient();
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client));

        var result = await service.LaunchAsync(CreateRequest(isSteam: true));

        Assert.AreEqual(MacLaunchResultKind.Failed, result.Kind);
        StringAssert.Contains(result.Message, "Steam login is not supported");
        Assert.AreEqual(0, client.LoginCalls);
        Assert.AreEqual(0, client.LaunchCalls);
    }

    [TestMethod]
    public async Task XivLauncherClientDisablesUidCacheForLogin()
    {
        var launcher = new FakeLauncherCore();
        var client = new XivLauncherClient(launcher);

        await client.LoginAsync(CreateRequest());

        Assert.IsFalse(launcher.LastUseCache);
    }

    private static MacLaunchRequest CreateRequest(bool isSteam = false)
    {
        var install = new OfficialMacAppInstall(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/game-root"),
            new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/wine"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

        return new MacLaunchRequest(
            install,
            "user",
            "password",
            string.Empty,
            ClientLanguage.English,
            IsSteam: isSteam,
            IsFreeTrial: false);
    }

    private static PatchListEntry CreatePatch(string repository, string version)
        => new()
        {
            VersionId = version,
            HashType = "sha1",
            Url = $"http://patch-dl.ffxiv.com/game/4e9a232b/{repository}/{version}.patch",
            HashBlockSize = 0,
            Hashes = [],
            Length = 1,
        };

    private sealed class FakeXivLauncherClientFactory : IXivLauncherClientFactory
    {
        private readonly IXivLauncherClient client;

        public FakeXivLauncherClientFactory(IXivLauncherClient client)
        {
            this.client = client;
        }

        public Task<IXivLauncherClient> CreateAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(this.client);
    }

    private sealed class FakeXivLauncherClient : IXivLauncherClient
    {
        public PatchListEntry[] BootPatches { get; set; } = [];

        public Launcher.LoginResult LoginResult { get; set; } = new()
        {
            State = Launcher.LoginState.Ok,
            UniqueId = "session-id",
            OauthLogin = new Launcher.OauthLoginResult
            {
                Region = 3,
                MaxExpansion = 5,
                Playable = true,
                TermsAccepted = true,
                SessionId = "oauth-session",
            },
        };

        public int LoginCalls { get; private set; }

        public int LaunchCalls { get; private set; }

        public int DalamudLaunchCalls { get; private set; }

        public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default)
            => Task.FromResult(this.BootPatches);

        public Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        {
            this.LoginCalls++;
            return Task.FromResult(this.LoginResult);
        }

        public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
        {
            if (runner is null)
                this.LaunchCalls++;
            else
                this.DalamudLaunchCalls++;

            return true;
        }
    }

    private sealed class FakeMacDalamudService : IMacDalamudService
    {
        public MacDalamudPrepareResult Result { get; set; } = MacDalamudPrepareResult.Failed("not configured");

        public int PrepareCalls { get; private set; }

        public Task<MacDalamudPrepareResult> PrepareAsync(
            OfficialMacAppInstall install,
            DirectoryInfo gamePath,
            ClientLanguage language,
            CancellationToken cancellationToken)
        {
            this.PrepareCalls++;
            return Task.FromResult(this.Result);
        }
    }

    private sealed class FakeGameRunner : IGameRunner
    {
        public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DpiAwareness dpiAwareness)
            => null;
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        private readonly List<T> reports = [];

        public IReadOnlyList<T> Reports => this.reports;

        public void Report(T value)
            => this.reports.Add(value);
    }

    private sealed class FakeLauncherCore : IXivLauncherCore
    {
        public bool? LastUseCache { get; private set; }

        public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath)
            => Task.FromResult(Array.Empty<PatchListEntry>());

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
        {
            this.LastUseCache = useCache;

            return Task.FromResult(new Launcher.LoginResult
            {
                State = Launcher.LoginState.Ok,
                UniqueId = "session-id",
                OauthLogin = new Launcher.OauthLoginResult
                {
                    Region = 3,
                    MaxExpansion = 5,
                    Playable = true,
                    TermsAccepted = true,
                    SessionId = "oauth-session",
                },
            });
        }

        public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
            => true;
    }
}
