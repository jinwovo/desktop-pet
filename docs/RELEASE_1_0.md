# DesktopPet 1.0 release review

The 1.0 target combines functional milestones: 0.11 configurable focus/break sessions, 0.12 daily quiet hours, 0.13 reduced motion and frame pacing, 0.14 portable preferences, 0.20 primary UI languages, 0.30 local diagnostics, 0.50 monitor recovery and 0.90 cancellable character installation. Version 0.99 validates the release candidate before 1.0 publication. Intermediate numbers represent changes; empty number-only releases are not generated.

## User-facing scope

- Windows x64 portable app with its .NET runtime, three original companions and a separate six-character optional fan-art ZIP.
- Grounded continuous walking, independent preview/gallery, safe folder/ZIP import and progress/cancellation before atomic installation.
- Local Claude Code/Codex turn-completion hooks, durable unread receipts, manual/quiet-hours suppression and fullscreen/session cooperation.
- Configurable focus/break timing, render work caps and reduced motion, settings transfer, read-only diagnostic preview and explicit monitor recovery.
- Korean/English/Simplified Chinese primary Settings and tray. Character-authored descriptions and some advanced dialogs/error details retain Korean.

## Validation gates

Each functional milestone has a Release build, core checks, nine-character WPF checks and a versioned personal executable. Final public output separately verifies the original-only collection and that `DistributionBuild` overrides `PersonalBuild`.

The final pipeline checks the actual six-pack ZIP import, all declared PNGs, unsafe archives and cancellation, settings migration/transfer, language changes, fullscreen/manual visibility, monitor geometry, notification delivery, assistant registration fixtures, packaging, installation and documentation links. The optional NVIDIA development helper is tested offline and is never a runtime dependency.

`DesktopPet.App.exe --check-install` returns sanitized JSON and exit 0 or 2 before normal startup. The extracted executable is checked with isolated profile roots. Actual payload installation/reinstallation/removal uses workspace fixtures and preserves inbox/custom/unrelated data; its disconnect child is deliberately stubbed, while separate assistant-hook fixtures test surgical connection changes. No real assistant profile is used as a regression target.

Both self-contained outputs are exercised with empty external runtime roots. Source, exported trees, binaries and ZIP contents are scanned for the configured development credential and accidental environment files. Tests record skipped link fixtures explicitly when symbolic-link privileges are unavailable.

## Publication

The [verified release workflow](../.github/workflows/draft-release.yml) first runs all Windows gates, creates a new draft and uploads the app/optional ZIPs plus SHA256 sidecars. Existing releases and tags are never replaced.

At the project owner's explicit request to distribute 1.0.0, only a pushed **`v1.0.0`** tag enables the stable publication step. It validates the matching source version and all four draft assets, publishes the draft as Latest, and verifies its public state. Other tags remain drafts. Manual workflow runs require an existing matching remote tag.

## Limits and follow-up

Automated fixtures are not a physical clean-PC or mixed-DPI monitor test. Those remain manual follow-up. No hardware-independent FPS, signing certificate, automatic update service, macOS/Linux or native ARM64 package is claimed. Runtime use needs no NVIDIA API key. Diagnostic reports do not include usernames, computer names, file paths, character identity, chat bodies, hook configuration or credentials.

Original source licensing and optional third-party fan-art rights remain separate; see [LICENSE](../LICENSE) and the [fan-art notice](../character-packs/fan-art/NOTICE.md).
