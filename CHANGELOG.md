# Changelog

## 0.6.0 — character gallery and expanded collection

- Add Pompompurin, Cinnamoroll and Kuromi as optional AI-assisted fan-art packs, selected using the official 2026 Sanrio ranking. Existing six companions are preserved, giving personal builds nine choices.
- Add a searchable character gallery with Korean/English aliases and original/fan-art/custom filters. Card selection and isolated motion previews preserve the active pet until explicit application.
- Decode only bounded frozen idle thumbnails for gallery cards, validate PNG headers before decoding, and handle corrupt or replaced resources without replacing the active pet.
- Share a validated six-pack catalog across packaging and personal builds. Keep the default public app's original collection and optional packs separate.
- Include reproducible source masters, generation prompts, nine-character validation and EN/KO/zh-CN gallery documentation. Preserve the console-free UTF-8 hook fix from 0.5.0.

## 0.5.0 — release preparation

- Add Nimbus and Ember, two AI-assisted original public character designs, alongside Companion.
- Add separately marked optional Chiikawa, Baikinman and Hello Kitty fan-art packs, importable through Settings and available to personal builds; third-party character rights are outside the project MIT grant.
- Add a local 25-minute focus / 5-minute break timer with start, pause, resume, restart and stop controls in Settings and the tray.
- Route timer completions and the new test-notification button through the normal durable inbox, retaining hidden and Do not disturb behavior.
- Keep walking continuous with reusable body/foot textures, distance-driven foot placement and fractional native-window position correction.
- Add English, Korean and Simplified Chinese READMEs, original-character previews and character authoring documentation.
- Add community templates, an MIT license, an allowlisted public-source exporter with SHA256 inventory, and a draft-release workflow triggered manually or by version tags.
- Expand public release validation to Companion, Nimbus and Ember; preserve compatibility with older Companion-only release manifests.

The actual runtime-included Windows ZIP was built and its extracted manifest, SHA256, public assets and isolated-runtime `--version` execution were verified on 2026-11-15. The subsequent console-free UTF-8 hook fix passed the [0.5.0 Windows Actions run](https://github.com/jinwovo/desktop-pet/actions/runs/43623762967). A clean-PC installation remains unverified. No published GitHub release is claimed by this changelog.
