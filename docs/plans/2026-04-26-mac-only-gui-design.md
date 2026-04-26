# Mac-Only GUI Fork Design

## Goal

Turn this fork into a macOS-focused launcher with a GUI that uses the user's existing official `FINAL FANTASY XIV ONLINE.app` installation.

## License Position

The fork remains GPLv3-or-later. Modified source and binaries distributed from this fork must keep GPL-compatible licensing, preserve notices, and make corresponding source available.

## Context

The current `XIVLauncher` app project is a Windows-only WPF executable:

```xml
<TargetFramework>net9.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
```

That means Mac launch behavior cannot live in `XIVLauncher/Windows/ViewModel/MainWindowViewModel.cs` as a real macOS GUI path. The reusable pieces should instead live in platform-neutral/shared projects, with a new macOS-capable GUI host consuming them.

The useful lower-level Mac support already added should remain:

- Official Square Enix Mac app bundle discovery in `XIVLauncher.Common`.
- Patch route helper in `XIVLauncher.Common`.
- Official Mac app Wine runner in `XIVLauncher.Common.Unix`.

The WPF wiring commit should be backed out because it couples the Windows app to Unix launch behavior and creates unreachable Mac code.

## Host Choice

Use Avalonia for the macOS GUI.

Reasons:

- Runs on macOS with .NET.
- Keeps the fork mostly in C#.
- Can consume `XIVLauncher.Common` and `XIVLauncher.Common.Unix` directly.
- Can package as a macOS `.app`.
- Avoids Swift/.NET IPC and avoids WPF's Windows-only target.

Rejected alternatives:

- .NET MAUI Mac Catalyst: heavier platform ceremony for this use case.
- SwiftUI shell plus .NET helper: best native feel, but adds two build stacks and IPC before the launch path is proven.
- Reusing WPF: not viable for a native macOS app.

## First Version Scope

The first Mac GUI should prove the complete basic flow:

1. Detect the official `FINAL FANTASY XIV ONLINE.app`.
2. Show the resolved app path and embedded game root.
3. Let the user enter username, password, OTP, and client language.
4. Log in through `XIVLauncher.Common.Game.Launcher`.
5. Check boot/game patch state.
6. Either report that patching is required or run the existing patch flow if it can be safely reused.
7. Launch `ffxiv_dx11.exe` through the official app bundle's Wine runtime.

Dalamud is out of scope for the first pass.

## GUI Shape

Create `src/XIVLauncher.Mac` as an Avalonia desktop project targeting `net9.0`.

Initial screens can be minimal:

- Main window with detected install status.
- Login form with username, password, OTP, language, and launch button.
- Status/progress area for login, patch check, patching, and launch results.
- Settings affordance for overriding the official app path if auto-detection fails.

Do not copy the WPF UI wholesale. Keep the Mac UI intentionally small until login, patch, and launch are verified.

## Data Flow

Startup:

1. Load Mac settings.
2. Resolve configured official app path or `/Applications/FINAL FANTASY XIV ONLINE.app`.
3. Display install status.

Launch:

1. Build `Launcher` with platform services.
2. Call `CheckBootVersion`.
3. Call `Login`.
4. If patches are pending, invoke or surface patch flow.
5. Call `LaunchGame` with `OfficialMacAppGameRunner`.

## Settings

Use a Mac-specific settings file for the first pass instead of trying to reuse WPF `Config.Net` settings immediately.

Suggested path:

```text
~/Library/Application Support/XIVLauncherMac/settings.json
```

Initial settings:

- Official app path override.
- Last username.
- Client language.
- Free trial flag if needed.
- Steam flag if needed.

Do not store passwords.

## Risks

- The official Square Enix app bundle is CrossOver-based and may need more environment variables than `WINEPREFIX`.
- The embedded game install in `/Applications` may have write permission issues during patching.
- Existing patch UI/RPC code is tied to WPF expectations and may need a simplified Avalonia-native progress flow.
- Steam account support on macOS may need separate validation.

## Implementation Direction

1. Revert the WPF integration commit `15cc245d`.
2. Keep the lower-level locator, patch route helper, and Mac runner commits.
3. Add `XIVLauncher.Mac`.
4. Build the smallest Avalonia GUI around official app detection and launch.
5. Add patch handling only after login and non-patching launch plumbing is proven.

## Out Of Scope

- Windows support in this fork.
- Dalamud injection on macOS.
- Full feature parity with the Windows launcher UI.
- Replacing or modifying the official Square Enix app bundle runtime.
