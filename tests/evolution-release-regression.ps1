$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$script:supplied = Join-Path $repositoryRoot 'scripts\publish-evolution-release.ps1'
$script:fixtureRoot = Join-Path $repositoryRoot ('.local\evolution-release-regression-' + [Guid]::NewGuid().ToString('N'))

function Write-Result {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    if ($Ok) { $script:passed++ } else { $script:failed++ }
    $status = if ($Ok) { 'PASS' } else { 'FAIL' }
    if ($Detail) { Write-Output ("{0} {1} : {2}" -f $status, $Name, $Detail) }
    else { Write-Output ("{0} {1}" -f $status, $Name) }
}

function New-SyntheticZip {
    param([string]$Path)
    $bytes = [byte[]]@(0x50,0x4B,0x03,0x04,0x14,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x50,0x4B,0x05,0x06,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-SyntheticChecksum {
    param([string]$ArchivePath, [string]$ChecksumPath, [string]$Hash)
    $name = [IO.Path]::GetFileName($ArchivePath)
    $line = '{0}  *{1}' -f $Hash, $name
    [IO.File]::WriteAllText($ChecksumPath, $line)
}

function Get-SyntheticHash {
    param([string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function New-ValidPlan {
    param([string]$PlanPath, [int]$VerifiedCount)
    $plan = @{ schemaVersion = 1; requiredPatches = 100; baselineVersion = '1.0.2'; targetVersion = '2.0.0'; patches = @() }
    for ($i = 1; $i -le 100; $i++) {
        $minor = [int][Math]::Ceiling($i / 10.0)
        $milestone = if ($minor -eq 10) { '2.0.0' } else { '1.' + $minor + '.0' }
        $status = if ($i -le $VerifiedCount) { 'verified' } else { 'planned' }
        $patch = @{
            id = 'P{0:D3}' -f $i
            title = 'Patch {0}' -f $i
            status = $status
            milestone = $milestone
            changedFiles = @('src\DesktopPet.App.csproj')
            checks = @('tests\Example.cs')
        }
        $plan.patches += $patch
    }
    [IO.File]::WriteAllText($PlanPath, ($plan | ConvertTo-Json -Depth 10))
    return $plan
}

try {
    if (-not [IO.File]::Exists($script:supplied)) { throw 'Supplied script not found.' }
    . $script:supplied

    [void][IO.Directory]::CreateDirectory($script:fixtureRoot)
    $srcDir = Join-Path $script:fixtureRoot 'src'
    $testsDir = Join-Path $script:fixtureRoot 'tests'
    $docsDir = Join-Path $script:fixtureRoot 'docs'
    [void][IO.Directory]::CreateDirectory($srcDir)
    [void][IO.Directory]::CreateDirectory($testsDir)
    [void][IO.Directory]::CreateDirectory($docsDir)

    [IO.File]::WriteAllText((Join-Path $srcDir 'DesktopPet.App.csproj'), '<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $srcDir 'Program.cs'), 'class P {}')
    [IO.File]::WriteAllText((Join-Path $testsDir 'Example.cs'), 'class Example {}')

    $planPath = Join-Path $docsDir 'evolution-2.0.json'
    $plan = New-ValidPlan -PlanPath $planPath -VerifiedCount 100

    $planPath10 = Join-Path $docsDir 'evolution-1.1.json'
    $plan10 = New-ValidPlan -PlanPath $planPath10 -VerifiedCount 10
    $result = Assert-DesktopPetEvolutionReady -Version '1.1.0' -PlanPath 'docs\evolution-1.1.json' -RepositoryRoot $script:fixtureRoot
    Write-Result 'Valid-1.1.0' ($result.Version -ceq '1.1.0' -and $result.RequiredPatches -eq 10 -and $result.VerifiedPatches -eq 10)

    $result = Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\evolution-2.0.json' -RepositoryRoot $script:fixtureRoot
    Write-Result 'Valid-2.0.0-All100' ($result.Version -ceq '2.0.0' -and $result.RequiredPatches -eq 100 -and $result.VerifiedPatches -eq 100 -and $result.TotalPatches -eq 100)

    $badPlanPath = Join-Path $docsDir 'bad-plan.json'
    $badPlan = New-ValidPlan -PlanPath $badPlanPath -VerifiedCount 10
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\bad-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Insufficient-Verification' $caught

    $dupPlanPath = Join-Path $docsDir 'dup-plan.json'
    $dupPlan = New-ValidPlan -PlanPath $dupPlanPath -VerifiedCount 100
    $dupPlan.patches[0].id = $dupPlan.patches[1].id
    [IO.File]::WriteAllText($dupPlanPath, ($dupPlan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\dup-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Duplicate-OutOfOrder-IDs' $caught

    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '1.0.0' -PlanPath 'docs\evolution-2.0.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Invalid-Target-Version' $caught

    $missingPlan = Join-Path $docsDir 'missing.json'
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\missing.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Missing-Plan' $caught

    $docsOnlyPlanPath = Join-Path $docsDir 'docs-only-plan.json'
    $docsOnlyPlan = New-ValidPlan -PlanPath $docsOnlyPlanPath -VerifiedCount 100
    foreach ($patch in $docsOnlyPlan.patches) { $patch.changedFiles = @('docs\readme.md') }
    [IO.File]::WriteAllText($docsOnlyPlanPath, ($docsOnlyPlan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\docs-only-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Docs-Only-Product-Evidence' $caught

    $caught = $false
    try { Resolve-DesktopPetEvolutionEvidence -Root $script:fixtureRoot -RelativePath 'C:\Windows\System32\drivers\etc\hosts' -Kind Product } catch { $caught = $true }
    Write-Result 'Absolute-Path-Rejected' $caught

    $caught = $false
    try { Resolve-DesktopPetEvolutionEvidence -Root $script:fixtureRoot -RelativePath '..\..\windows\win.ini' -Kind Product } catch { $caught = $true }
    Write-Result 'Traversal-Path-Rejected' $caught

    $caught = $false
    try { Get-DesktopPetEvolutionThreshold -Version '3.0.0' } catch { $caught = $true }
    Write-Result 'Unsupported-Version' $caught

    $assetDir = Join-Path $script:fixtureRoot 'assets'
    [void][IO.Directory]::CreateDirectory($assetDir)
    $zip1 = Join-Path $assetDir 'DesktopPet-2.0.0-win-x64.zip'
    $zip2 = Join-Path $assetDir 'DesktopPet-2.0.0-fan-art-packs.zip'
    $sha1 = $zip1 + '.sha256'
    $sha2 = $zip2 + '.sha256'
    New-SyntheticZip -Path $zip1
    New-SyntheticZip -Path $zip2
    $h1 = Get-SyntheticHash -Path $zip1
    $h2 = Get-SyntheticHash -Path $zip2
    New-SyntheticChecksum -ArchivePath $zip1 -ChecksumPath $sha1 -Hash $h1
    New-SyntheticChecksum -ArchivePath $zip2 -ChecksumPath $sha2 -Hash $h2
    $caught = $false
    try { Assert-DesktopPetEvolutionChecksums -Version '2.0.0' -Directory $assetDir } catch { $caught = $true }
    Write-Result 'Valid-Checksums' (-not $caught)

    [IO.File]::WriteAllBytes($zip1, [byte[]]@(0x00,0x01))
    $caught = $false
    try { Assert-DesktopPetEvolutionChecksums -Version '2.0.0' -Directory $assetDir } catch { $caught = $true }
    Write-Result 'Mismatched-Checksums' $caught

    $release = [pscustomobject]@{ tag_name = 'v2.0.0'; draft = $true; prerelease = $false; id = '12345' }
    $assets = @()
    foreach ($name in @("DesktopPet-2.0.0-win-x64.zip", "DesktopPet-2.0.0-win-x64.zip.sha256", "DesktopPet-2.0.0-fan-art-packs.zip", "DesktopPet-2.0.0-fan-art-packs.zip.sha256")) {
        $assets += [pscustomobject]@{ name = $name; state = 'uploaded'; size = (Get-Item -LiteralPath (Join-Path $assetDir $name)).Length; id = [guid]::NewGuid().ToString('N') }
    }
    $caught = $false
    try { Assert-DesktopPetEvolutionAssets -Version '2.0.0' -Release $release -Assets $assets } catch { $caught = $true }
    Write-Result 'Asset-Validation-Exact-Four' (-not $caught)

    $badAssets = @($assets[0], $assets[0], $assets[2], $assets[2])
    $caught = $false
    try { Assert-DesktopPetEvolutionAssets -Version '2.0.0' -Release $release -Assets $badAssets } catch { $caught = $true }
    Write-Result 'Duplicate-Asset-Names' $caught

    $emptyAsset = [pscustomobject]@{ name = 'DesktopPet-2.0.0-win-x64.zip'; state = 'uploaded'; size = '0'; id = '1' }
    $caught = $false
    try { Assert-DesktopPetEvolutionAssets -Version '2.0.0' -Release $release -Assets @($emptyAsset, $assets[1], $assets[2], $assets[3]) } catch { $caught = $true }
    Write-Result 'Empty-Asset-Rejected' $caught

    $count99PlanPath = Join-Path $docsDir 'count99-plan.json'
    $count99Plan = New-ValidPlan -PlanPath $count99PlanPath -VerifiedCount 100
    $count99Plan.requiredPatches = 99
    [IO.File]::WriteAllText($count99PlanPath, ($count99Plan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\count99-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Count99-Rejected' $caught

    $missingProductPlanPath = Join-Path $docsDir 'missing-product-plan.json'
    $missingProductPlan = New-ValidPlan -PlanPath $missingProductPlanPath -VerifiedCount 100
    foreach ($patch in $missingProductPlan.patches) { $patch.changedFiles = @('docs\readme.md') }
    [IO.File]::WriteAllText($missingProductPlanPath, ($missingProductPlan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\missing-product-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Missing-Product-File-Rejected' $caught

    $missingTestPlanPath = Join-Path $docsDir 'missing-test-plan.json'
    $missingTestPlan = New-ValidPlan -PlanPath $missingTestPlanPath -VerifiedCount 100
    foreach ($patch in $missingTestPlan.patches) { $patch.checks = @('docs\readme.md') }
    [IO.File]::WriteAllText($missingTestPlanPath, ($missingTestPlan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\missing-test-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Missing-Test-File-Rejected' $caught

    $emptyEvidencePlanPath = Join-Path $docsDir 'empty-evidence-plan.json'
    $emptyEvidencePlan = New-ValidPlan -PlanPath $emptyEvidencePlanPath -VerifiedCount 100
    $emptyEvidencePlan.patches[0].status = 'verified'
    $emptyEvidencePlan.patches[0].changedFiles = @()
    $emptyEvidencePlan.patches[0].checks = @()
    [IO.File]::WriteAllText($emptyEvidencePlanPath, ($emptyEvidencePlan | ConvertTo-Json -Depth 10))
    $caught = $false
    try { Assert-DesktopPetEvolutionReady -Version '2.0.0' -PlanPath 'docs\empty-evidence-plan.json' -RepositoryRoot $script:fixtureRoot } catch { $caught = $true }
    Write-Result 'Verified-Patch-Empty-Evidence-Rejected' $caught

    foreach ($value in @('C:\Temp', 'D:/runner/temp', '\\server\share\temp', '//server/share/temp')) {
        Write-Result ('Qualified-Path-'+$value) (Test-DesktopPetEvolutionAbsolutePath -Path $value)
    }
    foreach ($value in @('', 'relative/temp', 'C:relative', '\rooted-only', '\\server')) {
        Write-Result ('Relative-Path-Rejected-'+$value) (-not (Test-DesktopPetEvolutionAbsolutePath -Path $value))
    }
    Write-Output ("SUMMARY: {0} passed, {1} failed" -f $script:passed, $script:failed)
    if ($script:failed -gt 0) { exit 1 }
}
catch {
    Write-Output ("FATAL: {0}" -f $_.Exception.Message)
    exit 1
}
