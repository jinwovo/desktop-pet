<# Read-only release audit. Dot-sourcing defines functions without network access. #>
param([switch]$AuditOneTwo)

. (Join-Path $PSScriptRoot 'publish-evolution-release.ps1')

function Assert-DesktopPetPublicAssetCopies {
    param([string]$Version, [string]$ArtifactDirectory, [object[]]$Assets,
          [string]$DownloadDirectory, [scriptblock]$Download)
    $ErrorActionPreference = 'Stop'
    $null = Get-DesktopPetEvolutionThreshold $Version
    $expected = @("DesktopPet-$Version-win-x64.zip", "DesktopPet-$Version-win-x64.zip.sha256",
        "DesktopPet-$Version-fan-art-packs.zip", "DesktopPet-$Version-fan-art-packs.zip.sha256")
    Assert-DesktopPetEvolutionNoLinks $ArtifactDirectory
    Assert-DesktopPetEvolutionNoLinks $DownloadDirectory
    if (-not [IO.Directory]::Exists($DownloadDirectory) -or @(Get-ChildItem -LiteralPath $DownloadDirectory -Force).Count) {
        throw 'Use an existing empty download directory.'
    }
    $files = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Force)
    if ($files.Count -ne 4 -or $Assets.Count -ne 4 -or -not $Download) { throw 'Expected exactly four trusted files and four uploaded assets.' }
    foreach ($name in $expected) {
        $file = @($files | Where-Object { -not $_.PSIsContainer -and $_.Name -ceq $name })
        $asset = @($Assets | Where-Object { $_.name -ceq $name })
        $size = 0L
        if ($file.Count -ne 1 -or $asset.Count -ne 1 -or $asset[0].state -cne 'uploaded' -or
            [string]$asset[0].id -cnotmatch '^[1-9][0-9]*$' -or
            -not [long]::TryParse([string]$asset[0].size, [ref]$size) -or $size -le 0 -or $file[0].Length -ne $size) {
            throw 'Missing, ambiguous, incomplete or incorrectly sized release asset.'
        }
        Assert-DesktopPetEvolutionNoLinks $file[0].FullName
    }
    if (@($Assets | Select-Object -ExpandProperty id -Unique).Count -ne 4) { throw 'Asset IDs must be unique.' }
    Assert-DesktopPetEvolutionChecksums -Version $Version -Directory $ArtifactDirectory
    $result = @()
    foreach ($name in $expected) {
        $asset = @($Assets | Where-Object { $_.name -ceq $name })[0]
        $destination = Join-Path $DownloadDirectory $name
        $null = & $Download $asset $destination
        Assert-DesktopPetEvolutionNoLinks $destination
        if (-not [IO.File]::Exists($destination) -or (Get-Item -LiteralPath $destination).Length -ne $asset.size) {
            throw 'A public asset download is missing or has the wrong size.'
        }
        $actual = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        $trustedDigest = (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory $name) -Algorithm SHA256).Hash
        if ($actual -cne $trustedDigest) { throw ('Public asset differs from the verified CI artifact: ' + $name) }
        $result += [pscustomobject]@{ Name=$name; AssetId=$asset.id; Size=$asset.size; Sha256=$actual }
    }
    Assert-DesktopPetEvolutionChecksums -Version $Version -Directory $DownloadDirectory
    return $result
}

