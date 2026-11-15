# Shared curated collection metadata. Dot-sourcing does not copy or publish artwork.
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')

function Get-DesktopPetFanArtCatalog {
    param([Parameter(Mandatory=$true)][string]$Path)
    Assert-DesktopPetNoReparsePoint $Path
    if (-not [IO.File]::Exists($Path) -or (Get-Item -LiteralPath $Path).Length -gt 64KB) { throw 'The optional character catalog is missing or too large.' }
    $catalog = [IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($catalog.schemaVersion -ne 1 -or $catalog.assetKind -ne 'optional-personal-fan-art') { throw 'Unsupported optional character catalog.' }
    # IDs and on-disk names are a deliberate publication boundary, not arbitrary paths from JSON.
    $approved = @{'chiikawa'='치이카와';'baikinman'='세균맨';'hello-kitty'='헬로키티';'pompompurin'='폼폼푸린';'cinnamoroll'='시나모롤';'kuromi'='쿠로미'}
    $seenIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $seenNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $characters = @($catalog.characters)
    if ($characters.Count -ne $approved.Count) { throw 'The optional catalog must describe the six curated packs exactly.' }
    foreach ($entry in $characters) {
        if ($entry.id -isnot [string] -or $entry.id -cnotmatch '^[a-z][a-z0-9-]*$' -or -not $approved.ContainsKey($entry.id) -or $entry.name -cne $approved[$entry.id]) { throw 'The optional catalog contains an unknown character identity.' }
        Assert-DesktopPetPayloadRelativePath $entry.name
        if ($entry.name.Contains('/') -or $entry.name.Contains('\') -or -not $seenIds.Add($entry.id) -or -not $seenNames.Add($entry.name)) { throw 'The optional catalog contains an unsafe or duplicate character.' }
        if ([string]::IsNullOrWhiteSpace($entry.englishName) -or @($entry.aliases).Count -eq 0) { throw 'Catalog entries require an English name and search aliases.' }
        foreach ($alias in @($entry.aliases)) { if ($alias -isnot [string] -or [string]::IsNullOrWhiteSpace($alias) -or $alias.Length -gt 80) { throw 'Invalid character catalog alias.' } }
        $sourceUri = $null
        if (-not [Uri]::TryCreate([string]$entry.popularity.source,[UriKind]::Absolute,[ref]$sourceUri) -or $sourceUri.Scheme -ne 'https') { throw 'Catalog entries require an HTTPS source.' }
        if ($null -ne $entry.popularity.rank -and ($entry.popularity.rank -isnot [int] -or $entry.popularity.rank -lt 1)) { throw 'A published popularity rank must be a positive integer.' }
    }
    return $catalog
}
