<#
.SYNOPSIS
    Creates a Windows x64 ZIP. Self-contained single-file deployment is the default.
.DESCRIPTION
    A release contains the original Companion, Nimbus and Ember characters. Personal development character
    assets are excluded. A failed self-contained restore never silently becomes framework-dependent.
.EXAMPLE
    .\scripts\package-release.ps1 -Version 2.0.0
    .\scripts\package-release.ps1 -Version 2.0.0 -FrameworkDependent
#>
param(
    [string]$Version = '2.0.0',
    [string]$OutputDirectory,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')

function Assert-DesktopPetReleaseVersion {
    param([string]$ReleaseVersion)
    if ($ReleaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw "Use a semantic release version such as 0.4.0 or 0.4.0-preview.1: $ReleaseVersion"
    }
}

function Get-DesktopPetReleaseFiles {
    param([string]$Directory)
    $root = Get-DesktopPetInstallPath $Directory
    Assert-DesktopPetNoReparsePoint $root
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $result = New-Object 'System.Collections.Generic.List[string]'
    $pending.Enqueue($root)
    while ($pending.Count -gt 0) {
        foreach ($entry in Get-ChildItem -LiteralPath $pending.Dequeue() -Force -ErrorAction Stop) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Release payload contains a link: $($entry.FullName)" }
            $relative = Get-DesktopPetPayloadRelativePath -Root $root -Path $entry.FullName
            if ($entry.PSIsContainer) { $pending.Enqueue($entry.FullName) }
            else { $result.Add($relative) }
        }
    }
    return @($result | Sort-Object)
}

function Assert-DesktopPetPublicAssets {
    param([string]$Directory, [switch]$RequireFullCollection)
    foreach ($required in 'DesktopPet.App.exe','Assets\Characters\Companion\character.json','Hooks\assistant-stop-hook.ps1') {
        if (-not [System.IO.File]::Exists((Join-Path $Directory $required))) { throw "Release payload is missing: $required" }
    }
    $characterRoot = Join-Path $Directory 'Assets\Characters'
    $allowed = @('Companion','Nimbus','Ember')
    $entries = @(Get-ChildItem -LiteralPath $characterRoot -Force)
    foreach ($entry in $entries) {
        if (-not $entry.PSIsContainer -or $entry.Name -notin $allowed) {
            throw "Public release contains a character outside the original collection: $($entry.Name)"
        }
    }
    if ($RequireFullCollection) {
        if (-not [IO.File]::Exists((Join-Path $Directory 'LICENSE'))) { throw 'Public release is missing its license.' }
        foreach ($name in $allowed) {
            if (-not [IO.File]::Exists((Join-Path $characterRoot ($name + '\character.json')))) { throw "Public collection is missing: $name" }
        }
    }
    foreach ($entry in $entries) {
    $name = $entry.Name
    $companionRoot = $entry.FullName
    $character = [System.IO.File]::ReadAllText((Join-Path $companionRoot 'character.json')) | ConvertFrom-Json
    if ($character.name -ne $name -or $null -eq $character.frames -or @($character.frames.idle).Count -eq 0) {
        throw "Public character metadata is invalid: $name"
    }
    foreach ($state in $character.frames.PSObject.Properties) {
        foreach ($frame in @($state.Value)) {
            Assert-DesktopPetPayloadRelativePath $frame
            $framePath = Assert-DesktopPetChildPath -Root $companionRoot -Path (Join-Path $companionRoot $frame)
            Assert-DesktopPetNoReparsePoint $framePath
            if (-not [System.IO.File]::Exists($framePath)) { throw "Public character frame is missing: $name/$frame" }
        }
    }
    $rigProperty = $character.PSObject.Properties['walkRig']
    if ($null -ne $rigProperty) {
        $rig = $rigProperty.Value
        if ($rig -isnot [pscustomobject] -or $rig.side -isnot [pscustomobject] -or $rig.turn -isnot [pscustomobject]) {
            throw 'Companion walkRig must contain side and turn views.'
        }
        foreach ($view in 'side','turn') {
            foreach ($part in 'body','nearFoot','farFoot') {
                $relativePath = $rig.$view.$part
                if ($relativePath -isnot [string] -or [System.IO.Path]::GetExtension($relativePath) -ine '.png') {
                    throw "Companion walkRig $view/$part must reference a PNG file."
                }
                Assert-DesktopPetPayloadRelativePath $relativePath
                $partPath = Assert-DesktopPetChildPath -Root $companionRoot -Path (Join-Path $companionRoot $relativePath)
                Assert-DesktopPetNoReparsePoint $partPath
                if (-not [System.IO.File]::Exists($partPath)) { throw "Companion walkRig part is missing: $view/$part ($relativePath)" }
            }
        }
    }
    }
}

