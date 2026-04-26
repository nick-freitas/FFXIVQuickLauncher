# Mac Dalamud Spike Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a debug-only Mac launch path that proves whether the official macOS FFXIV app can start through `Dalamud.Injector.exe`.

**Architecture:** Keep the current Mac launch path unchanged by default. Add a parsed `--experimental-dalamud` option that routes successful launches through a Mac-specific Dalamud preparation service and official-Mac Wine runner. Reuse `DalamudUpdater` and `DalamudLauncher`, but add small Mac-specific glue for Wine path conversion, injector argument construction, and diagnostic failure reporting.

**Tech Stack:** .NET 9, Avalonia, MSTest, `XIVLauncher.Mac`, `XIVLauncher.Common`, `XIVLauncher.Common.Unix`, existing Dalamud updater/launcher classes.

---

### Task 1: Add Debug Launch Options

**Files:**
- Create: `src/XIVLauncher.Mac/Services/MacLaunchOptions.cs`
- Modify: `src/XIVLauncher.Mac/App.axaml.cs`
- Test: `src/XIVLauncher.Mac.Tests/Services/MacLaunchOptionsTests.cs`

**Step 1: Write the failing tests**

Create `src/XIVLauncher.Mac.Tests/Services/MacLaunchOptionsTests.cs`:

```csharp
using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacLaunchOptionsTests
{
    [TestMethod]
    public void FromArgsEnablesExperimentalDalamudWhenFlagIsPresent()
    {
        var options = MacLaunchOptions.FromArgs(["--experimental-dalamud"]);

        Assert.IsTrue(options.ExperimentalDalamud);
    }

    [TestMethod]
    public void FromArgsLeavesExperimentalDalamudDisabledByDefault()
    {
        var options = MacLaunchOptions.FromArgs([]);

        Assert.IsFalse(options.ExperimentalDalamud);
    }

    [TestMethod]
    public void FromArgsAcceptsCaseInsensitiveFlag()
    {
        var options = MacLaunchOptions.FromArgs(["--EXPERIMENTAL-DALAMUD"]);

        Assert.IsTrue(options.ExperimentalDalamud);
    }
}
```

**Step 2: Run test to verify it fails**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacLaunchOptionsTests
```

Expected: FAIL because `MacLaunchOptions` does not exist.

**Step 3: Add the options class**

Create `src/XIVLauncher.Mac/Services/MacLaunchOptions.cs`:

```csharp
namespace XIVLauncher.Mac.Services;

public sealed class MacLaunchOptions
{
    public bool ExperimentalDalamud { get; init; }

    public static MacLaunchOptions FromArgs(IEnumerable<string> args)
    {
        var experimentalDalamud = args.Any(arg =>
            string.Equals(arg, "--experimental-dalamud", StringComparison.OrdinalIgnoreCase));

        return new MacLaunchOptions
        {
            ExperimentalDalamud = experimentalDalamud,
        };
    }
}
```

Modify `src/XIVLauncher.Mac/App.axaml.cs` inside `OnFrameworkInitializationCompleted`:

```csharp
var launchOptions = MacLaunchOptions.FromArgs(desktop.Args ?? []);
```

Change the service construction from:

```csharp
new MacLauncherService(),
```

to:

```csharp
new MacLauncherService(launchOptions),
```

**Step 4: Run test to verify it passes**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacLaunchOptionsTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac/Services/MacLaunchOptions.cs src/XIVLauncher.Mac/App.axaml.cs src/XIVLauncher.Mac.Tests/Services/MacLaunchOptionsTests.cs
git commit -m "feat: add mac debug launch options"
```

### Task 2: Add Official Mac Wine Path Conversion

**Files:**
- Create: `src/XIVLauncher.Common.Unix/OfficialMacWinePathConverter.cs`
- Test: `src/XIVLauncher.Mac.Tests/Services/OfficialMacWinePathConverterTests.cs`

**Step 1: Write the failing tests**

Create `src/XIVLauncher.Mac.Tests/Services/OfficialMacWinePathConverterTests.cs`:

