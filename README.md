# DesktopPet

윈도우 바탕화면 위를 걸어다니는 데스크탑 마스코트 앱. 캐릭터가 화면 아래를 돌아다니고,
열려 있는 창 위에 올라서고, 마우스를 쳐다보고, 집어 들었다 놓으면 떨어져서 착지한다.

Claude Code 작업이 끝나면 캐릭터가 말풍선을 띄우고 화내면서 알려주는 기능이 핵심이다.

## 주요 기능

- **돌아다니기** — 작업표시줄 위뿐 아니라 **열려 있는 다른 창의 윗변**도 발판으로 인식한다.
  창 끝에 닿으면 옆에 있는 창으로 **뛰어내리거나 뛰어올라** 건너간다. 밟고 있던 창이 닫히거나
  움직이면 떨어진다.
- **던지기** — 드래그해서 휙 놓으면 관성으로 날아가고, 화면 끝에 부딪히면 튕긴다.
- **감정 표현** — 높은 곳에서 떨어뜨리면 울고(😢), 빠르게 여러 번 찌르면 화낸다(💢).
  오래 방치하면 잠든다(💤). 커서를 움직이거나 클릭하면 깨어난다.
- **작업 완료 알림** — 외부에서 알림을 보내면 말풍선을 띄우고 **확인할 때까지** 제자리에서
  뛰며 조른다.
- **창 닫힘 연출** — 다른 프로그램 창을 닫으면 그 창이 작업표시줄로 떨어지며 사라진다.
- **캐릭터 자동 생성** — 그림 파일을 넣기만 하면 걷기/울기/화내기/잠자기 프레임을 자동 합성한다.

## 빠른 시작

```powershell
# 1. 빌드
dotnet build DesktopPet.sln

# 2. 실행
.\src\DesktopPet.App\bin\Debug\net8.0-windows\DesktopPet.App.exe
```

트레이 아이콘을 더블클릭하면 설정 창이 열린다 — 캐릭터 선택, 크기/속도 슬라이더, 동작 토글.
슬라이더는 움직이는 즉시 펫에 반영된다.

> 무겁게 느껴지면 설정에서 **"창 닫을 때 떨어지는 연출"을 끄면 된다.** 그 기능이 다른 프로그램의
> 창을 주기적으로 캡처하는 유일한 부분이라, 끄면 체감이 크게 달라진다.

설치해서 쓰려면:

```powershell
.\scripts\install.ps1              # %LocalAppData%\DesktopPet 에 설치
.\scripts\install.ps1 -AddToStartup  # 로그인 시 자동 실행까지
```

## 캐릭터 추가하기

`images/` 폴더에 그림을 넣고 스크립트를 돌리면 끝이다. 좌표를 적을 필요가 없다.

```powershell
.\scripts\build-characters.ps1
```

배경색, 캐릭터 개수, 각 캐릭터의 경계를 알아서 판별한다. 이름은 파일 이름을 따르고,
한 그림에 여러 캐릭터가 있으면 왼쪽부터 번호가 붙는다. 이름을 직접 정하려면
`images/overrides.json`:

```json
{
  "trio.png":   { "names": ["Hachiware", "Chiikawa", "Usagi"], "walkSpeed": 65 },
  "복잡한거.jpg": { "skip": true }
}
```

자세한 동작 원리는 [SPEC.md](SPEC.md) 5-1절 참고.

## Claude Code 작업 완료 알림

`.claude/settings.json` 의 `Stop` 훅이 작업 종료 시 펫에게 신호를 보낸다.

```
Claude Code (작업 완료)
  → scripts/claude-stop-hook.ps1
  → %LocalAppData%\DesktopPet\inbox\*.json
  → 펫 앱이 감지 → 말풍선 + 화난 포즈 + 제자리 점프
```

펫이나 말풍선을 클릭하면 확인 처리된다. 다른 용도로도 쓸 수 있다:

```powershell
.\scripts\notify-pet.ps1 -Message "빌드 끝남"
.\scripts\notify-pet.ps1 -Title "테스트 실패" -Message "3개 실패" -Mood cry
```

펫 앱이 꺼져 있어도 메시지는 남아 있다가 다음 실행 때 표시된다.

현재는 이 저장소에서 작업할 때만 동작한다. 모든 폴더에서 알림을 받으려면 같은 `hooks` 설정을
`~/.claude/settings.json` 으로 옮기면 된다.

## 구조

```
src/
  DesktopPet.App/        진입점, 펫 창, 트레이 메뉴, 말풍선
  DesktopPet.Core/       펫 상태 머신, 발판 계산, 캐릭터 로딩, 알림 수신 (UI 비의존)
  DesktopPet.Interop/    Win32 P/Invoke (창 후킹, 캡처, z-order, DWM)
  DesktopPet.WindowFall/ 창 닫힘 감지와 낙하 연출
scripts/
  build-characters.ps1   그림 → 캐릭터 프레임 자동 생성
  CharacterBuilder.cs    이미지 처리 라이브러리
  notify-pet.ps1         펫에게 알림 보내기
  claude-stop-hook.ps1   Claude Code Stop 훅용 래퍼
  install.ps1 / uninstall.ps1
images/                  캐릭터 생성에 쓰는 원본 그림
Assets/Characters/       생성된 캐릭터 (프레임 PNG + character.json)
```

## 요구 사항

- Windows 10/11
- .NET 8 SDK

## 참고

- 캐릭터 이미지는 개인적으로 쓰려고 넣어둔 저작권 자료다. **이 저장소는 비공개로 유지할 것.**
- PowerShell 5.1이 BOM 없는 UTF-8을 CP949로 읽으므로, `scripts/*.ps1` 은 UTF-8 BOM으로
  저장해야 한글이 깨지지 않는다.
