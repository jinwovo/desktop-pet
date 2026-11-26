# Changelog

## 1.8.0 — evolution patches P071–P080

- P080 Use -PassThru to return the installed release, executable path and optional Windows integration results.
- P079 Accept -PackageArchive and validate the adjacent SHA-256 sidecar before extracting the same open archive and checking its contents.
- P078 Report a startup registration failure separately from app installation and preserve existing unrelated startup entries.
- P077 Stop managed operations on a folder containing an interrupted installer transaction. Show the retained recovery path for the user.
- P076 Validate the files copied to the staging area before changing the active installation.
- P075 Stop before overwriting a conflicting unmanaged file. Files that match the package can become managed on the first install.

- P074 Protect the current settings, previous backup and staging files from claiming or removal by installed payload inventories.
- P073 Support the Windows PowerShell -WhatIf preview before disconnecting assistants, deleting managed files or changing shortcuts.
- P072 Expose -VerifyOnly to check an extracted release manifest, version and file hashes without installing the app.
- P071 Create missing Start Menu folders when possible. Preserve unrelated shortcuts and report the installed executable when shortcut creation fails. Add an explicit switch to skip it.

## 1.7.0 — evolution patches P061–P070

- P070 Expose a localized retry button after settings cannot be saved. A successful retry saves the pending preferences and hides the button.
- P069 Capture portable preferences before starting a worker write. Show progress, keep existing files on a write failure and cancel pending writes when Settings closes.
- P068 Read the portable backup and load any replacement artwork on a worker. Apply only a live request and preserve settings if a closed window or newer character choice supersedes it.
- P067 Keep the latest backup or restore result visible when the Settings language changes.
- P066 When startup uses a valid recovery copy, schedule a write of the recovered settings after initialization succeeds. Preserve the recovery copy during repair.
- P065 Flush the staged settings file before replacing the current file and clean only the staging file created by this save.

- P064 Skip an unchanged write so the recovery copy keeps the older distinct version.
- P063 Limit local settings files to 256 KiB and fall back when the primary exceeds that size. Preserve BOM-aware decoding of previous settings.
- P062 Retry failed settings writes after 2, 4 and 8 seconds while the app is running. A persistent failure reports once and shutdown permits one final attempt without starting a timer.
- P061 Keep the previous valid local settings and recover it if the primary is missing or malformed. Repairing the primary preserves the good backup.

## 1.6.0 — evolution patches P051–P060

- P060 Show the current inbox snapshot directly when there is no search or source filter, avoiding a second list copy.
- P059 Use the original textures for endpoint poses and allocate the reusable blend bitmap only when needed.
- P058 Compute cumulative boundaries when loading a manifest and locate frames with binary search, including exact and repeated boundaries.
- P057 Propagate gallery cancellation through directory enumeration and manifest and thumbnail loading so superseded work can stop sooner.
- P056 Reuse the displayed items when revised search criteria yield the same card references in the same order.
- P055 Relabel the existing result source and keep the selected card and preview request.
- P054 Use the composition callback for live walking and settling, then return to the low-rate timer at rest.
- P053 Keep raster lookup on the raster path and verify native foot positions using the actual live pose.

- P052 Restrict repeated live-rig texture uploads to the changed region while keeping the same pixels and blend weights.

- P051 Remove per-call allocations from weighted animation frame selection while preserving frame timings.

## 1.5.0 — evolution patches P041–P050

- P050 Refresh the paused pose when reduced motion changes while preserving simulation time and the inspected stride.

- P049 Choose the same left or right look frame and display orientation during settling and after the raster handoff.

- P048: ease initial vertical gait lift and secondary motion over 90ms instead of instantly raising a lowered foot on restart. Preserve exact horizontal planted contacts, distance phase, legacy timing and bounded completion; resume interrupted settling continuously and skip the added easing for reduced motion. Apply the same behavior to desktop, Settings and motion previews without new bitmap captures or timers.

- P047: search the gallery with Ctrl+F and reset its query/category together with Ctrl+L, without rescanning or applying a character. Keep selection when still visible, preserve F5 refresh and text editing, provide localized visible/accessibility hints, and support input during discovery while rejecting disabled or closed actions.