function ConvertTo-DesktopPetCanonicalReleasePath {
    param([object]$RelativePath)
    if ($RelativePath -isnot [string]) { throw 'Release file paths must be strings.' }
    Assert-DesktopPetPayloadRelativePath $RelativePath
    return $RelativePath.Replace('/', '\')
}

function Expand-DesktopPetVerifiedArchive {
    param(
        [Parameter(Mandatory = $true)][string]$ArchivePath,
        [Parameter(Mandatory = $true)][string]$Destination
    )
    $archive = [System.IO.Path]::GetFullPath($ArchivePath)
    if ([System.IO.Path]::GetExtension($archive) -ne '.zip') { throw 'Select a DesktopPet release ZIP.' }
    $sidecar = $archive + '.sha256'
    if (-not [System.IO.File]::Exists($sidecar) -or (Get-Item -LiteralPath $sidecar).Length -gt 4096) {
        throw "Keep the matching SHA-256 sidecar next to the release ZIP: $sidecar"
    }
    $expected = [System.IO.File]::ReadAllText($sidecar).Trim()
    if ($expected -notmatch '^([0-9a-fA-F]{64})[ \t]+\*?([^\r\n]+)$' -or
        $Matches[2] -ne [System.IO.Path]::GetFileName($archive)) {
        throw 'The SHA-256 sidecar must name this release ZIP.'
    }
    $digest = $Matches[1]
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [System.IO.File]::Open($archive, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        if ((Get-FileHash -InputStream $zipStream -Algorithm SHA256).Hash -ne $digest) {
            throw 'The ZIP does not match its SHA-256 sidecar. Download the release again.'
        }
        $zipStream.Position = 0
        $zip = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Read, $true)
        try { [System.IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $Destination) }
        finally { $zip.Dispose() }
    }
    finally { $zipStream.Dispose() }
}

function Assert-DesktopPetX64Executable {
    param([Parameter(Mandatory = $true)][string]$Executable)
    $stream = [System.IO.File]::OpenRead($Executable)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw 'The release executable is not a Windows PE image.' }
        $stream.Position = 0x3C
        $header = $reader.ReadInt32()
        if ($header -lt 64 -or $header -gt $stream.Length - 6) { throw 'The Windows executable header is incomplete.' }
        $stream.Position = $header
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) {
            throw 'The win-x64 release must contain a Windows x64 executable.'
        }
    }
    finally { $stream.Dispose() }
}

