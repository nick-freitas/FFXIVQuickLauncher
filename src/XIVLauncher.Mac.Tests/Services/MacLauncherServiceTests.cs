using System.Diagnostics;
using System.Net;
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
    public async Task LaunchAsyncReportsBootPatchesWhenPatchingFailsAndDoesNotLoginOrLaunch()
    {
        var client = new FakeXivLauncherClient
        {
            BootPatches = [CreatePatch("boot", "2026.04.26.0000.0000")],
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client), new FakeMacPatchService { Result = false });

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.PatchingRequired, result.Kind);
        Assert.AreEqual(1, result.PendingPatchCount);
        Assert.AreEqual(0, client.LoginCalls);
        Assert.AreEqual(0, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncAppliesBootPatchesThenRetriesLaunchFlow()
    {
        var client = new FakeXivLauncherClient
        {
            BootPatchResults = new Queue<PatchListEntry[]>(
            [
                [CreatePatch("boot", "2026.04.26.0000.0000", length: 100)],
                [],
            ]),
        };
        var patchService = new FakeMacPatchService();
        var progress = new CapturingProgress();
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { UseDalamud = false },
            new FakeMacDalamudService(),
            patchService);

        var result = await service.LaunchAsync(CreateRequest(), progress);

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(2, client.BootCheckCalls);
        Assert.AreEqual(1, client.LoginCalls);
        Assert.AreEqual(1, client.LaunchCalls);
        Assert.AreEqual(Repository.Boot, patchService.LastRepository);
        Assert.AreEqual(1, patchService.PatchCalls);
        Assert.IsTrue(progress.Reports.Any(x => x.PercentComplete == 50));
    }

    [TestMethod]
    public async Task LaunchAsyncReportsGamePatchesWhenPatchingFailsAndDoesNotLaunch()
    {
        var client = new FakeXivLauncherClient
        {
            LoginResult = new Launcher.LoginResult
            {
                State = Launcher.LoginState.NeedsPatchGame,
                PendingPatches = [CreatePatch("game", "2026.04.26.0000.0001")],
            },
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client), new FakeMacPatchService { Result = false });

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.PatchingRequired, result.Kind);
        Assert.AreEqual(1, result.PendingPatchCount);
        Assert.AreEqual(1, client.LoginCalls);
        Assert.AreEqual(0, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncAppliesGamePatchesThenRetriesLaunchFlow()
    {
        var client = new FakeXivLauncherClient
        {
            LoginResults = new Queue<Launcher.LoginResult>(
            [
                new Launcher.LoginResult
                {
                    State = Launcher.LoginState.NeedsPatchGame,
                    UniqueId = "patch-session-id",
                    PendingPatches = [CreatePatch("game", "2026.04.26.0000.0001", length: 200)],
                },
                CreateSuccessfulLoginResult(),
            ]),
        };
        var patchService = new FakeMacPatchService();
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { UseDalamud = false },
            new FakeMacDalamudService(),
            patchService);

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(2, client.LoginCalls);
        Assert.AreEqual(1, client.LaunchCalls);
        Assert.AreEqual(Repository.Ffxiv, patchService.LastRepository);
        Assert.AreEqual("patch-session-id", patchService.LastSessionId);
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
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { UseDalamud = false },
            new FakeMacDalamudService());

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(1, client.LoginCalls);
        Assert.AreEqual(1, client.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchAsyncUsesNormalLaunchPathWhenDalamudIsDisabled()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService();
        IMacLauncherService service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions { UseDalamud = false },
            dalamud);
        var progress = new CollectingProgress<MacLaunchProgress>();

        var result = await service.LaunchAsync(CreateRequest(), progress);

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(1, client.LaunchCalls);
        Assert.AreEqual(0, client.DalamudLaunchCalls);
        Assert.AreEqual(0, dalamud.PrepareCalls);
        Assert.AreEqual("Starting game...", progress.Reports.Last().Message);
        Assert.IsFalse(progress.Reports.Any(report => report.Message.Contains("Dalamud")));
    }

    [TestMethod]
    public async Task LaunchAsyncUsesDalamudLaunchPathByDefault()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Prepared(new FakeGameRunner()) };
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions(),
            dalamud);
        var progress = new CollectingProgress<MacLaunchProgress>();

        var result = await service.LaunchAsync(CreateRequest(), progress);

        Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
        Assert.AreEqual(0, client.LaunchCalls);
        Assert.AreEqual(1, client.DalamudLaunchCalls);
        Assert.AreEqual(1, dalamud.PrepareCalls);
        var messages = progress.Reports.Select(report => report.Message).ToArray();
        CollectionAssert.Contains(messages, "Preparing Dalamud...");
        Assert.AreEqual("Starting game with Dalamud...", messages.Last());
    }

    [TestMethod]
    public async Task LaunchAsyncReportsDalamudPreparationFailure()
    {
        var client = new FakeXivLauncherClient();
        var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Failed("Could not prepare Dalamud: network failed") };
        var service = new MacLauncherService(
            new FakeXivLauncherClientFactory(client),
            new MacLaunchOptions(),
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
    public async Task LaunchAsyncReportsNoServiceOauthDetails()
    {
        var client = new FakeXivLauncherClient
        {
            LoginResult = new Launcher.LoginResult
            {
                State = Launcher.LoginState.NoService,
                OauthLogin = new Launcher.OauthLoginResult
                {
                    Playable = false,
                    TermsAccepted = true,
                    Region = 3,
                    MaxExpansion = 5,
                    SessionId = "oauth-session",
                },
            },
        };
        var service = new MacLauncherService(new FakeXivLauncherClientFactory(client), new FakeMacPatchService());

        var result = await service.LaunchAsync(CreateRequest());

        Assert.AreEqual(MacLaunchResultKind.NoService, result.Kind);
        StringAssert.Contains(result.Message, "playable=False");
        StringAssert.Contains(result.Message, "termsAccepted=True");
        StringAssert.Contains(result.Message, "region=3");
        StringAssert.Contains(result.Message, "freeTrial=False");
        StringAssert.Contains(result.Message, "steam=False");
    }


    [TestMethod]
    public async Task XivLauncherClientDisablesUidCacheForLogin()
    {
        var launcher = new FakeLauncherCore();
        var client = new XivLauncherClient(launcher);

        await client.LoginAsync(CreateRequest());

        Assert.IsFalse(launcher.LastUseCache);
    }

    [TestMethod]
    public async Task XivLauncherClientFactoryUsesMacOauthUserAgent()
    {
        using var httpClient = new HttpClient(new FrontierConfigHandler());
        var factory = new XivLauncherClientFactory(httpClient);

        var client = (XivLauncherClient)await factory.CreateAsync();

        Assert.AreEqual(Launcher.MacOfficialUserAgent, client.OauthUserAgent);
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
        => CreatePatch(repository, version, length: 1);

    private static PatchListEntry CreatePatch(string repository, string version, long length)
        => new()
        {
            VersionId = version,
            HashType = "sha1",
            Url = $"http://patch-dl.ffxiv.com/game/4e9a232b/{repository}/{version}.patch",
            HashBlockSize = 0,
            Hashes = [],
            Length = length,
        };

    private static Launcher.LoginResult CreateSuccessfulLoginResult()
        => new()
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

        public Queue<PatchListEntry[]> BootPatchResults { get; set; } = [];

        public Launcher.LoginResult LoginResult { get; set; } = CreateSuccessfulLoginResult();

        public Queue<Launcher.LoginResult> LoginResults { get; set; } = [];

        public int BootCheckCalls { get; private set; }

        public int LoginCalls { get; private set; }

        public int LaunchCalls { get; private set; }

        public int DalamudLaunchCalls { get; private set; }

        public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default)
        {
            this.BootCheckCalls++;
            return Task.FromResult(this.BootPatchResults.Count > 0 ? this.BootPatchResults.Dequeue() : this.BootPatches);
        }

        public Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        {
            this.LoginCalls++;
            return Task.FromResult(this.LoginResults.Count > 0 ? this.LoginResults.Dequeue() : this.LoginResult);
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

    private sealed class FakeMacPatchService : IMacPatchService
    {
        public bool Result { get; set; } = true;

        public int PatchCalls { get; private set; }

        public Repository? LastRepository { get; private set; }

        public string? LastSessionId { get; private set; }

        public Task<bool> PatchAsync(
            Repository repository,
            PatchListEntry[] patches,
            MacLaunchRequest request,
            string? sessionId,
            IProgress<MacLaunchProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            this.PatchCalls++;
            this.LastRepository = repository;
            this.LastSessionId = sessionId;
            progress?.Report(new MacLaunchProgress(MacLaunchStage.Patching, $"{repository} patching", 50, TimeSpan.FromSeconds(10)));
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
        public string OauthUserAgent => Launcher.WindowsOfficialUserAgent;

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

    private sealed class FrontierConfigHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                        "frontierUrl": "https://launcher.finalfantasyxiv.com/v650/index.html?rc_lang={0}&time={1}"
                    }
                    """),
            });
    }

    private sealed class CapturingProgress : IProgress<MacLaunchProgress>
    {
        public List<MacLaunchProgress> Reports { get; } = [];

        public void Report(MacLaunchProgress value)
            => this.Reports.Add(value);
    }
}
