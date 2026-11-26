<#
.SYNOPSIS
    Publishes only payload files into an install directory; dot-sourcing has no side effects.
#>

function Get-DesktopPetInstallPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($fullPath -eq [System.IO.Path]::GetPathRoot($fullPath).TrimEnd('\')) {
        throw "A filesystem root cannot be used as an installation directory: $fullPath"
    }
    return $fullPath
}

function Assert-DesktopPetChildPath {
    param([string]$Root, [string]$Path)
    $fullRoot = Get-DesktopPetInstallPath $Root
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($fullRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes its expected directory: $fullPath"
    }
    return $fullPath
}

function Assert-DesktopPetNoReparsePoint {
    param([string]$Path)
    $currentPath = [System.IO.Path]::GetFullPath($Path)
    while ($currentPath) {
        if ([System.IO.File]::Exists($currentPath) -or [System.IO.Directory]::Exists($currentPath)) {
            $item = Get-Item -LiteralPath $currentPath -Force -ErrorAction Stop
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Installer paths must not pass through links or junctions: $currentPath"
            }
        }
        $currentPath = [System.IO.Path]::GetDirectoryName($currentPath)
    }
}

function Get-DesktopPetPayloadRelativePath {
    param([string]$Root, [string]$Path)
    $fullPath = Assert-DesktopPetChildPath -Root $Root -Path $Path
    $relativePath = $fullPath.Substring((Get-DesktopPetInstallPath $Root).Length + 1)
    Assert-DesktopPetPayloadRelativePath $relativePath
    return $relativePath
}

function Assert-DesktopPetPayloadRelativePath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [System.IO.Path]::IsPathRooted($Path)) {
        throw "Payload paths must be relative: $Path"
    }
    $segments = $Path.Replace('/', '\').Split('\')
    foreach ($segment in $segments) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.' -or $segment -eq '..' -or
            $segment.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            $segment.EndsWith('.') -or $segment.EndsWith(' ')) {
            throw "Invalid payload path: $Path"
        }
    }
    if ($segments[0] -eq 'inbox' -or $segments[0] -eq 'Characters' -or
        $segments[0] -eq 'settings.json' -or
        $segments[0].StartsWith('settings.json.', [System.StringComparison]::OrdinalIgnoreCase) -or
        $segments[0].StartsWith('.settings.json.', [System.StringComparison]::OrdinalIgnoreCase) -or
        $segments[0] -eq 'assistant-connections.json' -or
        $segments[0].StartsWith('assistant-connections.json.', [System.StringComparison]::OrdinalIgnoreCase) -or
        $segments[0] -eq '.desktop-pet-manifest.json' -or
        $segments[0].StartsWith('.desktop-pet-install-', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Payload contains a reserved user-data or installer path: $Path"
    }
}

function Set-DesktopPetStartMenuShortcut {
    [CmdletBinding()]
    param([string]$ProgramsDirectory, [Parameter(Mandatory = $true)][string]$Executable)
    if ([string]::IsNullOrWhiteSpace($ProgramsDirectory)) {
        Write-Warning '시작 메뉴 폴더를 찾지 못했습니다. 설치 폴더의 DesktopPet.App.exe를 실행할 수 있습니다.'
        return
    }

    try {
        $programs = [System.IO.Path]::GetFullPath($ProgramsDirectory)
        [System.IO.Directory]::CreateDirectory($programs) | Out-Null
        $shortcutPath = Join-Path $programs 'DesktopPet.lnk'
        $shell = New-Object -ComObject WScript.Shell -ErrorAction Stop
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ([System.IO.File]::Exists($shortcutPath) -and $shortcut.TargetPath -and
            -not ([System.IO.Path]::GetFullPath($shortcut.TargetPath)).Equals($Executable, [StringComparison]::OrdinalIgnoreCase)) {
            Write-Warning '기존 DesktopPet 바로가기가 다른 프로그램을 가리켜 보존했습니다.'
            return
        }
        $shortcut.TargetPath = $Executable
        $shortcut.WorkingDirectory = [System.IO.Path]::GetDirectoryName($Executable)
        $shortcut.IconLocation = Join-Path $shortcut.WorkingDirectory 'Assets\AppIcon.ico'
        $shortcut.Save()
        Write-Host "시작 메뉴 바로가기: $shortcutPath"
    }
    catch {
        Write-Warning ('앱은 설치했지만 시작 메뉴 바로가기를 만들지 못했습니다. 실행 파일: ' + $Executable)
    }
}

