# Optional fan-art package checks using isolated workspace fixtures. No live user library changes.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\package-character-packs.ps1')
function Assert-Optional([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-OptionalFailure([scriptblock]$Action) {
    $failed=$false
    try { & $Action | Out-Null } catch { $failed=$true }
    Assert-Optional $failed 'Expected invalid optional pack operation to fail.'
}
function Write-OptionalMetadata([string]$Folder,[string]$Name,[string]$Reference='idle.png',[int]$Width=128) {
    $metadata = @{name=$Name;assetKind='optional-personal-fan-art';frameSizePx=@{width=$Width;height=128};frames=@{idle=@($Reference)}}
    [IO.File]::WriteAllText((Join-Path $Folder 'character.json'),($metadata|ConvertTo-Json -Depth 4),(New-Object Text.UTF8Encoding $false))
}
$root = Join-Path $repoRoot ('.local\optional-pack-regression-'+[Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'source'
[void][IO.Directory]::CreateDirectory($source)
$names = @('치이카와','세균맨','헬로키티')
foreach ($name in $names) {
    $original = Get-DesktopPetFanArtPackInventory -Directory (Join-Path (Join-Path $repoRoot 'character-packs\fan-art') $name) -ExpectedName $name
    Assert-Optional ($original.FrameCount -eq 211 -and $original.Files.Count -eq 212) 'A declared optional character resource was omitted.'
    $folder = Join-Path $source $name
    [void][IO.Directory]::CreateDirectory($folder)
    Write-OptionalMetadata $folder $name
    [IO.File]::Copy((Join-Path $original.Directory 'idle.png'),(Join-Path $folder 'idle.png'),$false)
    [IO.File]::WriteAllText((Join-Path $folder 'unreferenced-secret.txt'),'NEVER_PACKAGE_THIS_FIXTURE')
}
foreach ($notice in 'README.md','NOTICE.md') {
    [IO.File]::Copy((Join-Path (Join-Path $repoRoot 'character-packs\fan-art') $notice),(Join-Path $source $notice),$false)
}
Write-Output 'PASS All three optional source packs contain their 211 declared PNGs and retain fan-art identity'

$destination = Join-Path $root 'release'
[void][IO.Directory]::CreateDirectory($destination)
[IO.File]::WriteAllText((Join-Path $destination 'existing-public-release.txt'),'keep existing release')
$package = New-DesktopPetCharacterPacksPackage -SourceDirectory $source -DestinationDirectory $destination -ReleaseVersion '0.5.0'
Assert-Optional ([IO.File]::ReadAllText((Join-Path $destination 'existing-public-release.txt')) -eq 'keep existing release') 'Packaging changed another release.'
$expectedHash = (Get-FileHash -LiteralPath $package.Zip -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Optional ([IO.File]::ReadAllText($package.Zip+'.sha256').StartsWith($expectedHash)) 'Optional ZIP checksum differs.'
$manifest = [IO.File]::ReadAllText((Join-Path $package.Directory 'CHARACTER_PACKS_MANIFEST.json')) | ConvertFrom-Json
Assert-Optional (@($manifest.characters).Count -eq 3 -and @($manifest.files).Count -eq 8 -and $manifest.assetKind -eq 'optional-personal-fan-art') 'Optional package manifest has the wrong collection or file inventory.'
foreach ($entry in $manifest.files) {
    Assert-Optional ((Get-FileHash -LiteralPath (Join-Path $package.Directory $entry.path) -Algorithm SHA256).Hash -ieq $entry.sha256) 'Staged optional content differs from its hash manifest.'
    Assert-Optional ($entry.path -notmatch 'unreferenced|secret') 'Unreferenced source files entered the package.'
}
$archive = [IO.Compression.ZipFile]::OpenRead($package.Zip)
try {
    Assert-Optional ($archive.Entries.Count -eq 9) 'Optional ZIP has unexpected files.'
    foreach ($name in $names) { Assert-Optional (@($archive.Entries | Where-Object {$_.FullName.Replace('\','/') -eq ($name+'/character.json')}).Count -eq 1) 'A Unicode import folder is missing.' }
} finally { $archive.Dispose() }
Write-Output 'PASS ZIP preserves all Unicode import folders, notice, SHA256 inventory and existing unrelated release data'

$zipBefore = (Get-FileHash -LiteralPath $package.Zip).Hash
Assert-OptionalFailure { New-DesktopPetCharacterPacksPackage -SourceDirectory $source -DestinationDirectory $destination -ReleaseVersion '0.5.0' }
Assert-Optional ((Get-FileHash -LiteralPath $package.Zip).Hash -eq $zipBefore) 'Repeated packaging overwrote the ZIP.'
$sidecarOutput = Join-Path $root 'checksum-only'
[void][IO.Directory]::CreateDirectory($sidecarOutput)
$sidecar = Join-Path $sidecarOutput 'DesktopPet-0.5.0-fan-art-packs.zip.sha256'
[IO.File]::WriteAllText($sidecar,'keep checksum')
Assert-OptionalFailure { New-DesktopPetCharacterPacksPackage -SourceDirectory $source -DestinationDirectory $sidecarOutput }
Assert-Optional ([IO.File]::ReadAllText($sidecar) -eq 'keep checksum') 'An existing checksum was overwritten.'
Write-Output 'PASS Existing optional archives and checksum sidecars cannot be overwritten'

$badFolder = Join-Path $source $names[0]
foreach ($reference in @('..\outside.png','C:\outside.png','idle.png:stream','unreferenced-secret.txt','missing.png')) {
    Write-OptionalMetadata $badFolder $names[0] $reference
    Assert-OptionalFailure { Get-DesktopPetFanArtPackInventory -Directory $badFolder -ExpectedName $names[0] }
}
Write-OptionalMetadata $badFolder $names[0] 'idle.png' 64
Assert-OptionalFailure { Get-DesktopPetFanArtPackInventory -Directory $badFolder -ExpectedName $names[0] }
Write-OptionalMetadata $badFolder $names[0]
[IO.File]::WriteAllText((Join-Path $badFolder 'idle.png'),'not PNG data')
Assert-OptionalFailure { Get-DesktopPetFanArtPackInventory -Directory $badFolder -ExpectedName $names[0] }
Write-Output 'PASS Unsafe, missing, non-PNG, falsely sized and corrupt image resources fail before packaging'

Assert-OptionalFailure { New-DesktopPetCharacterPacksPackage -SourceDirectory $source -DestinationDirectory (Join-Path $root 'unsafe-version') -ReleaseVersion '..\unsafe' }
Assert-OptionalFailure { New-DesktopPetCharacterPacksPackage -SourceDirectory $source -DestinationDirectory (Join-Path $source 'inside-source') }
Assert-Optional (-not (Test-Path -LiteralPath (Join-Path $source 'inside-source'))) 'Overlapping output changed its source.'
Write-Output 'PASS Unsafe versions and overlapping source/output roots leave the source untouched'
Write-Output '5/5 optional character-pack regression groups passed'