function Test-DesktopPetReleasePayload {
    param([Parameter(Mandatory = $true)][string]$Directory, [string]$ExpectedVersion, [string]$ExpectedDeployment)
    $root = Get-DesktopPetInstallPath $Directory
    $allFiles = @(Get-DesktopPetReleaseFiles $root)
    $manifestPath = Join-Path $root 'DesktopPet.release.json'
    if (-not [System.IO.File]::Exists($manifestPath)) { throw 'Release manifest is missing.' }
    $manifest = [System.IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -ErrorAction Stop
    Assert-DesktopPetReleaseVersion $manifest.version
    Assert-DesktopPetPublicAssets $root -RequireFullCollection:($manifest.version -notmatch '^0\.[0-4]\.')
    if ($manifest.schemaVersion -ne 1 -or $manifest.product -ne 'DesktopPet' -or $manifest.runtimeIdentifier -ne 'win-x64' -or
        $manifest.entryPoint -ne 'DesktopPet.App.exe' -or $manifest.defaultCharacter -ne 'Companion' -or
        $manifest.assistantDisconnectCliVersion -ne 1 -or $null -eq $manifest.files) { throw 'Unsupported release manifest.' }
    if ($manifest.deployment -notin 'self-contained-single-file','framework-dependent-single-file') { throw 'Invalid release deployment kind.' }
    if ($ExpectedVersion -and $manifest.version -ne $ExpectedVersion) { throw 'Release version does not match the requested version.' }
    if ($ExpectedDeployment -and $manifest.deployment -ne $ExpectedDeployment) { throw 'Release deployment does not match the requested kind.' }
    if ($manifest.requiresDotNetRuntime -isnot [bool] -or $manifest.requiresDotNetRuntime -ne ($manifest.deployment -eq 'framework-dependent-single-file')) {
        throw 'Runtime requirement does not match deployment kind.'
    }
    $actualFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in $allFiles) {
        $canonical = ConvertTo-DesktopPetCanonicalReleasePath $relative
        if ($canonical -ne 'DesktopPet.release.json' -and -not $actualFiles.Add($canonical)) { throw 'Release contains ambiguous duplicate file paths.' }
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in @($manifest.files)) {
        $canonical = ConvertTo-DesktopPetCanonicalReleasePath $entry.path
        if ($canonical -eq 'DesktopPet.release.json' -or -not $seen.Add($canonical)) { throw 'Duplicate or self-referencing manifest path.' }
        if (-not $actualFiles.Contains($canonical)) { throw "Manifest file is absent from the release: $canonical" }
        $path = Assert-DesktopPetChildPath -Root $root -Path (Join-Path $root $canonical)
        if (-not [System.IO.File]::Exists($path) -or $entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid manifest file: $($entry.path)" }
        if ((Get-Item -LiteralPath $path).Length -ne $entry.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Release checksum mismatch: $($entry.path)"
        }
    }
    if (-not $seen.SetEquals($actualFiles)) { throw 'Release contains files absent from its manifest.' }
    $numeric = ($manifest.version -split '-')[0] + '.0'
    $entryPoint = Join-Path $root $manifest.entryPoint
    Assert-DesktopPetX64Executable -Executable $entryPoint
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint)
    $actual = '{0}.{1}.{2}.{3}' -f $versionInfo.FileMajorPart,$versionInfo.FileMinorPart,$versionInfo.FileBuildPart,$versionInfo.FilePrivatePart
    if ($actual -ne $numeric) { throw "Executable file version '$actual' does not match release '$numeric'." }
    return $manifest
}

