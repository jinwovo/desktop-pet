# Changelog

## Unreleased ? 1.3.0 development

- P021: release the gallery's reference to a closed motion preview so its decoded presentation can be collected while the gallery stays open. Keep replacement-preview ownership intact and allow reopening normally.

## 1.2.0 — evolution patches P011–P020

- Add session-only roaming pause and keep drag/throw physics intact when reactions are requested; play the latest reaction after landing.
- Apply reduced motion to notification fades, show compact source labels, and copy notification text without marking it read.
- Share tray actions with a pet right-click menu. Mark current notifications read in the background while preserving new arrivals, failed receipts and responsive UI.
- Keep notification source/event identities distinct and protect unread duplicates beyond the bounded recent-history window.
- Cancel obsolete gallery preview requests and ignore late results/errors without altering current busy state. Localize live motion previews without resetting their caches or simulation.
- Reach 20 verified product patches in the [2.0 ledger](docs/evolution-2.0.json). Documentation, version changes and release repairs are excluded from that count.

## 1.1.0 — evolution patches P001–P010

- Stabilize cursor-facing artwork around the pet's center; retain the grounded stopping phase when Settings changes from walking to rest.
- Pause hidden/minimized Settings and motion previews, rebase their render clocks on resume, and share the weighted idle timeline through walking-to-rest handoff.
- Preserve explicit unread review across scheduled quiet-hour boundaries. Apply dismissal-policy changes directly to the existing timer so the displayed receipt remains the one acknowledged.
- Localize bubble controls and gallery categories/status/counts in KO/EN/zh-CN while retaining user notification text, search, selection and image caches. Language changes reuse the current Settings presentation and gait phase.
- Pass cancellation into an already running gallery preview decoder and ignore late results after close.
- Replace overlapping rest-layer opacity fades with premultiplied pixel interpolation from one canonical walking snapshot, preserving opaque interior pixels. The final short blend reuses that snapshot and ends on the exact resting bitmap.
- Begin the [100-patch evolution ledger](docs/ROADMAP_2_0.md). Verified milestone tags publish after the patch threshold and Windows distribution gates pass; release administration is excluded from the product-patch count.

## 1.0.2 — brisker gait and bounded turns

- Ease upper-body facing changes over 120 ms through a cached, footless frontal pivot instead of mirroring the whole character in one frame or superimposing opposite faces. Keep immediate distance-based foot reflection and ground contact; normal walking remains side/quarter-view. Derive pivot/reflected body images once and reuse the existing premultiplied blend buffer.
- Finish side/quarter-view interpolation in 100 ms rather than carrying an exponential tail. Retarget from the current pose, preserve refresh-rate independence and honor reduced motion.
- Raise the nine curated characters' base walking speeds by about 20% and lower live-rig foot lift from 4 to 3 sprite pixels. Keep user speed multipliers, custom packs and fallback PNG cycles intact; update source generators to match the curated tuning.
- Check actual upper-body reversal pixels and contact anchors for every curated character, fixed transition deadlines and rapid retargets. Include a real WPF walk/reverse/stop capture; desktop smoothness still depends on the display and host workload.

## 1.0.1 — layered walking transitions

- Separate the cached body artwork into clipped head and torso layers alongside two independent feet. Apply small rigid rotations and head lag; keep planted feet unrotated and remove secondary motion during settling.
- Interpolate side/quarter-view pixels in premultiplied alpha using three reusable buffers, avoiding instant texture swaps and the opacity dip of two translucent view layers. Blend the final settling pose into the next resting frame in the desktop, Settings and motion preview.
- Preserve existing pack PNGs/manifests, distance-based contact, runtime frame caps and reduced-motion preferences. No additional PNG frames or development API calls are needed at runtime.
- Add refresh-rate independence, rapid-retarget and actual WPF pixel/contact/rest-convergence checks for all nine curated characters. Capture an actual WPF motion GIF; renderer-loop measurements exclude render-thread/GPU cost and are not desktop FPS guarantees.

## 1.0.0 — public Windows release

- Ship the verified 0.11–0.99 functional milestones as the first stable runtime-included Windows x64 release, with three original companions and a separate six-character optional fan-art collection.
- Include configurable focus sessions, quiet hours, primary KO/EN/zh-CN UI, reduced motion/frame caps, portable settings, reviewed local diagnostics, monitor recovery and cancellable ZIP/folder installation.
- Retain grounded walking, durable local assistant notifications and explicit hook trust. Runtime use requires no development API credential.
- Gate publication on core/UI/asset/hook/package/docs checks, actual collection ZIP import, isolated executable checks and actual payload lifecycle fixtures. Publish only the approved `v1.0.0` tag; retain existing releases and immutable history.
- Keep physical clean-PC/mixed-DPI validation, signing and additional advanced-dialog translation as explicit follow-up work.

## 0.99.0 — release candidate installation checks

- Add `--check-install`: read-only JSON resource validation before normal startup, with exit 0 for valid installation or 2 for invalid resources. Decode every bundled character and check the original collection/hook bridge without opening settings, assistant profiles, inbox or pet windows.
- Sanitize check results to fixed error codes and counts; include no local paths or raw exception text.
- Exercise actual extracted executables with isolated profile roots and actual payload install/reinstall/remove fixtures before the stable release. Keep existing synthetic rollback, hook and package suites.

## 0.90.0 — cancellable character installation

- Report extraction/copy/PNG validation progress and add an installation Cancel button. Propagate cancellation through archive inspection, bounded stream copying, folder staging and image decoding; closing Settings also requests cancellation.
- Check cancellation immediately before atomic publication and clean unpublished stages. If publication already completed, keep the installed pack but respect Cancel by retaining the current pet. Ignore stale progress after completion/closure.
- Bound folder/startup manifests to the same 256 KiB limit as archives and retain source/copy/decode budgets. Add cancellation, retry/mutex, manifest and real UI token/progress regressions.

## 0.50.0 — monitor position recovery

- Add a monitor picker and explicit recovery button in Settings; the tray can bring the pet to the monitor nearest the pointer.
- Land at the selected work-area floor, clear old drag/throw/walk momentum and gait history, refresh platform scans and show a manually hidden pet while preserving fullscreen cooperation.
- Preserve unread receipts, quiet mode and focus countdown. Reject a monitor removed since selection and refresh the picker without moving the pet.

## 0.30.0 — reviewed local diagnostics

- Show app version and a manually opened official release page in Settings; never auto-download or execute updates.
- Add a read-only diagnostic preview and local JSON export with a fixed schema of runtime/architecture and bounded presentation facts. Exclude paths, usernames, character identity, chat contents, environment variables, API keys and hook configuration.
- Reuse a single owned diagnostic window and close it with Settings. Verify exact preview/export bytes and unchanged preferences.

## 0.20.0 — primary interface languages

- Add persisted Korean/English/Simplified Chinese language selection with an offline UI catalog for primary Settings, tray controls, focus/quiet/backup status and ZIP selection.
- New profiles can follow Windows language; older settings keep Korean. Character names, authored descriptions and user-entered text remain intact. Some advanced dialogs and error details retain Korean.
- Refresh static and dynamic primary controls together, preserve physical motion/assistant preferences and remove closed-window language subscriptions. Include language in portable backups.

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
