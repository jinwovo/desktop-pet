<#
.SYNOPSIS
    images\ 폴더의 그림들을 훑어서 DesktopPet 캐릭터를 자동으로 만들어낸다.

.DESCRIPTION
    원하는 캐릭터 그림을 images\ 폴더에 넣고 이 스크립트를 돌리기만 하면 된다. 좌표를 적거나
    설정을 건드릴 필요가 없다. 스크립트가 알아서 판단하는 것들:

      * 배경 — 테두리 픽셀을 투표해서 배경색을 찾고(흰색이든 분홍이든), 이미 투명하면 건너뛴다.
      * 캐릭터 개수 — 덩어리(연결 요소) 분석으로 떨어져 있는 캐릭터들을 분리하고,
        손을 잡고 붙어 있어서 한 덩어리가 된 경우는 '이음새'(세로로 그림이 가장 적은 열)를
        찾아 다시 쪼갠다.
      * 포즈 — 원본은 보통 정면 한 장뿐이므로 걷기/앉기/울기/화내기 프레임을 변형으로 합성한다.

    캐릭터 이름은 파일 이름에서 따온다. 한 그림에 여러 캐릭터가 있으면 왼쪽부터 번호가 붙는다
    (예: trio.png -> trio1, trio2, trio3).

    이름을 직접 정하고 싶으면 images\overrides.json 을 만들어 두면 된다:
        {
          "trio.png":  { "names": ["Hachiware", "Chiikawa", "Usagi"], "walkSpeed": 65 },
          "images.jpg": { "names": ["Chiikawa"], "walkSpeed": 55 }
        }

.EXAMPLE
    .\scripts\build-characters.ps1
    .\scripts\build-characters.ps1 -Clean      # 기존 캐릭터 폴더를 지우고 새로 생성
