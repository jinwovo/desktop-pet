# Geometry and visible-pose checks for the original Companion's directional walk assets.
# Fixture-free, read-only: neither generated assets nor user settings are changed.
param([string]$CharacterDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\Characters\Companion'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $repoRoot 'scripts\CharacterBuilder.cs')))

function Assert-Walk([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Get-ChangedPixels($First, $Second, [int]$Top = 0, [int]$Bottom = 128, [bool]$MirrorSecond = $false) {
    $changed = 0
    for ($y = $Top; $y -lt $Bottom; $y++) {
        for ($x = 0; $x -lt 128; $x++) {
            $otherX = if ($MirrorSecond) { 127 - $x } else { $x }
            $a = $First.GetPixel($x, $y)
            $b = $Second.GetPixel($otherX, $y)
            # Ignore transparent RGB and tiny interpolation changes.
            if (($a.A -lt 25) -and ($b.A -lt 25)) { continue }
            if ([Math]::Abs([int]$a.A - [int]$b.A) -gt 25 -or
                [Math]::Abs([int]$a.R - [int]$b.R) -gt 35 -or
                [Math]::Abs([int]$a.G - [int]$b.G) -gt 35 -or
                [Math]::Abs([int]$a.B - [int]$b.B) -gt 35) { $changed++ }
        }
    }
    return $changed
}

$definition = [IO.File]::ReadAllText((Join-Path $CharacterDirectory 'character.json')) | ConvertFrom-Json
$views = @('walk', 'walkTurn', 'walkFront')
$groundedWalk = $null -ne $definition.walkCycleDistancePx
$expectedSamples = if ($groundedWalk) { 32 } else { 8 }
$movingViews = if ($groundedWalk) { @('walk', 'walkTurn') } else { $views }
$assetViews = if ($groundedWalk) { $views + @('walkSettleSide', 'walkSettleTurn') } else { $views }
$passed = 0
foreach ($view in $assetViews) {
    $files = $definition.frames.$view
    $sampleCount = if ($groundedWalk -and $view -in @('walkSettleSide', 'walkSettleTurn')) { $expectedSamples * 2 }
        elseif ($groundedWalk -and $view -eq 'walkFront') { @($files).Count }
        else { $expectedSamples }
    Assert-Walk ($null -ne $files -and @($files).Count -eq $sampleCount -and $sampleCount -gt 0) "Companion/$view has an incomplete set of poses"
    foreach ($file in $files) {
        Assert-Walk (-not [IO.Path]::IsPathRooted($file) -and $file -notmatch '(^|[\\/])\.\.([\\/]|$)|:') "Unsafe frame path: $file"
        Assert-Walk ([IO.File]::Exists((Join-Path $CharacterDirectory $file))) "Missing directional frame: $file"
    }
}
if ($groundedWalk) {
    $cycleDistance = [double]$definition.walkCycleDistancePx
    Assert-Walk (-not [double]::IsNaN($cycleDistance) -and -not [double]::IsInfinity($cycleDistance) -and $cycleDistance -gt 0 -and $definition.walkSpeedPxPerSec -gt 0) 'Distance-driven walking requires physical stride metrics'
    $cycleSeconds = $cycleDistance / [double]$definition.walkSpeedPxPerSec
    Assert-Walk ($cycleSeconds -ge 0.25 -and $cycleSeconds -le 2.5) 'Companion stride cadence is outside a readable walking range'
}
$passed++
Write-Output "PASS Profile/quarter cycles contain $expectedSamples samples with complete front and optional settling poses"

$loaded = @{}
try {
    foreach ($file in @($assetViews | ForEach-Object { $definition.frames.$_ }) + @('idle.png', 'look_left.png', 'look_right.png') | Select-Object -Unique) {
        $frame = [CharacterBuilder]::LoadArgb((Join-Path $CharacterDirectory $file))
        $loaded[$file] = $frame
        Assert-Walk ($frame.Width -eq 128 -and $frame.Height -eq 128) "Directional frame changed canvas: $file"
        $bounds = [CharacterBuilder]::OpaqueBounds($frame, 100)
        Assert-Walk ($bounds.Bottom -eq 122 -and $definition.anchor.y -eq 122) "Directional feet moved off baseline: $file"
        Assert-Walk ($bounds.Left -gt 0 -and $bounds.Right -lt 128 -and $bounds.Top -gt 0) "Directional artwork is clipped: $file"
    }
    $passed++
    Write-Output 'PASS All views share a stable canvas and foot baseline without clipped artwork'

    $side = $loaded[$definition.frames.walk[0]]
    $turn = $loaded[$definition.frames.walkTurn[0]]
    $front = $loaded[$definition.frames.walkFront[0]]
    foreach ($pair in @(@($side, $front), @($turn, $front), @($side, $turn))) {
        $changed = Get-ChangedPixels $pair[0] $pair[1] 35 105
        Assert-Walk ($changed -gt 150) "Walking views still share the same front-facing artwork ($changed changed face/body pixels)"
    }
    $passed++
    Write-Output 'PASS Side, quarter-turn and front poses have visibly distinct face/body views'

    foreach ($view in $movingViews) {
        $files = $definition.frames.$view
        $reference = $loaded[$files[0]]
        $maximumFootDifference = 0
        foreach ($file in $files) {
            $difference = Get-ChangedPixels $reference $loaded[$file] 105 124
            $maximumFootDifference = [Math]::Max($maximumFootDifference, $difference)
        }
        Assert-Walk ($maximumFootDifference -gt 12) "Companion/$view has no visible stepping variation ($maximumFootDifference changed foot pixels)"
    }
    if ($groundedWalk) {
        foreach ($file in $definition.frames.walkFront) {
            Assert-Walk ((Get-ChangedPixels $loaded[$file] $loaded['idle.png']) -eq 0) 'Frontal rest includes a stepping pose'
        }
    }
    $passed++
    Write-Output 'PASS Moving views include visible foot travel while distance-driven frontal rest stays still'

    $left = $loaded['look_left.png']
    $right = $loaded['look_right.png']
    $idle = $loaded['idle.png']
    Assert-Walk ((Get-ChangedPixels $left $idle 35 105) -gt 100) 'Left look still uses the front idle pose'
    Assert-Walk ((Get-ChangedPixels $right $idle 35 105) -gt 100) 'Right look still uses the front idle pose'
    Assert-Walk ((Get-ChangedPixels $left $right 0 128 $true) -eq 0) 'Directional looks must be matched left/right counterparts'
    $passed++
    Write-Output 'PASS Cursor looks use matched directional poses instead of tilting the front image'
}
finally { foreach ($frame in $loaded.Values) { $frame.Dispose() } }
Write-Output "$passed/5 Companion directional visual regression groups passed"
