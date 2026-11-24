# Offline check of every relative documentation/image link in the public entry points.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$documents = @('README.md','README.ko.md','README.zh-CN.md','CONTRIBUTING.md','SECURITY.md','CHANGELOG.md',
    'docs\DISTRIBUTION.md','docs\CHARACTER_PACKS.md','docs\PUBLIC_ASSET_PROVENANCE.md','docs\PUBLIC_RELEASE.md',
    'character-packs\fan-art\README.md','character-packs\fan-art\NOTICE.md')
$documents += 'character-packs\fan-art\POPULAR_ASSETS.md'
$documents += 'docs\REFERENCE_REVIEW.ko.md'
$documents += 'docs\NVIDIA_WORK.md'
$documents += 'docs\RELEASE_1_0.md'
$documents += 'docs\ROADMAP_2_0.md'
$documents += 'docs\RELEASE_AUDIT_1_3.md'
$documents += 'docs\RELEASE_AUDIT_1_2.md'
$checked = 0
foreach ($relative in $documents) {
    $path = Join-Path $repoRoot $relative
    $content = [IO.File]::ReadAllText($path)
    if ($content.Contains('C:\Users\kownj') -or $content.Contains('C:\workspace\desktop-pet-main')) { throw "Public documentation includes a developer-specific path: $relative" }
    $withoutCode = [regex]::Replace($content, '(?s)```.*?```', '')
    $targets = New-Object 'System.Collections.Generic.List[string]'
    foreach ($match in [regex]::Matches($withoutCode, '\]\(([^)]+)\)')) { $targets.Add($match.Groups[1].Value) }
    foreach ($match in [regex]::Matches($withoutCode, 'src="([^"]+)"')) { $targets.Add($match.Groups[1].Value) }
    foreach ($target in $targets) {
        if ($target -match '^(https?://|mailto:|#)') { continue }
        $local = [Uri]::UnescapeDataString(($target -split '#')[0].Trim('<','>'))
        $resolved = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($path)) $local))
        if (-not $resolved.StartsWith($repoRoot + '\',[StringComparison]::OrdinalIgnoreCase) -or -not [IO.File]::Exists($resolved)) {
            throw "Broken or escaping public documentation link: $relative -> $target"
        }
        $checked++
    }
}
foreach ($readme in 'README.md','README.ko.md','README.zh-CN.md') {
    $content = [IO.File]::ReadAllText((Join-Path $repoRoot $readme))
    if (-not $content.Contains('public-walk.gif') -or -not $content.Contains('LICENSE') -or -not $content.Contains('Nimbus') -or -not $content.Contains('Ember')) { throw "Public README lacks its preview, license or character collection: $readme" }
}
Write-Output "PASS $($documents.Count) public documents resolve $checked local links without developer-specific paths"
Write-Output 'PASS English, Korean and Simplified Chinese entry points include the preview, license and public collection'
