# Character packs

DesktopPet 0.10.0 imports **character ZIPs and extracted directories**. Open Settings → **ZIP 추가** for an archive or **캐릭터 추가** for a folder containing `character.json`. The app validates and copies referenced PNGs to `%LOCALAPPDATA%\DesktopPet\Characters\`. Existing names are never overwritten; use a new folder name for an edited pack.

## ZIP layout

Put `character.json` and referenced PNGs at the ZIP root, in a named folder, or in several named folders for a collection. A wrapper folder is supported. A collection opens a selection dialog and installs one chosen character per operation. Folder-based packs use their folder leaf as the installed name; a root manifest uses its `name`. Forward-slash ZIP paths are preferred; older Windows PowerShell backslashes are normalized before traversal, duplicate and collision checks. UTF-8 BOM manifests from PowerShell are supported.

The archive stays read-locked through selection and installation. Every entry is checked for safe Windows paths, case-insensitive duplicates, file/folder conflicts and link/special-file attributes before extraction. Only the selected manifest and declared PNGs are extracted into a temporary folder, then passed through the normal folder importer and full image decoder before publication. Other collection packs and unrelated files are not installed. No script is executed.

Limits: 64 MiB archive size, 2,048 ZIP entries, 256 MiB declared expanded total, 64 candidate characters and 256 KiB per manifest. The selected pack also follows the folder limits below. Stream copies enforce actual output bounds. From 0.90.0, Settings displays extraction/copy/PNG validation progress and **Cancel installation**. Closing Settings also requests cancellation. Cancellation before publication removes staging files; if atomic installation already finished, the installed character stays in the library and the current pet is retained. Cancelling collection selection changes nothing. A corrupt or duplicate pack preserves installed characters.

## Minimal sprite pack

Create a uniquely named folder, for example `MyLittleCloud`, with one transparent 128×128 `idle.png` and this `character.json`:

```json
{
  "name": "MyLittleCloud",
  "frameSizePx": { "width": 128, "height": 128 },
  "anchor": { "x": 64, "y": 122 },
  "scale": 1,
  "walkSpeedPxPerSec": 40,
  "frameIntervalMs": 120,
  "frames": { "idle": ["idle.png"] }
}
```

Omitted states fall back to idle. This minimal example is an importable static character; walking animation requires additional artwork. Keep feet on `anchor.y`, leave transparent padding, and preserve the artwork's aspect ratio.

Supported states are `idle`, `walk`, `sit`, `lookLeft`, `lookRight`, `drag`, `fall`, `cry`, `angry` and `sleep`. Directional gait arrays use `walkTurn`, `walkFront`, `walkSettleSide` and `walkSettleTurn`. Set `frontFacingRest: true` only when resting artwork is drawn facing forward. Optional `frameDurationsMs` arrays assign per-frame milliseconds to a resolved state sequence, for example an occasional short blink inside a long open-eye idle.

## Optional character personality (0.9.0)

Add a `behavior` object to the manifest to tune autonomous activity without changing walking speed or foot placement:

```json
"behavior": {
  "walkWeight": 0.55,
  "idleWeight": 1.3,
  "sitWeight": 1.7,
  "restDurationMultiplier": 1.4,
  "walkDurationMultiplier": 0.75,
  "sleepAfterIdleMs": 60000,
  "description": "A relaxed companion that enjoys longer rests."
}
```

Weights multiply the existing calm/lively probabilities; each must be finite, between 0 and 10, and at least one must be positive. Duration multipliers range from 0.25 to 4. The sleep threshold ranges from 15,000 to 1,800,000 milliseconds and still respects the user's sleep checkbox. Description is optional, limited to 80 characters without control characters, and appears in Settings. Missing, null or empty behavior retains the legacy defaults. Switching personality preserves the current motion and applies the new timing to subsequent action windows.

The nine bundled/personal presets are recorded in [Assets/CharacterBehaviors.json](../Assets/CharacterBehaviors.json). Build scripts copy the matching preset into each manifest; the app reads the manifest and never needs the preset bank at runtime. Grounded-walk regeneration preserves an authored profile. Nimbus favors longer rests; Ember favors longer walks; Companion balances the two.

## Optional continuous walking rig

From 1.0.1 the renderer clips each existing body texture into overlapping head/torso regions and moves those regions with small rigid rotations. Authors still provide the same six PNGs; no new manifest fields or extra raster frames are required. Both feet retain distance-based contact and rotate only while airborne. Side/quarter-view changes use premultiplied pixel interpolation, with three reusable scratch bitmaps allocated per active rig. At 128×128, source copies, changed-pixel indexes and scratch pixel storage add roughly 1.5 MiB before WPF object overhead in 1.0.2. Buffers update only when the quantized view blend changes; steady endpoint views use the original frozen textures. Reduced motion disables this blending and secondary articulation. Legacy packs without a rig keep their existing frame animation.

The bundled public characters are complete examples. In addition to their fallback gait arrays they declare:

```json
{
  "walkCycleDistancePx": 30,
  "walkRig": {
    "version": 1,
    "footLiftPx": 4,
    "side": {
      "body": "rig_side_body.png",
      "nearFoot": "rig_side_near.png",
      "farFoot": "rig_side_far.png"
    },
    "turn": {
      "body": "rig_turn_body.png",
      "nearFoot": "rig_turn_near.png",
      "farFoot": "rig_turn_far.png"
    }
  }
}
```

Merge these fields into a complete manifest; this fragment is not a standalone pack. Every part is a full-size transparent canvas aligned to the same neutral body/foot coordinates. The runtime translates the near foot, far foot and body independently. Supply side/quarter fallback cycles of matching length; distance is measured in unscaled sprite pixels. Resting art stays separate. Do not bake movement into a full-body layer and expect the rig to remove it.

## Inspect a pose frame by frame

Open **Animation preview** from a gallery card, or the motion preview from Settings. **Pause** freezes the simulated pet, gait and PNG pose-dissolve clocks. **Next frame** advances exactly one interval of the configured 30/60/120 FPS cap; **Resume** continues from that point without replaying time spent paused.

Reset preserves the pause while rebuilding the scene. You can prepare a jump/drop or move the pet, then step through its trajectory. A paused window stays paused after hiding/minimizing, and changing the UI language keeps the inspected pose. Reduced motion still applies. The preview uses its own scene and does not pause or modify the desktop pet.

**Cry** and **Angry** start the real reaction timer so you can inspect authored expression frames, tear placement and their transitions. While dragging or falling, the preview keeps the trajectory and queues the newest reaction until landing. The localized header identifies the queued reaction and clears it when consumed; Reset clears pending reactions. Packs without dedicated expression frames retain their normal fallback artwork.

**Save scene PNG** captures that canvas at the moment you click, before the filename dialog opens. It includes the controlled pet, current live-rig pose or raster dissolve, and platforms. It does not capture other windows, the desktop or dialog contents. Transparent canvas areas remain transparent in the PNG. Pause and step to a problem frame before saving it for a bug report. Choose a new `.png` filename: existing files are never replaced. Encoding runs in the background, so playback controls remain available. Closing the preview cancels unfinished saving; an already committed PNG remains saved. The button and its tooltip report saving, success or a retry in the selected UI language.

**Frame guides** adds a dashed frame boundary, the declared `anchor.y` baseline and a cross at `(anchor.x, anchor.y)` in unmirrored frame coordinates. These are metadata guides, not detected joints or automatic corrections. They preserve non-square frame proportions, follow the pet through walking/dragging/falling and remain available during pause/step. Reset keeps the toggle; a new preview starts with it off. Guides do not alter artwork, motion, preferences or the desktop pet. Enabled guides appear in saved scene PNGs so authors can show an alignment problem.

## Export a character ZIP

Open the gallery, select a character and choose **Export ZIP**. Save to a new `.zip` filename outside the selected character's folder. Existing files and directories are never replaced.

The exporter copies the selected manifest and declared PNGs into private staging, validates the complete image set, and creates a ZIP with one character folder. Extra files such as local `.env`, preference files and unrelated images are excluded. The manifest, nested frame paths and behavior profile are preserved. The original character folder and the active pet/preferences remain unchanged.

Progress identifies the character being exported, even when you browse another card. **Cancel export** or closing the gallery cancels unfinished work. Validation, write failures and cancellation clean up the partial output; if the final ZIP was already committed, it remains saved. A new file created by another program during export is preserved. Recipients install the ZIP through **Add ZIP** as usual.

The same 256-file, 64 MiB source/decoded-image limits apply; the final ZIP must also fit the 64 MiB archive limit. Export includes every declared image, including raster fallback frames, and rejects a damaged image even when its gallery thumbnail is valid.

## Validation and limits

All referenced files must remain inside the selected folder. Import rejects absolute paths, parent traversal, alternate streams, links, missing files, invalid PNGs and conflicting names. The prepared folder is limited to256 files,64MiB source data and64MiB declared decoded image data. Manifests are limited to256KiB for folder, archive and startup loading. Actual PNG dimensions must match `frameSizePx`. Unreferenced files count toward source bounds but are not copied into the installed pack.

Legacy packs do not need a rig and retain their frame animation. For live rigs the runtime decodes only the textures it needs, while import validates the full referenced set. See the source loader for the complete accepted value ranges.

## Rebuild the public collection

```powershell
powershell -NoProfile -File scripts/build-companion.ps1
powershell -NoProfile -File scripts/build-grounded-walk.ps1 -Character Companion
powershell -NoProfile -File scripts/build-public-characters.ps1
powershell -NoProfile -File tests/public-characters-regression.ps1
```

Original atlases and design records are in [Public asset provenance](PUBLIC_ASSET_PROVENANCE.md). Public contributions must include suitable provenance and distribution permission; see [CONTRIBUTING.md](../CONTRIBUTING.md#character-packs).

From 1.0.2, upper-body facing transitions additionally reuse four derived body images (two reflected views and a frontal pivot in both orientations) over 120 ms. Feet reflect immediately with the reversed distance phase to preserve contact. Side/quarter blends finish in 100 ms; neither transition leaves an exponential tail. The curated rigs lift feet by 3 pixels, while their unchanged raster fallback/audit bank retains its authored 4-pixel lift. Custom pack speeds and lift values remain author-controlled.

The frontal pivot comes from the first cached idle frame, cropped below the lowest occupied row of the rig body layers to exclude feet. It is used only during a facing reversal; ordinary walking never glances front. A pivot does not change the distance-based feet or the rest blend weight. Mid-turn pixel tests verify a single face and no pixels below the rig body cutoff.