```csharp
using XIVLauncher.Common.Unix;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class OfficialMacWinePathConverterTests
{
    [TestMethod]
    public void ToWinePathMapsDriveCInsidePrefix()
    {
        var converter = new OfficialMacWinePathConverter(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

        var result = converter.ToWinePath("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/Program Files/Game/ffxiv_dx11.exe");

        Assert.AreEqual(@"C:\Program Files\Game\ffxiv_dx11.exe", result);
    }

    [TestMethod]
    public void ToWinePathMapsExternalAbsolutePathThroughZDrive()
    {
        var converter = new OfficialMacWinePathConverter(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

        var result = converter.ToWinePath("/Users/test/Library/Application Support/XIVLauncherMac/dalamudConfig.json");

        Assert.AreEqual(@"Z:\Users\test\Library\Application Support\XIVLauncherMac\dalamudConfig.json", result);
    }

    [TestMethod]
    public void ToWinePathRejectsRelativePaths()
    {
        var converter = new OfficialMacWinePathConverter(new DirectoryInfo("/prefix"));

        Assert.ThrowsException<ArgumentException>(() => converter.ToWinePath("relative/path"));
    }
}
```

**Step 2: Run test to verify it fails**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter OfficialMacWinePathConverterTests
```

Expected: FAIL because `OfficialMacWinePathConverter` does not exist.

**Step 3: Add the converter**

Create `src/XIVLauncher.Common.Unix/OfficialMacWinePathConverter.cs`:

```csharp
namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacWinePathConverter
{
    private readonly string driveCRoot;

    public OfficialMacWinePathConverter(DirectoryInfo winePrefix)
    {
        this.driveCRoot = Path.GetFullPath(Path.Combine(winePrefix.FullName, "drive_c"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public string ToWinePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        var fullPath = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(fullPath))
            throw new ArgumentException("Path must be absolute.", nameof(path));

        if (fullPath.StartsWith(this.driveCRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var relative = fullPath[(this.driveCRoot.Length + 1)..];
            return @"C:\" + relative.Replace('/', '\\');
        }

        return "Z:" + fullPath.Replace('/', '\\');
    }
}
```

**Step 4: Run test to verify it passes**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter OfficialMacWinePathConverterTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Common.Unix/OfficialMacWinePathConverter.cs src/XIVLauncher.Mac.Tests/Services/OfficialMacWinePathConverterTests.cs
git commit -m "feat: add official mac wine path conversion"
```

### Task 3: Add Official Mac Dalamud Runner

**Files:**
- Create: `src/XIVLauncher.Common.Unix/OfficialMacAppDalamudRunner.cs`
- Test: `src/XIVLauncher.Mac.Tests/Services/OfficialMacAppDalamudRunnerTests.cs`

**Step 1: Write the failing tests**

Create `src/XIVLauncher.Mac.Tests/Services/OfficialMacAppDalamudRunnerTests.cs`:

```csharp
using System.Diagnostics;
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.Unix;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class OfficialMacAppDalamudRunnerTests
{
    [TestMethod]
    public void BuildStartInfoUsesOfficialWineAndPrefix()
    {
        var install = CreateInstall();
        var runner = new FileInfo("/Users/test/Library/Application Support/XIVLauncherMac/addon/Hooks/1/Dalamud.Injector.exe");
        var startInfo = CreateDalamudStartInfo();

        var plan = OfficialMacAppDalamudRunner.BuildLaunchPlan(
            install,
            runner,
            fakeLogin: false,
            noPlugins: false,
            noThirdPlugins: false,
            gameExe: new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game/ffxiv_dx11.exe"),
            gameArgs: "DEV.TestSID",
            environment: new Dictionary<string, string> { ["DALAMUD_RUNTIME"] = "/runtime" },
            loadMethod: DalamudLoadMethod.DllInject,
            startInfo);

        Assert.AreEqual(install.WineExecutable.FullName, plan.FileName);
        Assert.AreEqual("DEV.TestSID", plan.Arguments.Split("-- ").Last());
        Assert.AreEqual(install.WinePrefix.FullName, plan.Environment["WINEPREFIX"]);
        StringAssert.Contains(plan.Arguments, @"""Z:\Users\test\Library\Application Support\XIVLauncherMac\addon\Hooks\1\Dalamud.Injector.exe""");
        StringAssert.Contains(plan.Arguments, "--mode=inject");
        StringAssert.Contains(plan.Arguments, @"--game=""C:\game\ffxiv_dx11.exe""");
    }

    [TestMethod]
    public void BuildStartInfoAddsNoPluginFlags()
    {
        var plan = OfficialMacAppDalamudRunner.BuildLaunchPlan(
            CreateInstall(),
            new FileInfo("/runner/Dalamud.Injector.exe"),
            fakeLogin: true,
            noPlugins: true,
            noThirdPlugins: true,
            gameExe: new FileInfo("/game/ffxiv_dx11.exe"),
            gameArgs: "args",
            environment: new Dictionary<string, string>(),
            loadMethod: DalamudLoadMethod.EntryPoint,
            CreateDalamudStartInfo());

        StringAssert.Contains(plan.Arguments, "--fake-arguments");
        StringAssert.Contains(plan.Arguments, "--no-plugin");
        StringAssert.Contains(plan.Arguments, "--no-3rd-plugin");
        StringAssert.Contains(plan.Arguments, "--mode=entrypoint");
    }

    private static OfficialMacAppInstall CreateInstall()
        => new(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game"),
            new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/wine"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

    private static DalamudStartInfo CreateDalamudStartInfo()
        => new()
        {
            Language = ClientLanguage.English,
            PluginDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/installedPlugins",
            ConfigurationPath = "/Users/test/Library/Application Support/XIVLauncherMac/dalamudConfig.json",
            LoggingPath = "/Users/test/Library/Application Support/XIVLauncherMac/logs",
            AssetDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/dalamudAssets/1",
            GameVersion = "2026.04.26.0000.0000",
            WorkingDirectory = "/Users/test/Library/Application Support/XIVLauncherMac/addon/Hooks/1",
            DelayInitializeMs = 0,
            TroubleshootingPackData = "{}",
        };
}
```

**Step 2: Run test to verify it fails**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter OfficialMacAppDalamudRunnerTests
```

Expected: FAIL because `OfficialMacAppDalamudRunner` does not exist.

**Step 3: Add the runner and launch plan**

Create `src/XIVLauncher.Common.Unix/OfficialMacAppDalamudRunner.cs`:

```csharp
using System.Diagnostics;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacAppDalamudRunner : IDalamudRunner
{
    private readonly OfficialMacAppInstall install;

    public OfficialMacAppDalamudRunner(OfficialMacAppInstall install)
    {
        this.install = install;
    }

    public Process? Run(FileInfo runner, bool fakeLogin, bool noPlugins, bool noThirdPlugins, FileInfo gameExe, string gameArgs, IDictionary<string, string> environment, DalamudLoadMethod loadMethod, DalamudStartInfo startInfo)
    {
        var plan = BuildLaunchPlan(this.install, runner, fakeLogin, noPlugins, noThirdPlugins, gameExe, gameArgs, environment, loadMethod, startInfo);
        var psi = new ProcessStartInfo(plan.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = plan.WorkingDirectory,
            Arguments = plan.Arguments,
        };

        foreach (var pair in plan.Environment)
            psi.EnvironmentVariables[pair.Key] = pair.Value;

        Log.Information("[MACDALAMUD] Starting Dalamud injector: {FileName} {Arguments}", psi.FileName, psi.Arguments);

        var process = Process.Start(psi);
        if (process is null)
            return null;

        var output = process.StandardOutput.ReadLine();
        if (string.IsNullOrWhiteSpace(output))
            throw new DalamudRunnerException("Dalamud injector did not report a game process.");

        Log.Information("[MACDALAMUD] {Output}", output);

        try
        {
            var dalamudOutput = JsonConvert.DeserializeObject<DalamudConsoleOutput>(output)
                ?? throw new DalamudRunnerException("Dalamud injector output was empty.");

            Log.Information("[MACDALAMUD] Injector reported Wine pid {Pid}; macOS process tracking is not implemented yet.", dalamudOutput.Pid);
            return process;
        }
        catch (JsonException ex)
        {
            throw new DalamudRunnerException($"Could not parse Dalamud injector output: {output}", ex);
        }
    }

    public static OfficialMacDalamudLaunchPlan BuildLaunchPlan(
        OfficialMacAppInstall install,
        FileInfo runner,
        bool fakeLogin,
        bool noPlugins,
        bool noThirdPlugins,
        FileInfo gameExe,
        string gameArgs,
        IDictionary<string, string> environment,
        DalamudLoadMethod loadMethod,
        DalamudStartInfo startInfo)
    {
        var converter = new OfficialMacWinePathConverter(install.WinePrefix);
        var convertedStartInfo = ConvertStartInfo(converter, startInfo);
        var env = new Dictionary<string, string>(environment)
        {
            ["WINEPREFIX"] = install.WinePrefix.FullName,
        };

        if (env.TryGetValue("DALAMUD_RUNTIME", out var runtime))
        {
            var runtimePath = converter.ToWinePath(runtime);
            env["DALAMUD_RUNTIME"] = runtimePath;
            env["DOTNET_ROOT"] = runtimePath;
        }

        var launchArguments = new List<string>
        {
            Quote(converter.ToWinePath(runner.FullName)),
            DalamudInjectorArgs.LAUNCH,
            DalamudInjectorArgs.Mode(loadMethod == DalamudLoadMethod.EntryPoint ? "entrypoint" : "inject"),
            DalamudInjectorArgs.Game(converter.ToWinePath(gameExe.FullName)),
            DalamudInjectorArgs.WorkingDirectory(convertedStartInfo.WorkingDirectory),
            DalamudInjectorArgs.ConfigurationPath(convertedStartInfo.ConfigurationPath),
            DalamudInjectorArgs.LoggingPath(convertedStartInfo.LoggingPath),
            DalamudInjectorArgs.PluginDirectory(convertedStartInfo.PluginDirectory),
            DalamudInjectorArgs.AssetDirectory(convertedStartInfo.AssetDirectory),
            DalamudInjectorArgs.ClientLanguage((int)convertedStartInfo.Language),
            DalamudInjectorArgs.DelayInitialize(convertedStartInfo.DelayInitializeMs),
            DalamudInjectorArgs.TsPackB64(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(convertedStartInfo.TroubleshootingPackData))),
        };

        if (loadMethod == DalamudLoadMethod.ACLonly)
            launchArguments.Add(DalamudInjectorArgs.WITHOUT_DALAMUD);

        if (fakeLogin)
            launchArguments.Add(DalamudInjectorArgs.FAKE_ARGUMENTS);

        if (noPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_PLUGIN);

        if (noThirdPlugins)
            launchArguments.Add(DalamudInjectorArgs.NO_THIRD_PARTY);

        launchArguments.Add("--");
        launchArguments.Add(gameArgs);

        return new OfficialMacDalamudLaunchPlan(
            install.WineExecutable.FullName,
            runner.DirectoryName ?? install.WinePrefix.FullName,
            string.Join(" ", launchArguments),
            env);
    }

    private static DalamudStartInfo ConvertStartInfo(OfficialMacWinePathConverter converter, DalamudStartInfo startInfo)
        => new()
        {
            Language = startInfo.Language,
            PluginDirectory = converter.ToWinePath(startInfo.PluginDirectory),
            ConfigurationPath = converter.ToWinePath(startInfo.ConfigurationPath),
            LoggingPath = converter.ToWinePath(startInfo.LoggingPath),
            AssetDirectory = converter.ToWinePath(startInfo.AssetDirectory),
            GameVersion = startInfo.GameVersion,
            WorkingDirectory = converter.ToWinePath(startInfo.WorkingDirectory),
            DelayInitializeMs = startInfo.DelayInitializeMs,
            TroubleshootingPackData = startInfo.TroubleshootingPackData,
        };

    private static string Quote(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";
}

public sealed record OfficialMacDalamudLaunchPlan(
    string FileName,
    string WorkingDirectory,
    string Arguments,
    IReadOnlyDictionary<string, string> Environment);
```

**Step 4: Run test to verify it passes**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter OfficialMacAppDalamudRunnerTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Common.Unix/OfficialMacAppDalamudRunner.cs src/XIVLauncher.Mac.Tests/Services/OfficialMacAppDalamudRunnerTests.cs
git commit -m "feat: add official mac dalamud runner"
```

### Task 4: Add Mac Dalamud Preparation Service

**Files:**
- Create: `src/XIVLauncher.Mac/Services/MacDalamudService.cs`
- Create: `src/XIVLauncher.Mac/Services/NullDalamudLoadingOverlay.cs`
- Test: `src/XIVLauncher.Mac.Tests/Services/MacDalamudServiceTests.cs`

**Step 1: Write the failing tests**

Create `src/XIVLauncher.Mac.Tests/Services/MacDalamudServiceTests.cs`:

```csharp
using XIVLauncher.Common;
using XIVLauncher.Common.Dalamud;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Mac.Services;

namespace XIVLauncher.Mac.Tests.Services;

[TestClass]
public sealed class MacDalamudServiceTests
{
    [TestMethod]
    public void CreatePathsUsesMacApplicationSupportDirectory()
    {
        var paths = MacDalamudPaths.Create("/Users/test/Library/Application Support/XIVLauncherMac");

        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/addon", paths.AddonDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/runtime", paths.RuntimeDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/dalamudAssets", paths.AssetRootDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/installedPlugins", paths.PluginDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac", paths.ConfigDirectory.FullName);
        Assert.AreEqual("/Users/test/Library/Application Support/XIVLauncherMac/logs", paths.LogDirectory.FullName);
    }

    [TestMethod]
    public async Task PrepareAsyncReturnsFailedWhenUpdaterFails()
    {
        var service = new MacDalamudService(new FailingDalamudUpdaterFactory());

        var result = await service.PrepareAsync(CreateInstall(), new DirectoryInfo("/game"), ClientLanguage.English, CancellationToken.None);

        Assert.IsFalse(result.IsSuccess);
        StringAssert.Contains(result.ErrorMessage, "Could not prepare Dalamud");
    }

    private static OfficialMacAppInstall CreateInstall()
        => new(
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix/drive_c/game"),
            new FileInfo("/Applications/FINAL FANTASY XIV ONLINE.app/wine"),
            new DirectoryInfo("/Applications/FINAL FANTASY XIV ONLINE.app/prefix"));

    private sealed class FailingDalamudUpdaterFactory : IMacDalamudUpdaterFactory
    {
        public DalamudUpdater Create(MacDalamudPaths paths)
            => throw new InvalidOperationException("network failed");
    }
}
```

**Step 2: Run test to verify it fails**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacDalamudServiceTests
```

Expected: FAIL because `MacDalamudService` does not exist.

**Step 3: Add the preparation service**

Create `src/XIVLauncher.Mac/Services/NullDalamudLoadingOverlay.cs`:

```csharp
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Mac.Services;

public sealed class NullDalamudLoadingOverlay : IDalamudLoadingOverlay
{
    public void SetStep(IDalamudLoadingOverlay.DalamudUpdateStep step)
    {
    }

    public void SetVisible()
    {
    }

    public void SetInvisible()
    {
    }

    public void ReportProgress(long? size, long downloaded, double? progress)
    {
    }
}
```

Create `src/XIVLauncher.Mac/Services/MacDalamudService.cs`:

```csharp
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
    {
        var updater = new DalamudUpdater(
            this.httpClient,
            paths.AddonDirectory,
            paths.RuntimeDirectory,
            paths.AssetRootDirectory,
            cache: null,
            dalamudRolloutBucket: null);

        updater.Overlay = new NullDalamudLoadingOverlay();
        return updater;
    }
}

public sealed class MacDalamudService : IMacDalamudService
{
    private readonly IMacDalamudUpdaterFactory updaterFactory;
    private readonly string applicationSupportDirectory;

    public MacDalamudService()
        : this(new MacDalamudUpdaterFactory(new HttpClient()), MacSettingsService.DefaultApplicationSupportDirectory)
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
                paths.PluginDirectory,
                paths.LogDirectory,
                language,
                injectionDelay: 0,
                fakeLogin: false,
                noPlugin: false,
                noThirdPlugin: false,
                troubleshootingData: "{}");

            var state = launcher.HoldForUpdate(gamePath);
            if (state != DalamudLauncher.DalamudInstallState.Ok)
                return Task.FromResult(MacDalamudPrepareResult.Failed("Dalamud is not available for the installed game version."));

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
    {
        var root = new DirectoryInfo(applicationSupportDirectory);
        return new MacDalamudPaths(
            new DirectoryInfo(Path.Combine(root.FullName, "addon")),
            new DirectoryInfo(Path.Combine(root.FullName, "runtime")),
            new DirectoryInfo(Path.Combine(root.FullName, "dalamudAssets")),
            new DirectoryInfo(Path.Combine(root.FullName, "installedPlugins")),
            root,
            new DirectoryInfo(Path.Combine(root.FullName, "logs")));
    }
}

