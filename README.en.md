# HDT-BobCoach

[中文](README.md) | [English](README.en.md)

[![CI](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml)

Bob Coach is a coaching plugin for Hearthstone Deck Tracker (HDT) focused on Hearthstone Battlegrounds. It reads match state already known to HDT on the local computer and assists with card choices, compositions, positioning, and combat decisions.

The current public beta is `0.2.0-beta.1`. Official installers are provided only through this repository's [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases). Do not treat source archives, CI artifacts, or third-party attachments as official installers.

## System Requirements

- Verified on physical hardware: Windows 11 24H2 x64
- Target compatibility: Windows 10 22H2 x64 (technically compatible, but not yet verified on dedicated physical hardware)
- Hearthstone Deck Tracker `1.53.5` x64
- The .NET Framework 4.8/4.8.1 runtime supplied by Windows
- Standard Windows user permissions

After installation, the plugin does not require Node.js, administrator privileges, or online dependency installation. Users must legally install HDT and Hearthstone themselves.

## Installation

1. Download `BobCoach-0.2.0-beta.1-win-x64.zip` and `BobCoach-0.2.0-beta.1-win-x64.zip.sha256` for the same version from [Releases](https://github.com/yueyang9999/HDT-BobCoach/releases).
2. Close HDT.
3. Verify the ZIP's SHA-256 and extract it to a normal local directory.
4. Run the following command from the extracted directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

The installer verifies package hashes, the DLL version, and the x64 architecture. It writes only to `%APPDATA%\HearthstoneDeckTracker\Plugins`. The `Plugins` directory under the HDT program directory is not the user plugin location and is rejected by the installer. See the [installation guide](docs/user/INSTALL.md) for complete instructions. See [UPGRADE](docs/user/UPGRADE.md), [ROLLBACK](docs/user/ROLLBACK.md), and [UNINSTALL](docs/user/UNINSTALL.md) for the corresponding procedures.

## Privacy and Network Access

Matches, logs, replays, and user profiles remain on the local computer and are not uploaded automatically. The plugin makes two read-only external data requests to validate aggregate trinket statistics and card facts for the current game build. Request failures do not block local recommendations, and unverified data does not enter production scoring or UI ordering. See [PRIVACY.md](PRIVACY.md) and [DATA_SOURCES.md](DATA_SOURCES.md) for the complete boundaries.

## Data Sources and Third-Party Rights

The current read-only runtime sources are aggregate trinket statistics from Firestone/Zero to Heroes and game facts from HearthstoneJSON/HearthSim hsdata. Bob Coach does not currently integrate, request, scrape, package, or redistribute HSReplay data. Third-party data, statistics, software, game content, and trademarks remain subject to their respective rights and terms. See [DATA_SOURCES.md](DATA_SOURCES.md) and [NOTICE](NOTICE).

## Build and Test

The repository does not contain HDT binaries. Building requires the .NET Framework 4.7.2 Developer Pack and a local HDT `1.53.5` directory:

```powershell
$env:BOBCOACH_HDT_DIR = 'C:\path\to\HDT'
npm test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build\build_release.ps1 `
  -HdtDirectory $env:BOBCOACH_HDT_DIR `
  -OutputDirectory "$env:TEMP\bobcoach-build" `
  -Force
```

Node.js only drives contract tests with no third-party dependencies and is not part of the plugin runtime. Maintainers should first read [BUILD.md](docs/maintainer/BUILD.md) and [RELEASE.md](docs/maintainer/RELEASE.md).

## Support and Voluntary Contributions

Use GitHub Issues to submit a minimal reproduction. Do not publicly upload a complete `Power.log`, replays, account information, tokens, or unredacted absolute paths. All user-facing features remain free. Contributions must be entirely voluntary, unlock no features, and never appear inside Hearthstone or the HDT overlay. No payment address, QR code, or live contribution link is currently published. See [SUPPORT.md](SUPPORT.md).

## Disclaimer

Bob Coach is an independent community project. It is not affiliated with, sponsored by, or endorsed by Blizzard Entertainment, HearthSim, HDT, Firestone, Zero to Heroes, Gamerhub, or HSReplay. Third-party names are used only to identify compatibility, data provenance, or non-use boundaries. See [NOTICE](NOTICE).

## License

Original project code and original materials that the contributors have the right to license are released under the [MIT License](LICENSE). The repository license does not relicense third-party software, data, statistics, game content, or trademarks.
