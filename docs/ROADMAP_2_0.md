# DesktopPet 2.0 evolution

The project owner requested 100 meaningful improvements from the 1.0.2 baseline to a public 2.0.0 release. Progress lives in the [patch ledger](evolution-2.0.json). A planned item is a work slot, not an implemented feature or a confirmed defect. Later titles are refined after reproduction and review.

## Counting and evidence

A verified patch changes product behavior or repairs a reproducible defect, identifies its changed source files and names the regression fixtures that passed. Documentation, version changes, release administration and repeated tests do not count as additional product patches. A patch may resolve related symptoms together. Review findings are checked against source and execution before acceptance.

The first ten patches address pointer-facing jitter, preview state transitions and background work, idle-frame continuity, quiet-review integrity, bubble/gallery localization, decode cancellation, redundant image loading and opaque rest blending. User notification text and character-authored names remain intact. Development API credentials are excluded from runtime output.

## Milestones

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

## Validation limits

Automated WPF rendering checks inspect actual pixels, contact transforms and window lifecycles. A renderer-loop timing is not a hardware-independent FPS measurement. Physical clean-PC, mixed-DPI and subjective motion checks remain explicitly distinguishable from automated results. The stable 2.0.0 tag is created after the ledger and final verification gates pass.
