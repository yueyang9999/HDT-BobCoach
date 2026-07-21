# HDT-BobCoach

[中文](README.md) | [English](README.en.md)

[Documentation index](docs/README.md) (Chinese-first, with categorized links to all current documents)

[![CI](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/yueyang9999/HDT-BobCoach/actions/workflows/ci.yml)

Bob Coach is a coaching plugin for Hearthstone Deck Tracker (HDT) focused on Hearthstone Battlegrounds. It reads match state already known to HDT on the local computer and assists with card choices, compositions, positioning, and combat decisions.

The current public beta is `0.2.0-beta.2`. Official installers are provided only through this repository's [GitHub Releases](https://github.com/yueyang9999/HDT-BobCoach/releases). Do not treat source archives, CI artifacts, or third-party attachments as official installers.

## Download and Install

Windows 10 and Windows 11 use the same 64-bit installer package:

| Your system | Download | Validation status |
| --- | --- | --- |
| Windows 11 24H2 x64 | [Download Bob Coach 0.2.0-beta.2](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.2/BobCoach-0.2.0-beta.2-win-x64.zip) | physically verified |
| Windows 10 22H2 x64 | [Download the same Bob Coach 0.2.0-beta.2 package](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.2/BobCoach-0.2.0-beta.2-win-x64.zip) | Technically compatible; not completed dedicated physical validation |

[Download the SHA-256 checksum file](https://github.com/yueyang9999/HDT-BobCoach/releases/download/v0.2.0-beta.2/BobCoach-0.2.0-beta.2-win-x64.zip.sha256)

**Do not download** the GitHub-generated `Source code (zip)` or `Source code (tar.gz)` entries at the bottom of the Release page. They are source snapshots, not Bob Coach installer packages.

First-time users should follow the [Chinese installation guide](docs/user/INSTALL.md). The package can be extracted normally, but installation is not complete until `INSTALL.ps1` has been run as described in the guide.

## System Requirements

- Verified on physical hardware: Windows 11 24H2 x64
- Target compatibility: Windows 10 22H2 x64 (technically compatible, but not yet verified on dedicated physical hardware)
- Hearthstone Deck Tracker `1.53.5` x64
- The .NET Framework 4.8/4.8.1 runtime supplied by Windows
- Standard Windows user permissions

After installation, the plugin does not require Node.js, administrator privileges, or online dependency installation. Users must legally install HDT and Hearthstone themselves.

## Installation Summary

1. Close HDT.
2. Verify the ZIP's SHA-256 and extract all files to a normal local directory.
3. Run the following command from the extracted directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\INSTALL.ps1
```

The installer verifies package hashes, the DLL version, and the x64 architecture. It writes only to `%APPDATA%\HearthstoneDeckTracker\Plugins`. The `Plugins` directory under the HDT program directory is not the user plugin location and is rejected by the installer. Installation is complete only after `PASS installed` or `PASS upgraded` appears. See [UPGRADE](docs/user/UPGRADE.md), [ROLLBACK](docs/user/ROLLBACK.md), and [UNINSTALL](docs/user/UNINSTALL.md) for the corresponding procedures.

## Privacy and Network Access

Matches, logs, replays, and user profiles remain on the local computer and are not uploaded automatically. The current public build does not request, cache, or display Firestone/Zero to Heroes trinket statistics, and it does not read, migrate, or delete historical caches that an earlier build may have left behind. The initial release does not display trinket-offer choice prompts or let them preempt other recommendations; the display switch controls rendering only. After the player equips a trinket, Bob Coach still recognizes it from HDT match state and applies deterministic effects through versioned local `CardId` rules to later buy, sell, refresh, tavern-upgrade, composition, and combat decisions. See [PRIVACY.md](PRIVACY.md) and [DATA_SOURCES.md](DATA_SOURCES.md) for the complete boundaries.

## Data Sources and Third-Party Rights

Firestone/Zero to Heroes is retained only as historical evaluation context, not as a current runtime data source. The code retains source-independent validation and a restricted HearthstoneJSON/HearthSim hsdata adapter boundary, but the production plugin does not drive an external trinket-statistics request path. Bob Coach does not currently integrate, request, scrape, package, or redistribute HSReplay data. Third-party data, statistics, software, game content, and trademarks remain subject to their respective rights and terms. See [DATA_SOURCES.md](DATA_SOURCES.md) and [NOTICE](NOTICE).

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