function Install-DesktopPetPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PublishDirectory,
        [Parameter(Mandatory = $true)][string]$InstallDirectory
    )
    $root = Get-DesktopPetInstallPath $InstallDirectory
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($hasher.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($root.ToUpperInvariant()))).Replace('-', '') }
    finally { $hasher.Dispose() }
    $mutex = New-Object System.Threading.Mutex($false, ('Local\DesktopPet.Install.' + $hash))
    $ownsMutex = $false
    try {
        try { $ownsMutex = $mutex.WaitOne(0) }
        catch [System.Threading.AbandonedMutexException] { $ownsMutex = $true }
        if (-not $ownsMutex) { throw 'Another DesktopPet update is already using this installation directory.' }
        Invoke-DesktopPetPayloadUpdate -PublishDirectory $PublishDirectory -InstallDirectory $root
    }
    finally {
        if ($ownsMutex) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

function Invoke-DesktopPetPayloadUpdate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$PublishDirectory,
        [Parameter(Mandatory = $true)][string]$InstallDirectory
    )
    $ErrorActionPreference = 'Stop'
    $sourceRoot = Get-DesktopPetInstallPath $PublishDirectory
    $installRoot = Get-DesktopPetInstallPath $InstallDirectory
    if ($sourceRoot.Equals($installRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($installRoot + '\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $installRoot.StartsWith($sourceRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Publish and installation directories must be separate and must not contain each other.'
    }
    Assert-DesktopPetNoReparsePoint $sourceRoot
    Assert-DesktopPetNoReparsePoint $installRoot
    if (-not [System.IO.File]::Exists((Join-Path $sourceRoot 'DesktopPet.App.exe'))) {
        throw 'The published payload must contain DesktopPet.App.exe.'
    }

    # Walk explicitly so a junction is rejected before traversing its children.
    $payloadFiles = New-Object 'System.Collections.Generic.List[string]'
    $pendingDirectories = New-Object 'System.Collections.Generic.Queue[string]'
    $pendingDirectories.Enqueue($sourceRoot)
    while ($pendingDirectories.Count -gt 0) {
        $directory = $pendingDirectories.Dequeue()
        foreach ($entry in Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Published payload contains a link or junction: $($entry.FullName)"
            }
            $relativePath = Get-DesktopPetPayloadRelativePath -Root $sourceRoot -Path $entry.FullName
            if ($entry.PSIsContainer) { $pendingDirectories.Enqueue($entry.FullName) }
            else { $payloadFiles.Add($relativePath) }
        }
    }
    $newFiles = @($payloadFiles | Sort-Object)
    $manifestName = '.desktop-pet-manifest.json'
    $manifestPath = Join-Path $installRoot $manifestName
    $previousFiles = @()
    if ([System.IO.File]::Exists($manifestPath)) {
        $previousManifest = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -ErrorAction Stop
        if ($previousManifest.version -ne 1 -or $null -eq $previousManifest.files) {
            throw 'The installed payload manifest is invalid; existing files were left untouched.'
        }
        $previousFiles = @($previousManifest.files)
        foreach ($relativePath in $previousFiles) { Assert-DesktopPetPayloadRelativePath $relativePath }
    }
    $managedPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relativePath in $previousFiles) {
        [void]$managedPaths.Add($relativePath.Replace('/', '\'))
    }
    $previousFiles = @($managedPaths)
    $obsoleteFiles = @($previousFiles | Where-Object { $newFiles -notcontains $_ } | Sort-Object -Unique)
    $touchedFiles = @(@($newFiles) + @($obsoleteFiles) + @($manifestName) | Sort-Object -Unique)
    foreach ($relativePath in $touchedFiles) {
        $targetPath = Assert-DesktopPetChildPath -Root $installRoot -Path (Join-Path $installRoot $relativePath)
        Assert-DesktopPetNoReparsePoint $targetPath
        if ([System.IO.Directory]::Exists($targetPath)) { throw "A directory occupies a payload file path: $targetPath" }
        if ([System.IO.File]::Exists($targetPath)) {
            # Abort before changing anything if the app or another process has a file open.
            try {
                $probe = [System.IO.File]::Open($targetPath, [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                $probe.Dispose()
            }
            catch { throw "Cannot update '$targetPath'. Close DesktopPet and other programs using this file, then retry. $($_.Exception.Message)" }
            if ($relativePath -ne $manifestName -and -not $managedPaths.Contains($relativePath) -and $newFiles -contains $relativePath) {
                $incomingPath = Join-Path $sourceRoot $relativePath
                if ((Get-FileHash -LiteralPath $incomingPath -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash) {
                    throw "An existing file is not managed by DesktopPet: $targetPath. Use another installation folder or move that file before retrying."
                }
            }
        }
    }

    [System.IO.Directory]::CreateDirectory($installRoot) | Out-Null
    $transactionRoot = Assert-DesktopPetChildPath -Root $installRoot -Path (
        Join-Path $installRoot ('.desktop-pet-install-' + [Guid]::NewGuid().ToString('N')))
    $stageRoot = Join-Path $transactionRoot 'stage'
    $backupRoot = Join-Path $transactionRoot 'backup'
    [System.IO.Directory]::CreateDirectory($stageRoot) | Out-Null
    [System.IO.Directory]::CreateDirectory($backupRoot) | Out-Null
    $applied = New-Object 'System.Collections.Generic.List[object]'
    $createdDirectories = New-Object 'System.Collections.Generic.List[string]'
    $rollbackFailed = $false
    try {
        # Prepare everything before touching the installed version.
        foreach ($relativePath in $newFiles) {
            $stagePath = Assert-DesktopPetChildPath -Root $stageRoot -Path (Join-Path $stageRoot $relativePath)
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($stagePath)) | Out-Null
            [System.IO.File]::Copy((Join-Path $sourceRoot $relativePath), $stagePath)
        }
        $manifestJson = [ordered]@{ version = 1; files = @($newFiles) } | ConvertTo-Json -Depth 4
        [System.IO.File]::WriteAllText((Join-Path $stageRoot $manifestName), $manifestJson,
            (New-Object System.Text.UTF8Encoding $false))

        foreach ($relativePath in @($newFiles) + @($manifestName)) {
            $targetPath = Assert-DesktopPetChildPath -Root $installRoot -Path (Join-Path $installRoot $relativePath)
            $stagePath = Assert-DesktopPetChildPath -Root $stageRoot -Path (Join-Path $stageRoot $relativePath)
            $backupPath = Assert-DesktopPetChildPath -Root $backupRoot -Path (Join-Path $backupRoot $relativePath)
            Assert-DesktopPetNoReparsePoint $targetPath
            $parentPath = [System.IO.Path]::GetDirectoryName($targetPath)
            $missingDirectories = New-Object 'System.Collections.Generic.List[string]'
            while (-not [System.IO.Directory]::Exists($parentPath)) {
                $missingDirectories.Add($parentPath)
                $parentPath = [System.IO.Path]::GetDirectoryName($parentPath)
            }
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($targetPath)) | Out-Null
            foreach ($createdDirectory in $missingDirectories) { $createdDirectories.Add($createdDirectory) }
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($backupPath)) | Out-Null
            if ([System.IO.File]::Exists($targetPath)) {
                [System.IO.File]::Replace($stagePath, $targetPath, $backupPath, $true)
                $applied.Add([pscustomobject]@{ Kind = 'Replace'; Target = $targetPath; Backup = $backupPath })
            }
            else {
                [System.IO.File]::Move($stagePath, $targetPath)
                $applied.Add([pscustomobject]@{ Kind = 'Create'; Target = $targetPath; Backup = $null })
            }
        }
        foreach ($relativePath in $obsoleteFiles) {
            $targetPath = Assert-DesktopPetChildPath -Root $installRoot -Path (Join-Path $installRoot $relativePath)
            if ([System.IO.File]::Exists($targetPath)) {
                $backupPath = Assert-DesktopPetChildPath -Root $backupRoot -Path (Join-Path $backupRoot $relativePath)
                [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($backupPath)) | Out-Null
                [System.IO.File]::Move($targetPath, $backupPath)
                $applied.Add([pscustomobject]@{ Kind = 'Remove'; Target = $targetPath; Backup = $backupPath })
            }
        }
    }
    catch {
        $installError = $_
        for ($index = $applied.Count - 1; $index -ge 0; $index--) {
            $change = $applied[$index]
            try {
                Assert-DesktopPetNoReparsePoint $change.Target
                if ($change.Kind -eq 'Replace') { [System.IO.File]::Replace($change.Backup, $change.Target, ($change.Backup + '.failed-update'), $true) }
                elseif ($change.Kind -eq 'Create') { [System.IO.File]::Delete($change.Target) }
                else { [System.IO.File]::Move($change.Backup, $change.Target) }
            }
            catch { $rollbackFailed = $true; Write-Warning "Rollback failed for '$($change.Target)': $($_.Exception.Message)" }
        }
        foreach ($createdDirectory in $createdDirectories) {
            try {
                $safeDirectory = Assert-DesktopPetChildPath -Root $installRoot -Path $createdDirectory
                Assert-DesktopPetNoReparsePoint $safeDirectory
                [System.IO.Directory]::Delete($safeDirectory, $false) # Only remove directories still empty.
            }
            catch { } # New user content must be retained.
        }
        if ($rollbackFailed) { throw "Update failed and some files could not be restored. Recovery files remain at '$transactionRoot'. Original error: $installError" }
        throw "Update failed; the previous payload was restored. $installError"
    }
    finally {
        if (-not $rollbackFailed -and [System.IO.Directory]::Exists($transactionRoot)) {
            $safeTransaction = Assert-DesktopPetChildPath -Root $installRoot -Path $transactionRoot
            Assert-DesktopPetNoReparsePoint $safeTransaction
            Remove-Item -LiteralPath $safeTransaction -Recurse -Force -ErrorAction Stop
        }
    }
}