- P046: localize gallery search/category/list accessibility names and usage help, and expose each card's original authored name with its localized category. Preserve Unicode/custom names, filter/selection and the active pet through language changes, and detach closed-window metadata updates.
- P045: keep Settings motion-test, AI-connection and Close actions plus the section-navigation hint visible in a fixed footer. Scroll only the unchanged body cards, preserve time-editor/section shortcuts and keep all controls inside the client area across KO/EN/zh-CN at 500px height. Closing still leaves unapplied drafts untouched.
- P044: jump directly to Settings character, size/speed, focus and notification sections with Ctrl+1–4. Scroll the target into view and focus it without applying edits or changing the active pet. Localize the visible hint/window accessibility help and reject unknown, disabled, hidden or closed navigation targets.
- P043: apply focus/break durations and quiet-hour schedules with Enter inside their four time editors, before the window's default Close action can receive it. Reuse existing validation, keep invalid drafts open, preserve active focus sessions and other key/IME routing, localize shortcut hints and ignore closed-editor input.
- P042: make saved-notification export, selected acknowledgement and filter reset available with Ctrl+S, Ctrl+Shift+R and Ctrl+L. Route the same existing actions, recheck empty/busy/closed guards on command execution, preserve Find/Escape and normal Delete editing, and localize shortcut tooltips/accessibility help in KO/EN/zh-CN.
- P041: provide localized UI Automation names for all eleven Settings selectors, sliders and time inputs, plus duration/HH:mm help for the four time fields. Refresh metadata on language changes without applying drafts or changing the active character, and ignore closed-window callbacks. Native automation-peer checks cover KO/EN/zh-CN; no manual screen-reader claim is made.

## 1.4.0 — evolution patches P031–P040

- P040: pause automatic notification bubbles for 15 minutes from either shared menu while retaining unread receipts and the running focus timer. Expire at the exact UTC deadline without another polling timer, preserve manual/daily quiet settings and do not replay saved popups on expiry/resume. Honor a menu's displayed Resume action even if its deadline passes while it remains open, and ignore disposed callbacks.
- P039: export the currently visible notification snapshot as local JSON through an explicit filename picker. Preserve original titles/messages/source/timestamps and unread receipts, capture before the picker can change filters, protect existing files and publish through an owned temporary file. Cancel unfinished saves on close, clean temporary files on failure/cancellation and localize saving/success/retry feedback. Keep all footer actions usable at minimum window size.
- P038: explicitly mark only the selected saved notification read without blocking the UI. Capture receipt identity before awaiting disk work, protect against duplicate/bulk actions and preserve other items and new arrivals. Failed deletions retain their receipt and offer a localized retry. Closing/reopening the browser cannot retarget an already requested operation or update its closed controls; disposed popup callbacks are ignored.
- P037: search saved notification titles, messages and sources literally, combine the query with source filters and clear both through a button or Escape. Preserve raw search drafts, stable source filters and still-visible selected receipts through language/arrival updates. Show filtered/total counts and distinct empty/no-match/deselection hints, support Ctrl+F and keep long titles usable at minimum window size. Browsing/filtering leaves notifications unread.
- P036: browse saved notifications in one independent window from the tray or pet menu, including while the pet is hidden. Preserve original titles/messages, show source and local timestamps, keep selected receipts through arrivals/language changes and allow native text selection/copy without marking anything read. Coalesce arrival/locale refreshes, virtualize offscreen rows and detach queued work/subscriptions when closing.
- P031: add **Add 5 min** to Settings and the shared tray/pet focus menu. Extend only the current focus or break, retain pause state and saved defaults, and report added active time at completion. Enforce a 24-hour total session cap, complete expired sessions once, and keep an old request from extending a new session started by a completion callback. Localize controls/tooltips in KO/EN/zh-CN and ignore closed/disposed callbacks.
- P032: show completed focus sessions for this app run in Settings and the shared menu. Reset only the counter without ticking or stopping the timer, changing extensions/defaults or acknowledging unread notifications. Refresh immediately when only the count changes, exclude breaks, localize the count/reset controls and ignore closed/disposed callbacks. The count is not persisted.
- P033: preserve unapplied focus/break minutes, quiet-hour text and the pending schedule-enable choice when switching Settings language. Keep valid, partial, invalid and whitespace drafts intact until Apply. Explicit valid backup restores still replace drafts with restored preferences; rejected restores and closed language callbacks leave them intact.
- P034: retain timer/schedule validation and Apply feedback as resource keys so language changes translate the latest result instead of clearing it. Keep error, success and equal-range messages independent. Validated restores clear stale feedback; rejected restores retain it, and closed Apply callbacks cannot modify preferences.
- P035: add 25/5 and 50/10 duration quick-fill buttons. Change only the editable draft, show a localized Apply reminder and leave saved defaults, active/paused sessions and temporary extensions unchanged until explicit Apply. Preserve drafts through language changes, replace stale validation with draft feedback and ignore closed callbacks.

