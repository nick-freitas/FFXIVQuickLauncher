# Mac-Only GUI Fork Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a macOS GUI launcher that uses the existing official `FINAL FANTASY XIV ONLINE.app` installation.

**Architecture:** Keep reusable launcher logic in `XIVLauncher.Common` and `XIVLauncher.Common.Unix`, revert the unreachable Windows WPF Mac wiring, and add a new Avalonia-based `XIVLauncher.Mac` host. The first GUI version focuses on detecting the official Mac app, collecting login inputs, checking patch state, and launching through the official app's Wine runtime without Dalamud.

**Tech Stack:** .NET 9, Avalonia, MSTest, `XIVLauncher.Common`, `XIVLauncher.Common.Unix`, existing `Launcher`, `OfficialMacAppLocator`, and `OfficialMacAppGameRunner`.

---

### Task 1: Back Out WPF Mac Wiring

**Files:**
- Modify: `src/XIVLauncher/Settings/ILauncherSettingsV3.cs`
- Modify: `src/XIVLauncher/AppUtil.cs`
- Modify: `src/XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs`
- Modify: `src/XIVLauncher/XIVLauncher.csproj`

**Step 1: Inspect current diff from the WPF wiring commit**

Run:

```bash
git show --stat 15cc245d
git show -- src/XIVLauncher/Settings/ILauncherSettingsV3.cs src/XIVLauncher/AppUtil.cs src/XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs src/XIVLauncher/XIVLauncher.csproj
```

Expected: only the WPF integration files are shown.

**Step 2: Revert only the WPF integration commit**

Run:

```bash
git revert --no-edit 15cc245d
```

Expected: creates a revert commit removing WPF references to `XIVLauncher.Common.Unix`, `OfficialMacAppLocator`, `OfficialMacAppGameRunner`, and `OfficialMacAppPath`.

**Step 3: Verify reusable Mac support remains**

Run:

```bash
git log --oneline -- src/XIVLauncher.Common/Game/OfficialMacApp src/XIVLauncher.Common/Game/Patch/PatchPlatform.cs src/XIVLauncher.Common.Unix/OfficialMacAppGameRunner.cs
```

Expected: commits for locator, patch route helper, and runner remain.

**Step 4: Build reusable projects**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Common/XIVLauncher.Common.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Common.Unix/XIVLauncher.Common.Unix.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter "OfficialMacAppLocatorTests|PatchPlatformTests" /p:LangVersion=preview
```

Expected: reusable projects build and focused tests pass.

### Task 2: Add Mac Settings Model

**Files:**
- Create: `src/XIVLauncher.Mac/Settings/MacLauncherSettings.cs`
- Create: `src/XIVLauncher.Mac/Settings/MacSettingsService.cs`
- Test: `src/XIVLauncher.Mac.Tests/MacSettingsServiceTests.cs`
- Create: `src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj`

**Step 1: Write failing settings tests**

Create tests for:

```csharp
[TestMethod]
public async Task LoadAsyncReturnsDefaultsWhenFileDoesNotExist()
{
    var service = new MacSettingsService(new FileInfo(Path.Combine(tempDir, "settings.json")));

    var settings = await service.LoadAsync();

    Assert.AreEqual(ClientLanguage.English, settings.Language);
    Assert.IsNull(settings.OfficialAppPath);
}

[TestMethod]
public async Task SaveAndLoadRoundTripsSettings()
{
    var service = new MacSettingsService(new FileInfo(Path.Combine(tempDir, "settings.json")));
    var settings = new MacLauncherSettings
    {
        OfficialAppPath = "/Applications/FINAL FANTASY XIV ONLINE.app",
        LastUsername = "test-user",
        Language = ClientLanguage.Japanese,
        IsSteam = false,
        IsFreeTrial = false,
    };

    await service.SaveAsync(settings);
    var loaded = await service.LoadAsync();

    Assert.AreEqual(settings.OfficialAppPath, loaded.OfficialAppPath);
    Assert.AreEqual(settings.LastUsername, loaded.LastUsername);
    Assert.AreEqual(settings.Language, loaded.Language);
}
```

**Step 2: Run tests to verify failure**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
```

Expected: fails because project/service do not exist.

**Step 3: Implement settings**

Use `System.Text.Json`. Suggested defaults:

```csharp
public sealed class MacLauncherSettings
{
    public string? OfficialAppPath { get; set; }
    public string? LastUsername { get; set; }
    public ClientLanguage Language { get; set; } = ClientLanguage.English;
    public bool IsSteam { get; set; }
    public bool IsFreeTrial { get; set; }
}
```

`MacSettingsService` should:

- Accept a `FileInfo` path.
- Create parent directory on save.
- Return defaults when missing.
- Use indented JSON.