#>
param(
    [int]$CanvasSize = 128,
    [int]$TargetHeight = 100,
    [int]$BottomMargin = 6,
    [int]$BackgroundTolerance = 18,
    [double]$FootBandRatio = 0.17,
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

$repoRoot = Split-Path -Parent $PSScriptRoot
$helperSource = Get-Content (Join-Path $PSScriptRoot "CharacterBuilder.cs") -Raw
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition $helperSource

$imagesDir = Join-Path $repoRoot "images"
$charactersRoot = Join-Path $repoRoot "Assets\Characters"

if (-not (Test-Path $imagesDir)) {
    throw "images 폴더가 없습니다: $imagesDir"
}

$overrides = @{}
$overridePath = Join-Path $imagesDir "overrides.json"
if (Test-Path $overridePath) {
    $raw = Get-Content $overridePath -Raw | ConvertFrom-Json
    foreach ($prop in $raw.PSObject.Properties) { $overrides[$prop.Name] = $prop.Value }
    Write-Host "overrides.json 적용 ($($overrides.Count)개 항목)" -ForegroundColor DarkGray
}

# Whole-sprite poses. Scaling is kept close to 1.0 on purpose: anything stronger visibly
# squashes the character, which reads as a distorted photo rather than a pose.
$Poses = @(
    @{ File = "idle.png";       ScaleX = 1.00; ScaleY = 1.00; Rotate =  0.0; Dx =  0.0; Dy =  0.0 }
    @{ File = "idle_2.png";     ScaleX = 1.008; ScaleY = 0.985; Rotate = 0.0; Dx = 0.0; Dy =  0.0 }  # breathing
    @{ File = "sit.png";        ScaleX = 1.03; ScaleY = 0.92; Rotate =  0.0; Dx =  0.0; Dy =  0.0 }
    @{ File = "look_left.png";  ScaleX = 1.00; ScaleY = 1.00; Rotate = -3.0; Dx = -1.0; Dy =  0.0 }
    @{ File = "look_right.png"; ScaleX = 1.00; ScaleY = 1.00; Rotate =  3.0; Dx =  1.0; Dy =  0.0 }
    @{ File = "drag.png";       ScaleX = 0.97; ScaleY = 1.04; Rotate =  0.0; Dx =  0.0; Dy =  0.0 }
    @{ File = "fall.png";       ScaleX = 0.98; ScaleY = 1.03; Rotate = 11.0; Dx =  0.0; Dy =  0.0 }
)

# Walk frames rig the feet separately (see CharacterBuilder.PoseWalk). One foot lifts and swings
# forward while the other stays planted. The body leans toward the *planted* foot, since that's
# where the weight is -- leaning toward the raised foot looks like falling over.
# Lift stays small and the swing does the work: these characters' feet are already drawn as
# strokes detached from the body outline, so lifting one far just looks like a dropped part.
$WalkFrames = @(
    @{ File = "walk_1.png"; BodyDy =  0.0; Rotate =  1.8; LeftLift = 2.5; LeftDx =  4.0; RightLift = 0.0; RightDx = -1.5 }
    @{ File = "walk_2.png"; BodyDy = -1.5; Rotate =  0.0; LeftLift = 1.0; LeftDx =  1.5; RightLift = 1.0; RightDx =  0.0 }
    @{ File = "walk_3.png"; BodyDy =  0.0; Rotate = -1.8; LeftLift = 0.0; LeftDx = -1.5; RightLift = 2.5; RightDx =  4.0 }
    @{ File = "walk_4.png"; BodyDy = -1.5; Rotate =  0.0; LeftLift = 1.0; LeftDx =  0.0; RightLift = 1.0; RightDx =  1.5 }
)

$CryPoses = @(
    @{ File = "cry_1.png"; ScaleX = 1.03; ScaleY = 0.96; Rotate = -2.5; Dx = -1; Dy = 2; Drop = 0.15 }
    @{ File = "cry_2.png"; ScaleX = 1.04; ScaleY = 0.94; Rotate =  2.5; Dx =  1; Dy = 3; Drop = 0.85 }
)

$AngryPoses = @(
    @{ File = "angry_1.png"; ScaleX = 1.04; ScaleY = 1.03; Rotate = -3.0; Dx = -2; Dy = -1; Tint = 0.16; Pulse = 0.0 }
    @{ File = "angry_2.png"; ScaleX = 1.06; ScaleY = 1.05; Rotate =  3.0; Dx =  2; Dy = -2; Tint = 0.26; Pulse = 1.0 }
)

# Asleep: settled down and breathing slowly, with drifting z's.
$SleepPoses = @(
    @{ File = "sleep_1.png"; ScaleX = 1.05; ScaleY = 0.90; Rotate = -4.0; Dx = -1; Dy = 0; Drift = 0.0 }
    @{ File = "sleep_2.png"; ScaleX = 1.06; ScaleY = 0.88; Rotate = -4.0; Dx = -1; Dy = 0; Drift = 1.0 }
)

<#
    Loads any image into a GDI+ bitmap. System.Drawing can't read .webp, so route everything it
    rejects through the WPF imaging stack, which can -- preserving alpha.
#>
function Import-SourceImage {
    param([string]$Path)

    try {
        return [CharacterBuilder]::LoadArgb($Path)
    }
    catch {
        $uri = New-Object System.Uri $Path
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create($uri, 'PreservePixelFormat', 'OnLoad')
        $conv = New-Object System.Windows.Media.Imaging.FormatConvertedBitmap
        $conv.BeginInit()
        $conv.Source = $decoder.Frames[0]
        $conv.DestinationFormat = [System.Windows.Media.PixelFormats]::Bgra32
        $conv.EndInit()

        $tmp = Join-Path $env:TEMP ("dp_" + [System.IO.Path]::GetFileNameWithoutExtension($Path) + ".png")
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($conv))
        $fs = [System.IO.File]::Open($tmp, 'Create')
        $enc.Save($fs)
        $fs.Close()

        return [CharacterBuilder]::LoadArgb($tmp)
    }
}

