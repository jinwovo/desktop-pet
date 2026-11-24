# Public 1.3.0 verification

The [1.3.0 release](https://github.com/jinwovo/desktop-pet/releases/tag/v1.3.0) contains P001–P030 at immutable source commit `885d9df238b1ef0ed7f7f215a10db20f8eb2d0aa`. Annotated tag object: `fac0d9c4c983dee7f7761f91143709b2d2375bfa`. Release ID: `456390400`.

The [exact-source main checks](https://github.com/jinwovo/desktop-pet/actions/runs/45340658244) and [original tag release workflow](https://github.com/jinwovo/desktop-pet/actions/runs/45341377299) both succeeded. The tag workflow tested public-only and nine-character configurations, packaged both ZIPs, checked extracted installation/startup and published the release. Its publisher then downloaded all four public assets anonymously and compared sizes and SHA256 hashes with its authenticated pre-publication downloads.

An independent download after publication confirmed the annotated tag target, public release identity, all four uploaded sizes and GitHub asset digests, both checksum sidecars, and these actual bytes:

| File | Bytes | SHA256 |
| --- | ---: | --- |
| DesktopPet-1.3.0-win-x64.zip | 78471232 | `9935B1ABFBDAB3765E3D179081AECD771A11B23CA273975771DB1E8CC0FEF090` |
| DesktopPet-1.3.0-win-x64.zip.sha256 | 96 | `7DAF207EE57E0032E11FDD3B806882FBF6171BD83D7351775E0E047AE05B32E2` |
| DesktopPet-1.3.0-fan-art-packs.zip | 15267002 | `1EB7F996F8E51E5012837BB879EFCA6B28B2C6DFE7F2DC104C8CC789D6015BB3` |
| DesktopPet-1.3.0-fan-art-packs.zip.sha256 | 102 | `1D178FCABF0B7F7D3A075B89A13EAADDDACD64F4D6EB910AC18AC5237DC64121` |

The main ZIP contains 660 files and exactly Companion, Ember and Nimbus. The separate fan-art ZIP contains 1,277 files and the six catalog characters. Both archives passed ZIP integrity checks and a scan for the configured development credential. No private `.env`, `.codex` or `.local` payload paths were found; this record does not publish the credential value.

The downloaded public executable reports product version `1.3.0+885d9df238b1ef0ed7f7f215a10db20f8eb2d0aa`. With external .NET roots pointed to an empty fixture, its read-only `--version` startup returned `1.3.0.0`. This check did not start the normal pet GUI. Physical mixed-DPI and clean-PC testing remain manual follow-up work.

Local preparation passed all 238 core checks, the complete nine-character UI suite, 37 evolution publication guards and documentation/source checks. The [patch ledger](evolution-2.0.json) names the product and regression sources for each improvement. Release administration, screenshots and this document do not add product patches.

The reference is the original tag workflow. Independently built main-branch ZIPs can have different archive timestamps. The existing 1.2.0 tag and assets remain unchanged.
