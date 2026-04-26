using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.Game.Patch.PatchList;
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

    private static MacLaunchRequest CreateRequest()
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
            IsSteam: false,
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

        public Task<PatchListEntry[]> CheckBootVersionAsync(DirectoryInfo gamePath, CancellationToken cancellationToken = default)
            => Task.FromResult(this.BootPatches);

        public Task<Launcher.LoginResult> LoginAsync(MacLaunchRequest request, CancellationToken cancellationToken = default)
        {
            this.LoginCalls++;
            return Task.FromResult(this.LoginResult);
        }

        public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request)
        {
            this.LaunchCalls++;
            return true;
        }
    }
}
