# Character packs

DesktopPet imports an **extracted directory**, not an image or ZIP. Open Settings → **캐릭터 추가**, then select the folder containing `character.json`. The app validates and copies referenced PNGs to `%LOCALAPPDATA%\DesktopPet\Characters\`. Existing names are never overwritten; use a new folder name for an edited pack.

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
