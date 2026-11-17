<#
.SYNOPSIS
    Builds the optional personal character pack from transparent 2x4 master sheets.
.DESCRIPTION
    Existing characters and source images are preserved. Public DistributionBuild excludes this pack.
#>
param(
    [ValidateSet('All','Chiikawa','Baikinman','HelloKitty')][string]$Character = 'All',
    [string]$OutputDirectory = '',
    [string]$PreviewPath = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-DesktopPetCharacterBehavior.ps1')
Add-Type -AssemblyName System.Drawing
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
function Resolve-PersonalPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Personal character outputs must stay inside this workspace.' }
    $ancestor = $full
    while ($ancestor -and $ancestor.Length -ge $repoRoot.Length) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Personal paths cannot traverse links: $ancestor" }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
$characterRoot = Resolve-PersonalPath $(if ($OutputDirectory) { $OutputDirectory } else { Join-Path $repoRoot 'Assets\Characters' })
$imageRoot = Join-Path $repoRoot 'images'
if (($characterRoot + '\').StartsWith($imageRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Character output cannot replace source images.' }
$preview = Resolve-PersonalPath $(if ($PreviewPath) { $PreviewPath } else { Join-Path $repoRoot '.local\personal-characters-v6.png' })
if ($preview.StartsWith($imageRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $preview.StartsWith($characterRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Preview cannot replace source images or character outputs.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CharacterBuilder.cs')))
$profiles = @(
    @{ Id='Chiikawa'; Name='치이카와'; File='personal-chiikawa-master-v1.png'; EyeY=0.36; LeftX=0.36; RightX=0.64 },
    @{ Id='Baikinman'; Name='세균맨'; File='personal-baikinman-master-v1.png'; EyeY=0.30; LeftX=0.42; RightX=0.59 },
    @{ Id='HelloKitty'; Name='헬로키티'; File='personal-hellokitty-master-v1.png'; EyeY=0.38; LeftX=0.30; RightX=0.70 }
) | Where-Object { $Character -eq 'All' -or $_.Id -eq $Character }
function Get-PersonalCell($Sheet, [int]$Row, [int]$Column) {
    $left=[int][Math]::Floor($Sheet.Width*$Column/4.0); $right=[int][Math]::Floor($Sheet.Width*($Column+1)/4.0)
    $top=[int][Math]::Floor($Sheet.Height*$Row/2.0); $bottom=[int][Math]::Floor($Sheet.Height*($Row+1)/2.0)
    $cell=[CharacterBuilder]::Crop($Sheet,$left,$top,$right-$left,$bottom-$top)
    $trimmed=$null
    try {
        $bounds=[CharacterBuilder]::OpaqueBounds($cell,25)
        if ($bounds.Width -lt 1 -or $bounds.Height -lt 1 -or $bounds.Left -eq 0 -or $bounds.Right -ge $cell.Width -or $bounds.Top -eq 0 -or $bounds.Bottom -ge $cell.Height) { throw "Master cell $Row/$Column is empty or clipped." }
        $trimmed=[CharacterBuilder]::Trim($cell,25)
        $anchor=0
        return [CharacterBuilder]::Normalize($trimmed,128,128,100,6,[ref]$anchor)
    } finally { if ($trimmed) { $trimmed.Dispose() }; $cell.Dispose() }
}
function Save-PersonalFrame([string]$Folder,[string]$Name,$Frame) {
    try { [CharacterBuilder]::Save($Frame,(Resolve-PersonalPath (Join-Path $Folder $Name))) } finally { $Frame.Dispose() }
}
$built=@()
foreach ($profile in $profiles) {
    $source=Resolve-PersonalPath (Join-Path $imageRoot $profile.File)
    $folder=Resolve-PersonalPath (Join-Path $characterRoot $profile.Name)
    $marker=Join-Path $folder '.desktop-pet-personal-generated'
    if (Test-Path -LiteralPath $folder) {
        if (-not (Test-Path -LiteralPath $folder -PathType Container) -or
            (-not (Test-Path -LiteralPath $marker) -and @(Get-ChildItem -LiteralPath $folder -Force).Count -gt 0)) { throw "Refusing to overwrite an unrecognized character folder: $folder" }
        if ((Test-Path -LiteralPath $marker) -and [IO.File]::ReadAllText($marker).Trim() -ne 'DesktopPet.Personal.v1') { throw 'Invalid personal output marker.' }
    }
    $sheet=[CharacterBuilder]::LoadArgb($source)
    $poses=@()
    try {
        if ($sheet.GetPixel(0,0).A -ne 0) { throw 'Personal master sheets require transparency.' }
        $poses=@(
            (Get-PersonalCell $sheet 0 0), (Get-PersonalCell $sheet 0 1),
            (Get-PersonalCell $sheet 0 2), (Get-PersonalCell $sheet 0 3),
            (Get-PersonalCell $sheet 1 0), (Get-PersonalCell $sheet 1 1)
        )
        [void][IO.Directory]::CreateDirectory($folder)
        $idle=$poses[0]; $blink=$poses[1]
        Save-PersonalFrame $folder 'idle.png' ([CharacterBuilder]::PoseGrounded($idle,122,0,0))
        Save-PersonalFrame $folder 'blink.png' ([CharacterBuilder]::PoseGrounded($blink,122,0,0))
        Save-PersonalFrame $folder 'sit.png' ([CharacterBuilder]::PoseGrounded($idle,122,0,0))
        Save-PersonalFrame $folder 'look_right.png' ([CharacterBuilder]::PoseGrounded($poses[4],122,0,0))
        $leftLook=[CharacterBuilder]::PoseGrounded($poses[4],122,0,0)
        $leftLook.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX)
        Save-PersonalFrame $folder 'look_left.png' $leftLook
        Save-PersonalFrame $folder 'drag.png' ([CharacterBuilder]::Pose($idle,122,1,1,-3,0,0))
        Save-PersonalFrame $folder 'fall.png' ([CharacterBuilder]::Pose($idle,122,1,1,3,0,0))
        $walk=@(); $turn=@()
        for ($i=0;$i -lt 8;$i++) {
            $phase=2*[Math]::PI*$i/8
            $key=[int][Math]::Floor($i/2.0)%2
            $walkName='walk_{0}.png' -f ($i+1); $turnName='walk_turn_{0}.png' -f ($i+1)
            $walk+=$walkName; $turn+=$turnName
            Save-PersonalFrame $folder $walkName ([CharacterBuilder]::PoseGrounded($poses[2+$key],122,0.18*[Math]::Sin($phase),0))
            Save-PersonalFrame $folder $turnName ([CharacterBuilder]::PoseGrounded($poses[4+$key],122,0.18*[Math]::Sin($phase),0))
        }
        $bounds=[CharacterBuilder]::OpaqueBounds($idle,100)
        $leftX=$bounds.Left+$bounds.Width*$profile.LeftX; $rightX=$bounds.Left+$bounds.Width*$profile.RightX
        $eyeY=$bounds.Top+$bounds.Height*$profile.EyeY
        for ($i=1;$i -le 2;$i++) {
            $cry=[CharacterBuilder]::PoseGrounded($idle,122,0,0)
            [CharacterBuilder]::DrawTearsAt($cry,$leftX,$eyeY,$rightX,$eyeY,($i-1)*0.7)
            Save-PersonalFrame $folder "cry_$i.png" $cry
            $angry=[CharacterBuilder]::PoseGrounded($idle,122,0,0)
            [CharacterBuilder]::TintRed($angry,0.035)
            Save-PersonalFrame $folder "angry_$i.png" $angry
            Save-PersonalFrame $folder "sleep_$i.png" ([CharacterBuilder]::PoseGrounded($blink,122,0,0))
        }
        $manifest=[ordered]@{
            name=$profile.Name; scale=1.0; frontFacingRest=$true
            frameSizePx=@{width=128;height=128}; anchor=@{x=64;y=122}
            walkSpeedPxPerSec=42; frameIntervalMs=110
            frames=[ordered]@{
                idle=@('idle.png','blink.png','idle.png','blink.png')
                walk=$walk; walkTurn=$turn; walkFront=@('idle.png')*8
                sit=@('sit.png'); lookLeft=@('look_left.png'); lookRight=@('look_right.png')
                drag=@('drag.png'); fall=@('fall.png'); cry=@('cry_1.png','cry_2.png')
                angry=@('angry_1.png','angry_2.png'); sleep=@('sleep_1.png','sleep_2.png')
            }
            frameDurationsMs=@{idle=@(4200,120,3300,110)}
            eyePositionsPx=@{left=@{x=$leftX;y=$eyeY};right=@{x=$rightX;y=$eyeY}}
            assetKind='optional-personal-fan-art'
        }
        Set-DesktopPetCharacterBehavior -Definition $manifest -Name $profile.Name
        [IO.File]::WriteAllText((Join-Path $folder 'character.json'),($manifest|ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding $false))
        [IO.File]::WriteAllText($marker,'DesktopPet.Personal.v1',(New-Object Text.UTF8Encoding $false))
        $built+=@{Profile=$profile;Folder=$folder}
        Write-Output "Built personal character: $($profile.Name)"
    } finally { foreach ($pose in $poses) { $pose.Dispose() }; $sheet.Dispose() }
}
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preview))
$previewSheet=New-Object Drawing.Bitmap (7*160),($built.Count*190)
$graphics=[Drawing.Graphics]::FromImage($previewSheet)
$font=New-Object Drawing.Font 'Malgun Gothic',11
$brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(53,65,70))
try {
    $graphics.Clear([Drawing.Color]::FromArgb(244,247,241))
    $files=@('idle.png','blink.png','walk_1.png','walk_3.png','look_left.png','look_right.png','sleep_1.png')
    for ($row=0;$row -lt $built.Count;$row++) {
        for ($column=0;$column -lt $files.Count;$column++) {
            $label=if ($column -eq 0) { $built[$row].Profile.Name } else { $files[$column].Replace('.png','') }
            $graphics.DrawString($label,$font,$brush,[single]($column*160+12),[single]($row*190+12))
            $frame=[CharacterBuilder]::LoadArgb((Join-Path $built[$row].Folder $files[$column]))
            try { $graphics.DrawImageUnscaled($frame,$column*160+16,$row*190+42) } finally { $frame.Dispose() }
        }
    }
    [CharacterBuilder]::Save($previewSheet,$preview)
} finally { $brush.Dispose();$font.Dispose();$graphics.Dispose();$previewSheet.Dispose() }
Write-Output "Preview: $preview"
