<#
.SYNOPSIS
    기존 Claude Stop 등록을 배포용 공용 브리지로 연결한다.
.NOTES
    stdin JSON은 브리지가 읽는다. 대화 원문은 알림에 저장하지 않는다.
    이전 -Mood 인자는 호환을 위해 받지만 자동 완료 알림은 gentle로 표시한다.
#>
param([string]$Mood = 'gentle', [string]$RegistrationId = '')
$ProgressPreference = 'SilentlyContinue'

try {
    $bridge = Join-Path (Split-Path -Parent $PSScriptRoot) 'Hooks\assistant-stop-hook.ps1'
    if (Test-Path -LiteralPath $bridge -PathType Leaf) {
        & $bridge -Source claude -RegistrationId $RegistrationId
    }
    else { [Console]::Out.WriteLine('{}') }
}
catch { [Console]::Out.WriteLine('{}') }
exit 0
