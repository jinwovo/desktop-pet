<#
.SYNOPSIS
    데스크탑 펫에게 "사용자를 불러달라"고 알린다.

.DESCRIPTION
    %LocalAppData%\DesktopPet\inbox 에 JSON 파일을 하나 떨어뜨리면 펫 앱이 그걸 감지해서
    현재 숨김·방해금지·자동 닫힘 설정을 따르는 말풍선으로 알린다.

    펫 앱이 꺼져 있어도 파일은 남아 있다가 다음에 켤 때 표시된다.

.PARAMETER Message
    말풍선 본문.

.PARAMETER Title
    말풍선 제목 (기본값: "작업 완료").

.PARAMETER Mood
    gentle(조용한 완료 안내, 기본), shout(화내기), cry(울기).

.EXAMPLE
    .\scripts\notify-pet.ps1 -Message "빌드 끝났어요. 확인해주세요!"
    .\scripts\notify-pet.ps1 -Title "테스트 실패" -Message "3개 실패" -Mood cry
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Message,

    [string]$Title = "작업 완료",

    [ValidateSet("shout", "cry", "gentle")]
    [string]$Mood = "gentle",

    [string]$InboxDirectory = (Join-Path $env:LOCALAPPDATA "DesktopPet\inbox")
)

$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$inbox = [System.IO.Path]::GetFullPath($InboxDirectory)
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

$payload = [ordered]@{
    title   = $Title
    message = $Message
    mood    = $Mood
} | ConvertTo-Json -Compress

# Write to a temp name first, then rename: the watcher must never see a half-written file.
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss_fff")
$notificationId = "{0}_{1}" -f $stamp, ([Guid]::NewGuid().ToString("N"))
$tmp = Join-Path $inbox "$notificationId.tmp"
$final = Join-Path $inbox "$notificationId.json"

[System.IO.File]::WriteAllText($tmp, $payload, (New-Object System.Text.UTF8Encoding $false))
Move-Item -LiteralPath $tmp -Destination $final
