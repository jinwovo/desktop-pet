<#
.SYNOPSIS
    Validates the 100-patch evolution plan and publishes an existing exact-tag draft in Actions.
.DESCRIPTION
    Dot-sourcing defines functions only. No tag, release or asset is created, replaced or uploaded.
#>
param([string]$Version = '', [string]$RepositoryRoot = '')

function Get-DesktopPetEvolutionThreshold {
    param([Parameter(Mandatory=$true)][string]$Version)
    if ($Version -cmatch '^1\.([1-9])\.0$') { return [int]$Matches[1] * 10 }
    if ($Version -ceq '2.0.0') { return 100 }
    throw 'Only stable evolution milestones 1.1.0 through 1.9.0 and 2.0.0 are supported.'
}

function Test-DesktopPetEvolutionAbsolutePath {
    param([AllowNull()][AllowEmptyString()][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    # Windows PowerShell 5.1 runs on .NET Framework, which lacks Path.IsPathFullyQualified.
    return $Path -match '^(?:[A-Za-z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+(?:[\\/]|$))'
}

function Assert-DesktopPetEvolutionNoLinks {
    param([Parameter(Mandatory=$true)][string]$Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if ([IO.File]::Exists($current) -or [IO.Directory]::Exists($current)) {
            if (([IO.File]::GetAttributes($current) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Evolution evidence and publication paths cannot pass through links.'
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-DesktopPetEvolutionRoot {
    param([Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\','/')
    if (-not [IO.Directory]::Exists($root) -or $root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\','/')) {
        throw 'Use an existing repository directory, not a filesystem root.'
    }
    Assert-DesktopPetEvolutionNoLinks $root
    return $root
}

function Resolve-DesktopPetEvolutionEvidence {
    param([Parameter(Mandatory=$true)][string]$Root,
          [Parameter(Mandatory=$true)][string]$RelativePath,
          [Parameter(Mandatory=$true)][ValidateSet('Product','Check')][string]$Kind)
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw 'Patch evidence must use repository-relative file paths.'
    }
    $parts = $RelativePath.Replace('/','\').Split('\')
    if ($parts.Count -lt 2) { throw 'Patch evidence must belong to a product or test directory.' }
    foreach ($part in $parts) {
        if ([string]::IsNullOrWhiteSpace($part) -or $part -in @('.','..') -or
            $part.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or $part.EndsWith('.') -or $part.EndsWith(' ')) {
            throw 'Patch evidence contains an unsafe or ambiguous path segment.'
        }
    }
    if ($Kind -eq 'Product' -and $parts[0] -notin @('src','Assets','character-packs','scripts','Hooks','.github')) {
        throw 'A verified patch needs an actual product file; documentation and tests alone do not qualify.'
    }
    if ($Kind -eq 'Check' -and $parts[0] -cne 'tests') { throw 'A patch check must refer to an actual tests file.' }
    $path = [IO.Path]::GetFullPath((Join-Path $Root ($parts -join '\')))
    if (-not $path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Patch evidence escapes the repository.'
    }
    Assert-DesktopPetEvolutionNoLinks $path
    if (-not [IO.File]::Exists($path)) { throw 'A referenced product or check file does not exist.' }
    return $path
}

function Assert-DesktopPetEvolutionReady {
    param([Parameter(Mandatory=$true)][string]$Version,
          [Parameter(Mandatory=$true)][string]$PlanPath,
          [Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $ErrorActionPreference = 'Stop'
    $required = Get-DesktopPetEvolutionThreshold $Version
    $root = Get-DesktopPetEvolutionRoot $RepositoryRoot
    $path = if ([IO.Path]::IsPathRooted($PlanPath)) { [IO.Path]::GetFullPath($PlanPath) }
        else { [IO.Path]::GetFullPath((Join-Path $root $PlanPath)) }
    if (-not $path.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The evolution plan must remain inside the repository.'
    }
    Assert-DesktopPetEvolutionNoLinks $path
    if (-not [IO.File]::Exists($path)) { throw 'The evolution plan is missing.' }
    $plan = [IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
    if ($plan.schemaVersion -isnot [int] -and $plan.schemaVersion -isnot [long] -or $plan.schemaVersion -ne 1 -or
        $plan.requiredPatches -isnot [int] -and $plan.requiredPatches -isnot [long] -or $plan.requiredPatches -ne 100 -or
        $plan.baselineVersion -cne '1.0.2' -or $plan.targetVersion -cne '2.0.0' -or $plan.patches -isnot [array] -or
        @($plan.patches).Count -ne 100) { throw 'The evolution plan must describe exactly 100 patches from 1.0.2 to 2.0.0.' }
    $verified = 0
    for ($index = 0; $index -lt 100; $index++) {
        $patch = $plan.patches[$index]
        $expectedId = 'P{0:D3}' -f ($index + 1)
        $minor = [int][Math]::Ceiling(($index + 1) / 10.0)
        $milestone = if ($minor -eq 10) { '2.0.0' } else { '1.' + $minor + '.0' }
        if ($patch.id -cne $expectedId -or $patch.title -isnot [string] -or [string]::IsNullOrWhiteSpace($patch.title) -or
            @('planned','verified') -cnotcontains $patch.status -or $patch.milestone -cne $milestone -or
            $patch.changedFiles -isnot [array] -or $patch.checks -isnot [array]) {
            throw "Invalid, duplicate or out-of-order evolution patch: $expectedId."
        }
        if ($index -lt $required -and $patch.status -cne 'verified') { throw "Required patch $expectedId is not verified." }
        if ($patch.status -ceq 'verified') {
            $verified++
            if ($patch.changedFiles.Count -lt 1 -or $patch.checks.Count -lt 1) {
                throw "Verified patch $expectedId needs product changes and a check fixture."
            }
        }
        foreach ($changed in $patch.changedFiles) {
            if ($changed -isnot [string]) { throw "Patch $expectedId contains invalid product evidence." }
            $null = Resolve-DesktopPetEvolutionEvidence -Root $root -RelativePath $changed -Kind Product
        }
        foreach ($check in $patch.checks) {
            if ($check -isnot [string]) { throw "Patch $expectedId contains invalid check evidence." }
            $null = Resolve-DesktopPetEvolutionEvidence -Root $root -RelativePath $check -Kind Check
        }
    }
    [pscustomobject]@{ Version=$Version; RequiredPatches=$required; VerifiedPatches=$verified; TotalPatches=100; PlanPath=$path; RepositoryRoot=$root }
}

function Assert-DesktopPetEvolutionAssets {
    param([Parameter(Mandatory=$true)][string]$Version,
          [Parameter(Mandatory=$true)]$Release,
          [Parameter(Mandatory=$true)][object[]]$Assets)
    $null = Get-DesktopPetEvolutionThreshold $Version
    if ($Release.tag_name -cne ('v' + $Version) -or $Release.draft -isnot [bool] -or -not $Release.draft -or
        $Release.prerelease -isnot [bool] -or $Release.prerelease) {
        throw 'Only the exact stable, currently unpublished draft may be published.'
    }
    $expected = @("DesktopPet-$Version-win-x64.zip", "DesktopPet-$Version-win-x64.zip.sha256",
        "DesktopPet-$Version-fan-art-packs.zip", "DesktopPet-$Version-fan-art-packs.zip.sha256")
    if ($Assets.Count -ne 4) { throw 'Expected exactly two ZIPs and their two SHA256 sidecars.' }
    foreach ($name in $expected) {
        $found = @($Assets | Where-Object { $_.name -ceq $name })
        $size = 0L
        if ($found.Count -ne 1 -or $found[0].state -cne 'uploaded' -or
            -not [long]::TryParse([string]$found[0].size, [ref]$size) -or $size -le 0) {
            throw 'Release assets are missing, duplicated, empty or not completely uploaded.'
        }
    }
}

function Assert-DesktopPetEvolutionChecksums {
    param([Parameter(Mandatory=$true)][string]$Version,
          [Parameter(Mandatory=$true)][string]$Directory)
    $null = Get-DesktopPetEvolutionThreshold $Version
    foreach ($name in @("DesktopPet-$Version-win-x64.zip", "DesktopPet-$Version-fan-art-packs.zip")) {
        $archive = Join-Path $Directory $name
        $checksum = $archive + '.sha256'
        if (-not [IO.File]::Exists($archive) -or -not [IO.File]::Exists($checksum)) { throw 'A downloaded release asset is missing.' }
        $text = [IO.File]::ReadAllText($checksum).Trim()
        $pattern = '^(?<hash>[0-9a-fA-F]{64})[ \t]+\*?' + [Regex]::Escape($name) + '$'
        if ($text -notmatch $pattern) { throw 'A release checksum has an invalid hash or mismatched archive name.' }
        $hash = $Matches.hash
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ine $hash) {
            throw 'Downloaded release checksum mismatch; the draft remains unpublished.'
        }
    }
}

function Wait-DesktopPetPublicRelease {
    param([Parameter(Mandatory=$true)][string]$Version,
          [Parameter(Mandatory=$true)][ValidatePattern('^[1-9][0-9]*$')][string]$ReleaseId,
          [Parameter(Mandatory=$true)][scriptblock]$Fetch,
          [scriptblock]$Wait = { param($seconds) Start-Sleep -Seconds $seconds },
          [ValidateRange(1,5)][int]$Attempts = 5)
    $null = Get-DesktopPetEvolutionThreshold $Version
    $reason = 'release state did not match'
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $confirmed = $true
        $publicResult = $null
        foreach ($anonymous in @($false, $true)) {
            try {
                $observed = & $Fetch $anonymous
                if ([string]$observed.id -cne $ReleaseId -or $observed.tag_name -cne ('v' + $Version) -or
                    $observed.draft -isnot [bool] -or $observed.draft -or
                    $observed.prerelease -isnot [bool] -or $observed.prerelease) {
                    $confirmed = $false
                    $reason = 'release state did not match'
                }
                if ($anonymous) { $publicResult = $observed }
            } catch {
                $confirmed = $false
                # HTTP exception text may contain request details. Do not print credentials or URLs.
                $reason = 'a release read was temporarily unavailable'
            }
        }
        if ($confirmed) { return $publicResult }
        if ($attempt -lt $Attempts) { $null = & $Wait ([int][Math]::Min([Math]::Pow(2, $attempt), 16)) }
    }
    throw ("Published release confirmation failed after $Attempts read attempts: $reason.")
}

function Publish-DesktopPetEvolutionRelease {
    param([Parameter(Mandatory=$true)][string]$Version,
          [Parameter(Mandatory=$true)][string]$RepositoryRoot)
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    $null = Get-DesktopPetEvolutionThreshold $Version
    if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'jinwovo/desktop-pet' -or
        $env:GITHUB_REF -cne ('refs/tags/v' + $Version) -or [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN) -or
        $env:GITHUB_SHA -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Publication requires the exact repository tag workflow and its GitHub token.'
    }
    $root = Get-DesktopPetEvolutionRoot $RepositoryRoot
    $head = (& git -C $root rev-parse --verify HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $env:GITHUB_SHA) { throw 'Checked-out HEAD does not match the workflow SHA.' }
    $tag = (& git -C $root rev-parse --verify ('refs/tags/v' + $Version + '^{commit}') | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tag -cne $head) { throw 'The immutable release tag does not match the checked-out source.' }
    $project = Join-Path $root 'src\DesktopPet.App\DesktopPet.App.csproj'
    Assert-DesktopPetEvolutionNoLinks $project
    [xml]$projectXml = [IO.File]::ReadAllText($project)
    $versions = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })
    if ($versions.Count -ne 1 -or [string]$versions[0] -cne $Version) { throw 'The source version does not match the release tag.' }
    $null = Assert-DesktopPetEvolutionReady -Version $Version -PlanPath 'docs\evolution-2.0.json' -RepositoryRoot $root
    $headers = @{ Authorization=('Bearer ' + $env:GITHUB_TOKEN); Accept='application/vnd.github+json';
        'X-GitHub-Api-Version'='2022-11-28'; 'User-Agent'='DesktopPet-evolution-publication' }
    $api = 'https://api.github.com/repos/jinwovo/desktop-pet'
    $matches = @()
    for ($page = 1; $page -le 20; $page++) {
        $batch = @(Invoke-RestMethod -Uri ($api + '/releases?per_page=100&page=' + $page) -Headers $headers)
        if ($batch.Count -eq 1 -and $batch[0] -is [array]) { $batch = @($batch[0]) }
        $matches += @($batch | Where-Object { $_.tag_name -ceq ('v' + $Version) })
        if ($batch.Count -lt 100) { break }
        if ($page -eq 20) { throw 'Release listing exceeded the bounded review window; nothing was published.' }
    }
    if ($matches.Count -ne 1) { throw 'Expected exactly one existing exact-tag draft; no release was created or replaced.' }
    $release = $matches[0]
    if ([string]$release.id -notmatch '^[1-9][0-9]*$') { throw 'The draft release ID is invalid.' }
    $assetsUrl = $api + '/releases/' + $release.id + '/assets?per_page=100'
    $assets = @(Invoke-RestMethod -Uri $assetsUrl -Headers $headers)
    if ($assets.Count -eq 1 -and $assets[0] -is [array]) { $assets = @($assets[0]) }
    Assert-DesktopPetEvolutionAssets -Version $Version -Release $release -Assets $assets
    if (-not (Test-DesktopPetEvolutionAbsolutePath -Path $env:RUNNER_TEMP)) {
        throw 'An explicit workflow temporary directory is required.'
    }
    $temporaryRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\','/')
    Assert-DesktopPetEvolutionNoLinks $temporaryRoot
    $download = Join-Path $temporaryRoot ('DesktopPet-evolution-publication-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($download)
    try {
        $assetHeaders = $headers.Clone()
        $assetHeaders['Accept'] = 'application/octet-stream'
        foreach ($asset in $assets) {
            if ([string]$asset.id -notmatch '^[1-9][0-9]*$') { throw 'A release asset ID is invalid.' }
            $destination = Join-Path $download $asset.name
            Invoke-WebRequest -UseBasicParsing -Uri ($api + '/releases/assets/' + $asset.id) -Headers $assetHeaders -OutFile $destination | Out-Null
            if (-not [IO.File]::Exists($destination) -or (Get-Item -LiteralPath $destination).Length -ne $asset.size) {
                throw 'A downloaded asset does not match the uploaded asset size.'
            }
        }
        Assert-DesktopPetEvolutionChecksums -Version $Version -Directory $download
        $current = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Headers $headers
        $currentAssets = @(Invoke-RestMethod -Uri $assetsUrl -Headers $headers)
        if ($currentAssets.Count -eq 1 -and $currentAssets[0] -is [array]) { $currentAssets = @($currentAssets[0]) }
        Assert-DesktopPetEvolutionAssets -Version $Version -Release $current -Assets $currentAssets
        foreach ($asset in $assets) {
            $same = @($currentAssets | Where-Object { $_.id -eq $asset.id -and $_.name -ceq $asset.name -and $_.size -eq $asset.size })
            if ($same.Count -ne 1) { throw 'Release assets changed during verification; the draft remains unpublished.' }
        }
        $body = @{ draft=$false; prerelease=$false; make_latest='true' } | ConvertTo-Json -Compress
        $null = Invoke-RestMethod -Uri ($api + '/releases/' + $release.id) -Method Patch -Headers $headers -ContentType 'application/json' -Body $body
        $public = Wait-DesktopPetPublicRelease -Version $Version -ReleaseId ([string]$release.id) -Fetch {
            param($anonymous)
            $readHeaders = if ($anonymous) {
                @{ Accept='application/vnd.github+json'; 'User-Agent'='DesktopPet-evolution-public-read-verification' }
            } else { $headers.Clone() }
            $readHeaders['Cache-Control'] = 'no-cache'
            $resource = if ($anonymous) { '/releases/tags/v' + $Version } else { '/releases/' + $release.id }
            Invoke-RestMethod -Uri ($api + $resource + '?confirmation=' + [Guid]::NewGuid().ToString('N')) -Headers $readHeaders
        }
        [pscustomobject]@{ Version=$Version; ReleaseId=$public.id; Published=$true; Url=$public.html_url }
    }
    finally {
        # Only four generated downloads exist here. Avoid recursive deletion of a computed path.
        if ($download.StartsWith($temporaryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($download).StartsWith('DesktopPet-evolution-publication-', [StringComparison]::Ordinal)) {
            try {
                Assert-DesktopPetEvolutionNoLinks $download
                foreach ($asset in $assets) {
                    $file = Join-Path $download $asset.name
                    Assert-DesktopPetEvolutionNoLinks $file
                    if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
                }
                if ([IO.Directory]::Exists($download)) { [IO.Directory]::Delete($download) }
            }
            catch { Write-Warning 'Publication temporary downloads could not be fully cleaned.' }
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $RepositoryRoot) { $RepositoryRoot = Split-Path -Parent $PSScriptRoot }
    Publish-DesktopPetEvolutionRelease -Version $Version -RepositoryRoot $RepositoryRoot
}