public sealed class MacDalamudPrepareResult
{
    private MacDalamudPrepareResult(bool isSuccess, IGameRunner? gameRunner, string? errorMessage)
    {
        this.IsSuccess = isSuccess;
        this.GameRunner = gameRunner;
        this.ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public IGameRunner? GameRunner { get; }

    public string? ErrorMessage { get; }

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

    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DpiAwareness dpiAwareness)
        => this.launcher.Run(new FileInfo(path), arguments, environment);
}
```

**Step 4: Run test to verify it passes**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacDalamudServiceTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac/Services/MacDalamudService.cs src/XIVLauncher.Mac/Services/NullDalamudLoadingOverlay.cs src/XIVLauncher.Mac.Tests/Services/MacDalamudServiceTests.cs
git commit -m "feat: prepare dalamud for mac launch spike"
```

### Task 5: Route Experimental Launches Through Dalamud

**Files:**
- Modify: `src/XIVLauncher.Mac/Services/MacLauncherService.cs`
- Test: `src/XIVLauncher.Mac.Tests/Services/MacLauncherServiceTests.cs`

**Step 1: Write the failing tests**

Add these usings to `MacLauncherServiceTests` if they are not already present:

```csharp
using System.Diagnostics;
using XIVLauncher.Common.PlatformAbstractions;
```

