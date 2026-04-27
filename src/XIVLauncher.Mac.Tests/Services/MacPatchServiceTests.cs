using System.Net;
using XIVLauncher.Common;
using XIVLauncher.Common.Game;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.Game.Patch.PatchList;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Mac.Services;
using XIVLauncher.PlatformAbstractions;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacPatchServiceTests
{
    [TestMethod]
    public async Task PatchAsyncReturnsFailureWhenDownloadNeverResponds()
    {
        using var tempDirectory = new TempDirectory();
        using var httpClient = new HttpClient(new StalledHttpHandler())
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        var service = new MacPatchService(
            httpClient,
            new Launcher((ISteam?)null, new CommonUniqueIdCache(null), string.Empty, "en-US"),
            TimeSpan.FromMilliseconds(50));
        var progress = new CapturingProgress();

        var result = await service.PatchAsync(
            Repository.Ffxiv,
            [CreatePatch()],
            CreateRequest(tempDirectory.Directory),
            sessionId: "patch-session",
            progress);

        Assert.IsFalse(result);
        Assert.IsTrue(progress.Reports.Any(x => x.Message.Contains("Patch failed during Download")));
    }

    private static MacLaunchRequest CreateRequest(DirectoryInfo directory)
    {
        var appBundle = directory.CreateSubdirectory("FINAL FANTASY XIV ONLINE.app");
        var gameRoot = appBundle.CreateSubdirectory("game-root");
        var prefix = appBundle.CreateSubdirectory("prefix");
        var wine = new FileInfo(Path.Combine(appBundle.FullName, "wine"));

        return new MacLaunchRequest(
            new OfficialMacAppInstall(appBundle, gameRoot, wine, prefix),
            "user",
            "password",
            string.Empty,
            ClientLanguage.English,
            IsSteam: false,
            IsFreeTrial: false);
    }

    private static PatchListEntry CreatePatch()
        => new()
        {
            VersionId = "2026.04.26.0000.0001",
            HashType = "sha1",
            Url = "http://patch-dl.ffxiv.com/game/4e9a232b/2026.04.26.0000.0001.patch",
            HashBlockSize = 0,
            Hashes = [],
            Length = 1,
        };

    private sealed class StalledHttpHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class CapturingProgress : IProgress<MacLaunchProgress>
    {
        public List<MacLaunchProgress> Reports { get; } = [];

        public void Report(MacLaunchProgress value)
            => this.Reports.Add(value);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            this.Directory = System.IO.Directory.CreateTempSubdirectory("xivlauncher-mac-patch-tests-");
        }

        public DirectoryInfo Directory { get; }

        public void Dispose()
        {
            this.Directory.Delete(recursive: true);
        }
    }
}
