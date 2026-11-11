<#
.SYNOPSIS
    Claude Code의 Stop 훅에서 호출되어, 작업이 끝났음을 데스크탑 펫으로 알린다.

.DESCRIPTION
    Claude Code는 훅에 JSON을 stdin으로 넘긴다. 여기서는 그 내용 자체보다 "어느 폴더에서
    작업이 끝났는지"가 유용하므로 현재 작업 디렉터리 이름을 메시지에 넣는다.

    훅은 Claude가 응답을 마칠 때마다 실행되므로, 실패해도 조용히 끝나야 한다
    (여기서 오류를 내면 Claude Code 쪽에 오류가 표시된다).

.NOTES
    .claude/settings.json 의 hooks.Stop 에 등록해서 쓴다.
#>
param(
    [string]$Mood = "shout"
)

try {
    # Claude Code writes the hook payload to stdin; drain it so the pipe never blocks,
    # even though the folder name is all we actually need.
    if (-not [Console]::IsInputRedirected) { } else { [Console]::In.ReadToEnd() | Out-Null }

    $folder = Split-Path -Leaf (Get-Location).Path
    $time = (Get-Date).ToString("HH:mm")
    $message = "[$folder] 작업이 끝났어요. 확인해주세요! ($time)"

    & (Join-Path $PSScriptRoot "notify-pet.ps1") -Title "Claude Code 작업 완료" -Message $message -Mood $Mood
}
catch {
    # Never surface hook failures into the Claude Code session.
    exit 0
}
