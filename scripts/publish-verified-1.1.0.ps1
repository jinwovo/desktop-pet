param()

function Publish-DesktopPetOneOneVerified {
    [CmdletBinding()]
    param()

    if (-not $env:GITHUB_ACTIONS -or $env:GITHUB_ACTIONS -ne 'true') {
        throw "GITHUB_ACTIONS must be 'true'"
    }
    if (-not $env:GITHUB_REPOSITORY -or $env:GITHUB_REPOSITORY -ne 'jinwovo/desktop-pet') {
        throw "GITHUB_REPOSITORY must be 'jinwovo/desktop-pet'"
    }
    if (-not $env:GH_TOKEN -or $env:GH_TOKEN.Trim() -eq '') {
        throw "GH_TOKEN must be non-empty"
    }

    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $ExpectedCommit = '4a69780dc24f0601599182603b6b1005d45831ac'
    $RunId = '44655275518'
    $Version = '1.1.0'
    $Tag = "v$Version"
    $RepoRoot = Split-Path -Parent $PSScriptRoot
    $tagCommit=(& git -C $RepoRoot rev-parse 'v1.1.0^{commit}' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $ExpectedCommit) { throw 'Immutable tag commit mismatch' }
    [xml]$project=[IO.File]::ReadAllText((Join-Path $RepoRoot 'src/DesktopPet.App/DesktopPet.App.csproj'))
    if ([string]$project.Project.PropertyGroup.Version -ne $Version) { throw 'Source version mismatch' }
    $ReleaseFilesDir = Join-Path $RepoRoot 'release-files'
    $Headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json' }

    Write-Host "::group::A. Validate Source Workflow Run $RunId"
    $RunUrl = "https://api.github.com/repos/jinwovo/desktop-pet/actions/runs/$RunId"
    $RunResp = Invoke-RestMethod -Uri $RunUrl -Headers $Headers -Method Get
    if ($RunResp.head_sha -ne $ExpectedCommit) { throw "Run head_sha mismatch: $($RunResp.head_sha) != $ExpectedCommit" }
    if ($RunResp.status -ne 'completed') { throw "Run status not completed: $($RunResp.status)" }
    if ($RunResp.conclusion -ne 'success') { throw "Run conclusion not success: $($RunResp.conclusion)" }
    if ($RunResp.path -ne '.github/workflows/windows-release.yml') { throw "Run path mismatch: $($RunResp.path)" }
    Write-Host "Run validation passed."

    $JobsUrl = "https://api.github.com/repos/jinwovo/desktop-pet/actions/runs/$RunId/jobs?per_page=100"
    $JobsResp = Invoke-RestMethod -Uri $JobsUrl -Headers $Headers -Method Get
    $VerifyJob = @($JobsResp.jobs | Where-Object { $_.name -eq 'verify-and-package' })
    if ($VerifyJob.Count -ne 1) { throw "verify-and-package job not found" }
    if ($VerifyJob.conclusion -ne 'success') { throw "verify-and-package job conclusion not success: $($VerifyJob.conclusion)" }
    Write-Host "verify-and-package job validation passed."
    Write-Host "::endgroup::"

    Write-Host "::group::B. Load Helpers & Assert Evolution Ready"
    . (Join-Path $RepoRoot 'scripts/publish-evolution-release.ps1') -Version '1.1.0'
    . (Join-Path $RepoRoot 'scripts/package-release.ps1') -Version '1.1.0'
    Assert-DesktopPetEvolutionReady -Version $Version -PlanPath 'docs/evolution-2.0.json' -RepositoryRoot $RepoRoot
    Write-Host "::endgroup::"

    Write-Host "::group::C. Download Artifact & Validate Checksums"
    if (-not (Test-Path -LiteralPath $ReleaseFilesDir -PathType Container)) { throw 'Verified artifact directory is missing' }
    $ExpectedFiles = @(
        "DesktopPet-$Version-win-x64.zip",
        "DesktopPet-$Version-win-x64.zip.sha256",
        "DesktopPet-$Version-fan-art-packs.zip",
        "DesktopPet-$Version-fan-art-packs.zip.sha256"
    )
    $actualFiles=@(Get-ChildItem -LiteralPath $ReleaseFilesDir -File)
    if ($actualFiles.Count -ne 4 -or @($actualFiles | Where-Object { $_.Name -notin $ExpectedFiles }).Count) { throw 'Unexpected artifact file inventory' }
    foreach ($f in $ExpectedFiles) {
        $fp = Join-Path $ReleaseFilesDir $f
        if (-not (Test-Path $fp)) { throw "Missing expected artifact file: $f" }
    }
    Assert-DesktopPetEvolutionChecksums -Version $Version -Directory $ReleaseFilesDir
    Write-Host "::endgroup::"

    Write-Host "::group::D. Validate Payload & Run Regression Tests"
    $AppZip = Join-Path $ReleaseFilesDir "DesktopPet-$Version-win-x64.zip"
    $ExtractRoot = Join-Path $env:RUNNER_TEMP ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $ExtractRoot | Out-Null
    Expand-Archive -Path $AppZip -DestinationPath $ExtractRoot -Force
    Test-DesktopPetReleasePayload -Directory $ExtractRoot -ExpectedVersion $Version -ExpectedDeployment 'self-contained-single-file'

    $TestScript = Join-Path $RepoRoot 'tests/installation-check-regression.ps1'
    $AppExe = Join-Path $ExtractRoot 'DesktopPet.App.exe'
    if (-not (Test-Path $AppExe)) { throw "DesktopPet.App.exe not found in extracted payload" }
    & powershell -NoProfile -File $TestScript -ApplicationPath $AppExe
    if ($LASTEXITCODE -ne 0) { throw "installation-check-regression.ps1 failed with exit code $LASTEXITCODE" }
    Write-Host "::endgroup::"

    $uri="https://api.github.com/repos/jinwovo/desktop-pet/releases?per_page=100"
    $resp=Invoke-RestMethod -Method GET -Uri $uri -Headers $Headers
    $releases=@($resp)
    $rel=$releases | Where-Object { $_.tag_name -eq $Tag }
    if (-not $rel) {
      $filePaths=@($ExpectedFiles | ForEach-Object { Join-Path $ReleaseFilesDir $_ })
      gh release create $Tag @filePaths --draft --verify-tag --target $ExpectedCommit --title "DesktopPet$Version" --notes "P001-P010"
      if ($LASTEXITCODE -ne 0) { throw "gh release create failed" }
      $resp=Invoke-RestMethod -Method GET -Uri $uri -Headers $Headers
      $releases=@($resp)
      $rel=$releases | Where-Object { $_.tag_name -eq $Tag }
    }
    if (($rel | Measure-Object).Count -ne 1) { throw "Expected exactly one release for $Tag" }
    $Release=$rel[0]
    $idRef = 0L
    if (-not ([int64]::TryParse($Release.id, [ref]$idRef) -and $idRef -gt 0)) { throw "Release ID not positive numeric" }
    if ($Release.prerelease -eq $true) { throw "Release must not be prerelease" }
    $IsDraft = [bool]::Parse($Release.draft.ToString())

    Write-Host "::group::F. Validate Release Assets"
    $AssetsUrl = $Release.assets_url
    $UploadedAssets = Invoke-RestMethod -Uri $AssetsUrl -Headers $Headers -Method Get
    if ($UploadedAssets.Count -ne 4) { throw "Expected 4 assets, found $($UploadedAssets.Count)" }

    if ($IsDraft) {
        Assert-DesktopPetEvolutionAssets -Version $Version -Release $Release -Assets $UploadedAssets
    }
    else {
        $ExpectedNames = @(
            "DesktopPet-$Version-win-x64.zip",
            "DesktopPet-$Version-win-x64.zip.sha256",
            "DesktopPet-$Version-fan-art-packs.zip",
            "DesktopPet-$Version-fan-art-packs.zip.sha256"
        )
        foreach ($exp in $ExpectedNames) {
            $asset = $UploadedAssets | Where-Object { $_.name -eq $exp }
            if (-not $asset) { throw "Missing asset: $exp" }
            if ($asset.size -le 0) { throw "Asset $exp has zero size" }
            if ($asset.state -ne 'uploaded') { throw "Asset $exp not uploaded" }
        }
        if ($Release.tag_name -ne $Tag) { throw "Release tag mismatch" }
    }

    $DownloadDir = Join-Path $env:RUNNER_TEMP ([Guid]::NewGuid().ToString())
    New-Item -ItemType Directory -Path $DownloadDir | Out-Null
    $DownloadHeaders = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/octet-stream' }

    foreach ($asset in $UploadedAssets) {
        $dest = Join-Path $DownloadDir $asset.name
        Invoke-RestMethod -Uri $asset.url -Headers $DownloadHeaders -Method Get -OutFile $dest
        $src = Join-Path $ReleaseFilesDir $asset.name
        if ((Get-Item $dest).Length -ne (Get-Item $src).Length) { throw "Size mismatch for $($asset.name)" }
        $h1 = (Get-FileHash -Path $dest -Algorithm SHA256).Hash.ToLower()
        $h2 = (Get-FileHash -Path $src -Algorithm SHA256).Hash.ToLower()
        if ($h1 -ne $h2) { throw "SHA256 mismatch for $($asset.name)" }
    }
    Assert-DesktopPetEvolutionChecksums -Version $Version -Directory $DownloadDir
    Write-Host "::endgroup::"

    if ($IsDraft) {
      $patchUri="https://api.github.com/repos/jinwovo/desktop-pet/releases/$($Release.id)"
      $body=@{ draft=$false; prerelease=$false; make_latest="true" } | ConvertTo-Json
      Invoke-RestMethod -Method PATCH -Uri $patchUri -Headers $Headers -Body $body -ContentType "application/json" | Out-Null
    }
    $anonUri="https://api.github.com/repos/jinwovo/desktop-pet/releases/tags/$Tag"
    $public=Invoke-RestMethod -Method GET -Uri $anonUri -Headers @{ "User-Agent"="PowerShell" }
    if ($public.id -ne $Release.id -or $public.tag_name -ne $Tag -or $public.draft -or $public.prerelease) { throw "Public release verification failed" }
    if (($public.assets | Measure-Object).Count -ne 4) { throw "Expected four assets" }
    $Release=$public

    $Summary = @"
## Publish Verified v1.1.0 :white_check_mark:
- Source Run: $RunId (commit $ExpectedCommit)
- Artifacts: 4 files validated
- Payload: Self-contained single-file, regression tests passed
- Release: $($Release.html_url)
- Assets: 4/4 verified (size + SHA256)
"@
    Write-Host "::endgroup::"
    Write-Host "::notice::$Summary"
    if ($env:GITHUB_STEP_SUMMARY) { $Summary | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Append }
}

if ($MyInvocation.InvocationName -ne '.') {
    Publish-DesktopPetOneOneVerified
}