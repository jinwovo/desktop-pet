# DesktopPet 2.0 evolution

The project owner requested 100 meaningful improvements from the 1.0.2 baseline to a public 2.0.0 release. Progress lives in the [patch ledger](evolution-2.0.json). A planned item is a work slot, not an implemented feature or a confirmed defect. Later titles are refined after reproduction and review.

## Counting and evidence

[1.7.0 is publicly verified](https://github.com/jinwovo/desktop-pet/releases/tag/v1.7.0) with P001–P070. The [Windows release run](https://github.com/jinwovo/desktop-pet/actions/runs/45738458189) passed. An independent download confirmed both archives, their SHA-256 sidecars, and isolated startup of the published executable. Earlier milestones are available in the [1.6.0 release](https://github.com/jinwovo/desktop-pet/releases/tag/v1.6.0) and [1.4.0 release audit](RELEASE_AUDIT_1_4.md).

A verified patch changes product behavior or repairs a reproducible defect, identifies its changed source files and names the regression fixtures that passed. Documentation, version changes, release administration and repeated tests do not count as additional product patches. A patch may resolve related symptoms together. Review findings are checked against source and execution before acceptance.

The first ten patches address pointer-facing jitter, preview state transitions and background work, idle-frame continuity, quiet-review integrity, bubble/gallery localization, decode cancellation, redundant image loading and opaque rest blending. User notification text and character-authored names remain intact. Development API credentials are excluded from runtime output.

[1.1.0 is publicly available](https://github.com/jinwovo/desktop-pet/releases/tag/v1.1.0) with P001-P010. 1.2.0 adds P011: the tray's **Pause roaming** toggle gently stops an existing walk and prevents new automatic walks and ledge hops. The pet stays visible; manual dragging, gravity, reactions and notification delivery continue. The toggle lasts for the current session, survives language and character changes, and resumes through the same menu item. Pausing at either ledge preserves the current facing direction. P011 has core regression and WPF localization checks and is not included in the 1.1.0 download.

P012 preserves a drag or throw when a manual reaction is requested. The latest requested reaction starts after landing with its full duration, while a pending notification keeps priority. Desktop recovery clears old queued reactions. Deterministic tests compare the full trajectory against an unaffected pet and check landing, duration, drag offsets and stale-reaction cleanup. Included in 1.2.0.

P013 makes the reduced-motion setting apply to notification appearance and dismissal fades. Enabling it completes an active appearance immediately or hides a popup already being dismissed; restoring preferences applies the same policy. Actual WPF animation-clock and visibility checks cover both transitions while preserving the sender's text. Included in 1.2.0.

P014 identifies Claude, Codex, focus-timer and test notifications with a compact source label. A label matching the title is hidden to avoid duplicate lines. User titles and messages stay unchanged, focus/test labels follow the UI language and unknown sender identifiers stay hidden. Tests cover live localization, unread review and actual delivery wiring. Included in 1.2.0.

P015 adds a copy button that keeps the notification visible and unread. It copies the displayed title and original message, offers localized retry feedback if the clipboard is busy, and resets feedback for the next notification. Routed input checks verify that the button neither acknowledges the receipt nor changes keyboard focus. Clipboard tests use an injected fake rather than touching the developer's clipboard; KO/EN/zh-CN WPF captures check the actual layout. Included in 1.2.0.

P016 localizes the motion preview title, instructions, buttons and live state labels. Changing KO/EN/zh-CN preserves the same presentation, simulation state, gait phase and animation clocks without another rig update. Closing the window releases both localization subscriptions. Automated checks and real WPF captures cover the three languages. Included in 1.2.0.

P017 cancels obsolete gallery preview requests when selection, filtering, application or catalog refresh changes their context. A late success or failure cannot replace the newest preview, overwrite its status or unlock its busy button. Relabeling an unchanged selection keeps its active decoder. Gated-worker regression tests cover cancellation-aware and late-finishing decoders, plus closing the gallery. Included in 1.2.0.

P018 keeps notification identity as a source/event pair, so delimiters inside either value cannot cause a different message to be discarded. Unread identities remain protected even after the bounded recent-history cache evicts them, and failed acknowledgements retain that protection. Real inbox/file tests cross the 512-entry history boundary, lock a receipt during acknowledgement and verify notifications without event IDs remain independent. Included in 1.2.0.

P019 opens the existing action menu from a right-click on the pet. The tray and pet share one menu builder and the same command callbacks, current pause state and localized labels. Right-clicks during a drag, while hidden, during presentation suppression or after disposal cannot open it or change motion. Routed-input tests open the actual WinForms menu, execute injected navigation callbacks and verify the pause command. Included in 1.2.0.

P020 marks the current unread snapshot read from either action menu. Receipt IO runs on a worker; new arrivals and failed receipts stay available. A second bulk request cannot start competing work, while a new receipt can still be acknowledged separately. Partial failures report a localized retry message, an expired new popup stays closed and late completion after disposal cannot update UI. Tests cover gated workers, a locked real receipt, dispatcher responsiveness and actual menu retry. Included in 1.2.0.

## Milestones

[1.3.0 is publicly available](https://github.com/jinwovo/desktop-pet/releases/tag/v1.3.0) with 30 verified product patches. The [1.3.0 release audit](RELEASE_AUDIT_1_3.md) records its exact source, successful tag workflow, actual download hashes, archive contents and isolated-runtime startup.

P021: closing a gallery motion preview removes the gallery's reference to that window. Weak-reference tests demonstrate that its decoded presentation becomes collectible while the gallery remains open, and reopening still works. This does not claim a particular working-set reduction or change the immutable 1.2.0 build.

P022 moves directory discovery and catalog JSON reads off WPF's UI thread. A gated worker test reproduces the original failure, verifies input/search while blocked, refreshes to a newer snapshot and closes during a late failure. The full nine-character UI suite and 227 core checks pass. Included in 1.3.0; the immutable 1.2.0 files remain unchanged.

P023 adds a localized **Refresh (F5)** action. External character additions/removals and temporary scan errors can be resolved without reopening the gallery. Filters, selection and the current pet/preferences remain intact; repeated F5 is ignored while loading, and an older request cannot enable the button while its successor is busy. Routed keyboard/button tests, gated workers and KO/EN/zh-CN minimum-size captures pass. Included in 1.3.0.

P024 moves character discovery and full image decoding to a background task for Settings selection, gallery application and post-import activation. The pet and Settings share the same frozen presentation; an exclusive PNG lock verifies that the preview does not reopen its files. Current scale/simulation stay live while loading. Gated tests cover rapid selections, stale success/failure, cancellation, disposal, locale changes while pending, closed owners, a newer gallery choice during import a cancelled import after a pending selection, and backup restore during a blocked decoder. Valid restores cancel older requests and immediately synchronize the combo/preview; rejected backups do not cancel a valid pending selection. Full nine-character UI checks pass. Included in 1.3.0.

P025 adds gallery **Export ZIP** and **Cancel export**. A private, fully validated snapshot contains only the character manifest and declared PNGs, including fallback frames. Publication uses a new destination filename; existing files and a destination created during compression remain intact. Core checks cover round-trip import, byte preservation, progress/cancellation, snapshot consistency, path/link rejection and cleanup. Real WPF checks export/re-import decoded images, reject corrupt walking art, preserve pet/preferences, localize progress and cancel closed-window work. All 238 core checks and the nine-character UI suite pass locally. Included in 1.3.0.

P026 adds motion-preview **Pause / Resume** and **Next frame** for character authors. The simulation advances by exactly 1/30, 1/60 or 1/120 second per manual frame and drives actual gait pixels. Controllable raster-dissolve clocks freeze too, advance by manual intervals and release the old sprite when complete. Tests cover stale render callbacks, current frame-cap changes, real pixels, jump/drop commands, locale changes, hidden/minimized windows, reset, resume, closed callbacks and reduced motion. Existing nine-character UI checks pass, including automatic pose dissolves and preview collection. KO/EN/zh-CN captures retain the original window size. Included in 1.3.0.

P027 exposes **Cry / Angry** in the same motion scene, using the real `PetBrain.StartEmote` path. Authors can pause and step through expression frames directly. Queued reactions retain drag offsets and the physical trajectory, identify the pending reaction in KO/EN/zh-CN, then play the newest request after landing with its own timer. Tests compare a reaction-free control trajectory and actual authored PNG/pixel changes for all nine installed curated characters; the complete UI suite passes. An actual WPF emotion overview supports visual review. This patch changes preview controls, not authored artwork. Included in 1.3.0.

P028 repairs an artwork defect exposed by that overview: the fixed anger overlay overwrote 28–296 existing character pixels at its base pulse across six marked characters. The generator now searches clear space near the head, uses the same maximum-pulse envelope for every frame and scales down only when necessary. Tests preserve every nontransparent source pixel, require a clear gap and no canvas clipping, verify stable centers and shipped marks, and exercise a synthetic sprout, invalid pulse values and full/tiny canvases. Only twelve anger PNGs change; an inventory comparison preserves all other 1,917 asset files. Native WPF captures and the complete nine-character UI suite pass. The other three fan-art characters retain their existing tint-only reactions. Included in 1.3.0.

P029 adds **Save scene PNG** to share an exact inspected frame. It captures only the preview canvas before the modal filename picker, freezes that bitmap for background encoding and publishes a new filename without overwriting existing files. Closing cancels unfinished output; late completion cannot update a closed window. Tests round-trip Cry and live-rig walk pixels for all nine curated characters, capture both raster-dissolve layers and a facing transform, exercise a picker that changes the scene, preserve playback/stepping during gated writes, verify localized busy/error/retry feedback and reject unfrozen/cancelled work. All existing UI checks pass. Header placement retains the original window size and over 200 scene pixels in each tested language. Included in 1.3.0.

P030 adds optional **Frame guides** for the frame boundary, declared baseline and anchor in unmirrored frame coordinates. Retained vector shapes follow the actor without ticking the simulation or changing its gait. Actual-pixel tests for all nine curated characters show that enabling guides changes the render, disabling restores it exactly, and a saved scene includes enabled guides. Tests also cover both walking directions, queued drag/throw reactions, reset, hidden/minimized/closed callbacks, localization and a 160×80 frame with an off-center anchor. The complete UI suite passes. P030 brings the verified product count to 30; release/version preparation is not an additional patch.

The [1.2.0 release audit](RELEASE_AUDIT_1_2.md) records the original post-publication API rate-limit failure and the successful read-only comparison of all four public files.

P031 begins 1.4.0 development with **Add 5 min** in Settings and the shared tray/pet focus menu. It extends only the current running/paused focus or break and its completion duration; saved defaults and pause state stay intact. Elapsed time is reconciled before extending, expired sessions complete once, and a completion callback's new session cannot inherit the older command. Eight core regression groups cover exact 24-hour limits, invalid intervals, eligibility purity, delayed ticks, callback restart/disposal and future defaults. Real WPF/native-menu tests cover commands, stale deadline clicks, localized cached menus, temporary-duration restart, close/disposal and durable receipts; the complete nine-character UI suite and all 246 core checks pass. KO/EN/zh-CN focus-card captures were visually inspected. P031 remains unreleased until the 40-patch 1.4.0 milestone.

P032 displays the app-run completed-focus count in Settings and the shared tray/pet menu. Resetting it changes no timer fields, emits no completion, preserves unread receipt bytes and does not persist statistics. The UI event cache includes the count, so a count-only reset immediately refreshes the label; reverting to the original caption/status-only cache reproduces a stale-count failure, and restoring the fix passes. Five new core regression groups cover breaks, idempotence, unticked deadlines/checkpoints, paused extensions/defaults, generation callbacks and disposal. Actual WPF/native-menu tests cover localization, count-only event delivery, both menu commands, saved preferences, receipts and closed/disposed callbacks. All 251 core checks and the full nine-character UI suite pass; KO/EN/zh-CN focus-card captures were inspected. P032 remains 1.4.0 development work.

P033 repairs locale-refresh draft loss in Settings. Real language selection originally overwrote unapplied focus/break minutes, quiet-hour text and the pending schedule checkbox with stored values. The locale handler now preserves all five raw editor values around its existing preview refresh. Tests reproduce both original failures, then verify valid/partial/invalid/whitespace edits across KO/EN/zh-CN, explicit Apply, unchanged active countdown, authoritative valid backup restore, rejected backup and closed callbacks. This is a 1.4.0 development fix; the immutable 1.3.0 download is unchanged.

P034 preserves timer/schedule feedback across locale changes. Actual WPF tests reproduce the original loss of validation messages, then check localized errors, successful Apply, equal-time warnings, active-session preservation and closed callbacks across KO/EN/zh-CN. Resource keys retain the latest result while the generic translator refreshes static labels. Authoritative validated restores clear stale feedback; rejected restores keep it. Native focus-card captures confirm readable localized feedback. This remains 1.4.0 development work.

P035 adds 25/5 and 50/10 quick-fill controls. Tests prove that they only prepare a draft across stopped/running/paused timers, do not schedule a save, preserve temporary extensions and require explicit Apply/restart for configured defaults. KO/EN/zh-CN feedback/tooltips and closed callbacks are covered. The full suite exposed an older gaze-fixture dependency on whether automatic walking had started: a deterministic pre-walk reproduces it, and resetting only the resting-gaze fixture fixes its precondition without changing production settling or weakening assertions. That test repair is part of validation, not another product patch. P035 remains 1.4.0 development work.

P036 adds an independent read-only saved-notification browser through both shared menus. Real receipt files remain unchanged while browsing, and original user titles/messages survive KO/EN/zh-CN relabeling. Stable receipt identity keeps selection through arrivals and language changes. A hundred-arrival fixture proves one queued refresh and virtualized offscreen rows; language changes share that pending refresh. External explicit acknowledgement updates the open list, while closing aborts queued work, detaches subscriptions and clears Main's reference. Native captures verify all three locales. This is 1.4.0 development work, absent from the immutable 1.3.0 download.

P037 adds literal case-insensitive title/body/source search combined with fixed source categories. Native tests cover metacharacters, multilingual text, normalized sources, raw whitespace drafts, selection preservation, incoming matching/nonmatching receipts, localized source labels/counts/accessibility names, Ctrl+F, Escape and closed callbacks. An actual deselection fixture reproduces the previous false no-match hint before verifying separate empty/no-match/select-a-row messages. Minimum-size captures prove finite title width, visible ellipsis and complete selectable details in KO/EN/zh-CN. Filters do not acknowledge receipts or change pet preferences. This remains 1.4.0 development work.

P038 adds explicit selected-notification acknowledgement through the existing asynchronous receipt worker. Native gated-worker tests prove responsiveness, stable target identity despite selection/locale changes, duplicate-click suppression, blank-ID independence and arrival preservation. A real locked receipt remains on disk with retry feedback, then deletes only after an explicit unlocked retry. Closing and reopening preserves operation ownership and prevents late updates to the old controls; disposed popup callbacks do nothing. Browsing/searching still keeps notifications unread. This is 1.4.0 development work.

P039 exports a frozen visible-notification snapshot as local JSON. Native tests change the filter inside the injected filename picker and verify the original selected view is exported. Writer checks cover exactly four raw metadata fields, Unicode/quotes/newlines, null timestamps, no existing-file overwrite, pre/mid-write cancellation and owned temporary cleanup on failed publication. UI checks cover retry with a new filename, three-locale error/busy states, minimum-size action layout, asynchronous saving and closing either during the picker or pending writer. Notifications remain unread. This remains 1.4.0 development work.

P040 adds a session-only 15-minute notification snooze through both menus. Independent injected clocks prove the exact UTC boundary, preserved focus state and receipts, retained arrivals, no backlog replay and repeat-snooze deadline reset. Manual/daily quiet settings and manual hiding remain intact. Native tests reproduce an open Resume menu incorrectly restarting snooze after expiry; capturing its displayed action fixes that behavior. Cached KO/EN/zh-CN menu labels, checkmarks/tooltips and disposed callbacks are covered. This completes P031–P040 for the 1.4.0 milestone.

P041 starts 1.5.0 development with accessible names for all eleven Settings inputs and format help for the four time fields. A native automation-peer fixture reproduces an empty language-selector name, then verifies 33 localized names and all four help texts across KO/EN/zh-CN. Unapplied raw drafts, current presentation and preferences stay intact; closed callbacks cannot change metadata. This verifies programmatic Windows accessibility metadata; Narrator interaction has not been manually tested. P041 is not included in the immutable 1.4.0 download.

P042 adds three notification-browser keyboard actions as one accessibility improvement: Ctrl+S exports the visible snapshot, Ctrl+Shift+R acknowledges only the selected receipt and Ctrl+L clears search/source filters. Native routed-command fixtures verify the registered gestures, real JSON output, gated receipt ownership and forced empty/busy/closed execution guards. Existing Find/Escape behavior and ordinary Delete editing remain intact, with localized shortcut tooltips and automation help across KO/EN/zh-CN. This is 1.5.0 development work, outside the 1.4.0 download.

P043 scopes Enter to the four Settings time editors. A native routed-key fixture first reproduces missing duration application, then verifies valid/invalid duration and schedule drafts across three locales. It checks that the window remains open on validation failures, future defaults can change while the active focus snapshot stays intact, and other/IME-processed or closed-editor key events do not apply values. Localized tooltips and the Enter accelerator retain the earlier automation names/format help. Physical keyboard/IME interaction is not claimed. This is 1.5.0 development work.

P044 groups four Settings section shortcuts into one navigation improvement. Native tests check actual keyboard focus and viewport coordinates after Ctrl+1–4 routes in three locales, including the lower notification section. Raw drafts, focus snapshot, current presentation and preferences remain unchanged. Strict command parameters and enabled/visible/focusable/closed guards reject unavailable targets; the visible hint and window automation help relocalize without overwriting input-format metadata. This is 1.5.0 development work.

P045 fixes Settings actions scrolling out of reach. The native 500px fixture reproduces an action at Y=1607 outside the 462px client, then checks fixed footer bounds and constant Y at top/middle/bottom scroll positions in three locales. A two-row root grid pins the original three actions and navigation hint while leaving all body cards/names/handlers intact. Closing preserves unapplied drafts; section-focus/viewport checks still pass using the named body scroll viewer. Two source reviews, native captures and NIM visual review found no new layout defect. This is 1.5.0 development work.

P046 repairs gallery automation metadata. Native peers first show a Korean search name in English UI and a card name different from its authored name. The fix explicitly localizes search/category/list metadata and binds card names directly to authored strings, with localized category help. Tests cover KO/EN/zh-CN, a Unicode unavailable custom-card fixture, filter/selection/current-pet preservation and closed callbacks. All nine-character UI checks pass. This verifies native automation metadata, without claiming manual Narrator interaction, and remains 1.5.0 development work.

| Verified patch threshold | Planned release | Main review area |
| --- | --- | --- |
| 10 | 1.1.0 | Motion and runtime defects |
| 20 | 1.2.0 | Pet interactions and comfort |
| 30 | 1.3.0 | Character authoring and previews |
| 40 | 1.4.0 | Focus and notification workflows |
| 50 | 1.5.0 | Accessibility and localization |
| 60 | 1.6.0 | Resource and rendering performance |
| 70 | 1.7.0 | Recovery and durable preferences |
| 80 | 1.8.0 | Distribution and maintenance |
| 90 | 1.9.0 | Compatibility and release hardening |
| 100 | 2.0.0 | Final acceptance and publication |

Areas guide discovery and can be reprioritized when debugging reveals a higher-impact problem. Milestone publication requires its preceding patch threshold, matching source/tag version and successful Windows verification, packaged-archive checks and installation checks. Existing releases and tags remain immutable. The new evolution publication path supplements the historical 1.0.0 release/recovery path.

The [1.1.0 recovery workflow](../.github/workflows/publish-1.1.0.yml) reuses the successful Windows distribution run `44655275518` at immutable commit `4a69780dc24f0601599182603b6b1005d45831ac`. It validates both SHA256 sidecars, the extracted payload and isolated installation before creating a draft, compares all four uploaded files with the source artifact, then verifies the public release anonymously. Rerunning validates an existing public release without replacing it. [Offline recovery scenarios](../tests/evolution-recovery-regression.ps1) cover new, draft and public releases, failed source CI, wrong tag commits and corrupted downloads. These release repairs do not increase the product patch count.

## Validation limits

Automated WPF rendering checks inspect actual pixels, contact transforms and window lifecycles. A renderer-loop timing is not a hardware-independent FPS measurement. Physical clean-PC, mixed-DPI and subjective motion checks remain explicitly distinguishable from automated results. The stable 2.0.0 tag is created after the ledger and final verification gates pass.

P047 adds gallery Find and filter-reset commands with a localized visible hint and window accessibility help. Native tests cover actual focus/selection, query/category reset, preserved current pet/preferences/card selection, all three locales, input during blocked discovery without a rescan, explicit refresh, and disabled/closed actions. This remains 1.5.0 development work.

P048 reproduces a 3px instantaneous foot lift at a zero-time restart from fully settled phase 0.8. A bounded 90ms entry weight now scales only vertical lift and secondary motion; foot X, distance phase and gait/stage elapsed stay unchanged. Native motion-preview tests cover all nine rigs, stop/resume during partial settling, exact completion, left/right planted world contacts, reduced motion and invalid weights. Five new core cases cover refresh rates, invalid elapsed, interrupted settling, legacy timing and reset. All 256 core checks and the nine-character UI suite pass. Test contact probes explicitly run WPF layout before measuring display coordinates. This remains 1.5.0 development work.

P049 Choose the same left or right look frame and display orientation during settling and after the raster handoff. The WPF test reproduces the wrong frame when a nearby cursor interrupts settling and verifies both authored directions through the handoff.

P050 Refresh the paused pose when reduced motion changes while preserving simulation time and the inspected stride. The WPF test reproduces the stale blend and checks that a toggle updates its weight without advancing the paused simulation. An FPS-only update preserves the inspected pose.

P051 removes the enumerator allocation on weighted frame selection. A Release probe calling the actual method one million times changed from 32,000,000 bytes to zero thread allocations while producing the same frame-selection checksum. All 256 core checks passed.

P052 bounds repeated bitmap writes to the rectangle that covers the changing pixels, including reflected and pivot artwork. The synthetic WPF case updates 9 of 240 pixels after initializing the scratch bitmap and matches every expected output byte. Native contact, reversal and rest rendering checks pass across the nine rigs.

P053 Keep raster lookup on the raster path and verify native foot positions using the actual live pose.

P054 Use the composition callback for live walking and settling, then return to the low-rate timer at rest.

P055 Relabel the existing result source and keep the selected card and preview request.

P056 Reuse the displayed items when revised search criteria yield the same card references in the same order.

P057 Propagate gallery cancellation through directory enumeration and manifest and thumbnail loading so superseded work can stop sooner.

P058 Compute cumulative boundaries when loading a manifest and locate frames with binary search, including exact and repeated boundaries.

P059 Use the original textures for endpoint poses and allocate the reusable blend bitmap only when needed.

P060 Show the current inbox snapshot directly when there is no search or source filter, avoiding a second list copy.

P061 Keep the previous valid local settings and recover it if the primary is missing or malformed. Repairing the primary preserves the good backup.

P062 Retry failed settings writes after 2, 4 and 8 seconds while the app is running. A persistent failure reports once and shutdown permits one final attempt without starting a timer.

P063 Limit local settings files to 256 KiB and fall back when the primary exceeds that size. Preserve BOM-aware decoding of previous settings.

P064 Skip an unchanged write so the recovery copy keeps the older distinct version.

P065 Flush the staged settings file before replacing the current file and clean only the staging file created by this save.

P066 When startup uses a valid recovery copy, schedule a write of the recovered settings after initialization succeeds. Preserve the recovery copy during repair.

P067 Keep the latest backup or restore result visible when the Settings language changes.

P068 Read the portable backup and load any replacement artwork on a worker. Apply only a live request and preserve settings if a closed window or newer character choice supersedes it.

P069 Capture portable preferences before starting a worker write. Show progress, keep existing files on a write failure and cancel pending writes when Settings closes.

P070 Expose a localized retry button after settings cannot be saved. A successful retry saves the pending preferences and hides the button.
