# Real notification command, isolated workspace inbox. Never writes to the user's live inbox.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$inbox = Join-Path $repoRoot ('.local\notify-command-regression-' + [Guid]::NewGuid().ToString('N'))
$command = Join-Path $repoRoot 'scripts\notify-pet.ps1'
& $command -Message 'Hello from an isolated integration fixture' -InboxDirectory $inbox
$files = @(Get-ChildItem -LiteralPath $inbox -Filter '*.json')
if ($files.Count -ne 1) { throw 'Expected exactly one atomic notification.' }
$payload = [IO.File]::ReadAllText($files[0].FullName) | ConvertFrom-Json
if ($payload.mood -ne 'gentle' -or $payload.message -ne 'Hello from an isolated integration fixture') { throw 'Default notification is not gentle or lost its content.' }
Write-Output 'PASS Notification command defaults to a gentle message in the explicitly selected inbox'
& $command -Message 'Second message' -Title 'Fixture title' -Mood cry -InboxDirectory $inbox
$files = @(Get-ChildItem -LiteralPath $inbox -Filter '*.json')
if ($files.Count -ne 2) { throw 'Concurrent-looking notification IDs collided.' }
$second = @($files | ForEach-Object { [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json }) | Where-Object { $_.message -eq 'Second message' }
if ($second.title -ne 'Fixture title' -or $second.mood -ne 'cry') { throw 'Explicit notification parameters were ignored.' }
Write-Output 'PASS Explicit title and mood are retained in a uniquely named second notification'
if (@(Get-ChildItem -LiteralPath $inbox -Filter '*.tmp').Count -ne 0) { throw 'Partially written inbox files remain after publication.' }
Write-Output 'PASS Completed publications leave only complete JSON notifications'
Write-Output '3/3 notification command regression checks passed'