**Step 4: Run tests**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac/Settings src/XIVLauncher.Mac.Tests
git commit -m "feat: add mac launcher settings"
```

### Task 3: Scaffold Avalonia Mac Host

**Files:**
- Create: `src/XIVLauncher.Mac/XIVLauncher.Mac.csproj`
- Create: `src/XIVLauncher.Mac/Program.cs`
- Create: `src/XIVLauncher.Mac/App.axaml`
- Create: `src/XIVLauncher.Mac/App.axaml.cs`
- Create: `src/XIVLauncher.Mac/ViewModels/MainWindowViewModel.cs`
- Create: `src/XIVLauncher.Mac/Views/MainWindow.axaml`
- Create: `src/XIVLauncher.Mac/Views/MainWindow.axaml.cs`
- Modify: `src/XIVLauncher.sln`

**Step 1: Create minimal Avalonia project**

Add package references:

```xml
<PackageReference Include="Avalonia" />
<PackageReference Include="Avalonia.Desktop" />
<PackageReference Include="Avalonia.Themes.Fluent" />
<PackageReference Include="Avalonia.Fonts.Inter" />
```

Project references:

```xml
<ProjectReference Include="..\XIVLauncher.Common\XIVLauncher.Common.csproj" />
<ProjectReference Include="..\XIVLauncher.Common.Unix\XIVLauncher.Common.Unix.csproj" />
```

**Step 2: Build minimal window**

The first window should include:

- App title.
- Install status text.
- App path text.
- Username input.
- Password input.
- OTP input.
- Language selector.
- Launch button.
- Status output text.

Keep layout simple and functional.

**Step 3: Add project to solution**

Use `dotnet sln` if available:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet sln src/XIVLauncher.sln add src/XIVLauncher.Mac/XIVLauncher.Mac.csproj
```

If that mutates unrelated solution metadata heavily, edit the solution manually.

**Step 4: Build**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac src/XIVLauncher.sln
git commit -m "feat: add avalonia mac launcher host"
```

### Task 4: Add Install Detection View Model

**Files:**
- Modify: `src/XIVLauncher.Mac/ViewModels/MainWindowViewModel.cs`
- Test: `src/XIVLauncher.Mac.Tests/MainWindowViewModelTests.cs`

**Step 1: Write failing tests**

Test that the view model:

- Shows detected install status when `OfficialMacAppLocator.TryResolve` succeeds.
- Shows missing install status when resolution fails.
- Uses configured app path before default path.

Use a small abstraction, such as `IOfficialMacAppResolver`, rather than calling static locator directly from tests.

**Step 2: Implement resolver abstraction and view model state**

Add properties:

- `OfficialAppPath`
- `GameRootPath`
- `InstallStatus`
- `CanLaunch`
- `StatusMessage`

**Step 3: Run tests**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 4: Build Mac host**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac src/XIVLauncher.Mac.Tests
git commit -m "feat: show official mac app install status"
```

### Task 5: Wire Login And Launch

**Files:**
- Modify: `src/XIVLauncher.Mac/ViewModels/MainWindowViewModel.cs`
- Create: `src/XIVLauncher.Mac/Services/MacLaunchService.cs`
- Test: `src/XIVLauncher.Mac.Tests/MacLaunchServiceTests.cs`

**Step 1: Write failing service tests**

Test behavior around:

- Missing install throws or returns clear failure.
- Launch service passes resolved `OfficialMacAppGameRunner` and game root to `Launcher.LaunchGame`.
- Dalamud is not used.

Use test doubles around `Launcher` if direct testing is too coupled. If `Launcher` is hard to mock, extract a small interface inside the Mac project for the launch orchestration.

**Step 2: Implement launch service**

Flow:

1. Resolve install.
2. Create `Launcher`.
3. Check boot version.
4. Login.
5. If patch state is `NeedsPatchBoot` or `NeedsPatchGame`, return a patch-required status for the first pass unless patch handling is already straightforward.
6. If login state is `Ok`, call `LaunchGame` with `OfficialMacAppGameRunner`.

Use `CommonUniqueIdCache` under `~/Library/Application Support/XIVLauncherMac/uidCache.json`.

**Step 3: Wire view model command**

Launch button should:

- Disable while running.
- Save non-secret settings.
- Update status messages.
- Surface login/patch/launch errors without crashing the app.

**Step 4: Run tests and build**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: PASS.

**Step 5: Commit**

```bash
git add src/XIVLauncher.Mac src/XIVLauncher.Mac.Tests
git commit -m "feat: launch official mac app from gui"
```

### Task 6: Local Smoke Test

**Files:**
- No source edits expected unless smoke testing finds a concrete issue.

**Step 1: Verify detection against real app**

Run:

```bash
find '/Applications/FINAL FANTASY XIV ONLINE.app' -maxdepth 1 -print
find '/Applications/FINAL FANTASY XIV ONLINE.app/Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn' -maxdepth 1 -type d -print
```

Expected: app and game root exist.

**Step 2: Run the Mac GUI**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet run --project src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected:

- Window opens.
- Official app is detected.
- Login fields are usable.
- Launch flow reaches login/patch/launch status without unhandled exception.

**Step 3: Record blockers**

If the official app Wine runtime needs additional environment variables, update `OfficialMacAppGameRunner` with only observed requirements and add a note to `docs/plans/2026-04-26-mac-only-gui-design.md`.

**Step 4: Final verification**

Run:

```bash
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Common.Tests/XIVLauncher.Common.Tests.csproj --filter "OfficialMacAppLocatorTests|PatchPlatformTests" /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet test src/XIVLauncher.Mac.Tests/XIVLauncher.Mac.Tests.csproj /p:LangVersion=preview
PATH="/opt/homebrew/opt/dotnet@9/bin:$PATH" dotnet build src/XIVLauncher.Mac/XIVLauncher.Mac.csproj /p:LangVersion=preview
```

Expected: PASS.