function New-DesktopPetReleasePackage {
    param(
        [Parameter(Mandatory = $true)][string]$PublishDirectory,
        [Parameter(Mandatory = $true)][string]$DestinationDirectory,
        [Parameter(Mandatory = $true)][string]$ReleaseVersion,
        [switch]$RequiresRuntime
    )
    Assert-DesktopPetReleaseVersion $ReleaseVersion
    $sourceRoot = Get-DesktopPetInstallPath $PublishDirectory
    $outputRoot = Get-DesktopPetInstallPath $DestinationDirectory
    if ($sourceRoot.Equals($outputRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $sourceRoot.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $outputRoot.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish and release directories must not overlap.' }
    Assert-DesktopPetNoReparsePoint $outputRoot
    $sourceFiles = @(Get-DesktopPetReleaseFiles $sourceRoot)
    Assert-DesktopPetPublicAssets $sourceRoot -RequireFullCollection:($ReleaseVersion -notmatch '^0\.[0-4]\.')
    if ($sourceFiles -contains 'DesktopPet.release.json') { throw 'Use a fresh publish directory without a release manifest.' }
    $deployment = if ($RequiresRuntime) { 'framework-dependent-single-file' } else { 'self-contained-single-file' }
    $label = if ($RequiresRuntime) { 'win-x64-framework-dependent' } else { 'win-x64' }
    $baseName = "DesktopPet-$ReleaseVersion-$label"
    [System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    $zipPath = Join-Path $outputRoot ($baseName + '.zip')
    $hashPath = $zipPath + '.sha256'
    if ([System.IO.File]::Exists($zipPath) -or [System.IO.File]::Exists($hashPath)) { throw "Release already exists: $zipPath" }
    $stage = Assert-DesktopPetChildPath -Root $outputRoot -Path (Join-Path $outputRoot ('.desktop-pet-release-' + [Guid]::NewGuid().ToString('N')))
    [System.IO.Directory]::CreateDirectory($stage) | Out-Null
    $temporaryZip = Join-Path $outputRoot ('.desktop-pet-release-' + [Guid]::NewGuid().ToString('N') + '.zip')
    $temporaryHash = $temporaryZip + '.sha256'
    $publishedZip = $false
    $publishedHash = $false
    $completed = $false
    try {
        foreach ($relative in $sourceFiles) {
            $destination = Assert-DesktopPetChildPath -Root $stage -Path (Join-Path $stage $relative)
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
            [System.IO.File]::Copy((Join-Path $sourceRoot $relative), $destination)
        }
        foreach ($script in 'install.ps1','uninstall.ps1','Install-DesktopPetPayload.ps1','package-release.ps1') {
            [System.IO.File]::Copy((Join-Path $PSScriptRoot $script), (Join-Path $stage $script), $true)
        }
        $runtimeText = if ($RequiresRuntime) {
            '이 파일은 framework-dependent 개발/운영용 배포입니다. .NET 10 Desktop Runtime x64를 별도로 설치해야 실행됩니다. .NET SDK는 필요하지 않습니다.'
        } else { 'Windows 10/11 x64에서 .NET SDK나 별도 .NET Runtime 설치 없이 실행하는 self-contained 배포입니다.' }
        $readme = @"
DesktopPet $ReleaseVersion ($label)

English
Extract the entire ZIP and run DesktopPet.App.exe. Keep Assets and Hooks together.
This package type is $deployment. Framework-dependent builds require .NET 10 Desktop Runtime x64.
Double-click the tray icon for KO/EN/zh-CN Settings, configurable focus sessions and a test notification.
Use Settings for ZIP characters, quiet hours, reduced motion, backup, diagnostics and monitor recovery.
Read-only resource check: DesktopPet.App.exe --check-install (exit 0 valid, exit 2 invalid).
Claude Code: restart the session. Codex: review and approve the new hook yourself.
Hidden and Do not disturb modes keep unread receipts without automatic bubbles.
Public characters: Companion, Nimbus, Ember. Project license: see LICENSE.
Full guide: https://github.com/jinwovo/desktop-pet/blob/main/README.md

简体中文
解压整个 ZIP 后运行 DesktopPet.App.exe。请保留 Assets 和 Hooks 文件夹。
framework-dependent 版本需要另行安装 .NET 10 Desktop Runtime x64。
双击托盘图标打开三语设置，可调整专注时长、预约免打扰、导入 ZIP 角色并恢复位置。
只读资源检查: DesktopPet.App.exe --check-install（有效返回 0，无效返回 2）。
Claude Code 需要重启会话；Codex 的新钩子需要用户自行审核并信任。
隐藏和免打扰模式保留未读通知。项目许可证见 LICENSE。
完整指南: https://github.com/jinwovo/desktop-pet/blob/main/README.zh-CN.md

한국어

$runtimeText

시작: ZIP 전체를 쓰기 가능한 폴더에 풀고 DesktopPet.App.exe를 실행하세요.
설정: 트레이 아이콘을 두 번 클릭합니다. 처음 실행하면 도우미 연결을 선택할 수 있습니다.
설정에서 언어·집중 시간·예약 방해 금지·ZIP 캐릭터·모션·백업·진단·위치 복구를 조절합니다.
읽기 전용 점검: DesktopPet.App.exe --check-install (정상 0, 잘못된 리소스 2).
지원: Stop 훅을 제공하는 로컬 Claude Code와 Codex만 지원합니다. 웹 대화나 훅이 없는 앱은 연결되지 않습니다.
Claude Code는 세션을 다시 시작하고, Codex는 /hooks 또는 Settings → Hooks에서 새 훅을 한 번 검토·허용해주세요.
DesktopPet은 Codex의 신뢰 승인을 자동 생성하지 않습니다. 완료 알림은 응답 턴 종료를 뜻하며 전체 작업의 성공을 판정하지 않습니다.
종료: 트레이 메뉴에서 종료합니다.
설치(선택): 압축을 푼 폴더의 install.ps1을 PowerShell에서 실행합니다. SDK를 사용하지 않습니다.
제거: 설치 폴더의 uninstall.ps1을 실행합니다. 연결 해제 후 관리된 앱 파일만 제거하며 사용자 알림과 별도 캐릭터는 보존합니다.

이 일반 배포에는 프로젝트용 Companion, Nimbus, Ember 오리지널 캐릭터가 포함됩니다.
개발 저장소의 기존 개인용 캐릭터 이미지와 원본 그림은 포함하지 않았습니다.
Hooks/assistant-stop-hook.ps1은 앱의 도우미 연결 기능에서 사용하는 알림 브리지입니다.
사용자별 연결 파일 위치는 앱이 관리합니다. 다른 도구의 기존 설정을 통째로 덮어쓰지 않습니다.

무결성: ZIP과 함께 제공된 .sha256 값을 Get-FileHash -Algorithm SHA256으로 비교할 수 있습니다.
DesktopPet.release.json은 이 배포의 버전, 런타임 종류와 각 파일 SHA256을 기록합니다.
"@
        [System.IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme, (New-Object System.Text.UTF8Encoding $true))
        $manifestFiles = @()
        foreach ($relative in @(Get-DesktopPetReleaseFiles $stage)) {
            $path = Join-Path $stage $relative
            $manifestFiles += [ordered]@{ path = $relative; length = (Get-Item -LiteralPath $path).Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
        }
        $manifest = [ordered]@{
            schemaVersion = 1; product = 'DesktopPet'; version = $ReleaseVersion; runtimeIdentifier = 'win-x64'
            deployment = $deployment; requiresDotNetRuntime = [bool]$RequiresRuntime; entryPoint = 'DesktopPet.App.exe'
            defaultCharacter = 'Companion'; assistantDisconnectCliVersion = 1; files = @($manifestFiles)
        }
        [System.IO.File]::WriteAllText((Join-Path $stage 'DesktopPet.release.json'), ($manifest | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding $false))
        Test-DesktopPetReleasePayload -Directory $stage -ExpectedVersion $ReleaseVersion -ExpectedDeployment $deployment | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $temporaryZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        $zipHash = (Get-FileHash -LiteralPath $temporaryZip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText($temporaryHash, ($zipHash + '  ' + [System.IO.Path]::GetFileName($zipPath) + [Environment]::NewLine), (New-Object System.Text.UTF8Encoding $false))
        [System.IO.File]::Move($temporaryZip, $zipPath)
        $publishedZip = $true
        [System.IO.File]::Move($temporaryHash, $hashPath)
        $publishedHash = $true
        $completed = $true
        return [pscustomobject]@{ Zip = $zipPath; SHA256 = $zipHash; ChecksumFile = $hashPath; Deployment = $deployment }
    }
    finally {
        if (-not $completed) {
            if ($publishedHash) { [System.IO.File]::Delete((Assert-DesktopPetChildPath -Root $outputRoot -Path $hashPath)) }
            if ($publishedZip) { [System.IO.File]::Delete((Assert-DesktopPetChildPath -Root $outputRoot -Path $zipPath)) }
        }
        if ([System.IO.File]::Exists($temporaryZip)) { [System.IO.File]::Delete((Assert-DesktopPetChildPath -Root $outputRoot -Path $temporaryZip)) }
        if ([System.IO.File]::Exists($temporaryHash)) { [System.IO.File]::Delete((Assert-DesktopPetChildPath -Root $outputRoot -Path $temporaryHash)) }
        if ([System.IO.Directory]::Exists($stage)) {
            $safeStage = Assert-DesktopPetChildPath -Root $outputRoot -Path $stage
            Assert-DesktopPetNoReparsePoint $safeStage
            Remove-Item -LiteralPath $safeStage -Recurse -Force
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\releases' }
    Assert-DesktopPetReleaseVersion $Version
    if (-not [System.IO.File]::Exists((Join-Path $repoRoot 'Assets\Characters\Companion\character.json'))) {
        throw 'The original Companion mascot is not ready. Personal development characters are never substituted into a public release.'
    }
    if (-not [System.IO.File]::Exists((Join-Path $repoRoot 'Hooks\assistant-stop-hook.ps1'))) { throw 'Hooks/assistant-stop-hook.ps1 must be prepared before packaging.' }
    $publishRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('DesktopPet.release-publish-' + [Guid]::NewGuid().ToString('N'))
    try {
        $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
        dotnet publish (Join-Path $repoRoot 'src\DesktopPet.App\DesktopPet.App.csproj') -c Release -r win-x64 `
            --self-contained $selfContained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:DistributionBuild=true "-p:Version=$Version" -o $publishRoot
        if ($LASTEXITCODE -ne 0) { throw 'Release publish failed. Restore the required runtime packs on an allowed network; deployment kind was not downgraded.' }
        New-DesktopPetReleasePackage -PublishDirectory $publishRoot -DestinationDirectory $OutputDirectory -ReleaseVersion $Version -RequiresRuntime:$FrameworkDependent
    }
    finally {
        if ([System.IO.Directory]::Exists($publishRoot)) {
            $safePublish = Assert-DesktopPetChildPath -Root ([System.IO.Path]::GetTempPath()) -Path $publishRoot
            Assert-DesktopPetNoReparsePoint $safePublish
            Remove-Item -LiteralPath $safePublish -Recurse -Force
        }
    }
}
