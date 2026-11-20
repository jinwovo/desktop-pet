# All external commands are replaced in this process; no credentials or network are used.
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo ('.local/recovery-tests-' + [Guid]::NewGuid().ToString('N'))
$scripts = Join-Path $fixture 'scripts'
$artifacts = Join-Path $fixture 'release-files'
$temporary = Join-Path $fixture 'temporary'
foreach ($directory in @($scripts,$artifacts,$temporary,(Join-Path $fixture 'src/DesktopPet.App'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repo 'scripts/publish-verified-1.1.0.ps1') -Destination $scripts
[IO.File]::WriteAllText((Join-Path $fixture 'src/DesktopPet.App/DesktopPet.App.csproj'), '<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>')
$fakeExe = Join-Path $temporary 'DesktopPet.App.exe'
[IO.File]::WriteAllText($fakeExe, 'fixture executable: never launched')
$names = @('DesktopPet-1.1.0-win-x64.zip','DesktopPet-1.1.0-fan-art-packs.zip')
foreach ($name in $names) {
    $zip = Join-Path $artifacts $name
    Compress-Archive -LiteralPath $fakeExe -DestinationPath $zip
    [IO.File]::WriteAllText(($zip + '.sha256'), ((Get-FileHash $zip).Hash + '  ' + $name))
}

# Isolate the orchestration from helpers, whose real validation has its own regression suite.
@'
param([string]$Version)
function Assert-DesktopPetEvolutionReady {
    param($Version,$PlanPath,$RepositoryRoot)
    if ($Version -cne '1.1.0' -or $PlanPath -cne 'docs/evolution-2.0.json' -or $RepositoryRoot -ne $script:fixture) { throw 'Bad evolution helper arguments' }
    $script:readyCalls++
}
function Assert-DesktopPetEvolutionChecksums {
    param($Version,$Directory)
    if ($Version -cne '1.1.0' -or -not (Test-Path -LiteralPath $Directory -PathType Container)) { throw 'Bad checksum helper arguments' }
    $script:checksumCalls++
}
function Assert-DesktopPetEvolutionAssets {
    param($Version,$Release,$Assets)
    if ($Version -cne '1.1.0' -or $Release.id -ne 123 -or -not $Release.draft -or @($Assets).Count -ne 4) { throw 'Bad asset helper arguments' }
    $script:assetCalls++
}
'@ | Set-Content -LiteralPath (Join-Path $scripts 'publish-evolution-release.ps1') -Encoding UTF8
@'
param([string]$Version)
function Test-DesktopPetReleasePayload {
    param($Directory,$ExpectedVersion,$ExpectedDeployment)
    if ($ExpectedVersion -cne '1.1.0' -or $ExpectedDeployment -cne 'self-contained-single-file' -or -not (Test-Path -LiteralPath (Join-Path $Directory 'DesktopPet.App.exe'))) { throw 'Bad payload helper arguments' }
    $script:payloadCalls++
}
'@ | Set-Content -LiteralPath (Join-Path $scripts 'package-release.ps1') -Encoding UTF8

$script:api = 'https://api.github.com/repos/jinwovo/desktop-pet'
$script:commit = '4a69780dc24f0601599182603b6b1005d45831ac'
$script:assets = @(Get-ChildItem -LiteralPath $artifacts -File | ForEach-Object {
    [pscustomobject]@{ name=$_.Name; size=$_.Length; state='uploaded'; url=($script:api + '/releases/assets/' + $_.Name) }
})
function git {
    if ($args.Count -ne 4 -or $args[0] -cne '-C' -or $args[1] -ne $script:fixture -or $args[2] -cne 'rev-parse' -or $args[3] -cne 'v1.1.0^{commit}') { throw 'Unexpected git call' }
    $script:externalCalls++
    $global:LASTEXITCODE = 0
    if ($script:badTag) { return ('0' * 40) }
    return $script:commit
}
function gh {
    $script:externalCalls++
    if ($args.Count -ne 15 -or $args[0] -cne 'release' -or $args[1] -cne 'create' -or $args[2] -cne 'v1.1.0' -or $args[7] -cne '--draft' -or $args[8] -cne '--verify-tag' -or $args[9] -cne '--target' -or $args[10] -cne $script:commit) { throw 'Unexpected gh call' }
    foreach ($file in $args[3..6]) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Missing positional upload file' } }
    $script:createCalls++
    $script:exists = $true
    $global:LASTEXITCODE = 0
}
function powershell {
    $script:externalCalls++
    if ($args.Count -ne 5 -or $args[0] -cne '-NoProfile' -or $args[1] -cne '-File' -or $args[2] -ne (Join-Path $script:fixture 'tests/installation-check-regression.ps1') -or $args[3] -cne '-ApplicationPath' -or -not (Test-Path -LiteralPath $args[4])) { throw 'Unexpected installer command' }
    $script:installerCalls++
    $global:LASTEXITCODE = 0
}
function Invoke-RestMethod {
    param($Uri,$Headers,$Method,$Body,$ContentType,$OutFile)
    $script:externalCalls++
    if ($Uri -eq ($script:api + '/releases/tags/v1.1.0')) {
        if ($Method -ne 'GET' -or $Headers.ContainsKey('Authorization') -or $script:release.draft) { throw 'Bad anonymous verification' }
        $script:anonymousCalls++
        return $script:release
    }
    if ($Headers.Authorization -cne 'Bearer synthetic-test-token') { throw 'Missing synthetic authorization' }
    if ($Uri -eq ($script:api + '/actions/runs/44655275518') -and $Method -eq 'GET') {
        $conclusion = if ($script:badRun) { 'failure' } else { 'success' }
        return [pscustomobject]@{head_sha=$script:commit;status='completed';conclusion=$conclusion;path='.github/workflows/windows-release.yml'}
    }
    if ($Uri -eq ($script:api + '/actions/runs/44655275518/jobs?per_page=100') -and $Method -eq 'GET') {
        return [pscustomobject]@{jobs=@([pscustomobject]@{name='verify-and-package';conclusion='success'})}
    }
    if ($Uri -eq ($script:api + '/releases?per_page=100') -and $Method -eq 'GET') {
        if ($script:exists) { return ,@($script:release) }
        return ,@()
    }
    if ($Uri -eq $script:release.assets_url -and $Method -eq 'GET') { return ,$script:assets }
    if ($Method -eq 'GET' -and $OutFile) {
        $match = @($script:assets | Where-Object { $_.url -eq $Uri })
        if ($match.Count -ne 1 -or $Headers.Accept -cne 'application/octet-stream') { throw 'Unexpected asset download' }
        Copy-Item -LiteralPath (Join-Path $script:artifacts $match[0].name) -Destination $OutFile
        if ($script:corrupt -and $match[0].name -ceq 'DesktopPet-1.1.0-win-x64.zip') {
            $bytes = [IO.File]::ReadAllBytes($OutFile)
            $bytes[0] = $bytes[0] -bxor 255
            [IO.File]::WriteAllBytes($OutFile,$bytes)
        }
        return
    }
    if ($Uri -eq ($script:api + '/releases/123') -and $Method -eq 'PATCH') {
        $patch = $Body | ConvertFrom-Json
        if ($ContentType -cne 'application/json' -or $patch.draft -or $patch.prerelease -or $patch.make_latest -isnot [string] -or $patch.make_latest -cne 'true') { throw 'Bad publish PATCH' }
        $script:patchCalls++
        $script:release.draft = $false
        return $script:release
    }
    throw "Unexpected HTTP route: $Method $Uri"
}

$saved = @{}
foreach ($key in @('GITHUB_ACTIONS','GITHUB_REPOSITORY','GH_TOKEN','RUNNER_TEMP','GITHUB_STEP_SUMMARY')) { $saved[$key] = [Environment]::GetEnvironmentVariable($key,'Process') }
$script:externalCalls = 0
. (Join-Path $scripts 'publish-verified-1.1.0.ps1')
if ($script:externalCalls -ne 0) { throw 'Dot-sourcing invoked an external command' }
$passed = 0
$failed = 0
try {
    $env:GITHUB_ACTIONS = 'true'
    $env:GITHUB_REPOSITORY = 'jinwovo/desktop-pet'
    $env:GH_TOKEN = 'synthetic-test-token'
    $env:RUNNER_TEMP = $temporary
    $env:GITHUB_STEP_SUMMARY = Join-Path $temporary 'summary.md'
    foreach ($case in @('New','Draft','Public','BadRun','BadTag','Corrupt')) {
        $script:exists = $case -ne 'New'
        $script:badRun = $case -eq 'BadRun'
        $script:badTag = $case -eq 'BadTag'
        $script:corrupt = $case -eq 'Corrupt'
        $script:createCalls = $script:patchCalls = $script:anonymousCalls = 0
        $script:readyCalls = $script:checksumCalls = $script:assetCalls = $script:payloadCalls = $script:installerCalls = 0
        $script:release = [pscustomobject]@{ id=123L; tag_name='v1.1.0'; draft=($case -ne 'Public'); prerelease=$false; assets_url=($script:api + '/releases/123/assets'); assets=$script:assets; html_url='https://github.com/jinwovo/desktop-pet/releases/tag/v1.1.0' }
        $caught = ''
        try { Publish-DesktopPetOneOneVerified | Out-Null } catch { $caught = $_.Exception.Message }
        try {
            $expected = switch ($case) { 'BadRun' {'Run conclusion not success'} 'BadTag' {'Immutable tag commit mismatch'} 'Corrupt' {'SHA256 mismatch'} default {''} }
            if ($expected) {
                if ($caught -notmatch [Regex]::Escape($expected) -or $script:createCalls -ne 0 -or $script:patchCalls -ne 0) { throw "Expected guarded failure '$expected', got '$caught'" }
            } else {
                if ($caught) { throw $caught }
                $expectedCreate = if ($case -eq 'New') {1} else {0}
                $expectedPatch = if ($case -eq 'Public') {0} else {1}
                if ($script:createCalls -ne $expectedCreate -or $script:patchCalls -ne $expectedPatch -or $script:anonymousCalls -ne 1 -or $script:readyCalls -ne 1 -or $script:checksumCalls -ne 2 -or $script:payloadCalls -ne 1 -or $script:installerCalls -ne 1) { throw 'Incorrect publication/helper call counts' }
            }
            Write-Host "PASS $case"
            $passed++
        } catch { Write-Host "FAIL ${case}: $_"; $failed++ }
    }
} finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key],'Process') }
}
Write-Host "SUMMARY: $passed passed, $failed failed"
if ($failed) { exit 1 }
