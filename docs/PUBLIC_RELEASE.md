# Public release preparation

Target repository: [jinwovo/desktop-pet](https://github.com/jinwovo/desktop-pet). Version 0.6.0 adds a searchable character gallery and three more optional fan-art characters. Local validation covers the original-only release and all nine personal-build characters. Check [Windows distribution runs](https://github.com/jinwovo/desktop-pet/actions/workflows/windows-release.yml) for the status and exact commit of remote verification. Release publication is a separate publisher-controlled operation.

## What is ready

- An already-open gallery refreshes after Settings imports, retains filters and selected cards, and rejects obsolete or closed-window load results. Nine local gallery regression groups cover these flows and the existing preview/apply behavior.
- Version 0.6.1 pauses hidden window-platform scans, queues an immediate background refresh on show and rejects stale in-flight results. It keeps native scans sequential and leaves notifications/focus timers running. Local core validation passes 139 cases with 7 link-privilege skips out of 146, and the nine-character WPF suite includes a real visibility/monitor wiring check. NVIDIA-assisted reviews are advisory; implementation is checked against local tests. This does not claim fullscreen, session-lock or stacked-monitor support, which remain separate work.
- Three original public characters: Companion, Nimbus and Ember. The separate [optional pack directory](../character-packs/fan-art/README.md) contains six explicitly selected fan-art characters: Chiikawa, Baikinman, Hello Kitty, Pompompurin, Cinnamoroll and Kuromi. The default release keeps the original three; importing optional packs or building with `PersonalBuild=true` makes all nine available. `DistributionBuild=true` takes precedence and keeps the original three.
- A character gallery with Korean/English name and alias search, original/fan-art/custom filters, static card previews, a separate motion preview and explicit application. Searching and previewing preserve the active desktop pet until **이 친구와 함께하기** is pressed.
- The three new fan-art characters were selected from the official [2026 Sanrio Character Ranking](https://corporate.sanrio.co.jp/en/news/20260628_03.html), placing first, second and fourth respectively. [Creation records](../character-packs/fan-art/POPULAR_ASSETS.md), a versioned catalog and a [three-character preview](../character-packs/fan-art/popular-characters-preview.png) accompany the artwork. Third-party character rights and fan-art images are outside the MIT grant.
- A local25/5 focus timer and test-notification button. Notifications use the existing durable inbox and honor hidden/quiet mode.
- English, Korean and Simplified Chinese READMEs, a recorded GIF, character gallery, authoring guide, issue/PR templates, contribution/security guidance and MIT license.
- Continuous body/foot rendering and fractional native-position correction. Whole-body angle changes are still immediate transitions.
- Windows verification/package workflow and a **Draft GitHub release** workflow triggered manually or by a pushed `v*` tag. The latter creates a reviewable draft after the verification workflow succeeds; it does not publish the draft automatically.

## Export a clean public source tree

```powershell
powershell -NoProfile -File scripts/export-public-source.ps1
```

The command creates a fresh `.local/public-source-<timestamp>/` folder, a matching ZIP and SHA256 sidecar. `PUBLIC_SOURCE_MANIFEST.json` lists every exported source file and its checksum. Existing folders, ZIPs and checksum sidecars are never overwritten.

The allowlist includes source, tests, public docs, original public atlases, the three original character folders and the six curated optional fan-art packs. Optional pack files must be referenced by their validated metadata. The exact three new source masters, creation records, catalog and preview are also included; unrelated private artwork is excluded. It excludes `.claude`, `.codex`, `.aws`, `.agents`, old private preview pages, `.git`, build outputs, local settings and inbox data. The clean source can be built without the original workspace's private `Assets/Characters` folders.

Review the exported folder before copying it into an authenticated clone of the repository. SSH access was verified on 2026-11-15 and the original two-commit repository was inspected in an isolated clone. Earlier character resources are replaced in the current tree by the original collection and the separately marked optional packs. `.gitignore` does not remove already tracked files or erase historical commits: the original history still contains earlier private/third-party character artwork. The MIT notice does not grant third-party character rights in either the historical images or optional packs. A history rewrite is not performed automatically.

## Build and review a Windows release

The default package is self-contained and requires access to missing NuGet runtime/build packs:

```powershell
powershell -NoProfile -File scripts/package-release.ps1 -Version 0.9.0
```

An explicitly labelled alternative requires the user's .NET10 Desktop Runtime x64:

```powershell
powershell -NoProfile -File scripts/package-release.ps1 -Version 0.9.0 -FrameworkDependent
```

The output directory is `artifacts/releases/`. Keep the entire ZIP layout. Compare its `.zip.sha256`, extract it and validate the manifest before reviewing the app on a test Windows profile. Do not use your real assistant configuration as a regression fixture.

For GitHub, push the reviewed source through your normal authenticated workflow. Run **Windows distribution** to collect artifacts, or run **Draft GitHub release** with version `0.6.1` (or push a `v0.6.1` tag) to create a draft after checks. The draft includes the runtime-included app and separate optional character pack ZIPs, each with a checksum. It validates that its requested version matches the source and never replaces or publishes an existing release automatically. Review the draft assets and notes before publishing. See [GitHub's workflow trigger documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).

Review the gallery by searching Korean and English aliases, selecting a static card preview, opening **움직임 미리보기**, and applying with **이 친구와 함께하기**. Confirm that search and both preview steps leave the active desktop pet unchanged. For optional packs, verify six import folders plus the catalog, notices, provenance and per-file SHA256 inventory in the separate archive.

## Validation baseline and current scope

Version 0.6.0 local Release builds pass with zero warnings/errors. The original-only and nine-character WPF suites both pass, including six gallery groups, actual PNG foot contact, continuous live-rig transforms and settling. All six optional characters activate with 19 decoded textures each. The new raster assets use 4x supersampling during generation to avoid fractional sole sampling jitter; independently measured worst contact drift is below 0.18 source pixels for each new character, within the existing 1.25-pixel regression limit. Core checks pass 130 cases with 7 link-privilege skips. Optional packaging and source-export fixtures each pass all six groups; 13 public documents resolve 86 local links. A runtime-included original-only 0.6.0 ZIP was extracted and its full manifest, checksum and isolated-runtime version startup verified. This does not substitute for a clean-PC test.

The following results record the historical 0.5.0 baseline.

The Release build has zero warnings/errors. Core checks pass130 cases, with7 link-privilege skips out of137. Public-only WPF checks pass40/40, including actual retained drawing, pixel placement, all three public packs, focus controls and durable quiet/hidden notifications. Capture bounds pass16/16. Package fixtures pass14/14, installer fixtures9/9, assistant hooks12/12, source export6/6, notification command3/3 and public characters3/3 groups.

A real **framework-dependent single-file** executable was also published locally using installed build packs. Its analyzer-only package dependency was disabled for this local bundle; trimming and AOT were not enabled. This does not change the default self-contained release recipe. Its version command reports`0.5.0.0` and the generated ZIP is checked against its manifest. It requires .NET10 Desktop Runtime x64.

A real **self-contained single-file** ZIP was built on 2026-11-15 after obtaining the official runtime packs. Its extracted manifest, public character collection, license and SHA256 were checked. With external .NET roots pointed at an empty fixture, the executable still reported `0.5.0.0`; its runtime configuration includes .NET Core and Windows Desktop 10.0.14 rather than requiring external frameworks. This is not a clean-PC installation test.

The [0.5.0 Windows Actions run](https://github.com/jinwovo/desktop-pet/actions/runs/43623762967) was verified successful on 2026-11-15. That result covers the 0.5.0 source at that run. The current workflow additionally checks all nine characters before packaging. A clean PC without .NET and mixed-DPI physical-monitor movement remain unverified. No hardware-independent FPS, app-wide RAM saving, signed binary, automatic update or Mac/Linux support is claimed.

## Repository presentation

Suggested description:

> A quiet Windows desktop pet with grounded animation, local Claude Code/Codex completion alerts, a focus timer, and custom character packs.

Suggested topics: `desktop-pet`, `windows`, `wpf`, `dotnet`, `claude-code`, `codex`, `pomodoro`, `mascot`, `open-source`.

Use the original character gallery or an excerpt from the GIF as a preview. Keep the English README as the default entry and the language switch links visible. A clear installation path, a small working release, honest limitations and responsive issue handling give visitors a useful reason to try and recommend the project; star counts cannot be guaranteed.
