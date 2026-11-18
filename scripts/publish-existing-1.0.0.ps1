# One-time, idempotent publication recovery. Never recreates a tag, release or asset.
$ErrorActionPreference='Stop'
function Assert-DesktopPetStableAssets {
    param([Parameter(Mandatory=$true)]$Release,[Parameter(Mandatory=$true)][object[]]$Assets)
    if ($Release.tag_name -ne 'v1.0.0' -or $Release.prerelease) { throw 'Only stable v1.0.0 is authorized.' }
    $expected=@('DesktopPet-1.0.0-win-x64.zip','DesktopPet-1.0.0-win-x64.zip.sha256','DesktopPet-1.0.0-fan-art-packs.zip','DesktopPet-1.0.0-fan-art-packs.zip.sha256')
    if ($Assets.Count -ne 4) { throw 'Expected exactly four verified assets.' }
    foreach ($name in $expected) {
        $found=@($Assets | Where-Object name -eq $name)
        if ($found.Count -ne 1 -or $found[0].size -le 0 -or $found[0].state -ne 'uploaded') { throw 'Missing, duplicate or incomplete release asset.' }
    }
}
function Publish-DesktopPetExistingOneZero {
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -ne 'jinwovo/desktop-pet' -or -not $env:GH_TOKEN) { throw 'This operation is restricted to the authorized repository workflow.' }
    $expectedCommit='29425627075350104d0f68e00237e9382ac8ee2e'
    $tagCommit=(& git rev-parse 'v1.0.0^{commit}' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $expectedCommit) { throw 'The immutable verified tag does not match.' }
    $headers=@{Authorization=('Bearer '+$env:GH_TOKEN);Accept='application/vnd.github+json';'User-Agent'='DesktopPet-stable-publication'}
    $api='https://api.github.com/repos/jinwovo/desktop-pet'
    $distribution=Invoke-RestMethod -Uri ($api+'/actions/runs/44205993829') -Headers $headers
    $tagRun=Invoke-RestMethod -Uri ($api+'/actions/runs/44205993879') -Headers $headers
    $jobs=Invoke-RestMethod -Uri ($api+'/actions/runs/44205993879/jobs?per_page=100') -Headers $headers
    if ($distribution.head_sha -ne $expectedCommit -or $distribution.conclusion -ne 'success' -or $tagRun.head_sha -ne $expectedCommit -or
        @($jobs.jobs | Where-Object { $_.name -eq 'verify / verify-and-package' -and $_.conclusion -eq 'success' }).Count -ne 1) { throw 'Exact-tag Windows verification has not succeeded.' }
    # Tag lookup is for published releases. Authenticated listing also returns drafts.
    $releases=Invoke-RestMethod -Uri ($api+'/releases?per_page=100') -Headers $headers
    $matches=@($releases | Where-Object tag_name -eq 'v1.0.0')
    if ($matches.Count -ne 1) { throw 'Expected one existing 1.0.0 release; nothing was created or replaced.' }
    $release=$matches[0]
    $assets=@(Invoke-RestMethod -Uri ($api+'/releases/'+$release.id+'/assets?per_page=100') -Headers $headers)
    # Some PowerShell versions wrap a returned root JSON array in one pipeline object.
    if ($assets.Count -eq 1 -and $assets[0] -is [array]) { $assets=@($assets[0]) }
    Assert-DesktopPetStableAssets -Release $release -Assets $assets
    $download=Join-Path $env:RUNNER_TEMP ('DesktopPet-publication-'+[Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($download)
    $assetHeaders=$headers.Clone(); $assetHeaders['Accept']='application/octet-stream'
    foreach ($asset in $assets) {
        $destination=Join-Path $download $asset.name
        Invoke-WebRequest -UseBasicParsing -Uri $asset.url -Headers $assetHeaders -OutFile $destination
    }
    foreach ($name in 'DesktopPet-1.0.0-win-x64.zip','DesktopPet-1.0.0-fan-art-packs.zip') {
        $hash=([IO.File]::ReadAllText((Join-Path $download ($name+'.sha256'))).Trim() -split '\s+')[0]
        if ($hash -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath (Join-Path $download $name) -Algorithm SHA256).Hash -ine $hash) { throw 'Downloaded release checksum mismatch; draft retained.' }
    }
    if ($release.draft) {
        $body=@{draft=$false;prerelease=$false;make_latest='true'} | ConvertTo-Json -Compress
        $null=Invoke-RestMethod -Uri ($api+'/releases/'+$release.id) -Method Patch -Headers $headers -ContentType 'application/json' -Body $body
    }
    $published=Invoke-RestMethod -Uri ($api+'/releases/'+$release.id) -Headers $headers
    if ($published.draft -or $published.prerelease -or $published.tag_name -ne 'v1.0.0') { throw 'Publication state could not be confirmed.' }
    $public=Invoke-RestMethod -Uri ($api+'/releases/tags/v1.0.0') -Headers @{'User-Agent'='DesktopPet-public-read-verification';Accept='application/vnd.github+json'}
    if ($public.id -ne $release.id -or $public.draft) { throw 'Anonymous public release verification failed.' }
    Write-Output ('Published existing verified release: '+$public.html_url)
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ('DesktopPet 1.0.0 published with unchanged tag and checksummed assets: '+$public.html_url)
}
if ($MyInvocation.InvocationName -ne '.') { Publish-DesktopPetExistingOneZero }
