# Official Mac App Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Launch FFXIV through the user's existing official `/Applications/FINAL FANTASY XIV ONLINE.app` bundle.

**Architecture:** Add a small official Mac app model/locator in `XIVLauncher.Common`, a macOS Wine runner in `XIVLauncher.Common.Unix`, and replace hard-coded patch route strings with a helper that preserves current Windows/Linux behavior. The first pass only integrates with an already installed Square Enix app bundle and does not manage a separate Mac install.

**Tech Stack:** C#/.NET 9, MSTest, existing `IGameRunner`, `Launcher`, `PlatformHelpers`, `GameHelpers`, `XIVLauncher.Common.Unix`.

---

### Task 1: Add Official Mac App Locator

**Files:**
- Create: `src/XIVLauncher.Common/Game/OfficialMacApp/OfficialMacAppInstall.cs`
- Create: `src/XIVLauncher.Common/Game/OfficialMacApp/OfficialMacAppLocator.cs`
- Test: `src/XIVLauncher.Common.Tests/OfficialMacAppLocatorTests.cs`

**Step 1: Write the failing tests**

Add MSTest coverage for:

```csharp
[TestMethod]
public void TryResolveAcceptsOfficialBundle()
{
    var root = CreateOfficialBundle();
    var result = OfficialMacAppLocator.TryResolve(root);

    Assert.IsNotNull(result);
    Assert.AreEqual(root.FullName, result.AppBundle.FullName);
    Assert.IsTrue(result.GameRoot.FullName.EndsWith("FINAL FANTASY XIV - A Realm Reborn"));
    Assert.IsTrue(result.WineExecutable.FullName.EndsWith("FINAL FANTASY XIV ONLINE/wine"));
    Assert.IsTrue(result.WinePrefix.FullName.EndsWith("support/published_Final_Fantasy"));
}

[TestMethod]
public void TryResolveRejectsWrongBundleIdentifier()
{
    var root = CreateOfficialBundle(bundleIdentifier: "example.not.ffxiv");

    Assert.IsNull(OfficialMacAppLocator.TryResolve(root));
}

[TestMethod]
public void TryResolveRejectsMissingGameFolders()
{
    var root = CreateOfficialBundle(createGameFolders: false);

    Assert.IsNull(OfficialMacAppLocator.TryResolve(root));
}
```

Use helper methods in the test file to create a temporary `.app` tree under `TestContext.TestRunDirectory` or `Path.GetTempPath()`. Write `Info.plist` as XML with `CFBundleIdentifier` set to `com.square-enix.finalfantasyxiv`.

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter OfficialMacAppLocatorTests
```

Expected: FAIL because `OfficialMacAppLocator` does not exist.

**Step 3: Write minimal implementation**

Create immutable install metadata:

```csharp
namespace XIVLauncher.Common.Game.OfficialMacApp;

public sealed class OfficialMacAppInstall
{
    public OfficialMacAppInstall(DirectoryInfo appBundle, DirectoryInfo gameRoot, FileInfo wineExecutable, DirectoryInfo winePrefix)
    {
        AppBundle = appBundle;
        GameRoot = gameRoot;
        WineExecutable = wineExecutable;
        WinePrefix = winePrefix;
    }

    public DirectoryInfo AppBundle { get; }
    public DirectoryInfo GameRoot { get; }
    public FileInfo WineExecutable { get; }
    public DirectoryInfo WinePrefix { get; }
}
```

Create the resolver:

```csharp
namespace XIVLauncher.Common.Game.OfficialMacApp;

public static class OfficialMacAppLocator
{
    public const string DefaultAppPath = "/Applications/FINAL FANTASY XIV ONLINE.app";
    private const string BundleIdentifier = "com.square-enix.finalfantasyxiv";
    private const string RelativePrefix = "Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy";
    private const string RelativeWine = "Contents/SharedSupport/finalfantasyxiv/FINAL FANTASY XIV ONLINE/wine";
    private const string RelativeGameRoot = "drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn";

