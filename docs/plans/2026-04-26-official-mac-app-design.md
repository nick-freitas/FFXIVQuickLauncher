# Official Mac App Support Design

## Goal

Add support for launching the user's existing official `FINAL FANTASY XIV ONLINE.app` installation from XIVLauncher without creating a separate managed Mac install.

## Scope

The first version targets the Square Enix macOS app bundle installed at `/Applications/FINAL FANTASY XIV ONLINE.app`. That bundle is a CrossOver/Wine wrapper with a Windows-style FFXIV installation inside the app bundle. The launcher should detect that bundle, resolve its embedded game root, and use the bundle's runtime to start the game.

This does not add a new Mac installer, migrate game files out of the bundle, or replace the official app's CrossOver runtime.

## Current Repo Shape

The repo already has partial Mac awareness:

- `Platform.Mac` exists in `src/XIVLauncher.Common/Platform.cs`.
- `PlatformHelpers.GetPlatform()` returns `Platform.Mac` on macOS.
- `Constants.PatcherUserAgent` returns `FFXIV-MAC PATCH CLIENT` on Mac.
- `XIVLauncher.Common.Unix` contains Wine-oriented runners and macOS Steam library packaging.

The core game flow still assumes the Windows client in several places:

- Patch URLs are hard-coded to `http/win32/...`.
- `Launcher.LaunchGame()` launches `game/ffxiv_dx11.exe`.
- Existing Unix launch support assumes XL-managed Wine settings rather than the official Mac app bundle's CrossOver bottle.

## Target App Layout

The official app bundle uses this structure:

```text
/Applications/FINAL FANTASY XIV ONLINE.app/
  Contents/
    Info.plist
    SharedSupport/finalfantasyxiv/
      FINAL FANTASY XIV ONLINE/wine
      support/published_Final_Fantasy/
        cxbottle.conf
        drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn/
          boot/
          game/
```

The FFXIV game root is the directory that contains `boot` and `game`.

## Proposed Architecture

Add an official Mac app integration layer that is separate from the existing XL-managed Unix compatibility tools.

1. Discovery
   - Detect `/Applications/FINAL FANTASY XIV ONLINE.app` by default.
   - Verify `Contents/Info.plist` has `CFBundleIdentifier` set to `com.square-enix.finalfantasyxiv`.
   - Resolve the embedded game root under `Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy/drive_c/Program Files (x86)/SquareEnix/FINAL FANTASY XIV - A Realm Reborn`.
   - Accept the install only when the resolved root contains `boot` and `game`.

2. Launch
   - Add a Mac-specific `IGameRunner` implementation for the official bundle.
   - Use `Launcher.LaunchGame()` to build the normal encrypted launch arguments.
   - Start `ffxiv_dx11.exe` through the bundle's `Contents/SharedSupport/finalfantasyxiv/FINAL FANTASY XIV ONLINE/wine` executable.
   - Set `WINEPREFIX` to `Contents/SharedSupport/finalfantasyxiv/support/published_Final_Fantasy`.
   - Use the game directory as the working directory.

3. Patch and login compatibility
   - Preserve the existing `FFXIV-MAC PATCH CLIENT` user agent for macOS.
   - Replace hard-coded patch route strings with a platform patch channel helper.
   - Keep the current `win32` route for Windows, Wine-on-Linux, and Linux unless Mac-specific testing shows the official app requires a different route.

## Testing

Add focused tests for:

- Official Mac bundle path resolution.
- Rejection of missing or non-Square-Enix bundles.
- Patch channel selection.

Manual smoke testing on macOS should confirm:

- The launcher can discover the installed app.
- The resolved game root points at the app bundle's embedded FFXIV install.
- The launch runner builds the expected Wine command and environment.

## Risks

- The official app bundle is managed by Square Enix/CrossOver and may change layout in future updates.
- Running `ffxiv_dx11.exe` directly may require additional CrossOver environment variables beyond `WINEPREFIX`.
- Patching files inside `/Applications` may require permissions depending on install ownership.
- The app may only contain a partial game install until the official launcher has completed patching once.

## Out Of Scope

- Creating or managing a separate Mac install.
- Replacing the official app's Wine/CrossOver runtime.
- Adding native Dalamud injection support for the official Mac app in the first pass.
- Implementing a full macOS UI frontend for XIVLauncher.
