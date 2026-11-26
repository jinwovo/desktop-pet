# Synthetic installation tests only: no real installation, shortcuts, registry, or process changes.
$ErrorActionPreference = 'Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Install-DesktopPetPayload.ps1')

function Assert-Test {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Write-TestFile {
    param([string]$Root, [string]$RelativePath, [string]$Content)
    $path = Join-Path $Root $RelativePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $Content)
}

function Assert-InstallFails {
    param([scriptblock]$Action)
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-Test $failed 'Expected deployment to fail.'
}

$testRoot = Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) '.local') ('install-regression-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testRoot) | Out-Null
$passed = 0
try {
    $collisionSource = Join-Path $testRoot 'collision-source'
    $collisionInstall = Join-Path $testRoot 'collision-install'
    Write-TestFile $collisionSource 'DesktopPet.App.exe' 'new application'
    Write-TestFile $collisionSource 'Assets\theme.txt' 'official theme'
    Write-TestFile $collisionInstall 'Assets\theme.txt' 'personal theme'
    Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $collisionSource -InstallDirectory $collisionInstall }
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $collisionInstall 'Assets\theme.txt')) -eq 'personal theme' -and
        -not [System.IO.File]::Exists((Join-Path $collisionInstall 'DesktopPet.App.exe'))) 'Unmanaged conflict wrote part of the incoming package.'
    Write-TestFile $collisionInstall 'Assets\theme.txt' 'official theme'
    Install-DesktopPetPayload -PublishDirectory $collisionSource -InstallDirectory $collisionInstall
    Assert-Test ([System.IO.File]::Exists((Join-Path $collisionInstall '.desktop-pet-manifest.json'))) 'Identical unpacked files could not be adopted.'
    Write-Host 'PASS Conflicting unmanaged files stay intact and identical files can be adopted'
    $passed++

    $publishRoot = Join-Path $testRoot 'publish'
    $installRoot = Join-Path $testRoot 'installed'
    Write-TestFile $publishRoot 'DesktopPet.App.exe' 'version 1'
    Write-TestFile $publishRoot 'Assets\Characters\Bundled\idle.png' 'bundled version 1'
    Write-TestFile $publishRoot 'obsolete.dll' 'old dependency'
    Write-TestFile $installRoot 'inbox\pending.json' '{"message":"must survive"}'
    Write-TestFile $installRoot 'Assets\Characters\MyPet\character.json' 'custom character'
    Write-TestFile $installRoot 'unknown-user-file.txt' 'custom file'
    Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $installRoot
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'DesktopPet.App.exe')) -eq 'version 1') 'Initial installation failed.'
    Assert-Test ([System.IO.File]::Exists((Join-Path $installRoot 'inbox\pending.json'))) 'Inbox was deleted.'
    Assert-Test ([System.IO.File]::Exists((Join-Path $installRoot 'Assets\Characters\MyPet\character.json'))) 'Custom character was deleted.'
    Assert-Test ([System.IO.File]::Exists((Join-Path $installRoot 'unknown-user-file.txt'))) 'Unmanaged file was deleted.'
    Write-Host 'PASS First installation preserves existing notifications and custom characters'
    $passed++

    Remove-Item -LiteralPath (Join-Path $publishRoot 'obsolete.dll')
    Write-TestFile $publishRoot 'DesktopPet.App.exe' 'version 2'
    Write-TestFile $publishRoot 'Assets\Characters\Bundled\idle.png' 'bundled version 2'
    Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $installRoot
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'DesktopPet.App.exe')) -eq 'version 2') 'Upgrade did not update executable.'
    Assert-Test (-not [System.IO.File]::Exists((Join-Path $installRoot 'obsolete.dll'))) 'Obsolete managed payload was not removed.'
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'Assets\Characters\MyPet\character.json')) -eq 'custom character') 'Upgrade changed custom character.'
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'inbox\pending.json')) -eq '{"message":"must survive"}') 'Upgrade changed notification.'
    Write-Host 'PASS Upgrade updates managed files, removes old managed files, and preserves user data'
    $passed++

    Write-TestFile $publishRoot 'DesktopPet.App.exe' 'version 3'
    $lock = [System.IO.File]::Open((Join-Path $installRoot 'DesktopPet.App.exe'), [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try { Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $installRoot } }
    finally { $lock.Dispose() }
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'DesktopPet.App.exe')) -eq 'version 2') 'Locked executable was changed.'
    Assert-Test (@(Get-ChildItem -LiteralPath $installRoot -Force -Filter '.desktop-pet-install-*').Count -eq 0) 'Locked deployment leaked a transaction.'
    Write-Host 'PASS Locked executable aborts before changing the installed version'
    $passed++

    $programs = Join-Path $testRoot 'missing Programs Folder'
    $installedExe = Join-Path $installRoot 'DesktopPet.App.exe'
    Set-DesktopPetStartMenuShortcut -ProgramsDirectory $programs -Executable $installedExe
    $linkPath = Join-Path $programs 'DesktopPet.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($linkPath)
    Assert-Test ([System.IO.File]::Exists($linkPath) -and $link.TargetPath -eq $installedExe) 'Shortcut creation failed in a missing Programs folder.'
    $otherExe = Join-Path $testRoot 'Other.exe'
    $link.TargetPath = $otherExe
    $link.Save()
    Set-DesktopPetStartMenuShortcut -ProgramsDirectory $programs -Executable $installedExe
    Assert-Test ($shell.CreateShortcut($linkPath).TargetPath -eq $otherExe) 'Existing unrelated shortcut was overwritten.'
    $blockedPrograms = Join-Path $testRoot 'blocked Programs'
    [System.IO.File]::WriteAllText($blockedPrograms, 'not a directory')
    Set-DesktopPetStartMenuShortcut -ProgramsDirectory $blockedPrograms -Executable $installedExe -WarningVariable shortcutWarnings
    Assert-Test (@($shortcutWarnings).Count -eq 1 -and [System.IO.File]::ReadAllText($installedExe) -eq 'version 2') 'Shortcut failure did not preserve installed payload.'
    Write-Host 'PASS Start Menu integration creates missing folders, preserves unrelated shortcuts, and tolerates unavailable parents'
    $passed++

    $beforeManifest = [System.IO.File]::ReadAllText((Join-Path $installRoot '.desktop-pet-manifest.json'))
    Write-TestFile $publishRoot 'a-first.txt' 'new file that must roll back'
    Write-TestFile $publishRoot 'z-conflict\new-file.txt' 'cannot be deployed'
    Write-TestFile $installRoot 'z-conflict' 'existing user file occupying a directory'
    Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $installRoot }
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'DesktopPet.App.exe')) -eq 'version 2') 'Failed deployment did not restore executable.'
    Assert-Test (-not [System.IO.File]::Exists((Join-Path $installRoot 'a-first.txt'))) 'Failed deployment left a new file behind.'
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot '.desktop-pet-manifest.json')) -eq $beforeManifest) 'Failed deployment changed manifest.'
    Assert-Test ([System.IO.File]::ReadAllText((Join-Path $installRoot 'z-conflict')) -eq 'existing user file occupying a directory') 'Rollback changed unrelated file.'
    Assert-Test (@(Get-ChildItem -LiteralPath $installRoot -Force -Filter '.desktop-pet-install-*').Count -eq 0) 'Successful rollback leaked a transaction.'
    Write-Host 'PASS Mid-update failure rolls back replaced and newly created files'
    $passed++

    $invalidPublish = Join-Path $testRoot 'invalid-publish'
    Write-TestFile $invalidPublish 'DesktopPet.App.exe' 'payload'
    Write-TestFile $invalidPublish 'inbox\overwrite.json' 'forbidden'
    Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $invalidPublish -InstallDirectory $installRoot }
    Assert-Test (-not [System.IO.File]::Exists((Join-Path $installRoot 'inbox\overwrite.json'))) 'Payload was allowed to change inbox.'
    Write-Host 'PASS Payload cannot claim the notifications directory'
    $passed++

    $userDataRoot = Join-Path $testRoot 'user-data-protection'
    $userDataInstall = Join-Path $userDataRoot 'installed'
    Write-TestFile $userDataInstall 'DesktopPet.App.exe' 'installed before rejected update'
    Write-TestFile $userDataInstall '.desktop-pet-manifest.json' '{"version":1,"files":["DesktopPet.App.exe"]}'
    $userFiles = @(
        'Characters\Personal\character.json',
        'settings.json',
        'settings.json.bak',
        '.settings.json.fixture.tmp',
        'assistant-connections.json',
        'assistant-connections.json.fixture.bak',
        'assistant-connections.json.fixture.tmp',
        'assistant-connections.json.fixture.bak.conflict'
    )
    foreach ($relative in $userFiles) { Write-TestFile $userDataInstall $relative ('preserve ' + $relative) }
    $safeManifest = [System.IO.File]::ReadAllText((Join-Path $userDataInstall '.desktop-pet-manifest.json'))
    $case = 0
    foreach ($relative in $userFiles) {
        $reservedPublish = Join-Path $userDataRoot ('reserved-publish-' + $case++)
        Write-TestFile $reservedPublish 'DesktopPet.App.exe' 'must not replace installed app'
        Write-TestFile $reservedPublish $relative.ToUpperInvariant().Replace('\', '/') 'must not replace user data'
        Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $reservedPublish -InstallDirectory $userDataInstall }
        Assert-Test ([System.IO.File]::ReadAllText((Join-Path $userDataInstall 'DesktopPet.App.exe')) -eq 'installed before rejected update') 'Reserved payload changed app before rejecting user data.'
        Assert-Test ([System.IO.File]::ReadAllText((Join-Path $userDataInstall '.desktop-pet-manifest.json')) -eq $safeManifest) 'Reserved payload changed install manifest.'
        foreach ($kept in $userFiles) {
            Assert-Test ([System.IO.File]::ReadAllText((Join-Path $userDataInstall $kept)) -eq ('preserve ' + $kept)) 'Reserved payload changed a character, journal or recovery file.'
        }
    }
    Assert-DesktopPetPayloadRelativePath 'Assets/Characters/Companion/idle.png'
    Write-Host 'PASS Payload cannot claim custom Characters or settings, assistant journals and recovery files through case/slash aliases'
    $passed++

    $safePublish = Join-Path $userDataRoot 'safe-publish'
    Write-TestFile $safePublish 'DesktopPet.App.exe' 'must not delete claimed user data'
    foreach ($relative in $userFiles) {
        $claimed = [ordered]@{ version = 1; files = @('DesktopPet.App.exe', $relative.ToUpperInvariant().Replace('\', '/')) } | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText((Join-Path $userDataInstall '.desktop-pet-manifest.json'), $claimed)
        Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $safePublish -InstallDirectory $userDataInstall }
        Assert-Test ([System.IO.File]::ReadAllText((Join-Path $userDataInstall $relative)) -eq ('preserve ' + $relative)) 'A forged old manifest allowed an update to remove user data.'
        Assert-Test ([System.IO.File]::ReadAllText((Join-Path $userDataInstall 'DesktopPet.App.exe')) -eq 'installed before rejected update') 'A forged old manifest changed app before being rejected.'
    }
    Write-Host 'PASS An old install manifest cannot reclassify custom characters or assistant journals as removable payload'
    $passed++

    $escape = Join-Path $testRoot 'outside.txt'
    [System.IO.File]::WriteAllText($escape, 'must remain')
    [System.IO.File]::WriteAllText((Join-Path $installRoot '.desktop-pet-manifest.json'), '{"version":1,"files":["..\\outside.txt"]}')
    Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $installRoot }
    Assert-Test ([System.IO.File]::ReadAllText($escape) -eq 'must remain') 'Manifest traversal escaped installation directory.'
    Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $publishRoot -InstallDirectory $publishRoot }
    Assert-InstallFails { Get-DesktopPetInstallPath ([System.IO.Path]::GetPathRoot($testRoot)) }
    Write-Host 'PASS Invalid manifest paths, overlapping roots, and filesystem roots are rejected'
    $passed++

    $linkedPublish = Join-Path $testRoot 'linked-publish'
    $linkTarget = Join-Path $testRoot 'link-target'
    Write-TestFile $linkedPublish 'DesktopPet.App.exe' 'payload'
    Write-TestFile $linkTarget 'keep.txt' 'must remain'
    $junction = Assert-DesktopPetChildPath -Root $linkedPublish -Path (Join-Path $linkedPublish 'linked-assets')
    New-Item -ItemType Junction -Path $junction -Target $linkTarget | Out-Null
    try {
        Assert-InstallFails { Install-DesktopPetPayload -PublishDirectory $linkedPublish -InstallDirectory (Join-Path $testRoot 'linked-install') }
        Assert-Test ([System.IO.File]::ReadAllText((Join-Path $linkTarget 'keep.txt')) -eq 'must remain') 'Link target was changed.'
    }
    finally {
        $safeJunction = Assert-DesktopPetChildPath -Root $linkedPublish -Path $junction
        $junctionItem = Get-Item -LiteralPath $safeJunction -Force
        Assert-Test (($junctionItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) 'Expected a junction during cleanup.'
        [System.IO.Directory]::Delete($safeJunction, $false) # Delete the link itself, never its target.
    }
    Write-Host 'PASS Published junctions are rejected without touching their target'
    $passed++
    Write-Host "$passed/11 installation regression checks passed"
}
finally {
    $safeTestRoot = Assert-DesktopPetChildPath -Root (Join-Path (Split-Path -Parent $PSScriptRoot) '.local') -Path $testRoot
    Assert-DesktopPetNoReparsePoint $safeTestRoot
    Remove-Item -LiteralPath $safeTestRoot -Recurse -Force
}
