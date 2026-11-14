# Optional fan-art character packs

This optional collection contains **치이카와 (Chiikawa)**, **세균맨 (Baikinman)** and **헬로키티 (Hello Kitty)**. These are AI-assisted fan-art interpretations of existing third-party characters, included at the project owner's request. They are separate from the original Companion, Nimbus and Ember collection and are not bundled in the default public executable.

Read [NOTICE.md](NOTICE.md) for the artwork's status. The repository's MIT license does not grant rights to these third-party characters or their artwork. No permission from their rights holders is represented here.

## Import a character

1. Download and extract the optional character-packs ZIP, or obtain this directory from the source repository.
2. Open DesktopPet Settings and select **캐릭터 추가** (Add character).
3. Select one individual folder: `치이카와`, `세균맨` or `헬로키티`. The folder must directly contain `character.json` and its PNG files.
4. DesktopPet validates the images, copies the selected pack to the current user's character library and activates it.

Do not select this collection's parent directory or the ZIP itself. Existing installed names are not overwritten. Each pack includes all declared states, six live-rig textures, legacy gait frames and settling poses. No executable, assistant configuration or conversation data is included.

To build the optional ZIP from source:

```powershell
powershell -NoProfile -File scripts/package-character-packs.ps1 -Version 0.5.0
```

The script validates safe PNG references, image dimensions and import budgets, then writes a fresh ZIP, a SHA256 sidecar and a per-file inventory under `artifacts/character-packs/`. Existing outputs are never overwritten.

## 한국어

치이카와·세균맨·헬로키티를 선택해서 추가하는 팬아트 팩입니다. 기본 공개 실행파일의 오리지널 3종과 별도로 제공합니다. ZIP을 푼 다음 설정의 **캐릭터 추가**에서 `character.json`이 바로 들어 있는 개별 캐릭터 폴더를 선택하세요. 기존 같은 이름의 캐릭터는 덮어쓰지 않습니다.

이 캐릭터들은 프로젝트 소유 오리지널이 아닙니다. AI를 보조 도구로 사용한 팬아트이며, MIT 라이선스로 원작 캐릭터·그림의 권리를 허가하지 않습니다. 자세한 내용은 [NOTICE.md](NOTICE.md)를 확인하세요.

## 简体中文

这是可选的 Chiikawa、Baikinman 和 Hello Kitty 同人角色包，与默认公开程序中的三个原创角色分开提供。解压 ZIP 后，在设置中选择 **캐릭터 추가**，再选择直接包含 `character.json` 的单个角色文件夹。已有的同名角色不会被覆盖。

这些是借助 AI 制作的第三方角色同人素材，并非本项目拥有的原创角色。项目的 MIT 许可证不授予这些第三方角色或图片的权利。请阅读 [NOTICE.md](NOTICE.md)。
