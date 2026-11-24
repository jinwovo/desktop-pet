# DesktopPet 2.0 evolution

The project owner requested 100 meaningful improvements from the 1.0.2 baseline to a public 2.0.0 release. Progress lives in the [patch ledger](evolution-2.0.json). A planned item is a work slot, not an implemented feature or a confirmed defect. Later titles are refined after reproduction and review.

## Counting and evidence

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