function Invoke-DesktopPetOneTwoReadOnlyAudit {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'jinwovo/desktop-pet' -or
        [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) { throw 'This audit requires the repository Actions read token.' }
    $root = Split-Path -Parent $PSScriptRoot
    $expectedCommit = '4338fbe22f866762496c7dce37dd91805f88491b'
    $tag = (& git -C $root rev-parse --verify 'refs/tags/v1.2.0^{commit}' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tag -cne $expectedCommit) { throw 'Immutable 1.2.0 tag mismatch.' }
    $api = 'https://api.github.com/repos/jinwovo/desktop-pet'
    $headers = @{ Authorization=('Bearer ' + $env:GH_TOKEN); Accept='application/vnd.github+json';
        'User-Agent'='DesktopPet-read-only-release-audit'; 'Cache-Control'='no-cache' }
    $anonymous = @{ Accept='application/vnd.github+json'; 'User-Agent'='DesktopPet-public-release-audit'; 'Cache-Control'='no-cache' }
    $stamp = [Guid]::NewGuid().ToString('N')
    $run = Invoke-RestMethod -Uri ($api + '/actions/runs/44684227455?audit=' + $stamp) -Headers $headers
    if ($run.head_sha -cne $expectedCommit -or $run.path -cne '.github/workflows/draft-release.yml' -or
        $run.status -cne 'completed' -or $run.event -cne 'push' -or $run.head_branch -cne 'v1.2.0') { throw 'Trusted source CI run mismatch.' }
    # The release workflow downloads its own verify-job artifact, not a separate main build.
    # ZIP timestamps can differ between independent builds of the same source commit.
    $job = Invoke-RestMethod -Uri ($api + '/actions/jobs/136114607232?audit=' + $stamp) -Headers $headers
    if ([string]$job.run_id -cne '44684227455' -or $job.name -cne 'verify / verify-and-package' -or
        $job.status -cne 'completed' -or $job.conclusion -cne 'success') { throw 'Original release verification job did not succeed.' }
    $releaseUrl = $api + '/releases/tags/v1.2.0?audit='
    $release = Invoke-RestMethod -Uri ($releaseUrl + $stamp) -Headers $anonymous
    if ([string]$release.id -cne '452325501' -or $release.tag_name -cne 'v1.2.0' -or
        $release.draft -isnot [bool] -or $release.draft -or $release.prerelease -isnot [bool] -or $release.prerelease) {
        throw 'Expected publicly visible stable 1.2.0 release.'
    }
    if (-not (Test-DesktopPetEvolutionAbsolutePath $env:RUNNER_TEMP)) { throw 'Runner temporary directory is missing.' }
    $downloadDirectory = Join-Path $env:RUNNER_TEMP ('DesktopPet-public-audit-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($downloadDirectory)
    $copies = @(Assert-DesktopPetPublicAssetCopies -Version '1.2.0' -ArtifactDirectory (Join-Path $root 'release-files') `
        -Assets @($release.assets) -DownloadDirectory $downloadDirectory -Download {
            param($asset, $destination)
            # Construct the URL from validated IDs/names, never from an asset-supplied host.
            $uri = 'https://github.com/jinwovo/desktop-pet/releases/download/v1.2.0/' + $asset.name
            Invoke-WebRequest -UseBasicParsing -Uri $uri -OutFile $destination
        })
    $again = Invoke-RestMethod -Uri ($releaseUrl + [Guid]::NewGuid().ToString('N')) -Headers $anonymous
    if ($again.id -ne $release.id -or $again.tag_name -cne 'v1.2.0' -or $again.draft -isnot [bool] -or
        $again.draft -or $again.prerelease -isnot [bool] -or $again.prerelease -or @($again.assets).Count -ne 4) {
        throw 'Public release changed during audit.'
    }
    foreach ($copy in $copies) {
        $same = @($again.assets | Where-Object { $_.id -eq $copy.AssetId -and $_.name -ceq $copy.Name -and
            $_.size -eq $copy.Size -and $_.state -ceq 'uploaded' })
        if ($same.Count -ne 1) { throw 'Public asset identity changed during audit.' }
    }
    $evidence = [pscustomobject]@{ Version='1.2.0'; SourceCommit=$expectedCommit; SourceRunId='44684227455'; SourceVerificationJobId='136114607232';
        ReleaseId=$release.id; Public=$true; Assets=$copies; VerifiedUtc=[DateTime]::UtcNow.ToString('o') }
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'public-release-audit.json') -Encoding UTF8
    foreach ($copy in $copies) { Write-Host ('::notice::Verified public asset: ' + $copy.Name + ' SHA256=' + $copy.Sha256) }
    if ($env:GITHUB_STEP_SUMMARY) {
        @('Verified existing public 1.2.0 without modifying its tag, release or files.',
          ('Source: `' + $expectedCommit + '`; trusted release verification run: `44684227455`.'),
          '', '| Public file | Bytes | SHA256 |', '| --- | ---: | --- |') +
            @($copies | ForEach-Object { '| ' + $_.Name + ' | ' + $_.Size + ' | ' + $_.Sha256 + ' |' }) |
            Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    }
    return $evidence
}

if ($MyInvocation.InvocationName -ne '.' -and $AuditOneTwo) { Invoke-DesktopPetOneTwoReadOnlyAudit }
