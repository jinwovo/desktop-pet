$ErrorActionPreference = 'Stop'
$auditTestRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $auditTestRoot 'scripts/audit-public-release.ps1')
$fixture = Join-Path $auditTestRoot ('.local/public-audit-test-' + [Guid]::NewGuid().ToString('N'))
$trusted = Join-Path $fixture 'trusted'
[void][IO.Directory]::CreateDirectory($trusted)
$version = '1.2.0'
$names = @("DesktopPet-$version-win-x64.zip", "DesktopPet-$version-win-x64.zip.sha256",
    "DesktopPet-$version-fan-art-packs.zip", "DesktopPet-$version-fan-art-packs.zip.sha256")
foreach ($name in @($names[0], $names[2])) {
    $path = Join-Path $trusted $name
    [IO.File]::WriteAllText($path, 'synthetic archive bytes')
    [IO.File]::WriteAllText(($path + '.sha256'), ((Get-FileHash $path).Hash + '  *' + $name))
}
$baseline = @()
for ($i = 0; $i -lt 4; $i++) {
    $baseline += [pscustomobject]@{ id=($i+1); name=$names[$i]; size=(Get-Item (Join-Path $trusted $names[$i])).Length; state='uploaded' }
}
function Invoke-Case {
    param([string]$Name, [scriptblock]$Mutate, [bool]$ExpectedSuccess=$false, [int]$CorruptIndex=-1, [switch]$BadTrusted)
    $assets = $baseline | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    if ($Mutate) { & $Mutate $assets }
    $directory = Join-Path $fixture ([Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($directory)
    $sidecar = Join-Path $trusted $names[1]
    $original = [IO.File]::ReadAllBytes($sidecar)
    if ($BadTrusted) {
        $invalid = [IO.File]::ReadAllBytes($sidecar)
        $invalid[0] = if ($invalid[0] -eq 48) { 49 } else { 48 }
        [IO.File]::WriteAllBytes($sidecar, $invalid)
    }
    $failure = $null
    try {
        try {
            $result = @(Assert-DesktopPetPublicAssetCopies -Version $version -ArtifactDirectory $trusted -Assets $assets -DownloadDirectory $directory -Download {
                param($asset, $destination)
                Copy-Item -LiteralPath (Join-Path $trusted $asset.name) -Destination $destination
                if ($CorruptIndex -ge 0 -and $asset.name -ceq $names[$CorruptIndex]) {
                    $bytes = [IO.File]::ReadAllBytes($destination)
                    $bytes[0] = $bytes[0] -bxor 1
                    [IO.File]::WriteAllBytes($destination, $bytes)
                }
            })
        } catch { $failure = $_ }
        if ($ExpectedSuccess) {
            if ($failure) { throw $failure }
            if ($result.Count -ne 4) { throw 'Unexpected return pipeline output.' }
            foreach ($row in $result) {
                if ($names -cnotcontains $row.Name -or $row.Sha256 -cne (Get-FileHash (Join-Path $trusted $row.Name)).Hash -or
                    $row.Size -ne (Get-Item (Join-Path $trusted $row.Name)).Length) { throw 'Returned evidence mismatch.' }
            }
        } elseif (-not $failure) { throw ('Invalid release accepted: ' + $Name) }
        Write-Output ('PASS ' + $Name)
    } finally { [IO.File]::WriteAllBytes($sidecar, $original) }
}
Invoke-Case 'All four public files match trusted artifact' -ExpectedSuccess $true
Invoke-Case 'Missing expected asset' { param($a) $a[0].name='unexpected.zip' }
Invoke-Case 'Duplicate asset ID with four files' { param($a) $a[1].id=$a[0].id }
Invoke-Case 'Wrong-case filename' { param($a) $a[0].name=$a[0].name.ToLowerInvariant() }
Invoke-Case 'Traversal name' { param($a) $a[0].name='../outside.zip' }
Invoke-Case 'Incomplete upload' { param($a) $a[0].state='new' }
Invoke-Case 'Metadata size mismatch' { param($a) $a[0].size++ }
Invoke-Case 'Same-size altered archive' -CorruptIndex 0
Invoke-Case 'Same-size altered sidecar' -CorruptIndex 1
Invoke-Case 'Invalid trusted checksum' -BadTrusted
Write-Output 'PASS 10 public release audit checks (offline, no release mutations)'
