# Mac Dalamud Spike Design

## Goal

Prove whether this launcher can start the official macOS FFXIV app through Dalamud.

The spike is successful when FFXIV starts through `Dalamud.Injector.exe` and manual verification confirms that the in-game Dalamud UI or plugin installer opens. Discord Rich Presence is not part of this spike.

## Scope

The spike adds a developer/debug launch path only. The normal Mac launcher behavior remains unchanged unless the debug path is explicitly enabled.

In scope:

- Add a hidden/debug switch such as `--experimental-dalamud`.
- Reuse `DalamudUpdater` to download and verify Dalamud, its runtime, and its assets.
- Reuse `DalamudLauncher` to prepare launch metadata.
- Add a dedicated official-Mac Dalamud runner that launches `Dalamud.Injector.exe` inside the official app Wine environment.
- Produce clear diagnostic errors for each launch stage.
- Add unit tests for wiring, path conversion, argument construction, and error propagation.

Out of scope:

- Public UI for enabling Dalamud.
- Discord Rich Presence verification.
- Launcher-side plugin management.
- Custom Wine prefixes beyond the official Mac app.
- Steam account support.

## Architecture

`XIVLauncher.Mac` keeps its current default path. Without the debug switch, `MacLauncherService` continues to call `Launcher.LaunchGame` with `OfficialMacAppGameRunner`.

With the debug switch enabled, `MacLauncherService` routes launch through a Mac-specific Dalamud path:

1. Create a `DalamudUpdater` using Mac launcher data directories.
2. Run the updater for the release track.
3. Create a `DalamudLauncher`.
4. Verify the downloaded Dalamud release supports the installed game version.
5. Call `Launcher.LaunchGame` with a new official-Mac Dalamud runner.
6. The runner starts the downloaded `Dalamud.Injector.exe` with the official app's Wine executable and `WINEPREFIX`.

The runner should be specific to the official Mac app rather than adapting `CompatibilityTools`. The official app is a CrossOver-style app bundle with its own Wine runtime layout, while `CompatibilityTools` is designed for managed Linux/Steam Deck Wine prefixes.

## Data And Paths

Dalamud-managed files should live outside the official FFXIV app bundle, under the Mac launcher data folder.

Suggested paths:

- `~/Library/Application Support/XIVLauncher/addon/Hooks/<version>` for downloaded Dalamud.
- `~/Library/Application Support/XIVLauncher/runtime` for the downloaded Windows .NET runtime.
- `~/Library/Application Support/XIVLauncher/dalamudAssets` for shared assets.
- `~/Library/Application Support/XIVLauncher/installedPlugins` for Dalamud plugins.
- `~/Library/Application Support/XIVLauncher/dalamudConfig.json` for Dalamud configuration.
- `~/Library/Application Support/XIVLauncher/logs` or the app data root for logs.

The runner must convert macOS paths into Wine-visible Windows paths before passing them to `Dalamud.Injector.exe`.

Initial conversion rules:

- Paths under `WINEPREFIX/drive_c/...` become `C:\...`.
- Other absolute Unix paths use Wine's `Z:` mapping, for example `/Users/name/...` becomes `Z:\Users\name\...`.

The spike should log the injector command shape with secrets excluded.

## Error Handling

Failures should be explicit and diagnostic. The launch result should identify the failed stage when possible:

- official Mac app install resolution
- Dalamud download or integrity verification
- runtime or asset download and verification
- game version mismatch with current Dalamud release
- injector process start failure
- unreadable injector output
- injector succeeded but process tracking failed

For the spike, process tracking can be conservative. If the injector starts the game and reports a Wine PID, but the launcher cannot map that to a macOS process, the result may report that injection appears to have succeeded while process tracking is unknown. Perfect process tracking should not block the spike unless it prevents the game from launching.

## Testing

Automated tests should cover launcher behavior without running Wine or injecting into the game.

Unit coverage should include:

- the debug switch selects the experimental path only when enabled
- the normal Mac path remains unchanged
- macOS-to-Wine path conversion
- injector argument construction
- updater and runner failures becoming clear `MacLaunchResult.Failed(...)` messages

Manual verification is required for the real spike:

1. Run the Mac launcher with `--experimental-dalamud`.
2. Log in with the official Mac app install detected.
3. Confirm Dalamud downloads.
4. Confirm FFXIV launches.
5. Confirm the Dalamud in-game UI or plugin installer opens.
6. Capture logs if the injector fails.

## Open Risks

- The official Mac app Wine runtime may lack behavior expected by the current Dalamud injector.
- The downloaded Windows .NET runtime may need additional environment variables under the official app Wine runtime.
- Wine PID to macOS PID mapping may require additional CrossOver-specific handling.
- Some Dalamud plugins may still fail even if core Dalamud loads.
