# Allowlist and immutable export checks. Uses workspace-owned synthetic fixtures only.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\export-public-source.ps1')
function Assert-Source([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Write-SourceFixture([string]$Root, [string]$Relative, [string]$Content = 'fixture') {
    $path = Join-Path $Root $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Content)
}
function Assert-SourceFailure([scriptblock]$Action) {
    $failed = $false
    try { & $Action | Out-Null } catch { $failed = $true }
    Assert-Source $failed 'Expected unsafe or incomplete source export to fail.'
}
$fixture = Join-Path $repoRoot ('.local\public-source-regression-' + [Guid]::NewGuid().ToString('N'))
foreach ($relative in 'DesktopPet.sln','README.md','README.ko.md','README.zh-CN.md','LICENSE',
    'src\DesktopPet.App\DesktopPet.App.csproj','src\DesktopPet.Core\Example.cs',
    'Assets\Characters\Companion\character.json','Assets\Characters\Companion\idle.png',
    'Assets\Characters\Nimbus\character.json','Assets\Characters\Ember\character.json',
    'Hooks\assistant-stop-hook.ps1','docs\PUBLIC_ASSET_PROVENANCE.md') { Write-SourceFixture $fixture $relative }
$catalogPath = Join-Path $repoRoot 'character-packs\fan-art\catalog.json'
$catalog = Get-DesktopPetFanArtCatalog -Path $catalogPath
foreach ($character in $catalog.characters) {
    $prefix = 'character-packs\fan-art\'+$character.name
    $definition = @{name=$character.name;assetKind='optional-personal-fan-art';frameSizePx=@{width=128;height=128};frames=@{idle=@('idle.png')}}
    Write-SourceFixture $fixture ($prefix+'\character.json') ($definition | ConvertTo-Json -Depth 4)
    [IO.File]::Copy((Join-Path (Join-Path $repoRoot $prefix) 'idle.png'),(Join-Path (Join-Path $fixture $prefix) 'idle.png'),$false)
    Write-SourceFixture $fixture ($prefix+'\private-unreferenced.png') 'PRIVATE_EXPORT_CANARY'
}
Write-SourceFixture $fixture 'character-packs\fan-art\catalog.json' ([IO.File]::ReadAllText($catalogPath))
foreach ($relative in 'character-packs\fan-art\POPULAR_ASSETS.md','character-packs\fan-art\popular-characters-preview.png',
    'character-packs\fan-art\masters\popular-pompompurin-v1.png',
    'character-packs\fan-art\masters\popular-cinnamoroll-v1.png',
    'character-packs\fan-art\masters\popular-kuromi-v1.png') { Write-SourceFixture $fixture $relative 'selected new artwork provenance' }
foreach ($relative in '.claude\settings.json','.codex\auth.json','.env','src\DesktopPet.Core\secrets.json',
    'src\DesktopPet.Core\bin\Release\Private.cs','Assets\Characters\PrivateFanArt\idle.png',
    'images\private-source.png','docs\preview\personal-characters-v6.png','.local\private.json',
    '.github\.codex\auth.json','.github\.claude\settings.json','scripts\.aws\credentials.ps1',
    'src\.agents\private.cs','character-packs\fan-art\UnselectedPrivate\idle.png',
    'character-packs\fan-art\masters\unselected-private.png') {
    Write-SourceFixture $fixture $relative 'PRIVATE_EXPORT_CANARY'
}
$selected = @(Get-DesktopPetPublicSourceFiles $fixture)
Assert-Source ($selected -contains 'src\DesktopPet.Core\Example.cs') 'Public code was omitted.'
Assert-Source ($selected -contains 'character-packs\fan-art\치이카와\idle.png') 'Explicitly selected optional pack was omitted.'
foreach ($character in $catalog.characters) { Assert-Source ($selected -contains ('character-packs\fan-art\'+$character.name+'\idle.png')) 'A curated optional pack was omitted.' }
Assert-Source ($selected -contains 'character-packs\fan-art\POPULAR_ASSETS.md' -and $selected -contains 'character-packs\fan-art\masters\popular-kuromi-v1.png') 'New artwork provenance was omitted.'
Assert-Source ($selected -contains 'character-packs\fan-art\popular-characters-preview.png') 'New artwork preview was omitted.'
foreach ($relative in $selected) {
    Assert-Source (-not [IO.File]::ReadAllText((Join-Path $fixture $relative)).Contains('PRIVATE_EXPORT_CANARY')) 'Private content entered the source allowlist.'
}
Write-Output 'PASS Source allowlist excludes assistant configuration, secrets, private artwork and nested build outputs'

$destination = Join-Path $fixture 'export'
$result = Export-DesktopPetPublicSource $fixture $destination
Assert-Source ([IO.File]::Exists($result.Zip) -and [IO.File]::Exists($result.Zip + '.sha256')) 'Source ZIP or checksum was not created.'
$manifest = [IO.File]::ReadAllText((Join-Path $destination 'PUBLIC_SOURCE_MANIFEST.json')) | ConvertFrom-Json
Assert-Source (@($manifest.files).Count -eq $selected.Count) 'Source manifest inventory is incomplete.'
foreach ($entry in $manifest.files) {
    $actualHash = (Get-FileHash -LiteralPath (Join-Path $destination $entry.path) -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Source ($actualHash -eq $entry.sha256) 'Source manifest checksum differs from copied content.'
}
$zip = [IO.Compression.ZipFile]::OpenRead($result.Zip)
try { Assert-Source ($zip.Entries.Count -eq $selected.Count + 1) 'Source ZIP contains unlisted extra files or lost source.' }
finally { $zip.Dispose() }
Write-Output 'PASS Export ZIP contains exactly selected source files and a verified SHA256 inventory'

$before = (Get-FileHash -LiteralPath (Join-Path $destination 'README.md')).Hash
Assert-SourceFailure { Export-DesktopPetPublicSource $fixture $destination }
Assert-Source ((Get-FileHash -LiteralPath (Join-Path $destination 'README.md')).Hash -eq $before) 'Existing source was overwritten.'
Write-Output 'PASS Repeated export refuses to overwrite an existing source folder or ZIP'

$sidecarDestination = Join-Path $fixture 'checksum-collision'
Write-SourceFixture $fixture 'checksum-collision.zip.sha256' 'existing checksum'
Assert-SourceFailure { Export-DesktopPetPublicSource $fixture $sidecarDestination }
Assert-Source ([IO.File]::ReadAllText($sidecarDestination + '.zip.sha256') -eq 'existing checksum') 'Existing checksum was overwritten.'
Assert-Source (-not (Test-Path -LiteralPath $sidecarDestination)) 'Checksum conflict created a partial source folder.'
Write-Output 'PASS Existing checksum sidecars are preserved before any source publication'

Assert-SourceFailure { Export-DesktopPetPublicSource $fixture (Join-Path $repoRoot '.local\outside-source-fixture') }
Write-Output 'PASS Export paths cannot escape their intended source workspace'

$missingFixture = Join-Path $fixture 'incomplete'
Write-SourceFixture $missingFixture 'README.md'
Assert-SourceFailure { Export-DesktopPetPublicSource $missingFixture (Join-Path $missingFixture 'out') }
Assert-Source (-not (Test-Path -LiteralPath (Join-Path $missingFixture 'out'))) 'Incomplete source created a misleading export folder.'
Write-Output 'PASS Incomplete public source fails before publishing any output'
Write-Output '6/6 public source regression checks passed'