    public static OfficialMacAppInstall? TryResolveDefault() =>
        TryResolve(new DirectoryInfo(DefaultAppPath));

    public static OfficialMacAppInstall? TryResolve(DirectoryInfo appBundle)
    {
        if (!appBundle.Exists || !HasOfficialBundleIdentifier(appBundle))
            return null;

        var winePrefix = new DirectoryInfo(Path.Combine(appBundle.FullName, RelativePrefix));
        var wine = new FileInfo(Path.Combine(appBundle.FullName, RelativeWine));
        var gameRoot = new DirectoryInfo(Path.Combine(winePrefix.FullName, RelativeGameRoot));

        if (!wine.Exists || !winePrefix.Exists || !GameHelpers.PathHasExistingInstall(gameRoot.FullName))
            return null;

        return new OfficialMacAppInstall(appBundle, gameRoot, wine, winePrefix);
    }

    private static bool HasOfficialBundleIdentifier(DirectoryInfo appBundle)
    {
        var plist = Path.Combine(appBundle.FullName, "Contents", "Info.plist");
        if (!File.Exists(plist))
            return false;

        var text = File.ReadAllText(plist);
        return text.Contains("<key>CFBundleIdentifier</key>", StringComparison.Ordinal) &&
               text.Contains($"<string>{BundleIdentifier}</string>", StringComparison.Ordinal);
    }
}
```

**Step 4: Run tests**

Run:

```bash
dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter OfficialMacAppLocatorTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Common/Game/OfficialMacApp src/XIVLauncher.Common.Tests/OfficialMacAppLocatorTests.cs
git commit -m "feat: detect official mac app installs"
```

### Task 2: Add Patch Route Helper

**Files:**
- Create: `src/XIVLauncher.Common/Game/Patch/PatchPlatform.cs`
- Modify: `src/XIVLauncher.Common/Game/Launcher.cs`
- Test: `src/XIVLauncher.Common.Tests/PatchPlatformTests.cs`

**Step 1: Write the failing tests**

Cover current behavior and Mac behavior:

```csharp
[TestMethod]
public void GetPatchRouteKeepsWin32ForCurrentSupportedPlatforms()
{
    Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Win32));
    Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Win32OnLinux));
    Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Linux));
}

[TestMethod]
public void GetPatchRouteUsesWin32ForOfficialMacWrapperInitialSupport()
{
    Assert.AreEqual("win32", PatchPlatform.GetPatchRoute(Platform.Mac));
}
```

The Mac test intentionally locks the first implementation to the official app's Windows-style payload. If later testing proves Square Enix requires a different route, this helper is the single change point.

**Step 2: Run test to verify it fails**

Run:

```bash
dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter PatchPlatformTests
```

Expected: FAIL because `PatchPlatform` does not exist.

**Step 3: Implement helper and wire `Launcher`**

Create:

```csharp
namespace XIVLauncher.Common.Game.Patch;

public static class PatchPlatform
{
    public static string GetPatchRoute(Platform platform) => platform switch
    {
        Platform.Win32 => "win32",
        Platform.Win32OnLinux => "win32",
        Platform.Linux => "win32",
        Platform.Mac => "win32",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null),
    };
}
```

Modify `Launcher.CheckBootVersion()`:

```csharp
var patchRoute = PatchPlatform.GetPatchRoute(PlatformHelpers.GetPlatform());
var request = new HttpRequestMessage(HttpMethod.Get,
    $"http://patch-bootver.ffxiv.com/http/{patchRoute}/ffxivneo_release_boot/{bootVersion}/?time=" +
    GetLauncherFormattedTimeLongRounded());
```

Modify `Launcher.RegisterSession()`:

```csharp
var patchRoute = PatchPlatform.GetPatchRoute(PlatformHelpers.GetPlatform());
var request = new HttpRequestMessage(HttpMethod.Post,
    $"https://patch-gamever.ffxiv.com/http/{patchRoute}/ffxivneo_release_game/{(forceBaseVersion ? Constants.BASE_GAME_VERSION : Repository.Ffxiv.GetVer(gamePath))}/{loginResult.SessionId}");
