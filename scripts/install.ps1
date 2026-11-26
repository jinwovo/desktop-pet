<#
.SYNOPSIS
    Installs a DesktopPet release per user, preserving notifications and custom characters.
.DESCRIPTION
    Run from an extracted release ZIP without a .NET SDK. From the source repository, a
    self-contained public release is built first. Framework-dependent deployment is explicit.
#>
param(
    [switch]$AddToStartup,
    [switch]$SkipStartMenuShortcut,
    [switch]$VerifyOnly,
    [string]$PackageDirectory,
    [string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopPet'),
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')
. (Join-Path $PSScriptRoot 'package-release.ps1') -FrameworkDependent:$FrameworkDependent

if (-not $PackageDirectory -and [System.IO.File]::Exists((Join-Path $PSScriptRoot 'DesktopPet.release.json'))) {
    $PackageDirectory = $PSScriptRoot
}
if ($VerifyOnly) {
    if (-not $PackageDirectory) { throw 'Extract a DesktopPet release ZIP or pass its extracted folder with -PackageDirectory.' }
    Test-DesktopPetReleasePayload -Directory $PackageDirectory
    return
}

$installRoot = Get-DesktopPetInstallPath $InstallDirectory
$temporaryRoot = $null
try {
    if (-not $PackageDirectory) {
        $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('DesktopPet.install-package-' + [Guid]::NewGuid().ToString('N'))
        [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
        $archiveRoot = Join-Path $temporaryRoot 'archives'
        & (Join-Path $PSScriptRoot 'package-release.ps1') -OutputDirectory $archiveRoot -FrameworkDependent:$FrameworkDependent
        $archives = @(Get-ChildItem -LiteralPath $archiveRoot -Filter '*.zip' -File)
        if ($archives.Count -ne 1) { throw 'Expected exactly one release archive.' }
        $PackageDirectory = Join-Path $temporaryRoot 'extracted'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archives[0].FullName, $PackageDirectory)
    }
    $manifest = Test-DesktopPetReleasePayload -Directory $PackageDirectory
    if ($manifest.requiresDotNetRuntime) { Write-Host 'Framework-dependent 배포: .NET 10 Desktop Runtime x64가 필요합니다.' }
    Write-Host "설치 중... ($installRoot, 알림과 사용자 캐릭터 보존)"
    $expectedVersion = $manifest.version
    $expectedDeployment = $manifest.deployment
    Install-DesktopPetPayload -PublishDirectory $PackageDirectory -InstallDirectory $installRoot -ValidateStagedPayload {
        param($stagedPackage)
        Test-DesktopPetReleasePayload -Directory $stagedPackage -ExpectedVersion $expectedVersion -ExpectedDeployment $expectedDeployment
    }
}
finally {
    if ($temporaryRoot -and [System.IO.Directory]::Exists($temporaryRoot)) {
        $safeTemporary = Assert-DesktopPetChildPath -Root ([System.IO.Path]::GetTempPath()) -Path $temporaryRoot
        Assert-DesktopPetNoReparsePoint $safeTemporary
        Remove-Item -LiteralPath $safeTemporary -Recurse -Force
    }
}

$exePath = Join-Path $installRoot 'DesktopPet.App.exe'
if (-not $SkipStartMenuShortcut) {
    Set-DesktopPetStartMenuShortcut -ProgramsDirectory ([Environment]::GetFolderPath('Programs')) -Executable $exePath
}
if ($AddToStartup) {
    Set-DesktopPetStartupRegistration -Executable $exePath | Out-Null
}
Write-Host "설치 완료: $exePath"
