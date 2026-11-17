# Changelog

## 0.10.0 — ZIP character installation

- Import root, wrapped and multi-character ZIP packs directly from Settings. Collections show an explicit character picker; only the chosen pack is installed.
- Keep archives read-locked through selection and installation. Reject unsafe/duplicate paths, links, special entries, file-directory conflicts and oversized archives before extraction; enforce stream and per-pack bounds.
- Reuse full PNG validation and atomic library publication, preserve installed names and refresh an open gallery after success. Cancellation/failure preserves the active pet; closed Settings ignores late activation.
- Add malicious archive/core cases and actual WPF installation, collection selection, cancellation, corrupt-image, gallery and close-during-import checks. Update English, Korean and Simplified Chinese guides.

## 0.9.0 — character personalities

- Add optional validated behavior weights, rest/walk duration multipliers, sleep thresholds and short descriptions to character manifests.
- Give all nine original/personal characters distinct presets, with a shared generation input that preserves profiles when rebuilding grounded walking artwork.
- Apply the selected personality to autonomous behavior and show its description in Settings. Preserve speed, physical pose, gait and user sleep/calm preferences.
- Retain exact default behavior for older manifests. Add validation, deterministic choice/timing, sleep opt-out and mid-walk character-profile regression cases.

## 0.8.0 — monitor topology and landing recovery

- Represent monitor work areas with X/Y rectangles and choose the pet's floor using its actual desktop position. Fix drops on a lower stacked monitor snapping to the upper screen.
- Keep upper-boundary soles stable and avoid phantom landings when a throw turns inside a taskbar gap. Recover onto the nearest remaining monitor after display removal.
- Publish copied monitor geometry atomically and invalidate old platform scans/native placement after display changes. Preserve legacy floor-only consumers.
- Add stacked/reordered/negative/offset/gap/removal regression cases. Physical mixed-DPI movement remains an explicit manual-validation item.

## 0.7.0 — fullscreen and session cooperation

- Temporarily pause the pet, bubbles, platform scanning and optional window-fall capture while a foreground window covers the pet's monitor.
- Pause presentation on session lock and system suspend, preserving manual visibility, durable notifications and the logical focus timer. Refresh foreground state on unlock/resume.
- Add a default-on fullscreen cooperation checkbox. Own/shell/minimized/invisible windows and fullscreen on another monitor are excluded.
- Check fullscreen using local foreground geometry only; no screenshots or window titles are needed. Add pure geometry/settings cases and isolated WPF lock/power/notification tests.

## 0.6.1 — pause hidden background scans

- Stop window-platform scanning while the pet is hidden, including a hidden startup. Showing the pet requests an immediate background refresh.
- Use a single scan at a time and schedule subsequent scans after completion. Discard an old scan if the pet was hidden, shown again or disposed before it completed.
- Keep notification intake and focus timers active while hidden. Native window enumeration remains off the UI thread, and disposal does not wait for a slow native scan.
- Add nine deterministic/core timer regression groups and an actual pet-visibility wiring check; preserve all nine characters and the existing gallery.
- Refresh an open gallery after importing through Settings. Preserve search, category and card selection, cancel obsolete thumbnail loads and ignore results after closure.
- Use NVIDIA-assisted design/review through the optional development helper. No NVIDIA credential or model reply is bundled with the app.

## 0.6.0 — character gallery and expanded collection

- Add Pompompurin, Cinnamoroll and Kuromi as optional AI-assisted fan-art packs, selected using the official 2026 Sanrio ranking. Existing six companions are preserved, giving personal builds nine choices.
- Add a searchable character gallery with Korean/English aliases and original/fan-art/custom filters. Card selection and isolated motion previews preserve the active pet until explicit application.
- Decode only bounded frozen idle thumbnails for gallery cards, validate PNG headers before decoding, and handle corrupt or replaced resources without replacing the active pet.
- Share a validated six-pack catalog across packaging and personal builds. Keep the default public app's original collection and optional packs separate.
- Include reproducible source masters, generation prompts, nine-character validation and EN/KO/zh-CN gallery documentation. Preserve the console-free UTF-8 hook fix from 0.5.0.

## 0.5.0 — release preparation

- Add Nimbus and Ember, two AI-assisted original public character designs, alongside Companion.
- Add separately marked optional Chiikawa, Baikinman and Hello Kitty fan-art packs, importable through Settings and available to personal builds; third-party character rights are outside the project MIT grant.
- Add a local 25-minute focus / 5-minute break timer with start, pause, resume, restart and stop controls in Settings and the tray.
- Route timer completions and the new test-notification button through the normal durable inbox, retaining hidden and Do not disturb behavior.
- Keep walking continuous with reusable body/foot textures, distance-driven foot placement and fractional native-window position correction.
- Add English, Korean and Simplified Chinese READMEs, original-character previews and character authoring documentation.
- Add community templates, an MIT license, an allowlisted public-source exporter with SHA256 inventory, and a draft-release workflow triggered manually or by version tags.
- Expand public release validation to Companion, Nimbus and Ember; preserve compatibility with older Companion-only release manifests.

The actual runtime-included Windows ZIP was built and its extracted manifest, SHA256, public assets and isolated-runtime `--version` execution were verified on 2026-11-15. The subsequent console-free UTF-8 hook fix passed the [0.5.0 Windows Actions run](https://github.com/jinwovo/desktop-pet/actions/runs/43623762967). A clean-PC installation remains unverified. No published GitHub release is claimed by this changelog.
