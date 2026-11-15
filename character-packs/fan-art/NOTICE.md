# Fan-art notice and creation record

This directory contains optional AI-assisted fan-art interpretations of **Chiikawa**, **Baikinman**, **Hello Kitty**, **Pompompurin**, **Cinnamoroll** and **Kuromi**, requested by the project owner. Character identities and related rights belong to their respective rights holders. The project does not claim ownership of those identities, affiliation with those rights holders, or permission from them.

The repository's MIT license covers the project's own code and original project material. It does **not** grant a license to these third-party characters or fan-art images. This notice is not separate redistribution permission for the artwork. Inclusion in this repository or use without commercial intent does not establish permission.

## Creation record

- Illustration tool: the built-in `image_gen` tool, used for character pose sheets and separated body/foot rig components.
- Existing project source records identify `personal-chiikawa-master-v1.png`, `personal-baikinman-master-v1.png`, `personal-hellokitty-master-v1.png` and corresponding `rig-*-v7.png` component atlases. Those source atlases are not needed to import the rendered packs.
- New source masters and the official popularity source for Pompompurin, Cinnamoroll and Kuromi are recorded in [POPULAR_ASSETS.md](POPULAR_ASSETS.md). Popularity rankings describe the selection rationale; they do not establish rights or permissions.
- Deterministic rendering uses `CharacterBuilder.cs`, `GroundedWalkBuilder.cs`, `build-personal-characters.ps1`, `build-grounded-walk.ps1` and `build-popular-characters.ps1` to derive expressions, directional states, fallback walking, settling frames and six live-rig textures.
- Every prepared pack retains `assetKind: "optional-personal-fan-art"`. The versioned [catalog.json](catalog.json) records curated identities, import-folder names, search aliases and source links. A null rank means no numerical ranking is claimed.
- The optional import ZIP contains validated rendered PNGs, metadata, catalog and notices. Source atlases belong to the source repository, not the import archive. No user's settings, credentials or conversations are included.

Keep this notice with the collection. Report artwork or rights concerns through the repository's issue/contact channels without including private personal information.
