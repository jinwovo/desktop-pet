# 일반 사용자 배포 안내

## 사용자 시작 순서

기본 배포는 Windows 10/11 x64용 self-contained ZIP이다. ZIP 전체를 쓰기 가능한 폴더에 풀고 `DesktopPet.App.exe`를 실행한다. 단일 EXE만 다른 위치로 옮기면 캐릭터와 알림 스크립트가 없어지므로 폴더 구성을 유지한다.

첫 실행은 현재 Windows 사용자의 도우미 설정 폴더를 감지한다. 개인 개발자의 절대 경로나 프로젝트 위치를 요구하지 않는다. 첫 연결 화면과 트레이의 **AI 연결**에서 현재 상태를 확인하고 개별 연결·해제·자동 연결 설정을 바꾼다.

| 도구 | 기본 설정 위치 | 적용에 필요한 사용자 작업 |
| --- | --- | --- |
| Claude Code | 사용자 홈의 `.claude/settings.json` 또는 `CLAUDE_CONFIG_DIR/settings.json` | 연결 후 세션 재시작 |
| 훅을 지원하는 로컬 Codex | 사용자 홈의 `.codex/hooks.json` 또는 `CODEX_HOME/hooks.json` | `/hooks` 또는 앱의 Settings → Hooks에서 새 훅 검토·허용 |

