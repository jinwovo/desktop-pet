<# Rebuild three optional personal fan-art packs from transparent image_gen 3x3 master atlases. #>
param(
    [ValidateSet('All','Pompompurin','Cinnamoroll','Kuromi')][string]$Character = 'All',
    [string]$OutputRoot = '',
    [string]$PreviewPath = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-DesktopPetCharacterBehavior.ps1')
Add-Type -AssemblyName System.Drawing
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
function Resolve-PopularPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($repoRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated fan-art assets must stay inside the workspace.' }
    $ancestor = $full
    while ($ancestor -and $ancestor.Length -ge $repoRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Generated assets cannot traverse links.' }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
$output = Resolve-PopularPath $(if ($OutputRoot) { $OutputRoot } else { Join-Path $repoRoot 'character-packs\fan-art' })
$preview = Resolve-PopularPath $(if ($PreviewPath) { $PreviewPath } else { Join-Path $repoRoot 'character-packs\fan-art\popular-characters-preview.png' })
$imagesRoot = Join-Path $repoRoot 'character-packs\fan-art\masters'
$originalImagesRoot = Join-Path $repoRoot 'images'
if (($output + '\').StartsWith($originalImagesRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $preview.StartsWith($originalImagesRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Generated assets cannot overwrite original images.' }
if ([IO.Path]::GetDirectoryName($preview) -ne $output -and $preview.StartsWith($output + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Preview must stay outside individual character folders.' }
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CharacterBuilder.cs')))
# Render fractional foot translations at 4x before downsampling. Direct GDI+
# drawing at 128px can move an asymmetric paw's last two alpha rows by over a
# pixel when its translated destination crosses a device-pixel boundary.
# Shared gait math, contact anchors, source artwork and live rig parts stay intact.
$popularRenderer=@'
public static class PopularFrameRenderer
{
    private static void Foot(Graphics g, Bitmap bitmap, RigFootSample foot, double settle, bool far)
    {
        double lift=foot.Lift*(1-settle);
        double pitch=foot.IsPlanted?0:-Math.Min(12,lift*2.5)*Math.Sin(2*Math.PI*foot.Swing);
        var state=g.Save();
        g.TranslateTransform((float)(64+foot.X),(float)(122-lift));
        g.RotateTransform((float)pitch);
        if(far) {
            using(var attributes=new ImageAttributes()) {
                var matrix=new ColorMatrix();matrix.Matrix00=matrix.Matrix11=matrix.Matrix22=0.91f;
                attributes.SetColorMatrix(matrix);
                g.DrawImage(bitmap,new Rectangle(-bitmap.Width/2,-bitmap.Height,bitmap.Width,bitmap.Height),
                    0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attributes);
            }
        } else g.DrawImage(bitmap,-bitmap.Width/2f,-bitmap.Height,bitmap.Width,bitmap.Height);
        g.Restore(state);
    }
    public static Bitmap Render(Bitmap body,Bitmap nearFoot,Bitmap farFoot,double phase,double distance,double lift,double settle)
    {
        var pose=GroundedWalkBuilder.Sample(phase,distance,lift);
        var result=new Bitmap(128,128,PixelFormat.Format32bppArgb);
        using(var high=new Bitmap(512,512,PixelFormat.Format32bppArgb)) {
            using(var g=Graphics.FromImage(high)) {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.SmoothingMode=SmoothingMode.HighQuality;
                g.ScaleTransform(4,4);
                Foot(g,farFoot,pose.Far,settle,true);
                Foot(g,nearFoot,pose.Near,settle,false);
                g.DrawImage(body,(float)(64-body.Width/2.0),(float)(112-body.Height+pose.BodyBob*(1-settle)),body.Width,body.Height);
            }
            using(var g=Graphics.FromImage(result)) {
                g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                g.DrawImage(high,new Rectangle(0,0,128,128),new Rectangle(0,0,512,512),GraphicsUnit.Pixel);
            }
        }
        return result;
    }
}
'@
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GroundedWalkBuilder.cs'))+$popularRenderer)
$profiles = @(
    @{ Id='Pompompurin'; Name='폼폼푸린'; File='popular-pompompurin-v1.png'; Distance=25.0; Speed=48; EyeLeft=49.0; EyeRight=79.0; EyeY=57.0 },
    @{ Id='Cinnamoroll'; Name='시나모롤'; File='popular-cinnamoroll-v1.png'; Distance=24.0; Speed=50; EyeLeft=47.0; EyeRight=83.0; EyeY=67.0 },
    @{ Id='Kuromi'; Name='쿠로미'; File='popular-kuromi-v1.png'; Distance=23.0; Speed=53; EyeLeft=50.0; EyeRight=78.0; EyeY=74.0 }
) | Where-Object { $Character -eq 'All' -or $_.Id -eq $Character }
function Get-PopularRowEdges($Sheet) {
    # These masters were authored as equal 3x3 cells. Explicit thirds keep small
    # detached feet from becoming separate horizontal bands.
    return @(0,[int][Math]::Floor($Sheet.Height/3.0),[int][Math]::Floor(2*$Sheet.Height/3.0),$Sheet.Height)
}
function Read-PopularCell($Sheet, [int]$Row, [int]$Column, [int]$Height, [bool]$WholeCharacter, [int[]]$RowEdges) {
    $left=[int][Math]::Floor($Sheet.Width*$Column/3.0); $right=[int][Math]::Floor($Sheet.Width*($Column+1)/3.0)
    $top=$RowEdges[$Row]; $bottom=$RowEdges[$Row+1]
    $cell=[CharacterBuilder]::Crop($Sheet,$left,$top,$right-$left,$bottom-$top)
    $trimmed=$null
    try {
        $bounds=[CharacterBuilder]::OpaqueBounds($cell,25)
        if ($bounds.Width -lt 1 -or $bounds.Height -lt 1 -or $bounds.Left -le 0 -or $bounds.Right -ge $cell.Width -or $bounds.Top -le 0 -or $bounds.Bottom -ge $cell.Height) { throw 'Master cells must be complete isolated transparent cutouts.' }
        $trimmed=[CharacterBuilder]::Trim($cell,25)
        if ($WholeCharacter) {
            $anchor=0
            return [CharacterBuilder]::Normalize($trimmed,128,128,100,6,[ref]$anchor)
        }
        return [GroundedWalkBuilder]::ResizePart($trimmed,$Height)
    } finally { if ($trimmed) {$trimmed.Dispose()}; $cell.Dispose() }
}
function Save-PopularFrame([string]$Folder,[string]$Name,$Bitmap) {
    try { [CharacterBuilder]::Save($Bitmap,(Resolve-PopularPath (Join-Path $Folder $Name))) } finally { $Bitmap.Dispose() }
}
foreach ($profile in $profiles) {
    $folder=Resolve-PopularPath (Join-Path $output $profile.Name)
    $marker=Join-Path $folder '.desktop-pet-popular-generated'
    if ([IO.Directory]::Exists($folder) -and -not [IO.File]::Exists($marker) -and @(Get-ChildItem -LiteralPath $folder -Force).Count -gt 0) { throw 'Output character folder must be empty or previously generated by this script.' }
    if ([IO.File]::Exists($marker) -and [IO.File]::ReadAllText($marker) -ne 'DesktopPet.PopularCharacter.v1') { throw 'Unrecognized character output marker.' }
    $sheet=[CharacterBuilder]::LoadArgb((Resolve-PopularPath (Join-Path $imagesRoot $profile.File)))
    $front=@(); $parts=@()
    try {
        if ($sheet.GetPixel(0,0).A -ne 0) { throw 'Fan-art master requires transparent alpha.' }
        $rowEdges=Get-PopularRowEdges $sheet
        foreach ($column in 0..2) { $front+=Read-PopularCell $sheet 0 $column 100 $true $rowEdges }
        foreach ($row in 1..2) {
            $parts+=Read-PopularCell $sheet $row 0 90 $false $rowEdges
            $parts+=Read-PopularCell $sheet $row 1 12 $false $rowEdges
            $parts+=Read-PopularCell $sheet $row 2 11 $false $rowEdges
        }
        [void][IO.Directory]::CreateDirectory($folder)
        [IO.File]::WriteAllText($marker,'DesktopPet.PopularCharacter.v1',(New-Object Text.UTF8Encoding $false))
        Save-PopularFrame $folder 'idle.png' ([CharacterBuilder]::PoseGrounded($front[0],122,0,0))
        Save-PopularFrame $folder 'blink.png' ([CharacterBuilder]::PoseGrounded($front[1],122,0,0))
        Save-PopularFrame $folder 'sit.png' ([CharacterBuilder]::PoseGrounded($front[0],122,0,0))
        Save-PopularFrame $folder 'drag.png' ([CharacterBuilder]::Pose($front[0],122,1,1,-3,0,0))
        Save-PopularFrame $folder 'fall.png' ([CharacterBuilder]::Pose($front[0],122,1,1,4,0,0))
        $right=[PopularFrameRenderer]::Render($parts[3],$parts[4],$parts[5],0,$profile.Distance,4,1)
        Save-PopularFrame $folder 'look_right.png' $right
        $left=[PopularFrameRenderer]::Render($parts[3],$parts[4],$parts[5],0,$profile.Distance,4,1)
        $left.RotateFlip([Drawing.RotateFlipType]::RotateNoneFlipX)
        Save-PopularFrame $folder 'look_left.png' $left
        foreach ($view in 0..1) {
            $prefix=if ($view -eq 0) {'rig_side'} else {'rig_turn'}; $offset=$view*3
            Save-PopularFrame $folder ($prefix+'_body.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset],$true,$false))
            Save-PopularFrame $folder ($prefix+'_near.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset+1],$false,$false))
            Save-PopularFrame $folder ($prefix+'_far.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset+2],$false,$true))
        }
        $walk=@(); $turn=@(); $settleSide=@(); $settleTurn=@(); $audit=@()
        foreach ($i in 0..31) {
            $phase=$i/32.0; $pose=[GroundedWalkBuilder]::Sample($phase,$profile.Distance,4)
            $walkName='walk_{0}.png' -f ($i+1); $turnName='walk_turn_{0}.png' -f ($i+1)
            $walk+=$walkName; $turn+=$turnName
            Save-PopularFrame $folder $walkName ([PopularFrameRenderer]::Render($parts[0],$parts[1],$parts[2],$phase,$profile.Distance,4,0))
            Save-PopularFrame $folder $turnName ([PopularFrameRenderer]::Render($parts[3],$parts[4],$parts[5],$phase,$profile.Distance,4,0))
            $audit+=@{ phase=$phase; near=@{x=$pose.Near.X;lift=$pose.Near.Lift;planted=$pose.Near.IsPlanted}; far=@{x=$pose.Far.X;lift=$pose.Far.Lift;planted=$pose.Far.IsPlanted}; bodyBob=$pose.BodyBob }
        }
        foreach ($level in 1..2) { foreach ($i in 0..31) {
            $sideName='settle_side_{0}_{1}.png' -f $level,($i+1); $turnName='settle_turn_{0}_{1}.png' -f $level,($i+1)
            $settleSide+=$sideName; $settleTurn+=$turnName
            Save-PopularFrame $folder $sideName ([PopularFrameRenderer]::Render($parts[0],$parts[1],$parts[2],$i/32.0,$profile.Distance,4,$level/2.0))
            Save-PopularFrame $folder $turnName ([PopularFrameRenderer]::Render($parts[3],$parts[4],$parts[5],$i/32.0,$profile.Distance,4,$level/2.0))
        }}
        foreach ($i in 0..1) {
            $frame=[CharacterBuilder]::PoseGrounded($front[0],122,0,0)
            [CharacterBuilder]::DrawTearsAt($frame,$profile.EyeLeft,$profile.EyeY,$profile.EyeRight,$profile.EyeY,$(if($i -eq 0){0.15}else{0.85}))
            Save-PopularFrame $folder ('cry_{0}.png' -f ($i+1)) $frame
            $frame=[CharacterBuilder]::PoseGrounded($front[0],122,0,0)
            [CharacterBuilder]::TintRed($frame,0.04+$i*0.01)
            [CharacterBuilder]::DrawAngerMark($frame,122,100,$i*0.3)
            Save-PopularFrame $folder ('angry_{0}.png' -f ($i+1)) $frame
            Save-PopularFrame $folder ('sleep_{0}.png' -f ($i+1)) ([CharacterBuilder]::PoseGrounded($front[2],122,0,0))
        }
        $metadata=[ordered]@{
            name=$profile.Name; scale=1; assetKind='optional-personal-fan-art'; sourceMaster=('masters/'+$profile.File); frontFacingRest=$true
            frameSizePx=@{width=128;height=128};anchor=@{x=64;y=122};walkSpeedPxPerSec=$profile.Speed;frameIntervalMs=100
            frames=[ordered]@{idle=@('idle.png','blink.png','idle.png','blink.png');walk=$walk;walkTurn=$turn;walkFront=@('idle.png')*32;walkSettleSide=$settleSide;walkSettleTurn=$settleTurn;sit=@('sit.png');lookLeft=@('look_left.png');lookRight=@('look_right.png');drag=@('drag.png');fall=@('fall.png');cry=@('cry_1.png','cry_2.png');angry=@('angry_1.png','angry_2.png');sleep=@('sleep_1.png','sleep_2.png')}
            frameDurationsMs=@{idle=@(4200,120,3300,110)}
            eyePositionsPx=@{left=@{x=$profile.EyeLeft;y=$profile.EyeY};right=@{x=$profile.EyeRight;y=$profile.EyeY}}
            walkCycleDistancePx=$profile.Distance
            walkRig=@{version=1;footLiftPx=3;side=@{body='rig_side_body.png';nearFoot='rig_side_near.png';farFoot='rig_side_far.png'};turn=@{body='rig_turn_body.png';nearFoot='rig_turn_near.png';farFoot='rig_turn_far.png'}}
            groundedWalk=@{version=1;frameCount=32;stanceFraction=0.6;footLiftPx=4;settleLevels=2;bodyHeightPx=90;hipY=112;contactY=122;contacts=$audit}
        }
        Set-DesktopPetCharacterBehavior -Definition $metadata -Name $profile.Name
        [IO.File]::WriteAllText((Join-Path $folder 'character.json'),($metadata|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding $false))
        Write-Output "Optional fan-art character built: $($profile.Name)"
    } finally { foreach ($bitmap in $front+$parts) {$bitmap.Dispose()}; $sheet.Dispose() }
}
$names=@('폼폼푸린','시나모롤','쿠로미')|Where-Object{[IO.File]::Exists((Join-Path (Join-Path $output $_) 'character.json'))}
$samples=@('idle.png','walk_9.png','walk_turn_9.png','blink.png','sleep_1.png','cry_1.png')
$labels=@('Idle','Side walk','Quarter','Blink','Sleep','Tears')
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preview))
$sheet=New-Object Drawing.Bitmap (160*$samples.Count),(190*$names.Count)
$graphics=[Drawing.Graphics]::FromImage($sheet);$font=New-Object Drawing.Font 'Malgun Gothic',10;$brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(47,65,58))
try {
    $graphics.Clear([Drawing.Color]::FromArgb(244,247,242))
    for($row=0;$row-lt$names.Count;$row++){for($column=0;$column-lt$samples.Count;$column++){
        $graphics.DrawString(($names[$row]+' / '+$labels[$column]),$font,$brush,[single]($column*160+8),[single]($row*190+10))
        $frame=[CharacterBuilder]::LoadArgb((Join-Path (Join-Path $output $names[$row]) $samples[$column]))
        try{$graphics.DrawImageUnscaled($frame,$column*160+16,$row*190+42)}finally{$frame.Dispose()}
    }}
    [CharacterBuilder]::Save($sheet,$preview)
}finally{$brush.Dispose();$font.Dispose();$graphics.Dispose();$sheet.Dispose()}
Write-Output "Popular fan-art preview: $preview"
