param([string]$CharactersRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\Characters'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
function Assert-PublicCharacter([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$passed = 0
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $repoRoot 'scripts\Get-DesktopPetCharacterBehavior.ps1')
$presets = [IO.File]::ReadAllText((Join-Path $repoRoot 'Assets\CharacterBehaviors.json')) | ConvertFrom-Json
$authored = @{ behavior = @{ walkWeight = 0.123 } }
Set-DesktopPetCharacterBehavior -Definition $authored -Name 'Companion' -OnlyIfMissing
Assert-PublicCharacter ($authored.behavior.walkWeight -eq 0.123) 'Grounded regeneration overwrote an authored personality'
foreach ($name in 'Companion','Nimbus','Ember') {
    $folder = Join-Path $CharactersRoot $name
    $definition = [IO.File]::ReadAllText((Join-Path $folder 'character.json')) | ConvertFrom-Json
    Assert-PublicCharacter ($definition.name -eq $name -and $definition.frontFacingRest) "$name is missing its public identity or resting policy"
    Assert-PublicCharacter ($definition.walkCycleDistancePx -gt 0 -and $definition.walkRig.version -eq 1) "$name is missing its physical gait or live rig"
    $generated = [pscustomobject]@{}
    Set-DesktopPetCharacterBehavior -Definition $generated -Name $name
    foreach ($field in 'walkWeight','idleWeight','sitWeight','restDurationMultiplier','walkDurationMultiplier','sleepAfterIdleMs','description') {
        Assert-PublicCharacter ($null -ne $definition.behavior.$field -and $definition.behavior.$field -eq $presets.$name.$field -and $generated.behavior.$field -eq $definition.behavior.$field) "$name personality differs from its rebuild preset"
    }
    $references = New-Object 'System.Collections.Generic.List[string]'
    foreach ($state in 'idle','walk','walkTurn','walkFront','walkSettleSide','walkSettleTurn','sit','lookLeft','lookRight','drag','fall','cry','angry','sleep') {
        Assert-PublicCharacter (@($definition.frames.$state).Count -gt 0) "$name has no $state state"
        foreach ($reference in @($definition.frames.$state)) { $references.Add($reference) }
    }
    Assert-PublicCharacter (@($definition.frames.walk).Count -eq 32 -and @($definition.frames.walkTurn).Count -eq 32) "$name fallback gait phases are incomplete"
    Assert-PublicCharacter (@($definition.frames.walkSettleSide).Count -eq 64 -and @($definition.frames.walkSettleTurn).Count -eq 64) "$name settling banks are incomplete"
    foreach ($view in 'side','turn') {
        foreach ($part in 'body','nearFoot','farFoot') { $references.Add($definition.walkRig.$view.$part) }
    }
    foreach ($reference in @($references | Select-Object -Unique)) {
        Assert-PublicCharacter (-not [string]::IsNullOrWhiteSpace($reference) -and [IO.Path]::GetExtension($reference) -eq '.png' -and $reference -notmatch '\.\.|:|^[/\\]') "$name has an unsafe resource reference"
        $image = [Drawing.Bitmap]::FromFile((Join-Path $folder $reference))
        try {
            Assert-PublicCharacter ($image.Width -eq 128 -and $image.Height -eq 128) "$name/$reference has mismatched canvas dimensions"
            Assert-PublicCharacter ($image.GetPixel(0,0).A -eq 0) "$name/$reference has no alpha background"
            for ($i=0; $i -lt 128; $i++) {
                Assert-PublicCharacter ($image.GetPixel($i,0).A -lt 100 -and $image.GetPixel($i,127).A -lt 100 -and $image.GetPixel(0,$i).A -lt 100 -and $image.GetPixel(127,$i).A -lt 100) "$name/$reference clips the sprite canvas"
            }
        } finally { $image.Dispose() }
    }
    $idleHash = (Get-FileHash -LiteralPath (Join-Path $folder 'idle.png')).Hash
    Assert-PublicCharacter ((Get-FileHash -LiteralPath (Join-Path $folder 'blink.png')).Hash -ne $idleHash) "$name blink does not close its eyes"
    $passed++
    Write-Output "PASS ${name}: complete public poses, alpha bounds, live parts, fallback cycles and distinct blink"
}
Write-Output "$passed/3 public character regression groups passed"