Codex의 비관리 훅은 사용자가 정의를 신뢰해야 실행된다. 프로그램은 신뢰 저장소를 변경하지 않는다. 이 정책과 설정 경로는 [OpenAI Hooks 공식 문서](https://learn.chatgpt.com/docs/hooks)에 따른다. Claude의 사용자 설정·Stop 이벤트는 [Claude Code 훅 문서](https://code.claude.com/docs/en/hooks)와 [설정 문서](https://code.claude.com/docs/en/settings)를 따른다.

웹사이트의 Claude·ChatGPT 대화나 Stop 훅이 없는 앱의 완료는 지원하지 않는다. 응답 턴 종료 알림이 전체 작업 성공을 보장하지는 않는다. 관리자가 훅을 비활성화한 환경에서는 기존 정책을 보존한다.

## 알림과 저장 위치

완료 알림은 기본 8초 뒤 부드럽게 닫힌다. 연속 완료는 최신 내용을 보여주되 표시 시간을 계속 연장하지 않는다. 미확인 알림은 도착 순서대로 보존한다. 트레이의 **보관된 알림 확인**으로 다시 읽고 클릭하면 해당 파일만 삭제한다. 숨김·방해금지 중에도 알림은 보관한다.

- 환경설정: `%APPDATA%/DesktopPet/settings.json`
- 미확인 알림: `%LOCALAPPDATA%/DesktopPet/inbox/`
- 사용자별 연결 이력: `%LOCALAPPDATA%/DesktopPet/assistant-connections.json`
- 추가 캐릭터: `%LOCALAPPDATA%/DesktopPet/Characters/`
- 중복 억제 정보: inbox 안의 `.assistant-events/`

브리지는 대화 원문이나 전체 작업 폴더 경로를 알림에 저장하지 않는다. 도구 이름·폴더 이름·완료 시각·해시 식별자를 사용한다. Claude의 동일 문장 반복 구분에는 transcript의 파일 크기·수정 시각만 사용하고 내용은 읽지 않는다. 이 동작은 로컬 파일로 처리되며 네트워크로 전송하지 않는다.

## 선택 설치와 제거

ZIP에서 바로 실행할 수 있다. 사용자별 고정 설치가 필요하면 압축을 푼 폴더에서 실행한다.

```powershell
powershell -NoProfile -File ./install.ps1
# 로그인 시 실행을 원하는 경우만:
powershell -NoProfile -File ./install.ps1 -AddToStartup
```

기본 위치는 `%LOCALAPPDATA%/DesktopPet`이다. 패키지 안의 설치 스크립트는 SDK로 재빌드하지 않는다. 앱이 실행 중이거나 실행 파일이 잠겨 있으면 덮어쓰지 않고 중단한다. 업데이트 실패 시 관리 파일을 이전 상태로 복구하고 알림·사용자 캐릭터는 보존한다.

제거는 트레이에서 앱을 종료한 다음 설치 폴더의 `uninstall.ps1`을 실행한다. 현재 설정과 기록된 사용자 지정 설정 경로에서 DesktopPet의 훅만 해제한다. 연결 해제에 실패하면 설치 파일을 남기고 중단한다. 관리 파일 목록에 없는 개인 파일·알림·설정은 삭제하지 않는다.

설치하지 않고 사용하던 폴더를 옮기기 전에는 **AI 연결 → 연결 해제**로 이전 경로를 정리한다. 새 위치에서 실행해 연결하면 기존 DesktopPet 훅을 현재 스크립트 경로로 갱신한다. Codex는 바뀐 정의를 다시 검토해야 할 수 있다.

## 제작자 빌드

지원 중인 .NET 10 LTS를 사용한다. 기존 .NET 8은 2026-11-10 지원이 종료되었다. [Microsoft 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)

```powershell
powershell -NoProfile -File scripts/package-release.ps1 -Version 0.5.0
```

결과는 `artifacts/releases/DesktopPet-0.5.0-win-x64.zip`과 `.zip.sha256`이다. `DesktopPet.release.json`에는 버전·배포 종류·기본 캐릭터·연결 해제 CLI 지원 버전·파일별 크기와 SHA256을 기록한다. 설치 전 manifest와 파일 집합·실행 파일 버전을 검사한다.

`DistributionBuild=true`는 Companion·Nimbus·Ember 오리지널3종만 포함한다. 원본 이미지, 개인용 캐릭터, 테스트, 실제 사용자 설정은 실행 파일 배포에 넣지 않는다. 공용 원화와 제작 과정은 [PUBLIC_ASSET_PROVENANCE.md](PUBLIC_ASSET_PROVENANCE.md)에 기록했다. GitHub 공개용 소스 내보내기는 [PUBLIC_RELEASE.md](PUBLIC_RELEASE.md)를 참고한다.

`-FrameworkDependent`는 .NET 10 Desktop Runtime x64가 별도로 필요한 명시적 빌드다. 기본 ZIP으로 대체하지 않으며 이름에도 `framework-dependent`를 붙인다. 이 옵션도 단일 파일 게시용 빌드 패키지는 필요하다.

[Windows 자동 빌드](../.github/workflows/windows-release.yml)는 .NET 10 SDK 설정, 핵심·UI·브리지·설치·자산 검사, 공개 자산만으로 UI 실행, self-contained 패키징과 압축 해제 후 `--version` 실행을 수행하도록 구성했다. 액션 버전과 SDK 설정 방식은 [GitHub의 .NET 빌드 안내](https://docs.github.com/en/actions/tutorials/build-and-test-code/net)를 참고했다. 현재 원격 실행 결과는 없다.

## 현재 검증 한계

로컬 .NET 10 빌드와 일반 폴더 게시는 성공했다. 공개 자산만 포함한 UI 검사도 통과했다. ZIP·설치 검사는 합성 실행 파일을 사용하는 회귀 검사로 검증했다.

2026-11-15에 공식 런타임 팩을 받아 **실제 self-contained ZIP을 생성했다.** 압축 해제 후 manifest·SHA256·공용3종·라이선스를 검사했고, 외부 .NET 경로를 빈 폴더로 지정해도 `--version`이 `0.5.0.0`으로 정상 종료했다. .NET Core·Windows Desktop 10.0.14가 포함된다. 별도 .NET10 Desktop Runtime을 쓰는 framework-dependent 확인용 ZIP도 구분해 보존한다.

런타임이 없는 깨끗한 Windows PC의 실제 앱 실행과 혼합 DPI 물리 모니터 이동, 원격 GitHub Actions 결과는 아직 확인하지 않았다. 최종 배포 시 이 범위를 별도로 확인한다.