Add tests to `MacLauncherServiceTests`:

```csharp
[TestMethod]
public async Task LaunchAsyncUsesNormalLaunchPathWhenExperimentalDalamudIsDisabled()
{
    var client = new FakeXivLauncherClient();
    var dalamud = new FakeMacDalamudService();
    var service = new MacLauncherService(
        new FakeXivLauncherClientFactory(client),
        new FakeMacPatchService(),
        new MacLaunchOptions { ExperimentalDalamud = false },
        dalamud);

    var result = await service.LaunchAsync(CreateRequest());

    Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
    Assert.AreEqual(1, client.LaunchCalls);
    Assert.AreEqual(0, client.DalamudLaunchCalls);
    Assert.AreEqual(0, dalamud.PrepareCalls);
}

[TestMethod]
public async Task LaunchAsyncUsesDalamudLaunchPathWhenExperimentalDalamudIsEnabled()
{
    var client = new FakeXivLauncherClient();
    var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Prepared(new FakeGameRunner()) };
    var service = new MacLauncherService(
        new FakeXivLauncherClientFactory(client),
        new FakeMacPatchService(),
        new MacLaunchOptions { ExperimentalDalamud = true },
        dalamud);

    var result = await service.LaunchAsync(CreateRequest());

    Assert.AreEqual(MacLaunchResultKind.Launched, result.Kind);
    Assert.AreEqual(0, client.LaunchCalls);
    Assert.AreEqual(1, client.DalamudLaunchCalls);
    Assert.AreEqual(1, dalamud.PrepareCalls);
}

[TestMethod]
public async Task LaunchAsyncReportsDalamudPreparationFailure()
{
    var client = new FakeXivLauncherClient();
    var dalamud = new FakeMacDalamudService { Result = MacDalamudPrepareResult.Failed("Could not prepare Dalamud: network failed") };
    var service = new MacLauncherService(
        new FakeXivLauncherClientFactory(client),
        new FakeMacPatchService(),
        new MacLaunchOptions { ExperimentalDalamud = true },
        dalamud);

    var result = await service.LaunchAsync(CreateRequest());

    Assert.AreEqual(MacLaunchResultKind.Failed, result.Kind);
    StringAssert.Contains(result.Message, "Could not prepare Dalamud");
    Assert.AreEqual(0, client.LaunchCalls);
    Assert.AreEqual(0, client.DalamudLaunchCalls);
}
```

