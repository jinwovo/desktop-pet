# Public asset provenance

The public collection contains three project character designs: **Companion**, **Nimbus**, and **Ember**. Their source atlases are AI-assisted illustrations produced with the built-in `image_gen` tool. They are not presented as manually drawn artwork or an affiliation with another character brand. These public designs were commissioned for this project without a third-party character reference.

This document records the design briefs and production steps. The brief summaries below are not claimed to be verbatim copies of every generation prompt. The [MIT license](../LICENSE) covers the project material distributed here; keep provenance with redistributed source artwork. Separately selected [fan-art packs](../character-packs/fan-art/NOTICE.md) are outside this original collection and are not a grant of third-party character rights. The original Git history also contains older third-party character artwork; the new MIT notice does not grant rights to those earlier images.

| Character | Design brief | Source artwork |
| --- | --- | --- |
| Companion | A small warm cream seed-shaped companion, leaf sprout, round silhouette, gentle face and dark soft outlines. | [Rest/blink/sleep atlas](../images/companion-master-v1.png), [direction study](../images/companion-walk-master-v2.png), [separated walking parts](../images/rig-companion-v7.png) |
| Nimbus | An original round cloud-blue cat, cloud-shaped forehead tuft and chest patch, soft pink cheeks, small paws and curled tail. | [Transparent master atlas](../images/public-nimbus-master-v1.png) |
| Ember | An original small apricot-orange fox, cream chest and tail tip, rounded warm silhouette and expressive ears. | [Transparent master atlas](../images/public-ember-master-v1.png) |

Nimbus and Ember were prepared on 2026-11-14. Their three-row atlases provide front/open-eye, blink and sleep poses, then side and quarter body/near-foot/far-foot components. The master files are preserved. The builder detects transparent row gutters, crops complete isolated components, preserves aspect ratio, and exports 128×128 transparent PNGs.

`scripts/build-public-characters.ps1` generates the complete Nimbus/Ember packs. `scripts/build-companion.ps1` followed by `scripts/build-grounded-walk.ps1 -Character Companion` rebuilds Companion. Tears, gentle anger tint and legacy state variants are deterministic code overlays; six neutral rig canvases and raster fallback cycles come from `GroundedWalkBuilder.cs`. No online image download occurs when rebuilding.

The runtime uses the cached six-part rig for continuous body/foot translation. The 32-phase fallback and two settling banks remain included for compatibility and audits. The app icon is rebuilt from Companion's idle frame by `scripts/build-public-icon.ps1`. `tests/public-characters-regression.ps1` checks declared resources, transparency, canvas bounds, required states and rig completeness. WPF regressions check actual retained drawings, contact transforms and imported packs.

[Character gallery](preview/public-characters.png) · [Recorded walking preview](preview/public-walk.gif) · [Character pack format](CHARACTER_PACKS.md)
