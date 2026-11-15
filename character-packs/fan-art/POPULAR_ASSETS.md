# Popular personal fan-art assets

These three optional character packs are newly generated fan art for personal desktop use. Pompompurin, Cinnamoroll and Kuromi are Sanrio characters; character rights remain with their respective owners. These images are not official Sanrio artwork and this record does not grant a redistribution or commercial license.

The selection follows the [official 2026 Sanrio Character Ranking results](https://corporate.sanrio.co.jp/en/news/20260628_03.html): Pompompurin ranked 1st, Cinnamoroll 2nd and Kuromi 4th. The official announcement is a popularity reference only; no official artwork was copied into these packs.

## Generation and rebuild

- Created: 2026-11-16 (Asia/Seoul).
- Tool: built-in `image_gen.imagegen`, with `transparent_background: true`; no CLI/API fallback.
- Local account paths redacted; workspace master paths, prompts and hashes unchanged. Tool-cache result filenames are retained under the `<local image-generation cache>` placeholder.
- Use case: `stylized-concept`.
- Master layout: square 3×3 atlas. Top row: complete front open-eye, blink, sleep. Middle row: right profile footless body, detached near foot, detached far foot. Bottom row: right quarter footless body, detached near foot, detached far foot.
- Masters preserve generated transparent alpha; there is no color-key removal, erased background, or replacement vector drawing.
- Rebuild: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-popular-characters.ps1` from the repository root. Optional `-Character Pompompurin|Cinnamoroll|Kuromi` selects one pack.
- Builder: `scripts/build-popular-characters.ps1`, using the existing `CharacterBuilder.cs` and `GroundedWalkBuilder.cs` helpers. Existing masters, bundled characters and public builder are unchanged.
- Output: 211 transparent 128×128 PNGs plus `character.json` per character. The independent 90 px body, 12/11 px near/far foot parts are rendered at hip y=112 and ground y=122, with 32-frame side/quarter cycles and two settling levels. This is a separate body/feet rig, with no whole-body walk rotation or stretching.
- Raster gait frames use the popular builder's embedded `PopularFrameRenderer`: render the unchanged gait trajectories at 512×512, then downsample to 128×128. This avoids direct 128 px GDI+ subpixel sampling changes in asymmetric paw silhouettes. The transparent masters, exported live rig parts, shared gait helper, stride lengths and runtime math are unchanged.
- Preview: `character-packs/fan-art/popular-characters-preview.png`.

Validation: all three masters are 1254×1254 RGBA with transparent corners. Each generated pack has exactly 211 PNGs; every referenced frame exists, every PNG is 128×128 with a transparent corner, and every visible alpha bound stays inside the canvas. At alpha 25, all six body textures span y=22 through 111, near paws y=110 through 121, and far paws y=111 through 121; the visible paws overlap the body edge by 1–2 px at rest. Cinnamoroll's widest quarter body spans x=14 through 113, preserving both long ears. The generated front open, blink and sleep poses were visually inspected at application size.

The raster contact audit uses the existing UI regression's actual PNG alpha centroid on rows 120–121 (alpha at least 25), sampled during planted stance while the opposite foot lift is at least 3.5 px. Direct 128 px rendering initially produced up to 1.506132 px drift; isolated single-paw rendering reproduced it, ruling out swing-paw heel contamination. After supersampling, worst near/far planted world-space centroid drift is:

| Character | Side view (px) | Quarter view (px) |
| --- | ---: | ---: |
| Pompompurin | 0.174738 | 0.122817 |
| Cinnamoroll | 0.029856 | 0.027798 |
| Kuromi | 0.170295 | 0.154339 |

All 32 phases in both views also pass floor penetration checks (no alpha at least 100 below y=122). Both settling levels pass the same floor check, and every fully settled frame contacts row 121. The regression's existing 1.25 px tolerance and gait math were retained.

Master SHA-256 checksums:

| Master | SHA-256 |
| --- | --- |
| `popular-pompompurin-v1.png` | `B0071F9C8DAAC7C485851D1A9742B4B9D5F7624FFB8703767E904EEF96ACBACA` |
| `popular-cinnamoroll-v1.png` | `93D4A97CE79C4D6B27E4C78EDAC4C76E7F21C6BF17893F1884F0FDE8234E3367` |
| `popular-kuromi-v1.png` | `CE2DD58E8EAD69C541910739DA8EE66CC4ACD2F5996C5FABA21AEE1660B5E260` |

## pompompurin

Master: `character-packs/fan-art/masters/popular-pompompurin-v1.png`.

Initial generation result record:

```text
Generated image result: <local image-generation cache>/exec-de1d2498-24e7-41cd-9cb0-2da96b75ba4a.png.
If you need to use a generated image at another path, copy it and leave the original in place unless the user explicitly asks you to delete it.
The generated image is already displayed to the user. There is no need to render it in the final response as a Markdown image or file link.
```

Exact generation prompt:

```text
Use case: stylized-concept
Asset type: transparent production sprite-part atlas for a personal desktop companion, Pompompurin fan art.
Primary request: draw the recognizable Sanrio Pompompurin, a pale custard-yellow plump golden retriever wearing his small dark chocolate brown beret, tiny brown oval eyes, tiny oval nose and smiling W mouth, long floppy ears, stubby arms, no clothes, charming simple Japanese mascot illustration. Maintain the same recognizable identity and proportions in every cell.
Style/medium: exceptionally clean flat pastel digital mascot art with a smooth dark brown outline, subtle warm shading, no photorealism, no plush texture.
Composition/framing: exactly a square 3-column by 3-row atlas, with nine isolated cutouts centered inside nine equal square cells and large transparent gutters between all cells. No visible grid. Row 1: three complete front-facing full-body standing characters at matching scale and pose: column 1 relaxed OPEN eyes; column 2 identical with both eyes CLOSED for a blink; column 3 identical with both eyes CLOSED and peaceful sleeping mouth. Row 2: right-facing strict profile parts, column 1 is ONE footless body (head, beret, ears, torso, tiny arms, but absolutely NO feet or legs); column 2 one detached near foot; column 3 one detached far foot. Row 3: right-facing three-quarter parts, column 1 ONE footless body (matching head, beret, ears, torso and arms, NO feet); column 2 one detached near foot; column 3 one detached far foot. Lower-row detached feet must be horizontally elongated cute yellow rounded paw shapes, with outline, not round floating circles. Detached parts each centered in their own cell and not connected. Bodies upright, not leaning, open eyes in all body-part cells. The profile and three-quarter body height should be roughly 1.0 times width, round plump silhouette; feet each one fifth the body height. Front full-body has both short feet grounded.
Scene/backdrop: actual empty transparent alpha background everywhere outside artwork.
Constraints: keep all nine cells complete and far from their boundaries; all ears and berets uncut. No shadows on ground, checkerboard, text, labels, watermark, border, panel lines, decorative stars or props. Important exact layout: complete fronts in top row, isolated body plus two isolated feet in second row, isolated body plus two isolated feet in third row.
```

## cinnamoroll

Master: `character-packs/fan-art/masters/popular-cinnamoroll-v1.png`.

Initial generation result record:

```text
Generated image result: <local image-generation cache>/exec-e0b745dc-2ebe-44cb-9fea-936f136a4e9b.png.
If you need to use a generated image at another path, copy it and leave the original in place unless the user explicitly asks you to delete it.
The generated image is already displayed to the user. There is no need to render it in the final response as a Markdown image or file link.
```

Exact generation prompt:

```text
Use case: stylized-concept
Asset type: transparent production sprite-part atlas for a personal desktop companion, Cinnamoroll fan art.
Primary request: draw recognizable Sanrio Cinnamoroll, the small pure-white chubby puppy with very long floppy white ears hanging softly DOWN beside the body, blue oval eyes, cute tiny blue W mouth, soft pink cheek ovals, and a cinnamon-roll curled tail visible in profile and three-quarter views. Preserve recognizable identity and consistent head/body proportions in every cell. His ears should hang downward naturally to around his belly rather than sticking horizontally out; the full silhouette must fit inside a compact width no wider than 1.2 times its height.
Style/medium: exceptionally clean flat pastel digital Japanese mascot art, smooth blue-gray outline, subtle pale-blue shading for white volume, no photorealism, no plush texture.
Composition/framing: exactly a square 3-column by 3-row atlas, nine isolated cutouts centered inside nine equal square cells, large transparent gutters between all cells. No visible grid. Row 1: three complete front-facing full-body standing characters, identical scale and pose: column 1 relaxed OPEN blue eyes; column 2 same with both eyes CLOSED for blink; column 3 same with both eyes CLOSED, peaceful sleeping expression. Row 2: right-facing strict profile parts, column 1 ONE upright footless body (head, long downward ears, torso, arms, curled tail, absolutely NO feet or legs); column 2 one detached near foot; column 3 one detached far foot. Row 3: right-facing three-quarter parts, column 1 ONE upright footless body (head, ears, torso, arms, curled tail, NO feet); column 2 one detached near foot; column 3 one detached far foot. Detached feet must be horizontally elongated short white rounded paws with blue-gray outline, not circular floating blobs, each about one fifth body height. Every detached part centered alone in its own cell and not connected. Profile and three-quarter bodies have eyes OPEN. The body including ears should be compact, about 1.0–1.15 times wider than tall. Front full bodies have two short grounded feet.
Scene/backdrop: actual empty transparent alpha background outside artwork.
Constraints: preserve long soft ears, never cropped or touching any cell boundaries; all nine assets complete with generous padding. No shadows on ground, checkerboard, text, labels, watermark, border, panel lines, decorative stars or props. Exact layout: three complete front poses top row; one isolated body plus two isolated feet second row; one isolated body plus two isolated feet third row.
```

## kuromi

Master: `character-packs/fan-art/masters/popular-kuromi-v1.png`.

Initial generation result record:

```text
Generated image result: <local image-generation cache>/exec-52b2fadf-e8af-4d8d-a972-79f4a1d937a0.png.
If you need to use a generated image at another path, copy it and leave the original in place unless the user explicitly asks you to delete it.
The generated image is already displayed to the user. There is no need to render it in the final response as a Markdown image or file link.
```

Exact generation prompt:

```text
Use case: stylized-concept
Asset type: transparent production sprite-part atlas for a personal desktop companion, Kuromi fan art.
Primary request: draw the recognizable Sanrio Kuromi, small white chubby rabbit-like mascot wearing her black jester hood with two tall pointed ears ending in tiny black balls, centered small pink skull emblem on forehead, white face with black OPEN oval eyes and tiny lashes and short playful smile, soft pink cheeks, black pointed neck collar, little white torso and arms, stubby white feet, thin black devil tail visible in profile and quarter views. Keep her iconic hood, pink skull and two ears consistent in every view.
Style/medium: exceptionally clean flat pastel Japanese mascot digital art, smooth dark gray outline, solid charcoal-black hood, subtle soft shading, no photorealism, no plush texture.
Composition/framing: exactly square 3-column by 3-row atlas, nine isolated cutouts centered inside nine equal square cells with large transparent gutters between all cells. No visible grid. Row 1: three complete front-facing full-body standing characters at identical scale and pose: column 1 relaxed OPEN eyes; column 2 both eyes CLOSED for blink; column 3 both eyes CLOSED, peaceful sleeping expression. Row 2: right-facing strict profile parts, column 1 ONE upright footless body (head with jester hood/ears/skull, neck collar, white torso, arms, tail, absolutely NO feet or legs); column 2 one detached near foot; column 3 one detached far foot. Row 3: right-facing three-quarter parts, column 1 ONE upright footless body (same head, hood ears, skull visible on front-facing forehead plane, collar, white torso, arms, tail, NO feet); column 2 one detached near foot; column 3 one detached far foot. Detached feet are horizontally elongated stubby white rounded paws with dark outlines, each one fifth body height, not circular balls. All body-part cells have OPEN eyes. Bodies upright and centered, not leaning. Proportions: large round hood and face above small rounded torso; ears extend up without being excessively tall. All front full-bodies show both short grounded feet.
Scene/backdrop: actual empty transparent alpha background outside the cutouts.
Constraints: keep all nine assets complete and far inside cell boundaries; never clip ear tips, tail, feet or hood; no external props, no ground shadows, no checkerboard, text, labels, watermark, border, grid lines or decorations. Exact layout: complete fronts top row, isolated footless body plus two separate feet second row, isolated footless body plus two separate feet third row.
```

The initial Kuromi result included unwanted detached heads above the four paw cutouts. It was not used as the master. A targeted built-in image edit removed those heads while retaining the complete front poses, body parts and paws.

Edit input: `<local image-generation cache>/exec-52b2fadf-e8af-4d8d-a972-79f4a1d937a0.png`.

Exact edit prompt:

```text
Edit only the four detached-foot cells of this transparent Kuromi production sprite atlas: remove the extra detached Kuromi HEADS currently above each small white foot in row 2 columns 2 and 3 and row 3 columns 2 and 3. Those four cells must contain ONLY their single small detached white paw and empty transparency, absolutely no heads, no skulls, no other artwork. Preserve all other artwork pixel-identically: all three top-row full bodies, row 2 column 1 profile footless body, row 3 column 1 three-quarter footless body, and all four existing small feet. Keep canvas dimensions and real transparent alpha. Do not redraw characters, shift parts, add background or labels.
```

Final edit result record:

```text
Generated image result: <local image-generation cache>/exec-42811200-0182-4aa3-84ce-789c5b0f7035.png.
If you need to use a generated image at another path, copy it and leave the original in place unless the user explicitly asks you to delete it.
The generated image is already displayed to the user. There is no need to render it in the final response as a Markdown image or file link.
```