Add these fakes near the existing test fakes:

```csharp
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
```

**Step 2: Run test to verify it fails**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacLauncherServiceTests
```

Expected: FAIL because constructors and experimental Dalamud launch path do not exist.

**Step 3: Modify interfaces and launch flow**

Modify `IXivLauncherClient` in `MacLauncherService.cs`:

```csharp
bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null);
```

Modify `IXivLauncherCore` similarly:

```csharp
bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null);
```

Modify `XivLauncherClient.LaunchGame` to pass the optional runner through:

```csharp
public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
    => this.launcher.LaunchGame(loginResult, request, runner);
```

Modify `XivLauncherCore.LaunchGame`:

```csharp
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
```

Update the `FakeXivLauncherClient` implementation in the test file:

```csharp
public int DalamudLaunchCalls { get; private set; }

public bool LaunchGame(Launcher.LoginResult loginResult, MacLaunchRequest request, IGameRunner? runner = null)
{
    if (runner is null)
        this.LaunchCalls++;
    else
        this.DalamudLaunchCalls++;

    return true;
}
```

Modify `MacLauncherService` constructors:

```csharp
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
    this.clientFactory = clientFactory;
    this.patchService = patchService;
    this.launchOptions = launchOptions;
    this.dalamudService = dalamudService;
}
```

In the successful login branch before normal launch:

```csharp
progress?.Report(new MacLaunchProgress(MacLaunchStage.Launching, this.launchOptions.ExperimentalDalamud
    ? "Preparing Dalamud..."
    : "Starting game..."));

