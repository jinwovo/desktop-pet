# Synthetic ZIP/manifest/uninstall tests. No real installation, assistant config, registry or process changes.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\package-release.ps1')
. (Join-Path $repoRoot 'scripts\uninstall.ps1')

function Assert-PackageTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}
function Assert-PackageFailure {
    param([scriptblock]$Action)
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-PackageTest $failed 'Expected invalid package/deployment to fail.'
}
function Write-PackageFile {
    param([string]$Root, [string]$RelativePath, [string]$Content)
    $path = Join-Path $Root $RelativePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $Content)
}

$testRoot = Join-Path (Join-Path $repoRoot '.local') ('package-regression-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$passed = 0
try {
    $publish = Join-Path $testRoot 'publish'
    [System.IO.Directory]::CreateDirectory($publish) | Out-Null
    $fixtureType = 'PackageFixture' + [Guid]::NewGuid().ToString('N')
    $fixtureSource = @"
using System.Reflection;
[assembly: AssemblyVersion("0.4.0.0")]
[assembly: AssemblyFileVersion("0.4.0.0")]
public static class $fixtureType { public static void Main() { } }
"@
    Add-Type -TypeDefinition $fixtureSource -OutputAssembly (Join-Path $publish 'DesktopPet.App.exe') -OutputType ConsoleApplication
    Write-PackageFile $publish 'Assets\Characters\Companion\character.json' '{"name":"Companion","frames":{"idle":["idle.png"],"walk":["walk.png"]}}'
    Write-PackageFile $publish 'Assets\Characters\Companion\idle.png' 'synthetic image placeholder'
    Write-PackageFile $publish 'Assets\Characters\Companion\walk.png' 'synthetic walk placeholder'
    Write-PackageFile $publish 'Hooks\assistant-stop-hook.ps1' '# synthetic bridge'
    $releases = Join-Path $testRoot 'releases'
    $package = New-DesktopPetReleasePackage -PublishDirectory $publish -DestinationDirectory $releases -ReleaseVersion '0.4.0'
    Assert-PackageTest ([System.IO.File]::Exists($package.Zip) -and $package.Deployment -eq 'self-contained-single-file') 'Default package was not labelled self-contained.'
    $hash = (Get-FileHash -LiteralPath $package.Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-PackageTest ($hash -eq $package.SHA256 -and [System.IO.File]::ReadAllText($package.ChecksumFile).StartsWith($hash + '  ')) 'ZIP checksum differs.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $extracted = Join-Path $testRoot 'extracted'
    [System.IO.Compression.ZipFile]::ExtractToDirectory($package.Zip, $extracted)
    $manifest = Test-DesktopPetReleasePayload -Directory $extracted -ExpectedVersion '0.4.0' -ExpectedDeployment 'self-contained-single-file'
    Assert-PackageTest (-not $manifest.requiresDotNetRuntime) 'Default runtime label is wrong.'
    foreach ($script in 'install.ps1','uninstall.ps1','package-release.ps1','Install-DesktopPetPayload.ps1') {
        Assert-PackageTest ([System.IO.File]::Exists((Join-Path $extracted $script))) 'Release is missing installation tooling.'
    }
    Write-Host 'PASS Default ZIP metadata, version, bridge, tooling, file hashes and checksum sidecar'
    $passed++

    $unusedInstall = Join-Path $testRoot 'unused-verify-install'
    $installer = Join-Path $extracted 'install.ps1'
    $verified = & $installer -VerifyOnly -InstallDirectory $unusedInstall -SkipStartMenuShortcut
    Assert-PackageTest ($verified.version -eq '0.4.0' -and -not [System.IO.Directory]::Exists($unusedInstall)) 'Verification did not report package metadata or wrote to the install folder.'
    $originalImage = Join-Path $extracted 'Assets\Characters\Companion\idle.png'
    $originalBytes = [System.IO.File]::ReadAllBytes($originalImage)
    try {
        [System.IO.File]::AppendAllText($originalImage, 'corrupted')
        Assert-PackageFailure { & $installer -VerifyOnly -InstallDirectory $unusedInstall }
        Assert-PackageTest (-not [System.IO.Directory]::Exists($unusedInstall)) 'Invalid package verification created the installation folder.'
    }
    finally { [System.IO.File]::WriteAllBytes($originalImage, $originalBytes) }
    Assert-PackageFailure { & (Join-Path $repoRoot 'scripts\install.ps1') -VerifyOnly }
    Write-Host 'PASS Extracted installer verifies manifest and hashes in read-only mode, and rejects modified payloads and missing packages'
    $passed++

    $stageInstall = Join-Path $testRoot 'validated-install'
    & $installer -InstallDirectory $stageInstall -SkipStartMenuShortcut
    $activeExe = Join-Path $stageInstall 'DesktopPet.App.exe'
    $activeBefore = [System.IO.File]::ReadAllBytes($activeExe)
    $installManifest = Join-Path $stageInstall '.desktop-pet-manifest.json'
    $managedBefore = [System.IO.File]::ReadAllText($installManifest)
    Assert-PackageFailure {
        Install-DesktopPetPayload -PublishDirectory $extracted -InstallDirectory $stageInstall -ValidateStagedPayload {
            param($stagedPackage)
            [System.IO.File]::AppendAllText((Join-Path $stagedPackage 'Assets\Characters\Companion\walk.png'), 'mismatch')
            Test-DesktopPetReleasePayload -Directory $stagedPackage
        }
    }
    Assert-PackageTest ([Convert]::ToBase64String([System.IO.File]::ReadAllBytes($activeExe)) -eq [Convert]::ToBase64String($activeBefore) -and
        [System.IO.File]::ReadAllText($installManifest) -eq $managedBefore -and
        @(Get-ChildItem -LiteralPath $stageInstall -Force -Filter '.desktop-pet-install-*').Count -eq 0) 'Failed staging validation changed the active install or leaked its transaction.'
    Write-Host 'PASS The user installer validates staged bytes before installing and rejects corrupted copies before changing active files'
    $passed++

    $archiveInstall = Join-Path $testRoot 'archive-install'
    $archiveCheck = & $installer -PackageArchive $package.Zip -VerifyOnly -InstallDirectory $archiveInstall -SkipStartMenuShortcut
    Assert-PackageTest ($archiveCheck.version -eq '0.4.0' -and -not [System.IO.Directory]::Exists($archiveInstall)) 'Direct archive verification changed the installation.'
    $oldSidecar = [System.IO.File]::ReadAllText($package.ChecksumFile)
    try {
        $unrelatedSidecar = ([string]('0' * 64)) + '  ' + [System.IO.Path]::GetFileName($package.Zip)
        [System.IO.File]::WriteAllText($package.ChecksumFile, $unrelatedSidecar)
        Assert-PackageFailure { & $installer -PackageArchive $package.Zip -InstallDirectory $archiveInstall -SkipStartMenuShortcut }
        Assert-PackageTest (-not [System.IO.Directory]::Exists($archiveInstall)) 'Checksum rejection wrote an install folder.'
    }
    finally { [System.IO.File]::WriteAllText($package.ChecksumFile, $oldSidecar) }
    & $installer -PackageArchive $package.Zip -InstallDirectory $archiveInstall -SkipStartMenuShortcut
    Assert-PackageTest ([System.IO.File]::Exists((Join-Path $archiveInstall 'DesktopPet.App.exe'))) 'Direct archive installation did not publish its executable.'
    Write-Host 'PASS Install and VerifyOnly accept a matching ZIP sidecar and reject a mismatched archive before publication'
    $passed++

    $rigPublish = Join-Path $testRoot 'rig-publish'
    Copy-Item -LiteralPath $publish -Destination $rigPublish -Recurse
    $rigDefinition = [ordered]@{
        name = 'Companion'; frames = @{ idle = @('idle.png'); walk = @('walk.png') }
        walkRig = @{
            version = 1; footLiftPx = 4
            side = @{ body = 'rig_side_body.png'; nearFoot = 'rig_side_near.png'; farFoot = 'rig_side_far.png' }
            turn = @{ body = 'rig_turn_body.png'; nearFoot = 'rig_turn_near.png'; farFoot = 'rig_turn_far.png' }
        }
    }
    $rigJson = $rigDefinition | ConvertTo-Json -Depth 6
    Write-PackageFile $rigPublish 'Assets\Characters\Companion\character.json' $rigJson
    foreach ($view in 'side','turn') {
        foreach ($part in 'body','nearFoot','farFoot') {
            $relative = 'Assets\Characters\Companion\' + $rigDefinition.walkRig[$view][$part]
            Write-PackageFile $rigPublish $relative 'synthetic rig PNG placeholder'
        }
    }
    $rigPackage = New-DesktopPetReleasePackage -PublishDirectory $rigPublish -DestinationDirectory (Join-Path $testRoot 'rig-releases') -ReleaseVersion '0.4.0'
    Assert-PackageTest ([System.IO.File]::Exists($rigPackage.Zip)) 'Complete rig deployment was rejected.'
    foreach ($view in 'side','turn') {
        foreach ($part in 'body','nearFoot','farFoot') {
            $relative = 'Assets\Characters\Companion\' + $rigDefinition.walkRig[$view][$part]
            $partPath = Assert-DesktopPetChildPath -Root $rigPublish -Path (Join-Path $rigPublish $relative)
            [System.IO.File]::Delete($partPath)
            Assert-PackageFailure { Assert-DesktopPetPublicAssets $rigPublish }
            Write-PackageFile $rigPublish $relative 'synthetic rig PNG placeholder'
        }
    }
    Write-Host 'PASS Complete live rig can be packaged and each of its six missing PNG paths is rejected'
    $passed++

    Write-PackageFile $rigPublish 'Assets\Characters\Companion\notes.txt' 'existing non-PNG must not be accepted as a rig texture'
    foreach ($invalidPath in @('..\outside.png', (Join-Path $testRoot 'outside.png'), 'rig_side_body.png:stream', 'notes.txt', 42, $null)) {
        $invalidRig = $rigJson | ConvertFrom-Json
        $invalidRig.walkRig.side.body = $invalidPath
        Write-PackageFile $rigPublish 'Assets\Characters\Companion\character.json' ($invalidRig | ConvertTo-Json -Depth 6)
        Assert-PackageFailure { Assert-DesktopPetPublicAssets $rigPublish }
    }
    foreach ($invalidShape in @('{"side":null,"turn":{}}', '{"side":{},"turn":null}', 'null')) {
        $invalidRig = $rigJson | ConvertFrom-Json
        $invalidRig.walkRig = $invalidShape | ConvertFrom-Json
        Write-PackageFile $rigPublish 'Assets\Characters\Companion\character.json' ($invalidRig | ConvertTo-Json -Depth 6)
        Assert-PackageFailure { Assert-DesktopPetPublicAssets $rigPublish }
    }
    Write-PackageFile $rigPublish 'Assets\Characters\Companion\character.json' $rigJson
    Assert-DesktopPetPublicAssets $publish # Legacy metadata without walkRig remains supported.
    Write-Host 'PASS Rig paths retain relative-path, PNG-type and required-view validation with legacy compatibility'
    $passed++

    $collection = Join-Path $testRoot 'public-collection'
    [IO.Directory]::CreateDirectory($collection) | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $collection -Recurse
    Write-PackageFile $collection 'LICENSE' 'MIT fixture'
    Assert-PackageFailure { Assert-DesktopPetPublicAssets $collection -RequireFullCollection }
    foreach ($name in 'Nimbus','Ember') {
        Write-PackageFile $collection ("Assets\Characters\$name\character.json") ('{"name":"' + $name + '","frames":{"idle":["idle.png"]}}')
        Write-PackageFile $collection ("Assets\Characters\$name\idle.png") 'synthetic public PNG'
    }
    Assert-DesktopPetPublicAssets $collection -RequireFullCollection
    $nimbus = $rigJson | ConvertFrom-Json
    $nimbus.name = 'Nimbus'
    Write-PackageFile $collection 'Assets\Characters\Nimbus\character.json' ($nimbus | ConvertTo-Json -Depth 6)
    Assert-PackageFailure { Assert-DesktopPetPublicAssets $collection -RequireFullCollection }
    Write-Host 'PASS New public collection requires all three characters, a license and each character rig resource'
    $passed++

    $canonicalManifestPath = Join-Path $extracted 'DesktopPet.release.json'
    $canonicalOriginal = [System.IO.File]::ReadAllText($canonicalManifestPath)
    $aliasManifest = $canonicalOriginal | ConvertFrom-Json
    $aliasEntry = $aliasManifest.files | Where-Object { $_.path.Contains('\') } | Select-Object -First 1
    $aliasCopy = ($aliasEntry | ConvertTo-Json) | ConvertFrom-Json
    $aliasCopy.path = $aliasCopy.path.Replace('\', '/')
    $aliasManifest.files = @($aliasManifest.files) + @($aliasCopy)
    Write-PackageFile $extracted 'untracked-by-alias.bin' 'an alias must not hide this extra file'
    [System.IO.File]::WriteAllText($canonicalManifestPath, ($aliasManifest | ConvertTo-Json -Depth 8))
    Assert-PackageFailure { Test-DesktopPetReleasePayload -Directory $extracted }
    [System.IO.File]::Delete((Assert-DesktopPetChildPath -Root $extracted -Path (Join-Path $extracted 'untracked-by-alias.bin')))
    $normalizedManifest = $canonicalOriginal | ConvertFrom-Json
    foreach ($entry in $normalizedManifest.files) { $entry.path = $entry.path.Replace('\', '/').ToUpperInvariant() }
    [System.IO.File]::WriteAllText($canonicalManifestPath, ($normalizedManifest | ConvertTo-Json -Depth 8))
    Test-DesktopPetReleasePayload -Directory $extracted | Out-Null
    [System.IO.File]::WriteAllText($canonicalManifestPath, $canonicalOriginal)
    Write-Host 'PASS Canonical slash/case aliases cannot pad manifest counts to hide an untracked file'
    $passed++

    $frameworkPackage = New-DesktopPetReleasePackage -PublishDirectory $publish -DestinationDirectory $releases -ReleaseVersion '0.4.0' -RequiresRuntime
    Assert-PackageTest ($frameworkPackage.Zip.EndsWith('-framework-dependent.zip')) 'Framework-dependent ZIP is not clearly labelled.'
    $frameworkExtracted = Join-Path $testRoot 'framework-extracted'
    [System.IO.Compression.ZipFile]::ExtractToDirectory($frameworkPackage.Zip, $frameworkExtracted)
    $frameworkManifest = Test-DesktopPetReleasePayload -Directory $frameworkExtracted -ExpectedDeployment 'framework-dependent-single-file'
    Assert-PackageTest ($frameworkManifest.requiresDotNetRuntime) 'Framework-dependent runtime requirement is missing.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $frameworkExtracted 'README.txt')).Contains('.NET 10 Desktop Runtime x64')) 'README omits Desktop Runtime requirement.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $frameworkExtracted 'README.txt')).Contains('Codex는 /hooks')) 'README omits Codex trust review.'
    Write-Host 'PASS Explicit framework-dependent label and runtime instructions'
    $passed++

    [System.IO.File]::AppendAllText((Join-Path $frameworkExtracted 'Assets\Characters\Companion\idle.png'), 'tampered')
    Assert-PackageFailure { Test-DesktopPetReleasePayload -Directory $frameworkExtracted }
    Write-Host 'PASS Modified payload is rejected by manifest checksums'
    $passed++

    Write-PackageFile $publish 'Assets\Characters\PrivateCharacter\private.png' 'must never ship'
    Assert-PackageFailure { New-DesktopPetReleasePackage -PublishDirectory $publish -DestinationDirectory $releases -ReleaseVersion '0.4.1' }
    $privatePath = Assert-DesktopPetChildPath -Root $publish -Path (Join-Path $publish 'Assets\Characters\PrivateCharacter\private.png')
    [System.IO.File]::Delete($privatePath)
    [System.IO.Directory]::Delete([System.IO.Path]::GetDirectoryName($privatePath), $false)
    Write-Host 'PASS Personal development characters cannot enter a public archive'
    $passed++

    $reservedUserPaths = @(
        'Characters\Personal\character.json',
        'settings.json',
        'settings.json.bak',
        '.settings.json.fixture.tmp',
        'assistant-connections.json',
        'assistant-connections.json.fixture.bak',
        'assistant-connections.json.fixture.tmp',
        'assistant-connections.json.fixture.bak.conflict'
    )
    $reservedIndex = 0
    foreach ($relative in $reservedUserPaths) {
        $reservedPublish = Join-Path $testRoot ('reserved-publish-' + $reservedIndex)
        Copy-Item -LiteralPath $publish -Destination $reservedPublish -Recurse
        Write-PackageFile $reservedPublish $relative.ToUpperInvariant().Replace('\', '/') 'private user data must never ship'
        $reservedOutput = Join-Path $testRoot ('reserved-releases-' + $reservedIndex++)
        Assert-PackageFailure { New-DesktopPetReleasePackage -PublishDirectory $reservedPublish -DestinationDirectory $reservedOutput -ReleaseVersion '0.4.0' }
        Assert-PackageTest (-not [System.IO.File]::Exists((Join-Path $reservedOutput 'DesktopPet-0.4.0-win-x64.zip'))) 'A release archive included a user character or assistant journal.'
    }
    Write-Host 'PASS Public archives reject root custom Characters and assistant journals or recovery files'
    $passed++

    Assert-PackageFailure { New-DesktopPetReleasePackage -PublishDirectory $publish -DestinationDirectory $releases -ReleaseVersion '0.5.0' }
    Assert-PackageFailure { Assert-DesktopPetReleaseVersion '../outside' }
    Assert-PackageFailure { New-DesktopPetReleasePackage -PublishDirectory $publish -DestinationDirectory $publish -ReleaseVersion '0.4.0' }
    Write-Host 'PASS Wrong executable version, unsafe version and overlapping roots are rejected'
    $passed++

    $manifestPath = Join-Path $extracted 'DesktopPet.release.json'
    $originalManifest = [System.IO.File]::ReadAllText($manifestPath)
    $badManifest = $originalManifest | ConvertFrom-Json
    $badManifest.files[0].path = '..\outside.txt'
    [System.IO.File]::WriteAllText($manifestPath, ($badManifest | ConvertTo-Json -Depth 8))
    Assert-PackageFailure { Test-DesktopPetReleasePayload -Directory $extracted }
    [System.IO.File]::WriteAllText($manifestPath, $originalManifest)
    Write-PackageFile $extracted 'untracked.txt' 'must not be ignored'
    Assert-PackageFailure { Test-DesktopPetReleasePayload -Directory $extracted }
    Write-Host 'PASS Manifest traversal and untracked payload files are rejected'
    $passed++

    $recovering = Join-Path $testRoot 'interrupted-update'
    $pending = '.desktop-pet-install-' + [Guid]::NewGuid().ToString('N')
    Write-PackageFile $recovering 'DesktopPet.App.exe' 'installed application'
    Write-PackageFile $recovering '.desktop-pet-manifest.json' '{"version":1,"files":["DesktopPet.App.exe"]}'
    Write-PackageFile $recovering ($pending + '\backup\DesktopPet.App.exe') 'previous release'
    Assert-PackageFailure { Install-DesktopPetPayload -PublishDirectory $extracted -InstallDirectory $recovering }
    Assert-PackageFailure { Remove-DesktopPetInstalledPayload -Directory $recovering }
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $recovering 'DesktopPet.App.exe')) -eq 'installed application' -and
        [System.IO.File]::ReadAllText((Join-Path $recovering ($pending + '\backup\DesktopPet.App.exe'))) -eq 'previous release') 'A retry changed the installed or recovery payload.'
    Write-Host 'PASS Install and uninstall preserve retained installer recovery directories'
    $passed++

    $dryRunRoot = Join-Path $testRoot 'dry-run'
    Write-PackageFile $dryRunRoot 'DesktopPet.App.exe' 'managed application'
    Write-PackageFile $dryRunRoot 'inbox\pending.json' 'unread'
    Write-PackageFile $dryRunRoot '.desktop-pet-manifest.json' '{"version":1,"files":["DesktopPet.App.exe"]}'
    Write-PackageFile $dryRunRoot 'DesktopPet.release.json' 'This fixture must never be parsed or launched'
    & (Join-Path $repoRoot 'scripts\uninstall.ps1') -InstallDirectory $dryRunRoot -WhatIf
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $dryRunRoot 'DesktopPet.App.exe')) -eq 'managed application' -and
        [System.IO.File]::Exists((Join-Path $dryRunRoot '.desktop-pet-manifest.json')) -and
        [System.IO.File]::ReadAllText((Join-Path $dryRunRoot 'inbox\pending.json')) -eq 'unread') 'Uninstall preview modified the deployment.'
    Write-Host 'PASS Uninstall -WhatIf leaves the payload and unread data intact and skips assistant disconnect'
    $passed++

    $installed = Join-Path $testRoot 'installed'
    Write-PackageFile $installed 'inbox\pending.json' '{"message":"preserve"}'
    Write-PackageFile $installed 'Assets\Characters\MyPet\custom.txt' 'preserve custom'
    Write-PackageFile $installed 'Characters\Personal\character.json' 'preserve imported character'
    Write-PackageFile $installed 'settings.json.bak' 'preserve preferences recovery'
    Write-PackageFile $installed 'assistant-connections.json' 'preserve assistant journal'
    Write-PackageFile $installed 'assistant-connections.json.fixture.bak' 'preserve assistant recovery'
    Write-PackageFile $installed 'user-note.txt' 'preserve unrelated'
    # The fixture publish has no release CLI capability marker, so uninstall starts no executable.
    Install-DesktopPetPayload -PublishDirectory $publish -InstallDirectory $installed
    Remove-DesktopPetInstalledPayload -Directory $installed
    Assert-PackageTest (-not [System.IO.File]::Exists((Join-Path $installed 'DesktopPet.App.exe'))) 'Managed executable remains.'
    Assert-PackageTest ([System.IO.File]::Exists((Join-Path $installed 'inbox\pending.json'))) 'Uninstall deleted pending notification.'
    Assert-PackageTest ([System.IO.File]::Exists((Join-Path $installed 'Assets\Characters\MyPet\custom.txt'))) 'Uninstall deleted custom character.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed 'Characters\Personal\character.json')) -eq 'preserve imported character') 'Uninstall deleted imported user character.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed 'settings.json.bak')) -eq 'preserve preferences recovery') 'Uninstall deleted settings recovery.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed 'assistant-connections.json')) -eq 'preserve assistant journal') 'Uninstall deleted the assistant journal.'
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed 'assistant-connections.json.fixture.bak')) -eq 'preserve assistant recovery') 'Uninstall deleted assistant journal recovery.'
    Assert-PackageTest ([System.IO.File]::Exists((Join-Path $installed 'user-note.txt'))) 'Uninstall deleted unmanaged file.'
    Write-Host 'PASS Manifest-only uninstall preserves notifications and user content without running processes'
    $passed++

    foreach ($relative in $reservedUserPaths) {
        Write-PackageFile $installed $relative ('preserve ' + $relative)
        Write-PackageFile $installed 'DesktopPet.App.exe' 'managed app must survive rejected removal'
        $reservedManifest = [ordered]@{ version = 1; files = @('DesktopPet.App.exe', $relative.ToUpperInvariant().Replace('\', '/')) } | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText((Join-Path $installed '.desktop-pet-manifest.json'), $reservedManifest)
        Assert-PackageFailure { Remove-DesktopPetInstalledPayload -Directory $installed }
        Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed $relative)) -eq ('preserve ' + $relative)) 'A forged install manifest let uninstall remove custom characters or assistant history.'
        Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed 'DesktopPet.App.exe')) -eq 'managed app must survive rejected removal') 'Uninstall changed app before rejecting a user-data claim.'
        Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $installed '.desktop-pet-manifest.json')) -eq $reservedManifest) 'Rejected user-data manifest was removed or rewritten.'
    }
    Write-Host 'PASS Uninstall rejects forged manifests claiming custom Characters or settings, assistant journals and recovery files'
    $passed++

    Write-PackageFile $installed '.desktop-pet-manifest.json' '{"version":1,"files":["..\\outside.txt"]}'
    Write-PackageFile $testRoot 'outside.txt' 'must remain'
    Assert-PackageFailure { Remove-DesktopPetInstalledPayload -Directory $installed }
    Assert-PackageTest ([System.IO.File]::ReadAllText((Join-Path $testRoot 'outside.txt')) -eq 'must remain') 'Uninstall escaped its root.'
    Write-Host 'PASS Uninstall rejects a manifest escaping the installation directory'
    $passed++
    Write-Host "$passed/19 release packaging regression checks passed (synthetic binary and images, no runtime execution)"
}
finally {
    $safeTestRoot = Assert-DesktopPetChildPath -Root (Join-Path $repoRoot '.local') -Path $testRoot
    Assert-DesktopPetNoReparsePoint $safeTestRoot
    Remove-Item -LiteralPath $safeTestRoot -Recurse -Force
}