<# Turns one extracted sprite region into a full character folder. #>
function New-CharacterFromRegion {
    param($Source, [int]$X, [int]$Y, [int]$W, [int]$H, [string]$Name, [int]$WalkSpeed)

    $outDir = Join-Path $charactersRoot $Name
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $region = [CharacterBuilder]::Crop($Source, $X, $Y, $W, $H)
    $trimmed = [CharacterBuilder]::Trim($region, 25)
    $region.Dispose()

    $anchorY = 0
    $base = [CharacterBuilder]::Normalize($trimmed, $CanvasSize, $CanvasSize, $TargetHeight, $BottomMargin, [ref]$anchorY)
    $trimmed.Dispose()

    foreach ($pose in $Poses) {
        $frame = [CharacterBuilder]::Pose($base, $anchorY, $pose.ScaleX, $pose.ScaleY, $pose.Rotate, $pose.Dx, $pose.Dy)
        [CharacterBuilder]::Save($frame, (Join-Path $outDir $pose.File))
        $frame.Dispose()
    }

    # Cut between the feet only where there's genuinely a gap; -1 means they're one blob and the
    # band has to move as a unit (cutting through the foot tears a hole between the legs).
    $splitX = [CharacterBuilder]::FindFootSplitColumn($base, $anchorY, $TargetHeight, $FootBandRatio)
    Write-Host ("     다리 분리: {0}" -f $(if ($splitX -lt 0) { "없음 (한 덩어리로 이동)" } else { "x=$splitX" }))

    foreach ($wf in $WalkFrames) {
        $frame = [CharacterBuilder]::PoseWalk($base, $anchorY, $TargetHeight, $FootBandRatio, $splitX,
            $wf.BodyDy, $wf.Rotate, $wf.LeftLift, $wf.LeftDx, $wf.RightLift, $wf.RightDx)
        [CharacterBuilder]::Save($frame, (Join-Path $outDir $wf.File))
        $frame.Dispose()
    }
    foreach ($pose in $CryPoses) {
        $frame = [CharacterBuilder]::Pose($base, $anchorY, $pose.ScaleX, $pose.ScaleY, $pose.Rotate, $pose.Dx, $pose.Dy)
        [CharacterBuilder]::DrawTears($frame, $anchorY, $TargetHeight, $pose.Drop)
        [CharacterBuilder]::Save($frame, (Join-Path $outDir $pose.File))
        $frame.Dispose()
    }
    foreach ($pose in $AngryPoses) {
        $frame = [CharacterBuilder]::Pose($base, $anchorY, $pose.ScaleX, $pose.ScaleY, $pose.Rotate, $pose.Dx, $pose.Dy)
        [CharacterBuilder]::TintRed($frame, $pose.Tint)
        [CharacterBuilder]::DrawAngerMark($frame, $anchorY, $TargetHeight, $pose.Pulse)
        [CharacterBuilder]::Save($frame, (Join-Path $outDir $pose.File))
        $frame.Dispose()
    }

    foreach ($pose in $SleepPoses) {
        $frame = [CharacterBuilder]::Pose($base, $anchorY, $pose.ScaleX, $pose.ScaleY, $pose.Rotate, $pose.Dx, $pose.Dy)
        [CharacterBuilder]::DrawSleepMark($frame, $anchorY, $TargetHeight, $pose.Drift)
        [CharacterBuilder]::Save($frame, (Join-Path $outDir $pose.File))
        $frame.Dispose()
    }
    $base.Dispose()

    $json = @"
{
  "name": "$Name",
  "scale": 1.0,
  "frameSizePx": { "width": $CanvasSize, "height": $CanvasSize },
  "anchor": { "x": $([int]($CanvasSize / 2)), "y": $anchorY },
  "walkSpeedPxPerSec": $WalkSpeed,
  "frames": {
    "idle": ["idle.png", "idle_2.png"],
    "walk": ["walk_1.png", "walk_2.png", "walk_3.png", "walk_4.png"],
    "sit": ["sit.png"],
    "lookLeft": ["look_left.png"],
    "lookRight": ["look_right.png"],
    "drag": ["drag.png"],
    "fall": ["fall.png"],
    "cry": ["cry_1.png", "cry_2.png"],
    "angry": ["angry_1.png", "angry_2.png"],
    "sleep": ["sleep_1.png", "sleep_2.png"]
  },
  "frameIntervalMs": 160
}
"@
    Set-Content -Path (Join-Path $outDir "character.json") -Value $json -Encoding UTF8
}

function ConvertTo-SafeName {
    param([string]$Raw)
    # .NET's \w already covers Unicode letters, so Korean/Japanese names survive this.
    $clean = ($Raw -replace '[^\w\-]', '')
    if ([string]::IsNullOrWhiteSpace($clean)) { return "Character" }
    return $clean
}

if ($Clean -and (Test-Path $charactersRoot)) {
    Write-Host "기존 캐릭터 폴더 삭제 중..." -ForegroundColor DarkGray
    Get-ChildItem $charactersRoot -Directory | Remove-Item -Recurse -Force
}

$imageFiles = Get-ChildItem $imagesDir -File |
    Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|webp|bmp|gif)$' } |
    Sort-Object Name

if ($imageFiles.Count -eq 0) {
    throw "images 폴더에 이미지가 없습니다: $imagesDir"
}

