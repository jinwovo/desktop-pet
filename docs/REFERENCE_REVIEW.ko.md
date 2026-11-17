# 다른 데스크톱 펫 프로젝트 비교와 적용 검토

검토일: 2026-11-16. 기준 코드는 DesktopPet 0.6.0, 커밋 `95218ec4b56a914389ca9afd93402b80d965dda8`입니다. 공식 저장소의 README, 라이선스, 관련 구현을 확인하고 현재 코드와 비교했습니다. 외부 프로그램 실행이나 소스 편입은 수행하지 않았습니다. 아래의 적용 방향과 우선순위는 이 프로젝트에 대한 설계 판단이며, 구현 완료를 뜻하지 않습니다.

## 비교한 프로젝트

| 프로젝트 | 확인한 소스 라이선스 | 참고할 구현 | 우리 코드에 맞는 적용 방향 |
| --- | --- | --- | --- |
| [Adrianotiger/desktopPet · eSheep](https://github.com/Adrianotiger/desktopPet) | [MIT](https://github.com/Adrianotiger/desktopPet/blob/72c0fca54bdf6204f7f9d223cc4284278a0f09b9/LICENSE.md) | WinForms의 전체화면 감지, XML 행동 전환 | 전체화면 앱에 최상위 표시 양보, 캐릭터별 행동 프로필 |
| [LorisYounger/VPet](https://github.com/LorisYounger/VPet) | [Apache-2.0](https://github.com/LorisYounger/VPet/blob/b04db827e80be6fbbdd74dd76b01c2641215b0ea/LICENSE) | WPF 애니메이션의 지연 로딩과 유휴 캐시 회수 | 큰 프레임 기반 사용자 팩에 한정한 캐시 개선 |
| [pixelomer/Shijima-Qt](https://github.com/pixelomer/Shijima-Qt) | [GPLv3, 소스 헤더는 v3-or-later](https://github.com/pixelomer/Shijima-Qt/blob/main/ShijimaManager.cc) | 여러 압축팩 가져오기, 알파 판정, 동작 진단창 | 팩 설치 UX와 진단 요구사항 참고. 엔진 직접 편입은 보류 |
| [omesser/fidget](https://github.com/omesser/fidget) | [MIT](https://github.com/omesser/fidget/blob/main/LICENSE) | 활성·정지·숨김별 작업 주기, 행동 가중치와 간헐 제스처 | WPF용 상태별 작업 스케줄러, 캐릭터 성격 설정 |
| [ayangweb/BongoCat](https://github.com/ayangweb/BongoCat) | [Apache-2.0](https://github.com/ayangweb/BongoCat/blob/master/LICENSE), Cubism 등은 별도 | 입력 반응, 임시 숨김, 프레임 제한과 모델별 설정 | 간단한 로컬 반응과 방해 최소화 옵션. Live2D는 별도 확장 과제 |
| [OpenPetsHQ/openpets](https://github.com/OpenPetsHQ/openpets) | [MIT](https://github.com/OpenPetsHQ/openpets/blob/main/LICENSE) | 제한된 ZIP 입력, 설치 트랜잭션, 활동·연동 계층 | 기존 설치 검증을 재사용하는 ZIP 입력, 선택형 활동과 연동 상태 확장 |

eSheep과 VPet은 위의 확인 커밋으로 링크했습니다. 나머지 소스 링크는 이동하는 기본 브랜치이므로 실제 편입 시 커밋을 고정해야 합니다. Shijima-Qt는 저장소에 2026-04-29 보관 처리와 유지보수 중단이 표시되어 있습니다. BongoCat은 [현재 Cargo workspace](https://github.com/ayangweb/BongoCat/blob/master/Cargo.toml)와 [2.0 변경 기록](https://github.com/ayangweb/BongoCat/blob/master/CHANGELOG.md)에 따라 네이티브 Rust 구조로 확인했습니다. 과거 Tauri 구조를 현재 구현으로 간주하면 안 됩니다.

## 먼저 적용할 개선

후속 구현: 0.6.1에서 아래 1번 중 **수동 숨김 시 창 검색 중단·재표시 시 즉시 백그라운드 검색·오래된 결과 차단**과 열린 갤러리의 가져오기 후 갱신을 구현했습니다. 그 외 전체화면·잠금·모니터 배치 등의 제안은 여전히 미구현입니다. 이 문서의 문제 재현은 위에 명시한 0.6.0 기준 기록입니다.

### 1. 숨김·전체화면·잠금 상태의 작업 정책

eSheep의 [CheckFullScreen](https://github.com/Adrianotiger/desktopPet/blob/72c0fca54bdf6204f7f9d223cc4284278a0f09b9/src/dotNet/FormPet.cs#L1259-L1286)은 전경 창이 모니터를 덮으면 `TopMost`를 해제합니다. 애니메이션이나 창 검색을 중단하는 구현은 아닙니다. 포커스를 바꾸는 호출과 의심스러운 좌표 계산도 있어 해당 메서드의 직접 복사는 추천하지 않습니다.

현재 [MainWindow](../src/DesktopPet.App/MainWindow.xaml.cs)는 다음과 같습니다.

- `SetVisible(false)`는 렌더 구독을 해제하지만 `InitializePet`에서 시작한 300ms 창 검색 타이머는 종료 시까지 계속 실행합니다.
- 보이는 동안 약 900ms마다 최상위 위치를 다시 설정합니다. 전체화면·세션 잠금에 대한 별도 억제 정책은 없습니다.
- [Fidget의 frame_loop](https://github.com/omesser/fidget/blob/main/src-tauri/src/frame_loop.rs)는 활성 상태와 정지·숨김 상태의 대기 방식을 구분합니다. [BongoCat runtime](https://github.com/ayangweb/BongoCat/blob/master/crates/bongocat-runtime/src/lib.rs)도 최대 FPS와 숨김 프레임 간격을 분리합니다.

적용: 저장하는 사용자 숨김 설정과 일시적 실행 억제를 분리합니다. 수동 숨김·잠금에서는 시각 작업과 창 검색을 멈추고, 재개 직전에 한 번 새로 검색합니다. 전체화면은 펫이 있는 모니터를 기준으로 판정하고 드래그·설정 창·다른 모니터의 앱을 고려해야 합니다. 알림 수신과 집중 타이머는 유지하고, 풍선의 자동 표시만 정책에 따라 억제합니다. 걷기·전환·드래그의 현재 부드러운 렌더링은 유지합니다.

검증: 숨긴 뒤 추가 검색 없음, 재개 시 최신 플랫폼 반영, 진행 중 콜백의 늦은 결과 차단, 다른 모니터의 전체화면, 사용자 숨김 상태에서 잠금 해제, 읽지 않은 알림 보존. CPU 절감률은 별도 측정 전에는 주장하지 않습니다.

### 2. 위아래 모니터와 DPI 좌표 처리

현재 [PlatformLayout.GetMonitorFloorAt](../src/DesktopPet.Core/PlatformLayout.cs)는 모니터를 X 범위만으로 선택합니다. [PetBrain.TickFalling](../src/DesktopPet.Core/PetBrain.cs)은 그 결과를 최종 낙하 경계로 사용합니다. 같은 X 범위를 공유하는 위아래 모니터에서는 아래 모니터를 구분하지 못합니다.

실제 0.6.0 Core DLL을 사용하는 독립 임시 프로그램에서 다음을 재현했습니다. 사용자 설정이나 실제 모니터는 변경하지 않았습니다.

```text
상단 바닥: X=0..1920, Y=1080
하단 바닥: X=0..1920, Y=2160
펫 시작 발 위치: Y=1500, X=164
드래그 해제 후 1/60초 Tick
실제 결과: 발 Y=1080, 상태 Idle
기대 동작: 아래로 계속 떨어져 하단 바닥 Y=2160에 착지
```

적용: 모니터를 바닥 선만이 아니라 실제 작업 영역 사각형으로 표현하고 펫의 X·Y로 소속 모니터를 선택합니다. 연결 해제 후 가까운 모니터로 복구하는 규칙을 별도로 둡니다. 전역 `_dpiScale` 하나로 모든 화면을 환산하는 현재 방식도 함께 검토해야 합니다. 혼합 DPI 문제는 정적 검토에서 확인한 위험이며 실제 장비에서 재현한 결과는 아닙니다.

검증: 위아래 배치, 음수 좌표, 어긋난 배치, 모니터 제거의 순수 기하 테스트와 100%/150% 실제 모니터 이동 시험.

### 3. 캐릭터별 행동과 자연스러운 간헐 동작

eSheep의 [Animations.cs](https://github.com/Adrianotiger/desktopPet/blob/72c0fca54bdf6204f7f9d223cc4284278a0f09b9/src/dotNet/Animations.cs)는 경계·중력·종료 이벤트와 확률로 행동을 전환합니다. Fidget의 [manifest](https://github.com/omesser/fidget/blob/main/crates/core/src/character/manifest.rs)와 [idle cadence 검사](https://github.com/omesser/fidget/blob/main/crates/core/tests/idle_cadence.rs)는 캐릭터별 행동 가중치와 간헐 제스처를 다룹니다.

현재 `PetBrain`의 행동 확률과 휴식 시간은 모든 캐릭터에 공통입니다. `character.json`에 검증된 선택형 `behavior`를 추가해 걷기·앉기 선호, 휴식 길이부터 설정하면 기존 9종의 차이를 늘릴 수 있습니다. 기존 팩은 현재 기본값으로 동작하게 합니다. 스트레칭·귀 움직임 같은 새 제스처에는 실제 아트와 중단·복귀 규칙이 필요합니다. 그림 없이 기존 걷기를 재생해 흉내 내는 것은 자연스러움을 개선하지 못합니다.

검증: 누락 설정의 기존 호환성, 비정상 가중치·시간 거부, 고정 난수 시드의 행동 분포, 드래그·알림 우선순위, 감속·발 정착 후 정면 복귀 유지.

### 4. ZIP·드롭·여러 캐릭터 설치

[ShijimaManager의 가져오기](https://github.com/pixelomer/Shijima-Qt/blob/main/ShijimaManager.cc#L492-L571)는 여러 압축파일을 백그라운드에서 처리합니다. [Fidget 가져오기](https://github.com/omesser/fidget/blob/main/src-tauri/src/character_import.rs)와 [OpenPets 설치 코드](https://github.com/OpenPetsHQ/openpets/blob/main/apps/desktop/src/pet-installation.ts)도 ZIP 입력을 제공합니다.

현재 [CharacterLibrary.ImportFolder](../src/DesktopPet.Core/CharacterLibrary.cs)는 개별 폴더만 받지만 선언 PNG만 복사하는 스테이징, 경로·링크 검사, 크기 제한, 중복 방지와 디코드 검증이 이미 있습니다. 이 계층 앞에 제한된 ZIP 입력 어댑터를 두는 것이 적합합니다. 팬아트 6종 컬렉션을 선택하고 설치 결과를 한 번에 보여주는 UX도 유용합니다. 팩 여러 개의 부분 성공 정책과 현재 캐릭터 적용 시점을 먼저 정해야 합니다. Shimeji XML·JAR 호환은 별도의 변환기 과제이며 ZIP 지원만으로 호환된다고 표시하면 안 됩니다.

검증: 경로 탈출, 대소문자 중복, 링크, 과도한 압축 해제량, 중간 실패와 기존 설치 보존, Unicode 이름, 취소·창 닫기. 폴더 입력과 ZIP 입력에 같은 최종 검증을 적용합니다.

## 뒤이어 검토할 개선

- **진단 정보 복사:** [Shijima inspector](https://github.com/pixelomer/Shijima-Qt/blob/main/ShimejiInspectorDialog.cc)를 참고해 실제 상태·WalkView·보행 위상·지지 바닥·DPI·버전을 출력합니다. 알림 본문, 프로젝트 경로와 토큰은 포함하지 않습니다. 현재의 독립 움직임 미리보기에 더해 사용자의 실제 보행 문제를 재현하는 데 도움이 됩니다.
- **투명 여백 입력:** [ShijimaWidget의 알파 판정](https://github.com/pixelomer/Shijima-Qt/blob/main/ShijimaWidget.cc)과 [OpenPets 입력 처리](https://github.com/OpenPetsHQ/openpets/blob/main/apps/desktop/src/pet-window-interaction.ts)가 참고가 됩니다. 우리 코드는 명시적 알파 기반 입력 정책이 없습니다. 다만 WPF 투명 창의 실제 네이티브 동작을 시험하기 전에는 여백이 클릭을 차단한다고 단정할 수 없습니다. 다른 프로세스 창으로 클릭이 전달되는지, 드래그 캡처와 live-rig 변환이 유지되는지 확인해야 합니다.
- **갤러리 갱신 — 0.6.1 구현 완료:** 설정에서 팩을 가져온 뒤 열려 있는 갤러리를 갱신합니다. 검색·분류·선택을 유지하고 오래된 썸네일 작업을 취소하며, 닫힌 창에는 결과를 적용하지 않습니다.
- **큰 사용자 팩의 지연 캐시:** VPet의 [PNGAnimation](https://github.com/LorisYounger/VPet/blob/b04db827e80be6fbbdd74dd76b01c2641215b0ea/VPet-Simulator.Core/Graph/PNGAnimation.cs#L397-L452)과 [GraphCore](https://github.com/LorisYounger/VPet/blob/b04db827e80be6fbbdd74dd76b01c2641215b0ea/VPet-Simulator.Core/Graph/GraphCore.cs#L23-L47)를 참고할 수 있습니다. 현재 기본 팩은 이미 활성 이미지 19장만 읽으므로 렌더러 전체 교체의 이득은 작습니다.
- **선택형 AI 진행 상태:** OpenPets의 [로컬 연동 구조](https://github.com/OpenPetsHQ/openpets/blob/main/apps/desktop/src/codemap.md)를 참고해 완료 외에 작업 중·사용자 입력 대기·오류 상태를 분리할 수 있습니다. 우리 앱의 내구성 있는 알림함과 Claude/Codex 연결은 이미 있으므로 이를 유지하면서 확장합니다. 플러그인 엔진·Electron 전체 도입은 현재 목표에 비해 범위가 큽니다.

## 소스를 실제 가져올 때의 경계

MIT 코드를 재사용하면 해당 저작권·허가 고지를 보존해야 합니다. Apache-2.0 코드는 라이선스 사본, 해당 고지·NOTICE와 수정 표시 조건을 확인해야 합니다. Shijima-Qt의 GPL 소스를 현재 MIT 코드에 편입하는 것은 별도 배포 조건 검토가 필요하므로 이 검토에서는 보류했습니다.

소스 라이선스와 캐릭터 그림·모델의 허가는 구분합니다. VPet은 [README에 애니메이션 별도 조건](https://github.com/LorisYounger/VPet#动画版权声明与授权)을 표시하고, BongoCat의 [Cubism 라이선스 파일](https://github.com/ayangweb/BongoCat/blob/master/vendor/cubism/5-r.5/LICENSE.md)도 SDK·Core·샘플 모델의 별도 조건을 안내합니다. 저장소 루트 라이선스로 외부 캐릭터 아트까지 일괄 허가되었다고 판단하지 않습니다.

실제 편입 시에는 파일별 원본 URL·커밋·라이선스·변경 내역을 기록합니다. 현재 비교 검토로 추가된 외부 런타임 의존성이나 복사한 외부 소스는 없습니다.
