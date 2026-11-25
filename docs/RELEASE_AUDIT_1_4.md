# DesktopPet 1.4.0 release audit

[1.4.0 is public](https://github.com/jinwovo/desktop-pet/releases/tag/v1.4.0), with verified product patches P001–P040. The 1.5.0 development work on the main branch is outside this immutable release.

- Source commit: `7cc4879a4485f27ff2b56310465109f2f60a6632`.
- Annotated tag object: `aa7224f051b255192b6d98654fea591a6a35297e`.
- GitHub release ID: `457501576`.
- [Exact source CI](https://github.com/jinwovo/desktop-pet/actions/runs/45518119052) and the [original tag verification/publication run](https://github.com/jinwovo/desktop-pet/actions/runs/45518303046) both completed successfully.

The tag workflow reran core and native UI checks, public-only and nine-character gallery checks, hook/installer/package/source/documentation checks, ZIP import and extracted self-contained startup. Publication waited for those checks and validated the four release files. An independent verification then downloaded all four files through their public URLs, matched their sizes and GitHub digests, and validated both checksum sidecars.

| Public file | Bytes | SHA256 |
| --- | ---: | --- |
| DesktopPet-1.4.0-win-x64.zip | 78,494,675 | `ddaee077f6e91903065d6328e26d3af686fdff8bb7435c2d0c0e977e4c0f0d35` |
| DesktopPet-1.4.0-win-x64.zip.sha256 | 96 | `f8a5e68b07fc19df835f7389a0ff38a05c04c8d676be954907d256f061d16b40` |
| DesktopPet-1.4.0-fan-art-packs.zip | 15,267,003 | `16ddfabf41f67a9c4b7038cd83c6b94391598e123fab5a9e3cb1f1d8899f1400` |
| DesktopPet-1.4.0-fan-art-packs.zip.sha256 | 102 | `2b6d377bca3736062a1b1f7a212e296636524a35e034e28dcfaf5b1ff516ae5a` |

The downloaded main ZIP contains 660 files and exactly Companion, Ember and Nimbus. The optional ZIP contains 1,277 files and the six catalogued fan-art packs. ZIP integrity and paths were checked. Every decompressed entry, including the executable, was scanned for the actual development API key and excluded private `.env`, `.codex` and `.local` data.

The public executable reports product version `1.4.0+7cc4879a4485f27ff2b56310465109f2f60a6632`. With runtime roots redirected to an empty fixture, its `--version` command completed as `1.4.0.0`. The separate local personal build was also verified with nine characters, 54 live-rig textures and 1,931 files matching source assets.

This audit verifies packaged resources and isolated command startup. It does not claim a clean-PC GUI test, manual screen-reader interaction, or physical mixed-DPI monitor testing. Independent builds can have different ZIP timestamps; the public files above belong to the original tag run.