if (this.launchOptions.ExperimentalDalamud)
{
    var dalamud = await this.dalamudService.PrepareAsync(
        request.Install,
        request.Install.GameRoot,
        request.Language,
        cancellationToken);

    if (!dalamud.IsSuccess || dalamud.GameRunner is null)
        return MacLaunchResult.Failed(dalamud.ErrorMessage ?? "Could not prepare Dalamud.");

    progress?.Report(new MacLaunchProgress(MacLaunchStage.Launching, "Starting game with experimental Dalamud..."));
    return client.LaunchGame(loginResult, request, dalamud.GameRunner)
        ? MacLaunchResult.Launched()
        : MacLaunchResult.Failed("Game process did not start through experimental Dalamud.");
}

progress?.Report(new MacLaunchProgress(MacLaunchStage.Launching, "Starting game..."));
return client.LaunchGame(loginResult, request)
    ? MacLaunchResult.Launched()
    : MacLaunchResult.Failed("Game process did not start.");
```

**Step 4: Run tests**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview --filter MacLauncherServiceTests
```

Expected: PASS.

Then run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac/Services/MacLauncherService.cs src/XIVLauncher.Mac.Tests/Services/MacLauncherServiceTests.cs
git commit -m "feat: route experimental mac launches through dalamud"
```

### Task 6: Build And Manual Spike Verification

**Files:**
- Modify if needed: `docs/plans/2026-04-26-mac-dalamud-spike-design.md`
- Modify if needed: implementation files touched above

**Step 1: Run full build and tests**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 2: Run the normal launcher path**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet run --project src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: the launcher opens normally, with no Dalamud preparation message before launch.

**Step 3: Run the experimental Dalamud path**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet run --project src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview -- --experimental-dalamud
```