$made = 0
foreach ($file in $imageFiles) {
    Write-Host ""
    Write-Host "== $($file.Name)" -ForegroundColor Cyan

    $ov = $overrides[$file.Name]
    if ($ov -and $ov.skip) {
        Write-Host "   overrides.json에서 skip 지정됨 — 건너뜀" -ForegroundColor DarkGray
        continue
    }
    $walkSpeed = if ($ov -and $ov.walkSpeed) { [int]$ov.walkSpeed } else { 60 }

    $img = Import-SourceImage -Path $file.FullName
    $usedTol = $BackgroundTolerance
    $usedRadius = 1

    $bg = [CharacterBuilder]::DetectBackgroundColor($img)
    if ($bg -lt 0) {
        Write-Host "   배경: 이미 투명"
    }
    else {
        $r = ($bg -shr 16) -band 0xFF; $g = ($bg -shr 8) -band 0xFF; $b = $bg -band 0xFF
        Write-Host ("   배경: RGB({0},{1},{2})" -f $r, $g, $b)

        # A white character on a white background is the hard case: line art often leaves the
        # outline *open* (limbs drawn as separate strokes), so the fill pours through the gap and
        # eats the body, leaving a hollow shell. Rather than hand-tuning per image, try
        # progressively tighter tolerances with wider gap-sealing, and keep the first result whose
        # largest shape is still solid (see LargestBlobFillRatio).
        $attempts = @(
            @{ Tol = $BackgroundTolerance; R = 1 }
            @{ Tol = 12; R = 2 }
            @{ Tol = 10; R = 3 }
            @{ Tol = 8;  R = 5 }
            @{ Tol = 6;  R = 8 }
            @{ Tol = 5;  R = 12 }
        )

        $cleaned = $null
        foreach ($try in $attempts) {
            $candidate = [CharacterBuilder]::LoadArgbFrom($img)
            [CharacterBuilder]::RemoveBackground($candidate, $bg, $try.Tol, $try.R)
            $fill = [CharacterBuilder]::LargestBlobFillRatio($candidate)

            if ($fill -ge 0.35) {
                $cleaned = $candidate
                $usedTol = $try.Tol
                $usedRadius = $try.R
                Write-Host ("   제거 tolerance={0} gap={1} (채움률 {2:P0})" -f $try.Tol, $try.R, $fill)
                break
            }

            Write-Host ("   tolerance={0} gap={1} -> 채움률 {2:P0}, 재시도" -f $try.Tol, $try.R, $fill) -ForegroundColor DarkYellow
            $candidate.Dispose()
        }

        if (-not $cleaned) {
            Write-Host "   배경을 안전하게 제거하지 못해 건너뜁니다." -ForegroundColor Red
            $img.Dispose()
            continue
        }

        $img.Dispose()
        $img = $cleaned
    }

    # Separate blobs first; then split any blob wide enough to be several joined characters.
    # Low threshold so detached limb strokes are seen at all; FindBlobs merges them into bodies.
    $flat = [CharacterBuilder]::FindBlobs($img, 0.0015)
    $blobs = @()
    for ($i = 0; $i -lt $flat.Length; $i += 4) {
        $blobs += ,@{ X = $flat[$i]; Y = $flat[$i+1]; W = $flat[$i+2]; H = $flat[$i+3] }
    }

    $regions = @()
    foreach ($blob in $blobs) {
        $cuts = @([CharacterBuilder]::FindSplitColumns($img, $blob.X, $blob.Y, $blob.W, $blob.H))
        if ($cuts.Count -eq 0) {
            $regions += ,$blob
            continue
        }

        $edges = @($blob.X) + $cuts + @($blob.X + $blob.W)
        for ($i = 0; $i -lt $edges.Count - 1; $i++) {
            $regions += ,@{ X = $edges[$i]; Y = $blob.Y; W = ($edges[$i+1] - $edges[$i]); H = $blob.H }
        }
    }

    # Left-to-right, so override name lists line up with what a person sees.
    $regions = @($regions | Sort-Object { $_.X })
    Write-Host "   캐릭터 $($regions.Count)개 감지"

    # Only now shave the halo the gap-sealing barrier left around the character. Doing it earlier
    # would re-sever the limb strokes the barrier had bridged, and blob detection would see one
    # character as a handful of pieces again. The regions are already fixed, so losing those
    # bridges here is harmless -- and the sprite's bounds become the character, not the halo.
    if ($usedRadius -gt 1) {
        [CharacterBuilder]::ShrinkFringe($img, $bg, [Math]::Max($usedTol, 20), 1, $usedRadius)
    }
    if ($bg -ge 0) {
        [CharacterBuilder]::FeatherEdges($img, $bg, $usedTol)
    }

    $baseName = ConvertTo-SafeName ([System.IO.Path]::GetFileNameWithoutExtension($file.Name))
    for ($i = 0; $i -lt $regions.Count; $i++) {
        $region = $regions[$i]

        $name = $null
        if ($ov -and $ov.names -and $i -lt $ov.names.Count) { $name = ConvertTo-SafeName $ov.names[$i] }
        if (-not $name) { $name = if ($regions.Count -eq 1) { $baseName } else { "$baseName$($i + 1)" } }

        New-CharacterFromRegion -Source $img -X $region.X -Y $region.Y -W $region.W -H $region.H `
            -Name $name -WalkSpeed $walkSpeed
        Write-Host "     -> $name" -ForegroundColor Green
        $made++
    }

    $img.Dispose()
}

Write-Host ""
Write-Host "캐릭터 $($made)개 생성 완료 -> $charactersRoot" -ForegroundColor Green