```

Add `using XIVLauncher.Common.Game.Patch;` to `Launcher.cs` if needed.

**Step 4: Run tests**

Run:

```bash
dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter PatchPlatformTests
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Common/Game/Patch/PatchPlatform.cs src/XIVLauncher.Common/Game/Launcher.cs src/XIVLauncher.Common.Tests/PatchPlatformTests.cs
git commit -m "refactor: centralize patch platform route"
```

### Task 3: Add Official Mac App Game Runner

**Files:**
- Create: `src/XIVLauncher.Common.Unix/OfficialMacAppGameRunner.cs`
- Test manually by adding an internal command-building helper in the runner, or keep verification to build if tests would require changing visibility.

**Step 1: Write the failing compile target**

Add the new file with a class skeleton referenced by no production code yet:

```csharp
namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacAppGameRunner : IGameRunner
{
}
```

Run:

```bash
dotnet build src/XIVLauncher.Common.Unix/XIVLauncher.Common.Unix.csproj
```

Expected: FAIL because `IGameRunner.Start` is not implemented.

**Step 2: Implement runner**

Implementation shape:

```csharp
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using XIVLauncher.Common;
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Unix;

public sealed class OfficialMacAppGameRunner : IGameRunner
{
    private readonly OfficialMacAppInstall install;

    public OfficialMacAppGameRunner(OfficialMacAppInstall install)
    {
        this.install = install;
    }

    public Process? Start(string path, string workingDirectory, string arguments, IDictionary<string, string> environment, DpiAwareness dpiAwareness)
    {
        var psi = new ProcessStartInfo(this.install.WineExecutable.FullName)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add(arguments);
        Merge(psi.EnvironmentVariables, environment);
        psi.EnvironmentVariables["WINEPREFIX"] = this.install.WinePrefix.FullName;

        var process = new Process { StartInfo = psi };
        process.Start();
        return process;
    }

    private static void Merge(StringDictionary target, IDictionary<string, string> source)
    {
        foreach (var (key, value) in source)
            target[key] = value;
    }
}
```

If launching shows CrossOver needs more environment, add only the variables observed from the official app's launcher logs or wrapper scripts.

**Step 3: Build**

Run:

```bash
dotnet build src/XIVLauncher.Common.Unix/XIVLauncher.Common.Unix.csproj
```

Expected: PASS.

**Step 4: Commit**

```bash
git add src/XIVLauncher.Common.Unix/OfficialMacAppGameRunner.cs
git commit -m "feat: add official mac app game runner"
```

### Task 4: Use Official Mac App Game Path On macOS

**Files:**
- Modify: `src/XIVLauncher/Settings/ILauncherSettingsV3.cs`
- Modify: `src/XIVLauncher/AppUtil.cs`
- Modify: `src/XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs`
- Modify: `src/XIVLauncher/XIVLauncher.csproj`

**Step 1: Add settings shape**

Add nullable setting:

```csharp
DirectoryInfo? OfficialMacAppPath { get; set; }
```

This keeps the default `/Applications/...` path implicit while allowing future UI/CLI override.

**Step 2: Add project reference**

Add `XIVLauncher.Common.Unix` to `src/XIVLauncher/XIVLauncher.csproj` so the app can instantiate the runner when built on macOS:

```xml
<ProjectReference Include="..\XIVLauncher.Common.Unix\XIVLauncher.Common.Unix.csproj" />
```

If the WPF project cannot build on macOS after this, guard macOS-specific runner usage with conditional compilation and keep this step scoped to platforms where the WPF app is built.

**Step 3: Add discovery helper**

In `AppUtil.TryGamePaths()`, before Windows registry probing, add:

```csharp
if (PlatformHelpers.GetPlatform() == Platform.Mac)
{
    var configuredApp = App.Settings.OfficialMacAppPath;
    var install = configuredApp != null
        ? OfficialMacAppLocator.TryResolve(configuredApp)
        : OfficialMacAppLocator.TryResolveDefault();

    if (install != null)
        return install.GameRoot.FullName;
}
```

Add `using XIVLauncher.Common.Game.OfficialMacApp;`.

**Step 4: Choose the runner at launch**

In `MainWindowViewModel.StartGameAndAddon()`, create the runner based on platform:

```csharp
IGameRunner gameRunner;

