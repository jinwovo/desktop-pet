<#
.SYNOPSIS
    Package the optional fan-art PNG folders separately from the original public executable.
.DESCRIPTION
    Validates all declared PNGs before copying, writes a per-file hash manifest and ZIP checksum,
    and refuses existing destinations. Dot-sourcing defines functions without publishing a package.
#>
param([string]$Version = '0.7.0', [string]$OutputDirectory = '', [string]$SourceDirectory = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')
. (Join-Path $PSScriptRoot 'Get-DesktopPetFanArtCatalog.ps1')

function Get-DesktopPetFanArtPackInventory {
    param([Parameter(Mandatory=$true)][string]$Directory, [Parameter(Mandatory=$true)][string]$ExpectedName)
    $folder = Get-DesktopPetInstallPath $Directory
    Assert-DesktopPetNoReparsePoint $folder
    $metadataPath = Join-Path $folder 'character.json'
    Assert-DesktopPetNoReparsePoint $metadataPath
    if ((Get-Item -LiteralPath $metadataPath).Length -gt 64MB) { throw "Optional pack metadata exceeds its source budget: $ExpectedName" }
    $definition = [IO.File]::ReadAllText($metadataPath) | ConvertFrom-Json
    if ($definition.name -ne $ExpectedName -or $definition.assetKind -ne 'optional-personal-fan-art' -or $null -eq $definition.frames) {
        throw "Optional pack identity or artwork classification is invalid: $ExpectedName"
    }
    $width = [double]$definition.frameSizePx.width
    $height = [double]$definition.frameSizePx.height
    if ($width -ne [Math]::Floor($width) -or $height -ne [Math]::Floor($height) -or $width -lt 1 -or $height -lt 1 -or $width -gt 1024 -or $height -gt 1024) {
        throw "Optional pack dimensions are invalid: $ExpectedName"
    }
    if (@($definition.frames.idle).Count -eq 0) { throw "Optional pack needs idle artwork: $ExpectedName" }
    $references = New-Object 'System.Collections.Generic.List[string]'
    foreach ($state in $definition.frames.PSObject.Properties) {
        foreach ($reference in @($state.Value)) {
            if ($reference -isnot [string]) { throw "Optional pack frame paths must be strings: $ExpectedName" }
            $references.Add($reference)
        }
    }
    if ($null -ne $definition.PSObject.Properties['walkRig']) {
        if ($definition.walkRig -isnot [pscustomobject] -or $definition.walkRig.version -ne 1) { throw "Optional pack live rig is invalid: $ExpectedName" }
        foreach ($view in 'side','turn') {
            foreach ($part in 'body','nearFoot','farFoot') {
                $reference = $definition.walkRig.$view.$part
                if ($reference -isnot [string]) { throw "Optional pack is missing a live-rig PNG: $ExpectedName/$view/$part" }
                $references.Add($reference)
            }
        }
    }
    $frames = @($references | Sort-Object -Unique)
    if ($frames.Count + 1 -gt 256 -or [long]$width * [long]$height * 4 * $frames.Count -gt 64MB) { throw "Optional pack exceeds the import image budget: $ExpectedName" }
    $totalBytes = (Get-Item -LiteralPath $metadataPath).Length
    Add-Type -AssemblyName System.Drawing
    foreach ($reference in $frames) {
        Assert-DesktopPetPayloadRelativePath $reference
        if ([IO.Path]::GetExtension($reference) -ine '.png') { throw "Optional pack resources must be PNGs: $reference" }
        $path = Assert-DesktopPetChildPath -Root $folder -Path (Join-Path $folder $reference)
        Assert-DesktopPetNoReparsePoint $path
        if (-not [IO.File]::Exists($path)) { throw "Optional pack PNG is missing: $ExpectedName/$reference" }
        $totalBytes += (Get-Item -LiteralPath $path).Length
        if ($totalBytes -gt 64MB) { throw "Optional pack exceeds the import source budget: $ExpectedName" }
        $stream = [IO.File]::OpenRead($path)
        try {
            $header = New-Object byte[] 24
            $read = 0
            while ($read -lt 24) {
                $count = $stream.Read($header,$read,24-$read)
                if ($count -eq 0) { throw "Optional pack has a truncated PNG header: $reference" }
                $read += $count
            }
            $signature = @(137,80,78,71,13,10,26,10)
            for ($i=0;$i -lt 8;$i++) { if ($header[$i] -ne $signature[$i]) { throw "Optional pack has an invalid PNG signature: $reference" } }
            if ([Text.Encoding]::ASCII.GetString($header,12,4) -ne 'IHDR') { throw "Optional pack has an invalid PNG header: $reference" }
            $widthBytes = [byte[]]$header[16..19]; [Array]::Reverse($widthBytes)
            $heightBytes = [byte[]]$header[20..23]; [Array]::Reverse($heightBytes)
            if ([BitConverter]::ToUInt32($widthBytes,0) -ne $width -or [BitConverter]::ToUInt32($heightBytes,0) -ne $height) {
                throw "Optional pack PNG dimensions differ from its manifest: $reference"
            }
            $stream.Position = 0
            $bitmap = [Drawing.Bitmap]::FromStream($stream)
            try {
                if ($bitmap.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid -or $bitmap.Width -ne $width -or $bitmap.Height -ne $height) { throw "Optional pack PNG failed decoding: $reference" }
                [void]$bitmap.GetPixel(0,0)
            } finally { $bitmap.Dispose() }
        } finally { $stream.Dispose() }
    }
    [pscustomobject]@{Name=$ExpectedName; Directory=$folder; Files=@('character.json') + $frames; FrameCount=$frames.Count; SourceBytes=$totalBytes}
}

function New-DesktopPetCharacterPacksPackage {
    param([Parameter(Mandatory=$true)][string]$SourceDirectory,
          [Parameter(Mandatory=$true)][string]$DestinationDirectory,
          [string]$ReleaseVersion='0.7.0')
    if ($ReleaseVersion -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Use a semantic character-pack version.' }
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $source = Get-DesktopPetInstallPath $SourceDirectory
    $output = Assert-DesktopPetChildPath -Root $repoRoot -Path $DestinationDirectory
    Assert-DesktopPetNoReparsePoint $source
    Assert-DesktopPetNoReparsePoint $output
    if ($output -eq $source -or $output.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Optional pack source and output directories must not overlap.' }
    if ([IO.File]::Exists($output)) { throw 'Optional-pack output must be a directory.' }
    $zip = Join-Path $output ("DesktopPet-$ReleaseVersion-fan-art-packs.zip")
    if ((Test-Path -LiteralPath $zip) -or (Test-Path -LiteralPath ($zip+'.sha256'))) { throw 'Optional pack ZIP or checksum already exists; existing content is never overwritten.' }
    $catalog = Get-DesktopPetFanArtCatalog -Path (Join-Path $source 'catalog.json')
    $names = @($catalog.characters | ForEach-Object { $_.name })
    $packs = @()
    foreach ($name in $names) { $packs += Get-DesktopPetFanArtPackInventory -Directory (Join-Path $source $name) -ExpectedName $name }
    $collectionFiles = @('README.md','NOTICE.md','POPULAR_ASSETS.md','catalog.json')
    foreach ($notice in $collectionFiles) {
        $noticePath = Join-Path $source $notice
        Assert-DesktopPetNoReparsePoint $noticePath
        if (-not [IO.File]::Exists($noticePath)) { throw "Optional collection is missing $notice" }
    }
    $stage = Join-Path $output ('DesktopPet-fan-art-packs-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($stage)
    foreach ($notice in $collectionFiles) { [IO.File]::Copy((Join-Path $source $notice),(Join-Path $stage $notice),$false) }
    $entries = @()
    foreach ($pack in $packs) {
        foreach ($relative in $pack.Files) {
            $origin = Assert-DesktopPetChildPath -Root $pack.Directory -Path (Join-Path $pack.Directory $relative)
            Assert-DesktopPetNoReparsePoint $origin
            $target = Assert-DesktopPetChildPath -Root $stage -Path (Join-Path (Join-Path $stage $pack.Name) $relative)
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            [IO.File]::Copy($origin,$target,$false)
        }
        # Recheck the actual staged metadata/PNGs before publishing the archive.
        [void](Get-DesktopPetFanArtPackInventory -Directory (Join-Path $stage $pack.Name) -ExpectedName $pack.Name)
    }
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $relative = Get-DesktopPetPayloadRelativePath -Root $stage -Path $file.FullName
        $entries += [ordered]@{path=$relative.Replace('\','/');length=$file.Length;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
    }
    $manifest = [ordered]@{schemaVersion=1;version=$ReleaseVersion;assetKind='optional-personal-fan-art';characters=$names;catalog='catalog.json';notice='NOTICE.md';provenance='POPULAR_ASSETS.md';files=@($entries)}
    [IO.File]::WriteAllText((Join-Path $stage 'CHARACTER_PACKS_MANIFEST.json'),($manifest|ConvertTo-Json -Depth 6),(New-Object Text.UTF8Encoding $false))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zip+'.sha256'),($hash+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine),(New-Object Text.UTF8Encoding $false))
    [pscustomobject]@{Zip=$zip;SHA256=$hash;Directory=$stage;CharacterCount=$packs.Count;FrameCounts=@($packs.FrameCount)}
}

if ($MyInvocation.InvocationName -ne '.') {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    if (-not $SourceDirectory) { $SourceDirectory = Join-Path $repoRoot 'character-packs\fan-art' }
    if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('artifacts\character-packs\'+$Version+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')) }
    New-DesktopPetCharacterPacksPackage -SourceDirectory $SourceDirectory -DestinationDirectory $OutputDirectory -ReleaseVersion $Version
}
