# Public release preparation

Target repository: [jinwovo/desktop-pet](https://github.com/jinwovo/desktop-pet). Version 0.5.0 includes a validated runtime-included Windows package and a clean source export. Release publication and repository visibility are separate publisher-controlled operations; a local package does not imply a published GitHub release or a successful Actions run.

## What is ready

- Three original public characters: Companion, Nimbus and Ember. The repository also includes the explicitly selected Chiikawa, Baikinman and Hello Kitty fan-art packs in a separate [optional pack directory](../character-packs/fan-art/README.md). The default runtime-included release keeps the original collection; optional packs can be imported or included with `PersonalBuild=true`.
- A local25/5 focus timer and test-notification button. Notifications use the existing durable inbox and honor hidden/quiet mode.
- English, Korean and Simplified Chinese READMEs, a recorded GIF, character gallery, authoring guide, issue/PR templates, contribution/security guidance and MIT license.
- Continuous body/foot rendering and fractional native-position correction. Whole-body angle changes are still immediate transitions.
- Windows verification/package workflow and a **Draft GitHub release** workflow triggered manually or by a pushed `v*` tag. The latter creates a reviewable draft after the verification workflow succeeds; it does not publish the draft automatically.

## Export a clean public source tree

```powershell
powershell -NoProfile -File scripts/export-public-source.ps1
```

The command creates a fresh `.local/public-source-<timestamp>/` folder, a matching ZIP and SHA256 sidecar. `PUBLIC_SOURCE_MANIFEST.json` lists every exported source file and its checksum. Existing folders, ZIPs and checksum sidecars are never overwritten.

The allowlist includes source, tests, public docs, original public atlases, the three original character folders and the explicitly selected optional fan-art packs. It excludes `.claude`, `.codex`, `.aws`, `.agents`, unselected private artwork, old private preview pages, `.git`, build outputs, local settings and inbox data. The clean source can be built without the original workspace's private `Assets/Characters` folders.

Review the exported folder before copying it into an authenticated clone of the repository. SSH access was verified on 2026-11-15 and the original two-commit repository was inspected in an isolated clone. Earlier character resources are replaced in the current tree by the original collection and the separately marked optional packs. `.gitignore` does not remove already tracked files or erase historical commits: the original history still contains earlier private/third-party character artwork. The MIT notice does not grant third-party character rights in either the historical images or optional packs. A history rewrite is not performed automatically.

## Build and review a Windows release

The default package is self-contained and requires access to missing NuGet runtime/build packs:

```powershell
powershell -NoProfile -File scripts/package-release.ps1 -Version 0.5.0
```

An explicitly labelled alternative requires the user's .NET10 Desktop Runtime x64:

```powershell
powershell -NoProfile -File scripts/package-release.ps1 -Version 0.5.0 -FrameworkDependent
```

The output directory is `artifacts/releases/`. Keep the entire ZIP layout. Compare its `.zip.sha256`, extract it and validate the manifest before reviewing the app on a test Windows profile. Do not use your real assistant configuration as a regression fixture.

For GitHub, push the reviewed source through your normal authenticated workflow. Run **Windows distribution** to collect artifacts, or run **Draft GitHub release** with version `0.5.0` (or push a `v0.5.0` tag) to create a draft after checks. The draft includes the runtime-included app and separate optional character pack ZIPs, each with a checksum. It validates that its requested version matches the source and never replaces or publishes an existing release automatically. Review the draft assets and notes before publishing. See [GitHub's workflow trigger documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).

## Local validation status

The Release build has zero warnings/errors. Core checks pass130 cases, with7 link-privilege skips out of137. Public-only WPF checks pass40/40, including actual retained drawing, pixel placement, all three public packs, focus controls and durable quiet/hidden notifications. Capture bounds pass16/16. Package fixtures pass14/14, installer fixtures9/9, assistant hooks12/12, source export6/6, notification command3/3 and public characters3/3 groups.

A real **framework-dependent single-file** executable was also published locally using installed build packs. Its analyzer-only package dependency was disabled for this local bundle; trimming and AOT were not enabled. This does not change the default self-contained release recipe. Its version command reports`0.5.0.0` and the generated ZIP is checked against its manifest. It requires .NET10 Desktop Runtime x64.

A real **self-contained single-file** ZIP was built on 2026-11-15 after obtaining the official runtime packs. Its extracted manifest, public character collection, license and SHA256 were checked. With external .NET roots pointed at an empty fixture, the executable still reported `0.5.0.0`; its runtime configuration includes .NET Core and Windows Desktop 10.0.14 rather than requiring external frameworks. This is not a clean-PC installation test.

A clean PC without .NET, mixed-DPI physical-monitor movement, and remote GitHub Actions execution remain unverified. No hardware-independent FPS, app-wide RAM saving, signed binary, automatic update or Mac/Linux support is claimed.

## Repository presentation

Suggested description:

> A quiet Windows desktop pet with grounded animation, local Claude Code/Codex completion alerts, a focus timer, and custom character packs.

Suggested topics: `desktop-pet`, `windows`, `wpf`, `dotnet`, `claude-code`, `codex`, `pomodoro`, `mascot`, `open-source`.

Use the original character gallery or an excerpt from the GIF as a preview. Keep the English README as the default entry and the language switch links visible. A clear installation path, a small working release, honest limitations and responsive issue handling give visitors a useful reason to try and recommend the project; star counts cannot be guaranteed.
