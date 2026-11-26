<#
.SYNOPSIS
    Disconnects assistant hooks and removes only this installation's managed files.
.DESCRIPTION
    User notifications, preferences, custom characters and unrelated processes are preserved.
    Close DesktopPet from its tray menu before uninstalling. Dot-sourcing only loads functions.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DesktopPet')
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')

function Remove-DesktopPetInstalledPayload {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $root = Get-DesktopPetInstallPath $Directory
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))).Replace('-', '') }
    finally { $hasher.Dispose() }
    # Use the same lock as installation so an upgrade and uninstall cannot interleave files.
    $mutex = New-Object System.Threading.Mutex($false, ('Local\DesktopPet.Install.' + $hash))
    $ownsMutex = $false
    try {
        try { $ownsMutex = $mutex.WaitOne(0) }
        catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
        if (-not $ownsMutex) { throw '다른 설치·제거 작업이 이 폴더를 사용 중입니다. 완료 후 다시 시도해주세요.' }
        Invoke-DesktopPetPayloadRemoval -Directory $root
    }
    finally {
        if ($ownsMutex) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-DesktopPetPayloadRemoval {
    param([Parameter(Mandatory = $true)][string]$Directory)
    $root = Get-DesktopPetInstallPath $Directory
    Assert-DesktopPetNoReparsePoint $root
    if (-not [System.IO.Directory]::Exists($root)) { return }
    Assert-DesktopPetNoPendingInstallRecovery $root
    $exePath = Join-Path $root 'DesktopPet.App.exe'
    foreach ($process in @(Get-Process -Name 'DesktopPet.App' -ErrorAction SilentlyContinue)) {
        $processPath = $null
        try { $processPath = $process.Path } catch { }
        if ($processPath -and ([System.IO.Path]::GetFullPath($processPath)).Equals($exePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw '이 설치의 DesktopPet이 실행 중입니다. 트레이에서 종료한 뒤 다시 제거해주세요.'
        }
    }
    $manifestPath = Join-Path $root '.desktop-pet-manifest.json'
    if (-not [System.IO.File]::Exists($manifestPath)) { throw '관리 파일 목록이 없어 안전하게 제거할 수 없습니다. 설치 폴더와 사용자 데이터는 변경하지 않았습니다.' }
    $manifest = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -ErrorAction Stop
    if ($manifest.version -ne 1 -or $null -eq $manifest.files) { throw '설치 파일 목록이 손상되어 제거하지 않았습니다.' }
    $manifestProbe = [System.IO.File]::Open($manifestPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $manifestProbe.Dispose()
    $files = @($manifest.files)
    $paths = New-Object 'System.Collections.Generic.List[string]'
    foreach ($relative in $files) {
        if ($relative -isnot [string]) { throw '설치 파일 목록에 잘못된 경로가 있습니다.' }
        Assert-DesktopPetPayloadRelativePath $relative
        $path = Assert-DesktopPetChildPath -Root $root -Path (Join-Path $root $relative)
        Assert-DesktopPetNoReparsePoint $path
        if ([System.IO.Directory]::Exists($path)) { throw "관리 파일 경로에 폴더가 있습니다: $relative" }
        if ([System.IO.File]::Exists($path)) {
            $probe = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $probe.Dispose()
        }
        $paths.Add($path)
    }
    $releasePath = Join-Path $root 'DesktopPet.release.json'
    if ([System.IO.File]::Exists($releasePath)) {
        $release = [System.IO.File]::ReadAllText($releasePath) | ConvertFrom-Json -ErrorAction Stop
        if ($release.assistantDisconnectCliVersion -eq 1 -and [System.IO.File]::Exists($exePath)) {
            Write-Host '도우미 연결을 해제하는 중...'
            $disconnect = Start-Process -FilePath $exePath -ArgumentList '--disconnect-assistants' -WindowStyle Hidden -PassThru
            if (-not $disconnect.WaitForExit(15000)) { throw '도우미 연결 해제가 끝나지 않아 앱 파일을 보존했습니다. 연결 상태를 확인한 뒤 다시 제거해주세요.' }
            if ($disconnect.ExitCode -ne 0) { throw "도우미 연결 해제 실패(exit $($disconnect.ExitCode)). 앱 파일은 보존했습니다." }
        }
        else { throw '이 배포의 도우미 연결 해제 방식이 지원되지 않아 앱 파일을 보존했습니다.' }
    }
    # The application owns assistant configuration paths and surgically removes only its hooks.
    # This script never guesses those paths or kills processes by their executable name.
    foreach ($path in $paths) { if ([System.IO.File]::Exists($path)) { [System.IO.File]::Delete($path) } }
    [System.IO.File]::Delete($manifestPath)
    $parents = @($paths | ForEach-Object { [System.IO.Path]::GetDirectoryName($_) } | Sort-Object -Unique | Sort-Object { $_.Length } -Descending)
    foreach ($parent in $parents) {
        $current = $parent
        while ($current -and -not $current.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
            $safeDirectory = Assert-DesktopPetChildPath -Root $root -Path $current
            Assert-DesktopPetNoReparsePoint $safeDirectory
            try { [System.IO.Directory]::Delete($safeDirectory, $false) }
            catch { break } # Retain any directory containing user files.
            $current = [System.IO.Path]::GetDirectoryName($safeDirectory)
        }
    }
    try { [System.IO.Directory]::Delete($root, $false) } catch { }
}

if ($MyInvocation.InvocationName -ne '.') {
    $installRoot = Get-DesktopPetInstallPath $InstallDirectory
    if (-not $PSCmdlet.ShouldProcess($installRoot, 'Remove the managed DesktopPet installation and disconnect its assistant hooks')) { return }
    $exePath = Join-Path $installRoot 'DesktopPet.App.exe'
    Remove-DesktopPetInstalledPayload -Directory $installRoot
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'DesktopPet.lnk'
    if ([System.IO.File]::Exists($shortcutPath)) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -and ([System.IO.Path]::GetFullPath($shortcut.TargetPath)).Equals($exePath, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }
    }
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $startup = Get-ItemProperty -Path $runKey -Name DesktopPet -ErrorAction SilentlyContinue
    if ($startup -and $startup.DesktopPet -eq ('"' + $exePath + '"')) { Remove-ItemProperty -Path $runKey -Name DesktopPet }
    Write-Host '제거 완료. 사용자 설정·미확인 알림·별도 캐릭터는 보존했습니다.'
}
