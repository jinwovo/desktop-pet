# Changelog

## 0.14.0 — portable settings backup

- Export/import bounded, versioned JSON preferences through Settings. Transfer display, behavior, quiet hours and focus options; exclude assistant auto-registration/setup state, trust, inbox and local paths.
- Reject unknown/duplicate/mistyped fields before applying, normalize numeric bounds, preserve omitted preferences and validate replacement character artwork first. Missing characters keep the current pet.
- Update live controls and effects while retaining an active focus countdown. Save backup files atomically and preserve old bytes on failed replacement.

## 0.13.0 — motion comfort and frame pacing

- Add 30/60/120 FPS work caps with phase-preserving pacing on high-refresh displays; default to 60. Physics and planted-foot motion still follow elapsed time and actual displacement.
- Add reduced motion: suppress autonomous hops, nagging bounce, window-fall capture and pose fades while preserving the user's saved effect settings and continuous walking.
- Share options with Settings, gallery and open motion previews; release subscriptions when windows close. Verify pacing at 60/120/144/165 Hz and actual desktop/preview wiring.

## 0.12.0 — scheduled quiet hours

- Add an opt-in daily local-time quiet schedule with midnight crossing and explicit HH:mm validation. Equal start/end times leave the schedule inactive.
- Suppress automatic bubbles while retaining durable notifications and explicit unread review. Entering the interval closes an existing bubble; leaving never replays old alerts or changes manual quiet mode.
- Poll only while the schedule is enabled and stop on disposal. Cover all 1,440 minute boundaries and real WPF/inbox behavior.

## 0.11.0 — configurable focus sessions

- Add persisted focus (1–180 minutes) and break (1–60 minutes) controls, preserving 25/5 defaults for older settings.
- Apply new durations to future starts and explicit restarts. Running/paused sessions retain their countdown and report the duration they actually completed.
- Validate both duration values before changing either; cover settings normalization and actual WPF controls.

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
