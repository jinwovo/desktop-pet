<#
.SYNOPSIS
    데스크탑 펫에게 "사용자를 불러달라"고 알린다.

.DESCRIPTION
    %LocalAppData%\DesktopPet\inbox 에 JSON 파일을 하나 떨어뜨리면 펫 앱이 그걸 감지해서
    말풍선을 띄우고, 사용자가 클릭해서 확인할 때까지 계속 화내며 제자리 점프를 한다.

    펫 앱이 꺼져 있어도 파일은 남아 있다가 다음에 켤 때 표시된다.

.PARAMETER Message
    말풍선 본문.

.PARAMETER Title
    말풍선 제목 (기본값: "작업 완료").

.PARAMETER Mood
    shout(화내기, 기본) 또는 cry(울기).

.EXAMPLE
    .\scripts\notify-pet.ps1 -Message "빌드 끝났어요. 확인해주세요!"
    .\scripts\notify-pet.ps1 -Title "테스트 실패" -Message "3개 실패" -Mood cry
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Message,

    [string]$Title = "작업 완료",

    [ValidateSet("shout", "cry")]
    [string]$Mood = "shout"
)

$ErrorActionPreference = "Stop"

$inbox = Join-Path $env:LOCALAPPDATA "DesktopPet\inbox"
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

$payload = [ordered]@{
    title   = $Title
    message = $Message
    mood    = $Mood
} | ConvertTo-Json -Compress

# Write to a temp name first, then rename: the watcher must never see a half-written file.
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss_fff")
$tmp = Join-Path $inbox "$stamp.tmp"
$final = Join-Path $inbox "$stamp.json"

[System.IO.File]::WriteAllText($tmp, $payload, (New-Object System.Text.UTF8Encoding $false))
Move-Item $tmp $final -Force
