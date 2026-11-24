$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $repoRoot 'scripts\CharacterBuilder.cs')))
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
public static class AngerOverlayMetrics
{
    // All nontransparent source pixels, including antialiasing, belong to the character.
    public static double[] Compare(Bitmap source, Bitmap result)
    {
        int changed = 0, added = 0, red = 0, edge = 0, touching = 0;
        int left = result.Width, top = result.Height, right = -1, bottom = -1;
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                Color before = source.GetPixel(x, y), after = result.GetPixel(x, y);
                if (before.A > 0 && before.ToArgb() != after.ToArgb()) changed++;
                if (before.A != 0 || after.A == 0) continue;
                added++;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                if (after.R > 150 && after.G < 100 && after.B < 130) red++;
                if (x == 0 || y == 0 || x == source.Width - 1 || y == source.Height - 1) edge++;
                bool adjacent = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx >= 0 && nx < source.Width && ny >= 0 && ny < source.Height && source.GetPixel(nx, ny).A > 0)
                            adjacent = true;
                    }
                if (adjacent) touching++;
            }
        return new double[] { changed, added, red, edge, touching, (left + right) / 2.0, (top + bottom) / 2.0 };
    }
    public static bool Equal(Bitmap first, Bitmap second)
    {
        for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) return false;
        return true;
    }
    public static double[] ShippedMark(Bitmap source, Bitmap result)
    {
        int count = 0, overlaps = 0, left = result.Width, right = -1, top = result.Height, bottom = -1;
        for (int y = 0; y < result.Height; y++)
            for (int x = 0; x < result.Width; x++)
            {
                Color color = result.GetPixel(x, y);
                if (color.A < 240 || Math.Abs(color.R - 228) > 2 || Math.Abs(color.G - 35) > 2 || Math.Abs(color.B - 58) > 2) continue;
                count++;
                if (source.GetPixel(x, y).A > 0) overlaps++;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
        return new double[] { count, overlaps, (left + right) / 2.0, (top + bottom) / 2.0 };
    }
}
'@

$failures = New-Object 'System.Collections.Generic.List[string]'
function Assert-AngerInvariant([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:failures.Add($Message) }
}
function Test-AngerPlacement($Source, [string]$Name, [int]$SpriteHeight = 100) {
    $firstCenter = $null
    foreach ($pulse in @(0.0, 0.3, 1.0)) {
        $result = $Source.Clone()
        try {
            [CharacterBuilder]::DrawAngerMark($result, 122, $SpriteHeight, $pulse)
            $metrics = [AngerOverlayMetrics]::Compare($Source, $result)
            Write-Output ("{0} pulse={1}: changed-body={2}, added={3}, red={4}, edge={5}, touching={6}, center={7},{8}" -f $Name,$pulse,$metrics[0],$metrics[1],$metrics[2],$metrics[3],$metrics[4],$metrics[5],$metrics[6])
            Assert-AngerInvariant ($metrics[0] -eq 0) "$Name/$pulse overwrites character artwork"
            Assert-AngerInvariant ($metrics[2] -ge 10) "$Name/$pulse has no readable red glyph"
            Assert-AngerInvariant ($metrics[3] -eq 0) "$Name/$pulse clips the canvas edge"
            Assert-AngerInvariant ($metrics[4] -eq 0) "$Name/$pulse touches character artwork"
            if ($null -eq $firstCenter) { $firstCenter = @($metrics[5], $metrics[6]) }
            else {
                Assert-AngerInvariant ([Math]::Abs($metrics[5] - $firstCenter[0]) -le 0.5 -and [Math]::Abs($metrics[6] - $firstCenter[1]) -le 0.5) "$Name pulse shifts glyph center"
            }
        }
        finally { $result.Dispose() }
    }
}

foreach ($relative in @('Assets\Characters\Companion', 'Assets\Characters\Nimbus', 'Assets\Characters\Ember',
    'character-packs\fan-art\폼폼푸린', 'character-packs\fan-art\시나모롤', 'character-packs\fan-art\쿠로미')) {
    $source = [CharacterBuilder]::LoadArgb((Join-Path (Join-Path $repoRoot $relative) 'idle.png'))
    try {
        $name = Split-Path -Leaf $relative
        Test-AngerPlacement $source $name
        $previous = $null
        foreach ($frame in @('angry_1.png','angry_2.png')) {
            $shipped = [Drawing.Bitmap]::FromFile((Join-Path (Join-Path $repoRoot $relative) $frame))
            try {
                $mark = [AngerOverlayMetrics]::ShippedMark($source, $shipped)
                Assert-AngerInvariant ($mark[0] -ge 10) "$name/$frame shipped glyph missing"
                Assert-AngerInvariant ($mark[1] -eq 0) "$name/$frame shipped glyph overwrites the silhouette"
                if ($null -ne $previous) {
                    # Companion's two poses retain their authored +/-0.4 degree sway.
                    Assert-AngerInvariant ([Math]::Abs($mark[2] - $previous[2]) -le 2 -and [Math]::Abs($mark[3] - $previous[3]) -le 2) "$name shipped glyph jumps between frames"
                }
                $previous = $mark
            }
            finally { $shipped.Dispose() }
        }
    }
    finally { $source.Dispose() }
}

# A tall appendage occupies the old fixed anchor. The character must remain intact.
$synthetic = New-Object System.Drawing.Bitmap 128,128
$drawing = [System.Drawing.Graphics]::FromImage($synthetic)
try {
    $drawing.Clear([System.Drawing.Color]::Transparent)
    $drawing.FillRectangle([System.Drawing.Brushes]::Navy, 15, 35, 82, 87)
    $drawing.FillRectangle([System.Drawing.Brushes]::Green, 76, 18, 20, 26)
}
finally { $drawing.Dispose() }
try {
    Test-AngerPlacement $synthetic 'Synthetic sprout'
    foreach ($pair in @(@(0.0,-1.0), @(0.0,[double]::NaN), @(1.0,[double]::PositiveInfinity))) {
        $expected = $synthetic.Clone(); $actual = $synthetic.Clone()
        try {
            [CharacterBuilder]::DrawAngerMark($expected, 122, 100, $pair[0])
            [CharacterBuilder]::DrawAngerMark($actual, 122, 100, $pair[1])
            Assert-AngerInvariant ([AngerOverlayMetrics]::Equal($expected, $actual)) 'Out-of-range pulse differs from its safe endpoint'
        }
        finally { $expected.Dispose(); $actual.Dispose() }
    }
}
finally { $synthetic.Dispose() }

foreach ($size in @(8,128)) {
    $full = New-Object System.Drawing.Bitmap $size,$size
    $drawing = [System.Drawing.Graphics]::FromImage($full)
    try { $drawing.Clear([System.Drawing.Color]::Navy) }
    finally { $drawing.Dispose() }
    $result = $full.Clone()
    try {
        [CharacterBuilder]::DrawAngerMark($result, $size - 1, $size - 2, 1.0)
        Assert-AngerInvariant ([AngerOverlayMetrics]::Equal($full,$result)) "Full $size canvas must remain intact when no mark fits"
    }
    finally { $full.Dispose(); $result.Dispose() }
}

if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Output 'PASS anger overlay: six actual silhouettes, fixed pulse centers, clear gap, no clipping, synthetic sprout, pulse bounds and no-space safety'
