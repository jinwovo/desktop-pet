param([Parameter(Mandatory=$true)][string]$ApplicationPath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\Install-DesktopPetPayload.ps1')
$application=Get-DesktopPetInstallPath $ApplicationPath
if (-not [IO.File]::Exists($application)) { throw 'Application executable is missing.' }
$payload=[IO.Path]::GetDirectoryName($application)
$fixture=Join-Path $repoRoot ('.local\installation-check-regression-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$profileRoots=@{APPDATA=(Join-Path $fixture 'profile-roaming');LOCALAPPDATA=(Join-Path $fixture 'profile-local');USERPROFILE=(Join-Path $fixture 'profile-home');CLAUDE_CONFIG_DIR=(Join-Path $fixture 'profile-claude');CODEX_HOME=(Join-Path $fixture 'profile-codex')}
$previous=@{}
$overrides=$profileRoots.Clone()
$overrides['DOTNET_BUNDLE_EXTRACT_BASE_DIR']=Join-Path $fixture 'runtime-bundle-cache'
function Assert-Check([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Invoke-ReadOnlyCheck([string]$Exe,[bool]$ExpectedValid) {
    $id=[Guid]::NewGuid().ToString('N'); $stdout=Join-Path $fixture ($id+'.stdout.json'); $stderr=Join-Path $fixture ($id+'.stderr.txt')
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=$Exe; $start.Arguments='--check-install'; $start.UseShellExecute=$false
    $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    $stdoutTask=$process.StandardOutput.ReadToEndAsync(); $stderrTask=$process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Owned installation-check child timed out.' }
    $exitCode=$process.ExitCode
    [IO.File]::WriteAllText($stdout,$stdoutTask.GetAwaiter().GetResult())
    [IO.File]::WriteAllText($stderr,$stderrTask.GetAwaiter().GetResult())
    $process.Dispose()
    $text=[IO.File]::ReadAllText($stdout); $report=$text | ConvertFrom-Json
    $expectedExit=if ($ExpectedValid) {0} else {2}
    Assert-Check ($exitCode -eq $expectedExit -and [bool]$report.ResourcesValid -eq $ExpectedValid) 'Installation-check exit/report mismatch.'
    Assert-Check (-not $text.Contains($fixture)) 'Installation check exposed fixture paths.'
    foreach ($profile in $profileRoots.Values) { Assert-Check (-not (Test-Path -LiteralPath $profile)) 'Read-only check created profile or hook data.' }
    return $report
}
try {
    foreach ($name in $overrides.Keys) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process'); [Environment]::SetEnvironmentVariable($name,$overrides[$name],'Process') }
    $valid=Invoke-ReadOnlyCheck $application $true
    Assert-Check ($valid.ValidatedCharacters -eq $valid.CharacterCount -and $valid.HasOriginalCollection -and $valid.HasHookBridge) 'Complete resource validation was skipped.'
    Write-Output 'PASS Actual executable validates resources without creating isolated profile or hook files'
    $broken=Join-Path $fixture 'missing-assets'; [void][IO.Directory]::CreateDirectory($broken)
    foreach ($file in Get-ChildItem -LiteralPath $payload -File) {
        Assert-DesktopPetNoReparsePoint $file.FullName
        [IO.File]::Copy($file.FullName,(Join-Path $broken $file.Name),$false)
    }
    $invalid=Invoke-ReadOnlyCheck (Join-Path $broken 'DesktopPet.App.exe') $false
    Assert-Check ($invalid.CharacterCount -eq 0 -and @($invalid.Errors).Count -gt 0) 'Missing resources were accepted.'
    Write-Output 'PASS Actual executable returns exit 2 and sanitized JSON for missing resources without normal startup'
    . (Join-Path $repoRoot 'scripts\package-release.ps1')
    . (Join-Path $repoRoot 'scripts\uninstall.ps1')
    $null=Test-DesktopPetReleasePayload -Directory $payload
    $installed=Join-Path $fixture 'installed'
    Install-DesktopPetPayload -PublishDirectory $payload -InstallDirectory $installed
    $canaries=@('inbox\pending.json','Characters\MyPrivatePet\character.json','user-note.txt')
    foreach ($relative in $canaries) {
        $path=Join-Path $installed $relative; [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)); [IO.File]::WriteAllText($path,'USER_DATA_CANARY')
    }
    Install-DesktopPetPayload -PublishDirectory $payload -InstallDirectory $installed
    $null=Invoke-ReadOnlyCheck (Join-Path $installed 'DesktopPet.App.exe') $true
    foreach ($relative in $canaries) { Assert-Check ([IO.File]::ReadAllText((Join-Path $installed $relative)) -eq 'USER_DATA_CANARY') 'Reinstall changed user data.' }
    Write-Output 'PASS Actual payload install/reinstall remain runnable and preserve user data'
    $script:disconnectCalls=0
    function Start-Process {
        param([string]$FilePath,[string]$ArgumentList,[string]$WindowStyle,[switch]$PassThru)
        Assert-Check ($FilePath -eq (Join-Path $installed 'DesktopPet.App.exe') -and $ArgumentList -eq '--disconnect-assistants' -and $WindowStyle -eq 'Hidden') 'Removal attempted an unexpected process.'
        $script:disconnectCalls++; $stub=[pscustomobject]@{ExitCode=0}
        $stub | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {param([int]$Timeout) return $true}
        return $stub
    }
    Remove-DesktopPetInstalledPayload -Directory $installed
    Assert-Check ($script:disconnectCalls -eq 1 -and -not [IO.File]::Exists((Join-Path $installed 'DesktopPet.App.exe'))) 'Owned removal failed.'
    foreach ($relative in $canaries) { Assert-Check ([IO.File]::ReadAllText((Join-Path $installed $relative)) -eq 'USER_DATA_CANARY') 'Removal changed user data.' }
    Write-Output 'PASS Actual managed removal preserves user data (disconnect child stubbed; no real assistant config touched)'
    Write-Output '4/4 actual installation-check and payload lifecycle groups passed'
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
    $safe=[IO.Path]::GetFullPath($fixture); $owned=[IO.Path]::GetFullPath((Join-Path $repoRoot '.local'))
    if (-not $safe.StartsWith($owned+'\',[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($safe).StartsWith('installation-check-regression-')) { throw 'Unsafe fixture cleanup target.' }
    Assert-DesktopPetNoReparsePoint $safe
    Remove-Item -LiteralPath $safe -Recurse -Force
}
