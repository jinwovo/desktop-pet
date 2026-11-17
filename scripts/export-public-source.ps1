<#
.SYNOPSIS
    Creates a fresh, allowlisted source folder and ZIP for publishing on GitHub.
.DESCRIPTION
    Copies source code, public documentation, original characters and explicitly selected fan-art packs.
    Never copies unselected private artwork, assistant configuration, build outputs or Git metadata.
    Existing destinations are rejected; this script never uploads, deletes or overwrites a repository.
#>
param([string]$OutputDirectory = '')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')
. (Join-Path $PSScriptRoot 'Get-DesktopPetFanArtCatalog.ps1')

function Get-DesktopPetPublicSourceFiles {
    param([Parameter(Mandatory = $true)][string]$SourceRoot)
    $root = Get-DesktopPetInstallPath $SourceRoot
    Assert-DesktopPetNoReparsePoint $root
    . (Join-Path $PSScriptRoot 'package-character-packs.ps1')
    $files = New-Object 'System.Collections.Generic.List[string]'
    $fixed = @(
        'DesktopPet.sln', 'global.json', '.gitignore', '.gitattributes', '.editorconfig',
        'Assets\CharacterBehaviors.json',
        'README.md', 'README.ko.md', 'README.zh-CN.md', 'CONTRIBUTING.md', 'SECURITY.md', 'CHANGELOG.md', 'LICENSE',
        'docs\DISTRIBUTION.md', 'docs\PUBLIC_ASSET_PROVENANCE.md', 'docs\CHARACTER_PACKS.md', 'docs\PUBLIC_RELEASE.md', 'docs\REFERENCE_REVIEW.ko.md', 'docs\NVIDIA_WORK.md',
        'tests\test_nvidia_work.py',
        'docs\preview\public-characters.png', 'docs\preview\public-walk.gif', 'docs\preview\public-walk.html',
        'docs\preview\public-settings.png', 'docs\preview\live-rig-v8.png',
        'docs\preview\character-gallery-v060.png',
        'images\companion-master-v1.png', 'images\companion-walk-master-v2.png', 'images\rig-companion-v7.png',
        'images\public-nimbus-master-v1.png', 'images\public-ember-master-v1.png'
        'Assets\Characters\Nimbus\.desktop-pet-public-generated', 'Assets\Characters\Ember\.desktop-pet-public-generated'
        'character-packs\fan-art\README.md', 'character-packs\fan-art\NOTICE.md',
        'character-packs\fan-art\catalog.json', 'character-packs\fan-art\POPULAR_ASSETS.md',
        'character-packs\fan-art\popular-characters-preview.png',
        'character-packs\fan-art\masters\popular-pompompurin-v1.png',
        'character-packs\fan-art\masters\popular-cinnamoroll-v1.png',
        'character-packs\fan-art\masters\popular-kuromi-v1.png'
        'character-packs\fan-art\폼폼푸린\.desktop-pet-popular-generated',
        'character-packs\fan-art\시나모롤\.desktop-pet-popular-generated',
        'character-packs\fan-art\쿠로미\.desktop-pet-popular-generated'
    )
    foreach ($relative in $fixed) {
        $path = Assert-DesktopPetChildPath -Root $root -Path (Join-Path $root $relative)
        if ([IO.File]::Exists($path)) { Assert-DesktopPetNoReparsePoint $path; $files.Add($relative) }
    }
    $rules = @{
        'src' = @('.cs','.csproj','.xaml','.resx')
        'tests' = @('.cs','.csproj','.ps1')
        'scripts' = @('.cs','.ps1','.py')
        'Hooks' = @('.ps1')
        '.github' = @('.yml','.yaml','.md')
        'Assets\Characters\Companion' = @('.png','.json')
        'Assets\Characters\Nimbus' = @('.png','.json')
        'Assets\Characters\Ember' = @('.png','.json')
    }
    # Keep compatibility with source-only fixtures without a collection, while any present
    # collection must pass the same curated identity boundary as the optional ZIP.
    $catalogPath = Join-Path $root 'character-packs\fan-art\catalog.json'
    if (-not [IO.File]::Exists($catalogPath)) { $catalogPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'character-packs\fan-art\catalog.json' }
    $catalog = Get-DesktopPetFanArtCatalog -Path $catalogPath
    foreach ($character in $catalog.characters) {
        $prefix = 'character-packs\fan-art\'+$character.name
        $folder = Join-Path $root $prefix
        if (-not [IO.Directory]::Exists($folder)) { continue }
        $pack = Get-DesktopPetFanArtPackInventory -Directory $folder -ExpectedName $character.name
        foreach ($relative in $pack.Files) { $files.Add($prefix+'\'+$relative.Replace('/','\')) }
    }
    foreach ($prefix in $rules.Keys) {
        $folder = Join-Path $root $prefix
        if (-not [IO.Directory]::Exists($folder)) { continue }
        $pending = New-Object 'System.Collections.Generic.Queue[string]'
        $pending.Enqueue($folder)
        while ($pending.Count -gt 0) {
            $current = $pending.Dequeue()
            Assert-DesktopPetNoReparsePoint $current
            foreach ($entry in Get-ChildItem -LiteralPath $current -Force) {
                if ($entry.PSIsContainer -and $entry.Name -in @('bin','obj','.git','.local','.claude','.codex','.agents','.aws','.azure','.ssh','node_modules','artifacts')) { continue }
                if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Public source cannot contain linked entries: $($entry.Name)" }
                if ($entry.PSIsContainer) { $pending.Enqueue($entry.FullName); continue }
                if ($rules[$prefix] -contains $entry.Extension.ToLowerInvariant()) {
                    $files.Add((Get-DesktopPetPayloadRelativePath -Root $root -Path $entry.FullName))
                }
            }
        }
    }
    if ([IO.File]::Exists((Join-Path $root 'Assets\AppIcon.ico'))) { $files.Add('Assets\AppIcon.ico') }
    return @($files | Sort-Object -Unique)
}

function Export-DesktopPetPublicSource {
    param([Parameter(Mandatory = $true)][string]$SourceRoot,
          [Parameter(Mandatory = $true)][string]$DestinationDirectory)
    $root = Get-DesktopPetInstallPath $SourceRoot
    $destination = Assert-DesktopPetChildPath -Root $root -Path $DestinationDirectory
    Assert-DesktopPetNoReparsePoint $destination
    if (Test-Path -LiteralPath $destination) { throw 'Use a fresh destination. Existing source folders are never overwritten.' }
    $zipPath = $destination + '.zip'
    if (Test-Path -LiteralPath $zipPath) { throw 'The source ZIP already exists.' }
    if (Test-Path -LiteralPath ($zipPath + '.sha256')) { throw 'The source checksum already exists.' }
    $files = @(Get-DesktopPetPublicSourceFiles $root)
    foreach ($required in 'DesktopPet.sln','README.md','README.ko.md','README.zh-CN.md','LICENSE','src\DesktopPet.App\DesktopPet.App.csproj',
        'Assets\Characters\Companion\character.json','Assets\Characters\Nimbus\character.json','Assets\Characters\Ember\character.json',
        'Hooks\assistant-stop-hook.ps1','docs\PUBLIC_ASSET_PROVENANCE.md') {
        if ($files -notcontains $required) { throw "Public source is incomplete: $required" }
    }
    [void][IO.Directory]::CreateDirectory($destination)
    $manifest = @()
    foreach ($relative in $files) {
        $source = Assert-DesktopPetChildPath -Root $root -Path (Join-Path $root $relative)
        Assert-DesktopPetNoReparsePoint $source
        $target = Assert-DesktopPetChildPath -Root $destination -Path (Join-Path $destination $relative)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
        [IO.File]::Copy($source, $target, $false)
        $manifest += [ordered]@{ path = $relative.Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $report = [ordered]@{ schemaVersion=1; repository='https://github.com/jinwovo/desktop-pet'; files=@($manifest) }
    [IO.File]::WriteAllText((Join-Path $destination 'PUBLIC_SOURCE_MANIFEST.json'), ($report | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($destination, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zipPath + '.sha256'), ($hash + '  ' + [IO.Path]::GetFileName($zipPath) + [Environment]::NewLine), (New-Object Text.UTF8Encoding $false))
    [pscustomobject]@{ Directory=$destination; Zip=$zipPath; FileCount=$files.Count; SHA256=$hash }
}

if ($MyInvocation.InvocationName -ne '.') {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('.local\public-source-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
    Export-DesktopPetPublicSource -SourceRoot $repoRoot -DestinationDirectory $OutputDirectory
}
