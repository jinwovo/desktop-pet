<#
.SYNOPSIS
    Claude Code / Codex main Stop events become one quiet DesktopPet notification.
.NOTES
    Input is hook JSON on stdin. No transcript contents or assistant text are persisted.
    Hook errors never block the assistant. This script has no repository dependencies.
    RegistrationId identifies an installed registration; it deliberately does not affect deduplication.
#>
param(
    [string]$Source = 'claude',
    [string]$RegistrationId = '',
    [Parameter(Position = 0)]
    [string]$EventJson = '',
    [string]$InboxDirectory = ''
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Get-HookValue($Object, [string[]]$Names) {
    if ($null -eq $Object) { return $null }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties[$name]
        if ($null -ne $property -and $null -ne $property.Value) { return $property.Value }
    }
    return $null
}

function Get-HookText($Object, [string[]]$Names) {
    $value = Get-HookValue $Object $Names
    if ($value -is [string] -or $value -is [ValueType]) { return [string]$value }
    return ''
}

function Test-HookTrue($Value) {
    return ($Value -eq $true -or ($Value -is [string] -and $Value -ieq 'true'))
}

function Get-HookDigest([string]$Value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value)))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Send-AssistantCompletion($Hook, [string]$Assistant, [string]$Inbox) {
    if ($Assistant -notin @('claude', 'codex')) { return }
    $eventName = Get-HookText $Hook @('hook_event_name')
    $legacyCodex = $Assistant -eq 'codex' -and [string]::IsNullOrWhiteSpace($eventName) -and
        (Get-HookText $Hook @('type')) -eq 'agent-turn-complete'
    if ($eventName -ne 'Stop' -and -not $legacyCodex) { return }
    if (Test-HookTrue (Get-HookValue $Hook @('stop_hook_active'))) { return }
    if (Test-HookTrue (Get-HookValue $Hook @('is_subagent', 'is_sub_agent'))) { return }
    if (Test-HookTrue (Get-HookValue $Hook @('interrupted', 'cancelled', 'canceled'))) { return }
    if (-not [string]::IsNullOrWhiteSpace((Get-HookText $Hook @('parent_agent_id', 'parent_session_id', 'agent_transcript_path')))) { return }
    $agentType = Get-HookText $Hook @('agent_type', 'agent_role')
    if ($agentType -in @('subagent', 'sub_agent', 'worker')) { return }
    # Claude documents agent_id as a child/teammate identifier. A custom --agent main session
    # can have agent_type alone. Codex main Stop payloads may carry their own agent_id.
    if ($Assistant -eq 'claude' -and -not [string]::IsNullOrWhiteSpace((Get-HookText $Hook @('agent_id')))) { return }
    $status = Get-HookText $Hook @('status', 'result', 'outcome')
    if ($status -in @('failed', 'failure', 'error', 'cancelled', 'canceled', 'interrupted', 'aborted', 'timeout', 'timed_out', 'incomplete')) { return }
    $errorValue = Get-HookValue $Hook @('error')
    if ($null -ne $errorValue -and $errorValue -ne $false -and -not [string]::IsNullOrWhiteSpace([string]$errorValue)) { return }

    $session = Get-HookText $Hook @('session_id', 'thread-id', 'thread_id')
    $turn = Get-HookText $Hook @('turn_id', 'turn-id')
    $event = Get-HookText $Hook @('event_id', 'eventId')
    $cwd = Get-HookText $Hook @('cwd')
    if ([string]::IsNullOrWhiteSpace($session) -and [string]::IsNullOrWhiteSpace($turn) -and [string]::IsNullOrWhiteSpace($event)) { return }

    $strongIdentity = -not [string]::IsNullOrWhiteSpace($event) -or -not [string]::IsNullOrWhiteSpace($turn)
    $identity = "$Assistant`n$session`n$turn`n$event"
    if (-not $strongIdentity) {
        $lastMessage = Get-HookText $Hook @('last_assistant_message', 'last-assistant-message')
        if ([string]::IsNullOrWhiteSpace($lastMessage)) { return }
        $identity += "`n" + (Get-HookDigest $lastMessage)
        $transcript = Get-HookText $Hook @('transcript_path')
        if (-not [string]::IsNullOrWhiteSpace($transcript)) {
            try {
                # File metadata distinguishes successive equal replies without reading conversation data.
                $metadata = Get-Item -LiteralPath $transcript -ErrorAction Stop
                if (-not $metadata.PSIsContainer) {
                    $identity += "`n$($metadata.Length):$($metadata.LastWriteTimeUtc.Ticks)"
                    $strongIdentity = $true
                }
            }
            catch { }
        }
    }
    $digest = Get-HookDigest $identity
    $eventId = "$Assistant-$digest"
    $project = ''
    if (-not [string]::IsNullOrWhiteSpace($cwd)) {
        $project = [IO.Path]::GetFileName($cwd.TrimEnd([char[]]@('\', '/')))
    }
    if ([string]::IsNullOrWhiteSpace($project)) { $project = '프로젝트' }
    if ([string]::IsNullOrWhiteSpace($Inbox)) { $Inbox = Join-Path $env:LOCALAPPDATA 'DesktopPet\inbox' }
    $Inbox = [IO.Path]::GetFullPath($Inbox)
    [void][IO.Directory]::CreateDirectory($Inbox)
    $stateDirectory = Join-Path $Inbox '.assistant-events'
    [void][IO.Directory]::CreateDirectory($stateDirectory)
    # Retain only a short replay window, with no session IDs or conversation content on disk.
    $cutoff = [DateTime]::UtcNow.AddDays(-1)
    foreach ($old in [IO.Directory]::EnumerateFiles($stateDirectory, '*.claim')) {
        try { if ([IO.File]::GetLastWriteTimeUtc($old) -lt $cutoff) { [IO.File]::Delete($old) } } catch { }
    }
    $claim = Join-Path $stateDirectory "$eventId.claim"
    $claimStream = $null
    try { $claimStream = [IO.File]::Open($claim, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
    catch [IO.IOException] { return } # Another copy of the same hook is currently publishing this event.
    $temporary = $null
    try {
        # A nonempty timestamp is a committed publication. Empty/partial files left by a killed
        # process are retryable immediately; holding the stream serializes competing publishers.
        if ($claimStream.Length -gt 0 -and $claimStream.Length -le 128) {
            $saved = New-Object byte[] ([int]$claimStream.Length)
            [void]$claimStream.Read($saved, 0, $saved.Length)
            $committedAt = [DateTime]::MinValue
            $committed = [DateTime]::TryParseExact([Text.Encoding]::UTF8.GetString($saved), 'o',
                [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$committedAt)
            if ($committed -and ($strongIdentity -or $committedAt -gt [DateTime]::UtcNow.AddSeconds(-30))) { return }
        }
        $claimStream.SetLength(0)
        $claimStream.Position = 0
        $created = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        $label = if ($Assistant -eq 'claude') { 'Claude Code' } else { 'Codex' }
        $notification = [ordered]@{
            title = "$label 작업 완료"
            message = "[$project] 응답을 마쳤어요. 확인해주세요."
            mood = 'gentle'
            source = $Assistant
            eventId = $eventId
            createdAtUtc = $created
            projectFolder = $project
        } | ConvertTo-Json -Compress
        $fileId = [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss_fff', [Globalization.CultureInfo]::InvariantCulture) + '_' + [Guid]::NewGuid().ToString('N')
        $temporary = Join-Path $Inbox "$fileId.tmp"
        $destination = Join-Path $Inbox "$fileId.json"
        [IO.File]::WriteAllText($temporary, $notification, (New-Object Text.UTF8Encoding $false))
        [IO.File]::Move($temporary, $destination)
        # Commit only after the inbox file exists. A crash between publication and this stamp
        # may replay the same event ID; the app suppresses that duplicate without losing the event.
        $claimBytes = [Text.Encoding]::UTF8.GetBytes([DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture))
        $claimStream.Write($claimBytes, 0, $claimBytes.Length)
        $claimStream.Flush($true)
    }
    finally {
        $claimStream.Dispose()
        if ($temporary -and [IO.File]::Exists($temporary)) { try { [IO.File]::Delete($temporary) } catch { } }
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($EventJson) -and [Console]::IsInputRedirected) {
        # Hooks can run without an attached Windows console (desktop apps and CI services).
        # Console.InputEncoding/OutputEncoding setters call console APIs and can throw
        # ERROR_INVALID_HANDLE there even though redirected stdin/stdout pipes are valid.
        # Decode the pipe directly so UTF-8 does not depend on a console or the system locale.
        $reader = [IO.StreamReader]::new([Console]::OpenStandardInput(), [Text.UTF8Encoding]::new($false), $true)
        try { $EventJson = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    if (-not [string]::IsNullOrWhiteSpace($EventJson)) {
        $hook = $EventJson | ConvertFrom-Json -ErrorAction Stop
        Send-AssistantCompletion $hook $Source.ToLowerInvariant() $InboxDirectory
    }
}
catch {
    # A failed optional notification must never alter the assistant's stop decision or exit code.
}
finally { [Console]::Out.WriteLine('{}') }
exit 0