if (PlatformHelpers.GetPlatform() == Platform.Mac)
{
    var macInstall = App.Settings.OfficialMacAppPath != null
        ? OfficialMacAppLocator.TryResolve(App.Settings.OfficialMacAppPath)
        : OfficialMacAppLocator.TryResolveDefault();

    if (macInstall == null)
        throw new InvalidOperationException("Could not find the official FINAL FANTASY XIV ONLINE.app install.");

    gameRunner = new OfficialMacAppGameRunner(macInstall);
    dalamudOk = false;
}
else
{
    gameRunner = new WindowsGameRunner(dalamudLauncher, dalamudOk);
}
```

Add usings:

```csharp
using XIVLauncher.Common.Game.OfficialMacApp;
using XIVLauncher.Common.PlatformAbstractions;
using XIVLauncher.Common.Unix;
```

Keep Dalamud disabled for the Mac path in this first pass.

**Step 5: Build the touched projects**

Run:

```bash
dotnet build src/XIVLauncher.Common/XIVLauncher.Common.csproj
dotnet build src/XIVLauncher.Common.Unix/XIVLauncher.Common.Unix.csproj
```

Expected: PASS.

Run the WPF project only on a compatible host/toolchain:

```bash
dotnet build src/XIVLauncher/XIVLauncher.csproj
```

Expected: PASS on Windows-compatible build environments. On macOS this may fail due to WPF/Windows targeting, and that should be recorded rather than hidden.

**Step 6: Commit**

```bash
git add src/XIVLauncher/Settings/ILauncherSettingsV3.cs src/XIVLauncher/AppUtil.cs src/XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs src/XIVLauncher/XIVLauncher.csproj
git commit -m "feat: wire official mac app launch path"
```

### Task 5: Local macOS Smoke Verification

**Files:**
- No source edits expected.

**Step 1: Verify discovery against the real app**

Run a small temporary C# snippet or test-host command that calls:

```csharp
OfficialMacAppLocator.TryResolveDefault()
```

Expected values on this machine:

```text
AppBundle: /Applications/FINAL FANTASY XIV ONLINE.app
WinePrefix: /Applications/FINAL FANTASY XIV ONLINE.app/Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy
GameRoot: /Applications/FINAL FANTASY XIV ONLINE.app/Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn
```

**Step 2: Verify executable presence**

Run:

```bash
find '/Applications/FINAL FANTASY XIV ONLINE.app/Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/game' -maxdepth 1 -name 'ffxiv_dx11.exe' -print
```

Expected: prints `ffxiv_dx11.exe` after the official app has completed installing/patching the game. If missing, open the official launcher once and let it finish patching.

**Step 3: Run final tests**

Run:

```bash
dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter "OfficialMacAppLocatorTests|PatchPlatformTests"
dotnet build src/XIVLauncher.Common.Unix/XIVLauncher.Common.Unix.csproj
```

Expected: PASS.

**Step 4: Commit verification notes if needed**

If smoke testing discovers required CrossOver environment variables, update `docs/plans/2026-04-26-official-mac-app-design.md` or add implementation comments, then commit:

```bash
git add docs/plans/2026-04-26-official-mac-app-design.md src/XIVLauncher.Common.Unix/OfficialMacAppGameRunner.cs
git commit -m "docs: record official mac app smoke test notes"
```