Expected:

- status reaches "Preparing Dalamud..."
- `~/Library/Application Support/XIVLauncherMac/addon/Hooks/.../Dalamud.Injector.exe` exists
- `~/Library/Application Support/XIVLauncherMac/runtime` exists
- FFXIV starts
- in game, the Dalamud UI or plugin installer opens

**Step 4: Capture diagnostic output**

If launch fails, inspect:

```bash
tail -200 "$HOME/Library/Application Support/XIVLauncherMac/output.log"
find "$HOME/Library/Application Support/XIVLauncherMac" -maxdepth 4 -type f | sort | tail -100
```

Record the failing stage and exact non-secret error in the design doc under a new "Spike Notes" section.

**Step 5: Commit verification notes or fixes**

If only documentation changes:

```bash
git add docs/plans/2026-04-26-mac-dalamud-spike-design.md
git commit -m "docs: record mac dalamud spike results"
```

If code changes were needed:

```bash
git add src/XIVLauncher.Common.Unix src/XIVLauncher.Mac src/XIVLauncher.Mac.Tests docs/plans/2026-04-26-mac-dalamud-spike-design.md
git commit -m "fix: adjust mac dalamud spike launch"
```

### Final Verification

Run:

```bash
git status --short
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected:

- no unrelated files staged
- tests pass
- Mac launcher builds
- manual spike result is recorded
