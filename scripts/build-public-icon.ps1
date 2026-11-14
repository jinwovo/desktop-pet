# Build the app's multi-resolution icon from the original Companion idle artwork.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'Install-DesktopPetPayload.ps1')
$source = Join-Path $repoRoot 'Assets\Characters\Companion\idle.png'
$destination = Join-Path $repoRoot 'Assets\AppIcon.ico'
Assert-DesktopPetNoReparsePoint $source
Assert-DesktopPetNoReparsePoint $destination
$original = [Drawing.Bitmap]::FromFile($source)
$images = New-Object 'System.Collections.Generic.List[byte[]]'
$sizes = @(16,24,32,48,64,128)
try {
    foreach ($size in $sizes) {
        $bitmap = New-Object Drawing.Bitmap $size,$size
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = New-Object IO.MemoryStream
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($original,0,0,$size,$size)
            $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png)
            $images.Add($stream.ToArray())
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
    $stream = New-Object IO.MemoryStream
    $writer = New-Object IO.BinaryWriter $stream
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i=0; $i -lt $sizes.Count; $i++) {
            $writer.Write([byte]$sizes[$i]); $writer.Write([byte]$sizes[$i]); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
        $writer.Flush()
        [IO.File]::WriteAllBytes($destination,$stream.ToArray())
    } finally { $writer.Dispose(); $stream.Dispose() }
} finally { $original.Dispose() }
Write-Output 'Public app icon rebuilt from Companion (16 through128px).'
