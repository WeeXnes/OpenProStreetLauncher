# OpenProStreet Launcher

<p align="center">
  <img src="assets/logo.png" alt="OpenProStreet Logo" width="128" height="128">
</p>

The unified cross-platform launcher for Need for Speed: ProStreet with [OpenProStreet](https://code.weexnes.dev/OpenProStreet) on **Windows**, **Linux**, and the **Steam Deck**.

Built with [Avalonia UI](https://avaloniaui.net/) (.NET 9), it provides a native, modern interface and single-file self-contained executables for both operating systems.

---

## Features

- **Automated Component Management:**
  - Validates `nfs.exe` version 1.1 (unpacked No-CD).
  - Installs and updates [Ultimate ASI Loader](https://github.com/ThirteenAG/Ultimate-ASI-Loader), [XNFSPS MultiFix](https://code.weexnes.dev/OpenProStreetClient/XNFSPS-MultiFix), and the [OpenProStreet Client](https://code.weexnes.dev/OpenProStreetClient).
  - Installs controller support: [NFS-XtendedInput](https://github.com/xan1242/NFS-XtendedInput), from OpenProStreet's fork ([xtendedinput](https://code.weexnes.dev/xtendedinput)), which builds it for ProStreet and fixes its startup crash under Proton. It's updated with the client. It's on by default; the "Controller support" row turns it off and on again. Its settings (`scripts/NFS_XtendedInput.ini`) are never replaced.
  - Automatically notifies when a new OpenProStreet Client or Launcher version is available.
  - Detects and disables conflicting mods (such as `NFSProStreet.FusionFix` and `ProStreetRPC`).
- **Complete In-App Configuration:**
  - Visual editor for `scripts/OpenProStreetClient.ini`: server connection (official, custom, or LAN search), physics fixes (no tank slapper, no speed limiter), display options (borderless window, camera vibration, console window), and advanced debugging.
- **Steam Integration:**
  - Adds the game as a non-Steam game: **Need for Speed: ProStreet (OpenProStreet)** to all local Steam user profiles.
  - Automatically handles `shortcuts.vdf` backups (`*.openprostreet.bak`).
  - Sets up Proton compatibility and `WINEDLLOVERRIDES="dinput8=n,b"` on Linux.
- **Windows Features:**
  - Direct execution without requiring Steam.
  - Optional Steam shortcut integration.
  - Elevated permissions (UAC) support when game is in protected directories like `Program Files`.
- **Linux & Steam Deck Features:**
  - Proton Prefix setup and game installation from DVD ISO images or existing folders.
  - Automatic `nfs.exe` section header fix (`ExeFix`) for smooth Proton execution.
  - Desktop integration: `install` command registers desktop shortcut, menu entry, and hicolor icons for application launchers (GNOME, KDE, Rofi `drun`).
  - In-place auto-update via CLI (`update` command).

---

## Downloads & Usage

Download the release binary for your platform from the newest **Build Release Tag** build on [OneDev](https://code.weexnes.dev/OpenProStreetClient/OpenProStreetLauncher). Both builds are self-contained (no .NET runtime installation required).

### Windows

1. Download `OpenProStreetLauncher.exe`.
2. Place it in your Need for Speed: ProStreet game directory or anywhere on your PC.
3. Run `OpenProStreetLauncher.exe`. Select your game directory if prompted.
4. Click **Play** to start!

### Linux & Steam Deck

1. Download `OpenProStreetLauncher`.
2. Make it executable and run the installer:
   ```sh
   chmod +x OpenProStreetLauncher
   ./OpenProStreetLauncher install
   ```
3. The launcher is now available in your application menu and can be run with `openprostreet-launcher`.

#### Linux CLI Commands

| Command | Description |
|---|---|
| `install [--system]` | Installs binary to `~/.local/bin`, `.desktop` file to `~/.local/share/applications`, and icons to `~/.local/share/icons/hicolor`. Use `--system` (with `sudo`) to install system-wide. |
| `update [--force]` | Self-updates the launcher binary to the latest OneDev release build. |
| `uninstall [--system]` | Removes installed menu entries and binary. Settings and game prefix remain intact. |
| `version` / `-v` | Prints current version. |
| `help` / `-h` | Prints command line usage. |

---

## Building from Source

Prerequisites: [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) or newer.

Run the build script:
```sh
./build.sh
```

This will:
1. Run all unit and integration tests (`tests/LauncherTests.csproj`).
2. Build and publish self-contained `linux-x64` executable to `out/release/OpenProStreetLauncher`.
3. Build and publish self-contained `win-x64` executable to `out/release/OpenProStreetLauncher.exe`.
4. Verify CLI commands and integration.

---

## Releases

To release a new version:
1. Update `<Version>` in `OpenProStreetLauncher.csproj`.
2. Commit and push to `master`.
3. The OneDev CI pipeline will create the release tag and publish release artifacts.
