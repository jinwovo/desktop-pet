# Public 1.2.0 verification

The [1.2.0 release](https://github.com/jinwovo/desktop-pet/releases/tag/v1.2.0) contains P001-P020 at immutable source commit `4338fbe22f866762496c7dce37dd91805f88491b`.

The [original tag workflow](https://github.com/jinwovo/desktop-pet/actions/runs/44684227455) completed its Windows verification job successfully, uploaded the verified files and published release ID `452325501`. Its subsequent anonymous API request failed with `API rate limit exceeded`. The historical run remains failed; its tag and assets have not been replaced.

The [read-only audit](https://github.com/jinwovo/desktop-pet/actions/runs/45303142289) succeeded. It verified the immutable tag, the original successful `verify / verify-and-package` job, public stable release identity, and all four publicly downloaded files against that job's artifact. Both checksum sidecars also match their ZIPs. A separate build of the same source is not the byte-comparison reference: independent ZIP builds can have different timestamps.

| Public file | Bytes | SHA256 |
| --- | ---: | --- |
| DesktopPet-1.2.0-win-x64.zip | 78456866 | `0ED8A7CD84E677ECE09F211505E9D191DEFEA50C4A08EB7F6EC03BCE038557C8` |
| DesktopPet-1.2.0-win-x64.zip.sha256 | 96 | `BF4C1C8B42CAE9C26235D17FDF54DF83F65B45B87DEFF371733654BBBAB32861` |
| DesktopPet-1.2.0-fan-art-packs.zip | 15267542 | `814DFB1C93EECA4F36434BE3215C58C3B535D1D54DB66CEE621037DFE5076CCF` |
| DesktopPet-1.2.0-fan-art-packs.zip.sha256 | 102 | `DBFBA4E83936256517A8832D3CFCF4993F16D699567765B45D3F9BC521CEE429` |

Future publication checks use authenticated release metadata and actual anonymous asset downloads rather than the anonymous API's shared runner-IP quota. They retry reads within a bounded window and never repeat publication or replace a release. Offline checks cover stale draft visibility, transport failures, public-download failure, incorrect identities/states and bounded failure without request-detail disclosure.

This release repair is excluded from the 100 product patches. Local tests and an independent NIM review passed; execution of the revised publisher will be checked again at the next release milestone.
