# Read-only checks for the optional personal fan-art character pack.
param([string]$CharactersRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'character-packs\fan-art'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\CharacterBuilder.cs')))
function Assert-Personal([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
$passed=0
foreach ($name in @('치이카와','세균맨','헬로키티')) {
    $folder=Join-Path $CharactersRoot $name
    $definition=[IO.File]::ReadAllText((Join-Path $folder 'character.json'))|ConvertFrom-Json
    $groundedWalk=$null -ne $definition.walkCycleDistancePx
    $expectedSamples=if ($groundedWalk) { 32 } else { 8 }
    Assert-Personal ($definition.name -eq $name -and $definition.frontFacingRest -eq $true -and $definition.assetKind -eq 'optional-personal-fan-art') "$name is missing its identity or frontal-rest policy"
    Assert-Personal (@($definition.frames.walk).Count -eq $expectedSamples -and @($definition.frames.walkTurn).Count -eq $expectedSamples) "$name lacks complete side/quarter walk cycles"
    if ($groundedWalk) {
        $cycleDistance=[double]$definition.walkCycleDistancePx
        Assert-Personal (-not [double]::IsNaN($cycleDistance) -and -not [double]::IsInfinity($cycleDistance) -and $cycleDistance -gt 0 -and $definition.walkSpeedPxPerSec -gt 0) "$name is missing physical stride metrics"
        $cycleSeconds=$cycleDistance/[double]$definition.walkSpeedPxPerSec
        Assert-Personal ($cycleSeconds -ge 0.25 -and $cycleSeconds -le 2.5) "$name stride cadence is outside a readable walking range"
        Assert-Personal (@($definition.frames.walkSettleSide).Count -eq $expectedSamples*2 -and @($definition.frames.walkSettleTurn).Count -eq $expectedSamples*2) "$name cannot settle its feet from every stride phase"
    }
    $files=@($definition.frames.PSObject.Properties|ForEach-Object { $_.Value }|Select-Object -Unique)
    foreach ($file in $files) {
        Assert-Personal (-not [IO.Path]::IsPathRooted($file) -and $file -notmatch '\.\.|:') "Unsafe personal frame: $file"
        $frame=[CharacterBuilder]::LoadArgb((Join-Path $folder $file))
        try {
            $bounds=[CharacterBuilder]::OpaqueBounds($frame,100)
            Assert-Personal ($frame.Width -eq 128 -and $frame.Height -eq 128) "$name/$file changed the canvas"
            Assert-Personal ($bounds.Left -gt 0 -and $bounds.Right -lt 128 -and $bounds.Top -gt 0 -and $bounds.Bottom -lt 128) "$name/$file is clipped"
            if ($file -notin @('drag.png','fall.png')) { Assert-Personal ($bounds.Bottom -eq 122) "$name/$file moved off the foot baseline" }
        } finally { $frame.Dispose() }
    }
    $idleHash=(Get-FileHash -LiteralPath (Join-Path $folder 'idle.png')).Hash
    $blinkHash=(Get-FileHash -LiteralPath (Join-Path $folder 'blink.png')).Hash
    Assert-Personal ($idleHash -ne $blinkHash) "$name has no real blinking pose"
    Assert-Personal ((Get-FileHash -LiteralPath (Join-Path $folder 'sleep_1.png')).Hash -eq $blinkHash) "$name sleep changes its body scale instead of resting with closed eyes"
    foreach ($view in @('walk','walkTurn')) {
        $hashes=@($definition.frames.$view|ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $folder $_)).Hash }|Select-Object -Unique)
        Assert-Personal ($hashes.Count -ge 4 -and $hashes -notcontains $idleHash) "$name/$view still uses frontal idle artwork for moving frames"
    }
    foreach ($file in $definition.frames.walkFront) { Assert-Personal ($file -eq 'idle.png') "$name frontal view contains a walking animation" }
    $left=[CharacterBuilder]::LoadArgb((Join-Path $folder 'look_left.png'))
    $right=[CharacterBuilder]::LoadArgb((Join-Path $folder 'look_right.png'))
    try {
        for ($y=0;$y -lt 128;$y++) { for ($x=0;$x -lt 128;$x++) {
            Assert-Personal ($left.GetPixel($x,$y).ToArgb() -eq $right.GetPixel(127-$x,$y).ToArgb()) "$name directional looks are not exact counterparts"
        }}
    } finally { $left.Dispose();$right.Dispose() }
    $passed++
    Write-Output "PASS ${name}: complete poses, no clipping, grounded feet, real blink, stationary front, side gait and mirrored look"
}
$bundledRoot=Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\Characters'
foreach ($name in 'Companion','Nimbus','Ember') {
    Assert-Personal ([IO.File]::Exists((Join-Path $bundledRoot ($name+'\character.json')))) 'An original public character was removed'
}
$passed++
Write-Output 'PASS Original public characters remain available beside the optional collection'
Write-Output "$passed/4 personal character regression groups passed"
