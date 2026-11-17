# Optional fan-art character packs

This collection contains six AI-assisted fan-art interpretations requested by the project owner:

| Folder | English name | Selection source |
| --- | --- | --- |
| `치이카와` | Chiikawa | Existing personal collection |
| `세균맨` | Baikinman | Existing personal collection |
| `헬로키티` | Hello Kitty | Existing personal collection |
| `폼폼푸린` | Pompompurin | 2026 Sanrio Character Ranking, 1st |
| `시나모롤` | Cinnamoroll | 2026 Sanrio Character Ranking, 2nd |
| `쿠로미` | Kuromi | 2026 Sanrio Character Ranking, 4th |

The original Companion, Nimbus and Ember remain the three characters bundled in the default public executable. Read [NOTICE.md](NOTICE.md) and [POPULAR_ASSETS.md](POPULAR_ASSETS.md) for rights and creation records. The MIT license does not grant rights to third-party characters or these fan-art images; no permission from their rights holders is represented here.

## Import a character

1. Extract the optional character-packs ZIP, or obtain this directory from the source repository.
2. Open DesktopPet Settings and select **캐릭터 추가** (Add character).
3. Select one individual character folder from the table. It must directly contain `character.json` and its PNG files.
4. DesktopPet validates the images, copies the selected pack to the current user's character library and activates it.

Do not select the collection's parent directory or ZIP itself. Existing installed names are not overwritten. Each pack includes 211 referenced PNGs: declared states, six live-rig textures, fallback walking and settling poses. The import folders contain no executable or user settings.

## Build the collection

```powershell
powershell -NoProfile -File scripts/package-character-packs.ps1 -Version 0.6.1
```

The script validates `catalog.json`, character identities, safe PNG references, dimensions and image budgets. It copies only the six packs' declared files, this README, the notice, provenance and catalog; source masters and unreferenced files stay out of the import ZIP. A SHA256 sidecar and per-file inventory accompany the fresh archive under `artifacts/character-packs/`. Existing outputs are never overwritten.

For a personal executable containing the original three plus all six optional characters, build with `-p:PersonalBuild=true`. `-p:DistributionBuild=true` keeps only the original three, including when both flags are supplied.

## 한국어

치이카와·세균맨·헬로키티에 폼폼푸린·시나모롤·쿠로미를 더한 선택형 팬아트 팩입니다. ZIP을 푼 뒤 설정의 **캐릭터 추가**에서 `character.json`이 바로 들어 있는 개별 캐릭터 폴더를 선택하세요. 기본 공개 실행파일에는 오리지널 3종이 포함됩니다. 원작 캐릭터와 팬아트의 권리는 MIT 라이선스에 포함되지 않습니다.

## 简体中文

本可选同人素材包包含 Chiikawa、Baikinman、Hello Kitty、Pompompurin、Cinnamoroll 和 Kuromi。解压 ZIP 后，在设置中选择 **캐릭터 추가**，再选择直接包含 `character.json` 的单个角色文件夹。默认公开程序包含三个原创角色。MIT 许可证不授予原作角色或同人图片的使用权。
