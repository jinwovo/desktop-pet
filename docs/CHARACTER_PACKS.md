# Character packs

DesktopPet 0.10.0 imports **character ZIPs and extracted directories**. Open Settings → **ZIP 추가** for an archive or **캐릭터 추가** for a folder containing `character.json`. The app validates and copies referenced PNGs to `%LOCALAPPDATA%\DesktopPet\Characters\`. Existing names are never overwritten; use a new folder name for an edited pack.

## ZIP layout

Put `character.json` and referenced PNGs at the ZIP root, in a named folder, or in several named folders for a collection. A wrapper folder is supported. A collection opens a selection dialog and installs one chosen character per operation. Folder-based packs use their folder leaf as the installed name; a root manifest uses its `name`. Forward-slash ZIP paths are preferred; older Windows PowerShell backslashes are normalized before traversal, duplicate and collision checks. UTF-8 BOM manifests from PowerShell are supported.

The archive stays read-locked through selection and installation. Every entry is checked for safe Windows paths, case-insensitive duplicates, file/folder conflicts and link/special-file attributes before extraction. Only the selected manifest and declared PNGs are extracted into a temporary folder, then passed through the normal folder importer and full image decoder before publication. Other collection packs and unrelated files are not installed. No script is executed.

Limits: 64 MiB archive size, 2,048 ZIP entries, 256 MiB declared expanded total, 64 candidate characters and 256 KiB per manifest. The selected pack also follows the folder limits below. Stream copies enforce actual output bounds. Cancelling collection selection changes nothing; closing Settings prevents late activation, while a validated installation already running may finish in the library. A corrupt or duplicate pack preserves installed characters.

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

## Validation and limits

All referenced files must remain inside the selected folder. Import rejects absolute paths, parent traversal, alternate streams, links, missing files, invalid PNGs and conflicting names. The prepared folder is limited to256 files,64MiB source data and64MiB declared decoded image data. Actual PNG dimensions must match `frameSizePx`. Unreferenced files count toward source bounds but are not copied into the installed pack.

Legacy packs do not need a rig and retain their frame animation. For live rigs the runtime decodes only the textures it needs, while import validates the full referenced set. See the source loader for the complete accepted value ranges.

## Rebuild the public collection

```powershell
powershell -NoProfile -File scripts/build-companion.ps1
powershell -NoProfile -File scripts/build-grounded-walk.ps1 -Character Companion
powershell -NoProfile -File scripts/build-public-characters.ps1
powershell -NoProfile -File tests/public-characters-regression.ps1
```

Original atlases and design records are in [Public asset provenance](PUBLIC_ASSET_PROVENANCE.md). Public contributions must include suitable provenance and distribution permission; see [CONTRIBUTING.md](../CONTRIBUTING.md#character-packs).
