# Fan-art notice and creation record

This directory contains optional AI-assisted fan-art interpretations of **Chiikawa**, **Baikinman** and **Hello Kitty**, requested by the project owner. Character identities and related rights belong to their respective rights holders. The project does not claim ownership of those identities, an affiliation with those rights holders, or permission from them.

The repository's MIT license covers the project's own code and original project material. It does **not** grant a license to these third-party characters or fan-art images. This notice is not a separate redistribution permission for the artwork. Inclusion in this repository or use without commercial intent does not establish permission.

## Creation record

- Illustration tool: the built-in `image_gen` tool, used for character pose sheets and separated body/foot rig components.
- Existing project source records identify `personal-chiikawa-master-v1.png`, `personal-baikinman-master-v1.png`, `personal-hellokitty-master-v1.png`, and corresponding `rig-*-v7.png` component atlases. Those source atlases are not required to import these rendered packs.
- Deterministic rendering: `CharacterBuilder.cs`, `GroundedWalkBuilder.cs`, `build-personal-characters.ps1` and `build-grounded-walk.ps1` preserve transparent backgrounds and derive expressions, directional states, 32-phase fallback walking, settling frames and six live-rig textures.
- Prepared pack metadata retains `assetKind: "optional-personal-fan-art"` to distinguish these packs from project-original assets.
- This package contains rendered PNGs and metadata copied from the existing validated project assets. It does not include executable code or any user's settings, credentials or conversations.

Keep this notice with the collection. Report artwork or rights concerns through the repository's issue/contact channels without including private personal information.
