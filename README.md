<div align="center">

<img src="assets/custodian-1024.png" width="112" alt="Custodian logo" />

# Custodian

**An advanced companion for Sandboxie-Plus on Windows 11.**
Lists the applications that really live inside your sandboxes, launches them, and stays out of the way.

</div>

<p align="center">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" />
  <img alt="Windows" src="https://img.shields.io/badge/Windows%2011-22H2%2B-0078D4?logo=windows11&logoColor=white" />
  <img alt="UI" src="https://img.shields.io/badge/UI-WPF%20%2B%20Fluent%20%2B%20Mica-6E5BFF" />
  <a href="https://github.com/Dynesshely/Custodian/actions/workflows/release.yml"><img alt="Release pipeline" src="https://github.com/Dynesshely/Custodian/actions/workflows/release.yml/badge.svg" /></a>
  <img alt="Release" src="https://img.shields.io/github/v/release/Dynesshely/Custodian?color=2EA44F" />
  <img alt="Downloads" src="https://img.shields.io/github/downloads/Dynesshely/Custodian/total?color=2EA44F" />
  <img alt="License" src="https://img.shields.io/badge/license-Apache--2.0-blue" />
  <img alt="Stars" src="https://img.shields.io/github/stars/Dynesshely/Custodian?style=social" />
</p>

---

## Installation

| | |
|---|---|
| **Requirements** | Windows 11 22H2 or newer · [Sandboxie-Plus](https://sandboxie-plus.com/) installed |
| **Download** | [Latest release](https://github.com/Dynesshely/Custodian/releases/latest) → `Custodian-<version>-Setup.exe` |
| **Install** | Run the installer. Per-user, **no administrator rights required**, self-contained (no separate .NET runtime) |

Silent install: `Custodian-<version>-Setup.exe /S`. Uninstall from *Settings → Apps*.

## How to Develop

```powershell
git clone https://github.com/Dynesshely/Custodian.git
cd Custodian
dotnet build Custodian.slnx -c Release      # SDK version is pinned by global.json

pwsh -NoProfile -File tools/run-dev.ps1     # build + launch a dev instance with the debug channel
```

- `tools/custodian-debug.ps1 "<command>"` talks to the running instance (`state`, `nav`, `view`, `filter`, `folder`, `mem`, …). All automated checks go through this channel — no OS-level input injection.
- Performance tools: `tools/perf-monitor.ps1`, `perf-startup.ps1`, `perf-popup.ps1`, `gc-compare.ps1`.
- Settings live in `~/.custodian/settings.json`. An instance started with `--debug` (what `run-dev.ps1` does) **reads** the production settings but **writes** to `%TEMP%\custodian-dev`, so development never touches your real configuration.
- Installer: `dotnet publish` (win-x64, self-contained, single file) then `makensis installer/custodian.nsi`.

## Structure

```
src/
  Custodian.App/       WPF shell — MVVM, Fluent styles, card grid, folder popup, tray
  Custodian.Core/      sandbox discovery, application model, settings
  Custodian.Interop/   Win32 / DWM / icon / shortcut interop
installer/             NSIS installer script (per-user, no elevation)
tools/                 dev scripts, debug-channel client, probes, ThemeLab
assets/                logo and icon sources
```

## Key Info

- **Only what is really inside.** A program is listed only when it is installed *inside* a sandbox — host copies of the same executable are ignored.
- **Running badges are attributed, not guessed.** Liveness is resolved from each process image's physical path, so a same-named host process never lights up a sandbox card.
- **Keyboard-first.** Arrows move by visual rows, `Enter` launches, `Space` folds, `Esc` steps back one level. A mouse works too; nothing requires it.
- **Two views.** Standard cards (icon left, text right) and a compact 96×96 grid whose sandbox blocks flow and wrap; standard applications collapse into a folder tile that opens a frosted-glass popup.
- **Quiet and native.** Fluent + Mica, follows the system theme, ~0.4 % of one core while resident in the tray (sampling pauses while hidden).
- **Summon it anywhere.** Global hotkey `Ctrl+Alt+Space`; switching views with `Ctrl+Shift+V`.
- **Motion, kept quiet.** The panel eases in and out, the compact folder popup scales and fades, and launching an app sweeps a short accent segment around the window border before the panel retires. All of it follows the system "show animations" setting.
- **Settings window.** A separate window (also available from the tray menu): appearance (theme, card view) and a custom Sandboxie storage location when your sandboxes do not live in the default place.
- **Read-only by design.** Sandbox data is only read; the only writes are launching applications. An optional helper can clean host `.lnk` copies that leaked into sandbox folders.

<p align="center">
  <a href="https://star-history.com/#Dynesshely/Custodian&Date">
    <img alt="Star history" src="https://api.star-history.com/svg?repos=Dynesshely/Custodian&type=Date&theme=dark" width="620" />
  </a>
</p>