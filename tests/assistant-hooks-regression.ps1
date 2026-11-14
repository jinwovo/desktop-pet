<# Fixture-only hook checks: no assistant configuration, real inbox or application is touched. #>
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$bridge = Join-Path $repoRoot 'Hooks\assistant-stop-hook.ps1'
$wrapper = Join-Path $repoRoot 'scripts\claude-stop-hook.ps1'
$notify = Join-Path $repoRoot 'scripts\notify-pet.ps1'
$fixtureRoot = Join-Path $repoRoot ('.local\assistant-hooks-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureRoot)
$powershell = (Get-Command powershell.exe -ErrorAction Stop).Source
$script:passed = 0

function Assert-Hook([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Pass-Hook([string]$Name) { $script:passed++; Write-Output "PASS $Name" }
function Quote-ProcessArgument([string]$Value) {
    return '"' + (($Value -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"'
}
function Start-HookProcess([string]$ScriptPath, [string[]]$Parameters, [string]$AppDataRoot, [string]$EncodedScript = '') {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $powershell
    $arguments = if ($EncodedScript) {
        @('-NoProfile', '-NonInteractive', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-EncodedCommand',
            [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($EncodedScript)))
    } else { @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Parameters }
    $info.Arguments = ($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = New-Object Text.UTF8Encoding $false
    $info.StandardErrorEncoding = New-Object Text.UTF8Encoding $false
    $info.EnvironmentVariables['LOCALAPPDATA'] = $AppDataRoot
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $info
    [void]$process.Start()
    return $process
}
function Write-HookInput($Process, [string]$Json) {
    # .NET Framework ProcessStartInfo has no StandardInputEncoding property.
    $bytes = [Text.Encoding]::UTF8.GetBytes($Json)
    $Process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $Process.StandardInput.BaseStream.Flush()
    $Process.StandardInput.Close()
}
function Complete-HookProcess($Process, [bool]$ExpectJson = $true) {
    try {
        Assert-Hook ($Process.WaitForExit(15000)) 'Hook did not return promptly'
        $stdout = $Process.StandardOutput.ReadToEnd()
        $stderr = $Process.StandardError.ReadToEnd()
        Assert-Hook ($Process.ExitCode -eq 0) "Hook failed: $stderr"
        Assert-Hook ([string]::IsNullOrWhiteSpace($stderr)) "Hook wrote an error: $stderr"
        if ($ExpectJson) { Assert-Hook ($stdout.Trim() -eq '{}') "Unexpected hook stdout: $stdout" }
    }
    finally {
        if (-not $Process.HasExited) { $Process.Kill(); [void]$Process.WaitForExit(5000) }
        $Process.Dispose()
    }
}
function Invoke-Hook([string]$Assistant, $Payload, [string]$AppDataRoot, [string[]]$Extra = @(), [string]$ScriptPath = $bridge) {
    $parameters = if ($ScriptPath -eq $bridge) { @('-Source', $Assistant) + $Extra } else { $Extra }
    $process = Start-HookProcess $ScriptPath $parameters $AppDataRoot
    try {
        $json = if ($Payload -is [string]) { $Payload } else { $Payload | ConvertTo-Json -Compress -Depth 10 }
        Write-HookInput $process $json
    }
    finally { $process.StandardInput.Close() }
    Complete-HookProcess $process
}
function Read-Notifications([string]$AppDataRoot) {
    $inbox = Join-Path $AppDataRoot 'DesktopPet\inbox'
    if (-not [IO.Directory]::Exists($inbox)) { return @() }
    return @([IO.Directory]::GetFiles($inbox, '*.json') | ForEach-Object { [IO.File]::ReadAllText($_) | ConvertFrom-Json })
}
function New-Stop([string]$Session, [string]$Turn = '') {
    $payload = [ordered]@{
        session_id = $Session
        hook_event_name = 'Stop'
        cwd = 'C:\private-parent\한글 프로젝트'
        stop_hook_active = $false
        last_assistant_message = 'PRIVATE assistant response; never write this conversation text.'
    }
    if ($Turn) { $payload.turn_id = $Turn }
    return $payload
}

try {
    $appData = Join-Path $fixtureRoot 'main'
    $claude = New-Stop 'claude-session'
    $claude.agent_type = 'security-reviewer' # Main --agent is not a subagent.
    Invoke-Hook 'claude' $claude $appData
    $notifications = @(Read-Notifications $appData)
    Assert-Hook ($notifications.Count -eq 1) 'Claude main Stop was not delivered once'
    $message = $notifications[0]
    Assert-Hook ($message.source -eq 'claude' -and $message.mood -eq 'gentle') 'Claude source/mood missing'
    Assert-Hook ($message.projectFolder -eq '한글 프로젝트' -and $message.message.Contains('한글 프로젝트')) 'Project leaf or UTF-8 failed'
    Assert-Hook ($message.eventId -match '^claude-[0-9a-f]{64}$') 'Event ID must be an opaque digest'
    Assert-Hook ($message.createdAtUtc.EndsWith('Z')) 'Created time is not UTC'
    $disk = (@([IO.Directory]::GetFiles((Join-Path $appData 'DesktopPet\inbox'), '*', [IO.SearchOption]::AllDirectories)) | ForEach-Object { [IO.File]::ReadAllText($_) }) -join ''
    Assert-Hook (-not $disk.Contains('PRIVATE') -and -not $disk.Contains('private-parent') -and -not $disk.Contains('claude-session')) 'Hook persisted conversation content or private paths/identifiers'
    Pass-Hook 'Claude Stop emits gentle UTF-8 metadata without conversation or full project paths'

    Invoke-Hook 'claude' $claude $appData @('-RegistrationId', 'duplicate-registration')
    Assert-Hook (@(Read-Notifications $appData).Count -eq 1) 'Repeated registration delivered the same Stop twice'
    $claude.turn_id = 'turn-2'
    Invoke-Hook 'claude' $claude $appData
    Assert-Hook (@(Read-Notifications $appData).Count -eq 2) 'Distinct Claude turn was suppressed'
    Pass-Hook 'Repeated registrations deduplicate; distinct turns still deliver'

    $metadataData = Join-Path $fixtureRoot 'metadata'
    $transcriptFixture = Join-Path $fixtureRoot 'fixture-transcript.jsonl'
    [IO.File]::WriteAllText($transcriptFixture, 'fixture turn one')
    $sameReply = New-Stop 'claude-metadata-session'
    $sameReply.transcript_path = $transcriptFixture
    Invoke-Hook 'claude' $sameReply $metadataData
    Invoke-Hook 'claude' $sameReply $metadataData
    Assert-Hook (@(Read-Notifications $metadataData).Count -eq 1) 'Unchanged transcript metadata did not deduplicate'
    [IO.File]::AppendAllText($transcriptFixture, ' fixture turn two')
    Invoke-Hook 'claude' $sameReply $metadataData
    Assert-Hook (@(Read-Notifications $metadataData).Count -eq 2) 'Distinct Claude turns with equal reply text were conflated'
    Pass-Hook 'Claude transcript metadata distinguishes equal replies without reading conversation contents'

    $codex = New-Stop 'codex-session' 'codex-turn-1'
    $codex.agent_id = 'main-agent-id'
    Invoke-Hook 'codex' $codex $appData
    $codex.turn_id = 'codex-turn-2'
    Invoke-Hook 'codex' $codex $appData
    $notifications = @(Read-Notifications $appData)
    Assert-Hook (@($notifications | Where-Object { $_.source -eq 'codex' }).Count -eq 2) 'Codex main agent ID or a second turn was incorrectly filtered'
    Pass-Hook 'Codex main Stop without status is accepted and turn IDs distinguish replies'

    $before = @(Read-Notifications $appData).Count
    foreach ($assistant in @('claude', 'codex')) {
        foreach ($kind in @('StopFailure', 'Interrupt', 'SubagentStop', 'PostModelRequest')) {
            $ignored = New-Stop ('ignored-' + $assistant + $kind) 'ignored-turn'
            $ignored.hook_event_name = $kind
            Invoke-Hook $assistant $ignored $appData
        }
        foreach ($field in @('stop_hook_active', 'is_subagent', 'interrupted')) {
            $ignored = New-Stop ('ignored-' + $assistant + $field) 'ignored-turn'
            $ignored[$field] = $true
            Invoke-Hook $assistant $ignored $appData
        }
        foreach ($status in @('failed', 'cancelled', 'interrupted', 'error')) {
            $ignored = New-Stop ('ignored-' + $assistant + $status) 'ignored-turn'
            $ignored.status = $status
            Invoke-Hook $assistant $ignored $appData
        }
        $ignored = New-Stop ('ignored-' + $assistant + '-child') 'ignored-turn'
        $ignored.agent_type = 'subagent'
        Invoke-Hook $assistant $ignored $appData
        $ignored = New-Stop ('ignored-' + $assistant + '-error') 'ignored-turn'
        $ignored.error = 'request failed'
        Invoke-Hook $assistant $ignored $appData
    }
    $ignored = New-Stop 'claude-agent' 'ignored-turn'
    $ignored.agent_id = 'subagent-id'
    Invoke-Hook 'claude' $ignored $appData
    Assert-Hook (@(Read-Notifications $appData).Count -eq $before) 'Failure, interrupt, recursive stop or subagent emitted a completion'
    Pass-Hook 'Failures, interruption events, recursive stops and subagents stay quiet'

    foreach ($invalid in @('{', '{}', 'null', '[]', '')) { Invoke-Hook 'codex' $invalid $appData }
    Assert-Hook (@(Read-Notifications $appData).Count -eq $before) 'Malformed/missing JSON emitted a notification'
    $unwritable = Join-Path $fixtureRoot 'not-a-directory'
    [IO.File]::WriteAllText($unwritable, 'fixture')
    Invoke-Hook 'codex' (New-Stop 'failure-fixture' 'write-failed') $appData @('-InboxDirectory', $unwritable)
    Assert-Hook (@(Read-Notifications $appData).Count -eq $before) 'Failed write changed the normal inbox'
    Pass-Hook 'Malformed input and storage failures exit zero with exactly one empty JSON response'

    $parallelData = Join-Path $fixtureRoot 'parallel'
    $parallelPayload = (New-Stop 'parallel-session' 'same-turn') | ConvertTo-Json -Compress
    $processes = @()
    try {
        1..8 | ForEach-Object { $processes += Start-HookProcess $bridge @('-Source', 'codex') $parallelData }
        foreach ($process in $processes) { Write-HookInput $process $parallelPayload }
        foreach ($process in $processes) { Complete-HookProcess $process }
        $processes = @()
    }
    finally {
        foreach ($process in $processes) {
            try { if (-not $process.HasExited) { $process.Kill(); [void]$process.WaitForExit(5000) }; $process.Dispose() } catch { }
        }
    }
    Assert-Hook (@(Read-Notifications $parallelData).Count -eq 1) 'Parallel copies delivered duplicate notifications'
    Assert-Hook (@([IO.Directory]::GetFiles((Join-Path $parallelData 'DesktopPet\inbox'), '*.tmp')).Count -eq 0) 'Atomic notification temporary file leaked'
    Pass-Hook 'Eight parallel hook processes atomically publish one event'

    $recoveryData = Join-Path $fixtureRoot 'recovery'
    $recoveryState = Join-Path $recoveryData 'DesktopPet\inbox\.assistant-events'
    [void][IO.Directory]::CreateDirectory($recoveryState)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $recoveryHash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes("codex`nrecovery-session`nrecovery-turn`n")))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $recoveryClaim = Join-Path $recoveryState "codex-$recoveryHash.claim"
    [IO.File]::WriteAllText($recoveryClaim, '')
    Invoke-Hook 'codex' (New-Stop 'recovery-session' 'recovery-turn') $recoveryData
    Assert-Hook (@(Read-Notifications $recoveryData).Count -eq 1) 'A killed publisher empty claim suppressed the next notification'
    Assert-Hook (([IO.File]::ReadAllText($recoveryClaim)).Length -gt 0) 'Successful publication did not commit its claim'
    Invoke-Hook 'codex' (New-Stop 'recovery-session' 'recovery-turn') $recoveryData
    Assert-Hook (@(Read-Notifications $recoveryData).Count -eq 1) 'A committed recovery notification was published twice'
    Pass-Hook 'Empty claims recover after a killed publisher; committed claims still deduplicate'

    $encodedData = Join-Path $fixtureRoot 'encoded'
    $encodedFolder = Join-Path $fixtureRoot "한글 O'Brien & safe"
    [void][IO.Directory]::CreateDirectory($encodedFolder)
    $encodedBridge = Join-Path $encodedFolder 'assistant-stop-hook.ps1'
    [IO.File]::Copy($bridge, $encodedBridge)
    $literalBridge = "'" + $encodedBridge.Replace("'", "''") + "'"
    $encodedScript = "& $literalBridge -Source 'codex' -RegistrationId 'DesktopPet.StopHook.v1'"
    $process = Start-HookProcess $encodedBridge @() $encodedData $encodedScript
    Write-HookInput $process ((New-Stop 'encoded-session' 'encoded-turn') | ConvertTo-Json -Compress)
    Complete-HookProcess $process
    Assert-Hook (@(Read-Notifications $encodedData).Count -eq 1) 'Service-style EncodedCommand did not run the real bridge'
    $process = Start-HookProcess $encodedBridge @() $encodedData $encodedScript
    Write-HookInput $process '{'
    Complete-HookProcess $process
    Pass-Hook 'Actual EncodedCommand bridge stays quiet on success and failure, including Unicode and quoted paths'

    $legacyData = Join-Path $fixtureRoot 'legacy'
    $legacy = @{ type = 'agent-turn-complete'; 'thread-id' = 'legacy-thread'; 'turn-id' = 'legacy-turn'; cwd = 'C:\fixture\legacy-project'; 'last-assistant-message' = 'not persisted' } | ConvertTo-Json -Compress
    Invoke-Hook 'codex' '' $legacyData @($legacy)
    Assert-Hook (@(Read-Notifications $legacyData).Count -eq 1) 'Optional Codex legacy JSON argument failed'
    Invoke-Hook 'claude' (New-Stop 'wrapper-session' 'wrapper-turn') $legacyData @() $wrapper
    Assert-Hook (@(Read-Notifications $legacyData).Count -eq 2) 'Existing Claude script registration stopped working'
    Pass-Hook 'Optional Codex notify argument and existing Claude wrapper remain usable'

    $standalone = Join-Path $fixtureRoot 'standalone-hook.ps1'
    [IO.File]::Copy($bridge, $standalone)
    $standaloneProcess = Start-HookProcess $standalone @('-Source', 'codex') $legacyData
    Write-HookInput $standaloneProcess ((New-Stop 'standalone-session' 'standalone-turn') | ConvertTo-Json -Compress)
    Complete-HookProcess $standaloneProcess
    Assert-Hook (@(Read-Notifications $legacyData).Count -eq 3) 'Packaged hook depends on repository scripts'
    Pass-Hook 'Packaged bridge runs without the source repository'

    $manualData = Join-Path $fixtureRoot 'manual'
    foreach ($mood in @('shout', 'cry', 'gentle')) {
        $process = Start-HookProcess $notify @('-Message', '한글 수동 알림', '-Mood', $mood) $manualData
        $process.StandardInput.Close()
        Complete-HookProcess $process $false
    }
    $manual = @(Read-Notifications $manualData)
    Assert-Hook ($manual.Count -eq 3 -and @($manual.mood | Select-Object -Unique).Count -eq 3) 'Manual notification mood compatibility failed'
    foreach ($scriptPath in @($bridge, $wrapper, $notify)) {
        $bytes = [IO.File]::ReadAllBytes($scriptPath)
        Assert-Hook ($bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) 'PowerShell 5.1 script must keep UTF-8 BOM'
    }
    Pass-Hook 'Manual shout/cry/gentle and PowerShell 5.1 UTF-8 BOM are preserved'
    Write-Output "$script:passed/12 assistant hook regression groups passed"
}
finally {
    $workspace = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\') + '\'
    $target = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $target.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase) -or
        -not [IO.Path]::GetFileName($target).StartsWith('assistant-hooks-', [StringComparison]::Ordinal)) { throw 'Unsafe hook fixture cleanup path' }
    Remove-Item -LiteralPath $target -Recurse -Force
}
