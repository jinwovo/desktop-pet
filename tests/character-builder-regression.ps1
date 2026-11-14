param([string]$CharactersRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\Characters'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$builderPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\CharacterBuilder.cs'
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition (Get-Content -LiteralPath $builderPath -Raw -Encoding UTF8)

function Assert-VisualInvariant([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Get-OpaqueArea($Bitmap, [int]$Top = 0, [int]$Bottom = 128) {
    $area = 0
    for ($y = $Top; $y -lt [Math]::Min($Bottom, $Bitmap.Height); $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -ge 100) { $area++ }
        }
    }
    return $area
}

$passed = 0
$names = @('Companion', 'Chiikawa', 'Chiikawa2', 'Hachiware', 'Usagi') | Where-Object { Test-Path -LiteralPath (Join-Path (Join-Path $CharactersRoot $_) 'character.json') }
Assert-VisualInvariant (@($names).Count -gt 0) 'No character is available for geometry regression checks'
foreach ($name in $names) {
    $folder = Join-Path $CharactersRoot $name
    $definition = Get-Content -LiteralPath (Join-Path $folder 'character.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $groundedWalk = $null -ne $definition.walkCycleDistancePx
    $expectedSamples = if ($groundedWalk) { 32 } else { 8 }
    Assert-VisualInvariant ($definition.frames.walk.Count -eq $expectedSamples) "$name walk cycle has the wrong sample count"
    if ($groundedWalk) {
        $cycleDistance = [double]$definition.walkCycleDistancePx
        Assert-VisualInvariant (-not [double]::IsNaN($cycleDistance) -and -not [double]::IsInfinity($cycleDistance) -and $cycleDistance -gt 0) "$name walk cycle needs a physical distance"
        Assert-VisualInvariant ($definition.walkSpeedPxPerSec -gt 0 -and $definition.frameIntervalMs -gt 0) "$name physical gait needs positive nominal speed and fallback timing"
        $cycleSeconds = $cycleDistance / [double]$definition.walkSpeedPxPerSec
        Assert-VisualInvariant ($cycleSeconds -ge 0.25 -and $cycleSeconds -le 2.5) "$name nominal stride cadence is outside a readable walking range"
        Assert-VisualInvariant (@($definition.frames.walkSettleSide).Count -eq $expectedSamples * 2 -and @($definition.frames.walkSettleTurn).Count -eq $expectedSamples * 2) "$name cannot lower its feet from every gait phase"
    } else {
        Assert-VisualInvariant ($definition.frameIntervalMs -eq 100) "$name legacy walk cycle timing changed"
    }
    $idle = [CharacterBuilder]::LoadArgb((Join-Path $folder 'idle.png'))
    try {
        $idleBounds = [CharacterBuilder]::OpaqueBounds($idle, 100)
        $idleArea = Get-OpaqueArea $idle
        foreach ($state in @('idle', 'walk', 'walkFront', 'walkTurn', 'walkSettleSide', 'walkSettleTurn', 'cry', 'sleep', 'angry', 'sit', 'lookLeft', 'lookRight')) {
            $walkReference = $null
            $referenceBounds = $idleBounds
            $referenceArea = $idleArea
            $referenceTorsoArea = 0
            $articulated = $name -eq 'Companion' -and $state -in @('walk', 'walkTurn')
            $stateFrames = $definition.frames.$state
            if ($name -eq 'Companion' -and $state -in @('walk', 'walkFront', 'walkTurn') -and $null -ne $stateFrames -and @($stateFrames).Count -gt 0) {
                # Profiles have a different silhouette from front idle; preserve geometry within each view.
                $walkReference = [CharacterBuilder]::LoadArgb((Join-Path $folder $stateFrames[0]))
                $referenceBounds = [CharacterBuilder]::OpaqueBounds($walkReference, 100)
                $referenceArea = Get-OpaqueArea $walkReference
                $referenceTorsoArea = Get-OpaqueArea $walkReference 40 105
            }
            try {
            foreach ($file in $definition.frames.$state) {
                $frame = [CharacterBuilder]::LoadArgb((Join-Path $folder $file))
                try {
                    $bounds = [CharacterBuilder]::OpaqueBounds($frame, 100)
                    Assert-VisualInvariant ($frame.Width -eq 128 -and $frame.Height -eq 128) "$name/$file canvas changed"
                    Assert-VisualInvariant ($bounds.Bottom -eq $definition.anchor.y) "$name/$file feet are not aligned"
                    Assert-VisualInvariant ($bounds.Left -gt 0 -and $bounds.Right -lt 128 -and $bounds.Top -gt 0) "$name/$file is clipped"
                    if ($state -in @('walk', 'walkFront', 'walkTurn')) {
                        $area = Get-OpaqueArea $frame
                        # Articulated arms and the far foot legitimately overlap the torso in a profile.
                        # Keep the torso threshold strict while allowing its limbs to change the outline.
                        $areaTolerance = if ($articulated) { 0.07 } else { 0.035 }
                        $widthTolerance = if ($articulated) { 6 } else { 3 }
                        Assert-VisualInvariant ([Math]::Abs($area - $referenceArea) / $referenceArea -lt $areaTolerance) "$name/$file loses or stretches artwork"
                        if ($articulated) {
                            $torsoArea = Get-OpaqueArea $frame 40 105
                            Assert-VisualInvariant ([Math]::Abs($torsoArea - $referenceTorsoArea) / $referenceTorsoArea -lt 0.035) "$name/$file torso deformed"
                        }
                        Assert-VisualInvariant ([Math]::Abs($bounds.Width - $referenceBounds.Width) -le $widthTolerance) "$name/$file width deformed"
                        Assert-VisualInvariant ([Math]::Abs($bounds.Height - $referenceBounds.Height) -le 2) "$name/$file height deformed"
                    }
                    if ($state -eq 'cry') {
                        $newBlue = 0
                        $minimumY = [Math]::Min($definition.eyePositionsPx.left.y, $definition.eyePositionsPx.right.y) + 4
                        $maximumY = [Math]::Max($definition.eyePositionsPx.left.y, $definition.eyePositionsPx.right.y) + 23
                        for ($y = 0; $y -lt 128; $y++) {
                            for ($x = 0; $x -lt 128; $x++) {
                                $color = $frame.GetPixel($x, $y)
                                $original = $idle.GetPixel($x, $y)
                                $isNewBlue = $color.A -gt 100 -and $color.B -gt $color.R + 35 -and $color.B -gt $color.G + 8 -and $color.ToArgb() -ne $original.ToArgb()
                                if ($isNewBlue) {
                                    $newBlue++
                                    Assert-VisualInvariant ($y -ge $minimumY -and $y -le $maximumY) "$name/$file tear is away from the eyes"
                                }
                            }
                        }
                        Assert-VisualInvariant ($newBlue -gt 8) "$name/$file tears missing"
                    }
                }
                finally { $frame.Dispose() }
            }
            }
            finally { if ($walkReference) { $walkReference.Dispose() } }
        }
        $walkHashes = @($definition.frames.walk | ForEach-Object { (Get-FileHash -LiteralPath (Join-Path $folder $_) -Algorithm SHA256).Hash } | Select-Object -Unique)
        Assert-VisualInvariant ($walkHashes.Count -ge 5) "$name walk animation has too few distinct poses"
    }
    finally { $idle.Dispose() }
    $passed++
    Write-Output "PASS ${name}: baseline, geometry, $expectedSamples-sample cycle, tears below eye landmarks"
}

# A wide source must fit without horizontal clipping or independent X/Y rescaling.
$wide = New-Object System.Drawing.Bitmap 240,80
$drawing = [System.Drawing.Graphics]::FromImage($wide)
try { $drawing.Clear([System.Drawing.Color]::White) } finally { $drawing.Dispose() }
$anchor = 0
$normalized = [CharacterBuilder]::Normalize($wide, 128, 128, 100, 6, [ref]$anchor)
try {
    $bounds = [CharacterBuilder]::OpaqueBounds($normalized, 100)
    Assert-VisualInvariant ($bounds.Width -le 116 -and $bounds.Left -ge 6) 'Wide source was clipped'
    Assert-VisualInvariant ([Math]::Abs($bounds.Width / [double]$bounds.Height - 3.0) -lt 0.12) 'Wide source aspect ratio changed'
}
finally { $normalized.Dispose(); $wide.Dispose() }
$passed++
Write-Output 'PASS Wide artwork fits the canvas with its original aspect ratio'
Write-Output "$passed/$(@($names).Count + 1) visual regression groups passed"