## 1.3.0 — evolution patches P021–P030

- P021: release the gallery's reference to a closed motion preview so its decoded presentation can be collected while the gallery stays open. Keep replacement-preview ownership intact and allow reopening normally.
- P022: discover character directories and read gallery aliases on the existing background worker. Keep input/search responsive during slow discovery, cancel superseded/closed requests and discard late results without overwriting the current catalog.
- P023: refresh the gallery with a localized button or F5, discover externally added/removed characters and retry failed scans without reopening. Preserve search/category/selection and ignore repeat input while loading; stale workers cannot re-enable the action during a newer scan.
- P024: prepare character changes off the UI thread across Settings, gallery apply and post-import activation. Share the resulting frozen presentation with Settings instead of decoding it again. Cancel superseded/closed requests, retain current scale and simulation, and preserve newer explicit choices when an earlier import finishes. Failed/cancelled loads keep the active character and preferences intact. A validated backup restore supersedes pending selection and immediately synchronizes the Settings preview; rejected backups leave the request intact.
- P025: export a selected gallery character as a shareable ZIP. Validate a private snapshot and include only the manifest and declared PNGs, preserve nested paths/profile, cancel unfinished exports and keep existing/concurrently-created files intact. Progress follows the exported character while browsing other cards. Add KO/EN/zh-CN controls and documentation; round-trip exported ZIPs through the normal importer without changing the active pet.
- P026: pause/resume the motion preview and inspect one frame at the selected 30/60/120 FPS cap. Freeze both simulation and raster pose-dissolve clocks, retain paused poses through language changes and hide/minimize, prepare jump/drop trajectories and rebase time on resume. Reset stays paused; the real desktop pet remains independent. Keep the same window size and localize controls in KO/EN/zh-CN.
- P027: preview Cry/Angry from dedicated motion-scene controls and inspect their real frames while paused. Preserve drag offsets and throw trajectories; show localized queued-reaction feedback and play the latest request after landing. Reset clears pending feedback and inactive controls cannot affect the scene. Verify actual expression pixels for all nine curated characters.
- P028: place the procedural anger glyph in clear space around the head instead of covering leaves, ears or hats. Use a pulse-independent placement envelope, shrink only as needed and omit the ornament when no safe space exists. Regenerate only twelve anger frames for Companion, Nimbus, Ember, Pompompurin, Cinnamoroll and Kuromi; preserve masters, gait, manifests and other character art.
- P029: save the exact motion-preview scene as a PNG, including live rig poses, raster blends, facing transforms and platforms. Snapshot before the filename picker, encode off the UI thread, preserve existing files and cancel unfinished work when closing. Keep playback/stepping available and localize saving/success/retry feedback in KO/EN/zh-CN. The image contains only the controlled preview canvas.
- P030: add optional preview frame/anchor guides for artwork alignment. Retain cached vector shapes, preserve non-square proportions and declared anchor coordinates, follow the pet without advancing its gait, and include enabled guides in scene PNGs. Keep drag/throw/queued reactions intact; reset retains the toggle and new previews start with guides off. Localize labels/tooltips and disable inactive controls.

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
