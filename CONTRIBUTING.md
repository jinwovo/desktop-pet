# Contributing to DesktopPet

[English README](README.md) · [한국어](README.ko.md) · [简体中文](README.zh-CN.md)

Small, reviewable contributions are welcome: reproducible motion fixes, original characters, accessibility improvements, translations and tests. Open an issue before a large change so we can agree on its behavior and scope.

## Local development

Use Windows 10/11 x64 and the .NET 10 SDK. `global.json` selects a compatible stable SDK feature band.

```powershell
dotnet restore DesktopPet.sln
dotnet build DesktopPet.sln -c Release --no-restore
dotnet tests/DesktopPet.RegressionTests/bin/Release/net10.0-windows/DesktopPet.RegressionTests.dll
dotnet tests/DesktopPet.UiSmokeTests/bin/Release/net10.0-windows/DesktopPet.UiSmokeTests.dll --capture
powershell -NoProfile -File tests/public-characters-regression.ps1
powershell -NoProfile -File tests/assistant-hooks-regression.ps1
powershell -NoProfile -File tests/package-regression.ps1
powershell -NoProfile -File tests/install-regression.ps1
powershell -NoProfile -File tests/public-source-regression.ps1
powershell -NoProfile -File tests/notify-command-regression.ps1
```

Tests use isolated configuration and inbox folders. Do not use a real assistant configuration as a test fixture. Symbolic-link tests report a skip when Windows does not grant link-creation privileges. UI tests open real WPF windows.

## Pull requests

Describe the concrete problem, resulting behavior and validation. For motion changes, attach a short recording with character name, Windows version, DPI, refresh rate and reproduction steps. For a behavior change, add a regression covering the failure, rather than a test that only mirrors the implementation. Keep unrelated refactoring separate.

Do not commit `.claude`, `.codex`, tokens, user settings, notifications, `bin`, `obj`, `.local` or private artwork. Do not make live assistant registrations in tests. Follow `.editorconfig`; PowerShell scripts containing non-ASCII text use UTF-8 with BOM for Windows PowerShell 5.1.

## Character packs

See [Character pack format](docs/CHARACTER_PACKS.md). Public collection submissions must be original designs with clear provenance and permission to distribute. Include the source artwork, reproduction steps and preview. AI-assisted artwork must identify the tool and design brief honestly; do not present it as manually illustrated. Do not submit third-party characters, brands or downloaded images without suitable permission.

Preserve alpha transparency, aspect ratio, common foot baseline and unclipped outlines. Supply every referenced PNG. A legacy sprite pack is welcome; live rigs are optional. Public artwork must pass import validation and the character checks.

## Translations and accessibility

Keep README.md, README.ko.md and README.zh-CN.md aligned when changing supported behavior. The app currently uses mostly Korean labels; full UI localization is a future contribution area. Keyboard access, screen-reader labels and reduced motion improvements are welcome.

## Reporting security issues

Read [SECURITY.md](SECURITY.md) before sharing sensitive reports. Normal bugs belong in [Issues](https://github.com/jinwovo/desktop-pet/issues). Submitted contributions are provided under the repository's [MIT license](LICENSE); keep third-party notices with any approved dependency.
