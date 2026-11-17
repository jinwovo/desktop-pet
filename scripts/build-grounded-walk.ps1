<#
.SYNOPSIS
    Build 32-phase planted-foot cycles and two levels of in-place settling from body/foot rig parts.
#>
param(
    [ValidateSet('All','Companion','Chiikawa','Baikinman','HelloKitty')][string]$Character='All',
    [string]$PreviewPath=''
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Get-DesktopPetCharacterBehavior.ps1')
Add-Type -AssemblyName System.Drawing
$repoRoot=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
function Resolve-RigPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($repoRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Rig generation must stay inside the workspace.' }
    $ancestor=$full
    while ($ancestor -and $ancestor.Length -ge $repoRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Rig paths cannot traverse links.' }
        $ancestor=[IO.Path]::GetDirectoryName($ancestor)
    }
    return $full
}
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'CharacterBuilder.cs')))
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'GroundedWalkBuilder.cs')))
$profiles=@(
    @{Id='Companion';Name='Companion';File='rig-companion-v7.png';Distance=30.0;Interval=100},
    @{Id='Chiikawa';Name='치이카와';File='rig-chiikawa-v7.png';Distance=28.0;Interval=110},
    @{Id='Baikinman';Name='세균맨';File='rig-baikinman-v7.png';Distance=28.0;Interval=110},
    @{Id='HelloKitty';Name='헬로키티';File='rig-hellokitty-v7.png';Distance=28.0;Interval=110}
)|Where-Object { $Character -eq 'All' -or $_.Id -eq $Character }
if ($Character -eq 'All') {
    $profiles=@($profiles|Where-Object {
        $available=[IO.File]::Exists((Join-Path (Join-Path $repoRoot 'images') $_.File)) -and
            [IO.File]::Exists((Join-Path (Join-Path (Join-Path $repoRoot 'Assets\Characters') $_.Name) 'character.json'))
        if (-not $available -and $_.Id -eq 'Companion') { throw 'Build the public Companion base assets before rebuilding grounded walking.' }
        $available
    })
}
function Read-RigPart($Sheet,[int]$Row,[int]$Column,[int]$Height) {
    $left=[int][Math]::Floor($Sheet.Width*$Column/3.0);$right=[int][Math]::Floor($Sheet.Width*($Column+1)/3.0)
    $top=[int][Math]::Floor($Sheet.Height*$Row/2.0);$bottom=[int][Math]::Floor($Sheet.Height*($Row+1)/2.0)
    $cell=[CharacterBuilder]::Crop($Sheet,$left,$top,$right-$left,$bottom-$top)
    $trimmed=$null
    try {
        $bounds=[CharacterBuilder]::OpaqueBounds($cell,25)
        if ($bounds.Width -lt 1 -or $bounds.Height -lt 1 -or $bounds.Left -eq 0 -or $bounds.Right -ge $cell.Width -or $bounds.Top -eq 0 -or $bounds.Bottom -ge $cell.Height) { throw 'Rig parts must be complete isolated cutouts.' }
        $trimmed=[CharacterBuilder]::Trim($cell,25)
        return [GroundedWalkBuilder]::ResizePart($trimmed,$Height)
    } finally { if ($trimmed) {$trimmed.Dispose()};$cell.Dispose() }
}
function Save-RigFrame([string]$Folder,[string]$Name,$Bitmap) {
    try { [CharacterBuilder]::Save($Bitmap,(Resolve-RigPath (Join-Path $Folder $Name))) } finally {$Bitmap.Dispose()}
}
foreach ($profile in $profiles) {
    $folder=Resolve-RigPath (Join-Path (Join-Path $repoRoot 'Assets\Characters') $profile.Name)
    $manifestPath=Resolve-RigPath (Join-Path $folder 'character.json')
    if (-not [IO.File]::Exists($manifestPath)) {
        if ($Character -eq 'All' -and $profile.Id -ne 'Companion') { continue }
        throw "Build the base character first: $($profile.Name)"
    }
    $metadata=[IO.File]::ReadAllText($manifestPath)|ConvertFrom-Json
    $sheet=[CharacterBuilder]::LoadArgb((Resolve-RigPath (Join-Path (Join-Path $repoRoot 'images') $profile.File)))
    $side=@();$turn=@();$settleSide=@();$settleTurn=@();$parts=@();$audit=@()
    try {
        if ($sheet.GetPixel(0,0).A -ne 0) { throw 'Rig masters need genuine transparent alpha.' }
        foreach ($row in 0..1) {
            $parts+=Read-RigPart $sheet $row 0 90
            $parts+=Read-RigPart $sheet $row 1 12
            $parts+=Read-RigPart $sheet $row 2 11
        }
        foreach ($view in 0..1) {
            $prefix=if ($view -eq 0) {'rig_side'}else{'rig_turn'}
            $offset=$view*3
            Save-RigFrame $folder ($prefix+'_body.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset],$true,$false))
            Save-RigFrame $folder ($prefix+'_near.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset+1],$false,$false))
            Save-RigFrame $folder ($prefix+'_far.png') ([GroundedWalkBuilder]::ExportPart($parts[$offset+2],$false,$true))
        }
        for ($i=0;$i -lt 32;$i++) {
            $phase=$i/32.0
            $pose=[GroundedWalkBuilder]::Sample($phase,$profile.Distance,4)
            $audit+=@{phase=$phase;near=@{x=$pose.Near.X;lift=$pose.Near.Lift;planted=$pose.Near.IsPlanted};
                far=@{x=$pose.Far.X;lift=$pose.Far.Lift;planted=$pose.Far.IsPlanted};bodyBob=$pose.BodyBob}
            $sideName='walk_{0}.png' -f ($i+1);$turnName='walk_turn_{0}.png' -f ($i+1)
            $side+=$sideName;$turn+=$turnName
            Save-RigFrame $folder $sideName ([GroundedWalkBuilder]::Render($parts[0],$parts[1],$parts[2],$phase,$profile.Distance,4,0))
            Save-RigFrame $folder $turnName ([GroundedWalkBuilder]::Render($parts[3],$parts[4],$parts[5],$phase,$profile.Distance,4,0))
        }
        foreach ($level in 1..2) {
            for ($i=0;$i -lt 32;$i++) {
                $phase=$i/32.0
                $sideName='settle_side_{0}_{1}.png' -f $level,($i+1);$turnName='settle_turn_{0}_{1}.png' -f $level,($i+1)
                $settleSide+=$sideName;$settleTurn+=$turnName
                Save-RigFrame $folder $sideName ([GroundedWalkBuilder]::Render($parts[0],$parts[1],$parts[2],$phase,$profile.Distance,4,($level/2.0)))
                Save-RigFrame $folder $turnName ([GroundedWalkBuilder]::Render($parts[3],$parts[4],$parts[5],$phase,$profile.Distance,4,($level/2.0)))
            }
        }
        $metadata.frames.walk=$side
        $metadata.frames.walkTurn=$turn
        $metadata.frames.walkFront=@('idle.png')*32
        $metadata.frames|Add-Member -NotePropertyName walkSettleSide -NotePropertyValue $settleSide -Force
        $metadata.frames|Add-Member -NotePropertyName walkSettleTurn -NotePropertyValue $settleTurn -Force
        $metadata|Add-Member -NotePropertyName walkCycleDistancePx -NotePropertyValue $profile.Distance -Force
        $metadata|Add-Member -NotePropertyName walkRig -NotePropertyValue @{
            version=1;footLiftPx=4
            side=@{body='rig_side_body.png';nearFoot='rig_side_near.png';farFoot='rig_side_far.png'}
            turn=@{body='rig_turn_body.png';nearFoot='rig_turn_near.png';farFoot='rig_turn_far.png'}
        } -Force
        $metadata|Add-Member -NotePropertyName groundedWalk -NotePropertyValue @{
            version=1;frameCount=32;stanceFraction=0.6;footLiftPx=4;settleLevels=2;bodyHeightPx=90;hipY=112;contactY=122;contacts=$audit
        } -Force
        # Keep blink/emote/rest timing; rig walking uses distance, not this legacy interval.
        $metadata.frameIntervalMs=$profile.Interval
        Set-DesktopPetCharacterBehavior -Definition $metadata -Name $profile.Name -OnlyIfMissing
        [IO.File]::WriteAllText($manifestPath,($metadata|ConvertTo-Json -Depth 12),(New-Object Text.UTF8Encoding $false))
        Write-Output "Grounded gait built: $($profile.Name), stride $($profile.Distance)px, 32 phases + 2 settling banks"
    } finally { foreach ($part in $parts) {$part.Dispose()};$sheet.Dispose() }
}
$preview=Resolve-RigPath $(if ($PreviewPath) {$PreviewPath}else{Join-Path $repoRoot '.local\grounded-walk-v7.png'})
if ($preview.StartsWith((Join-Path $repoRoot 'images')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Preview cannot overwrite source images.' }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preview))
$previewSheet=New-Object Drawing.Bitmap (8*160),($profiles.Count*190)
$graphics=[Drawing.Graphics]::FromImage($previewSheet)
$font=New-Object Drawing.Font 'Malgun Gothic',10
$brush=New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(53,65,70))
$line=New-Object Drawing.Pen ([Drawing.Color]::FromArgb(180,198,176)),1
try {
    $graphics.Clear([Drawing.Color]::FromArgb(244,247,241))
    for($row=0;$row -lt $profiles.Count;$row++) {
        for($column=0;$column -lt 8;$column++) {
            $file='walk_{0}.png' -f ($column*4+1)
            $graphics.DrawString(($profiles[$row].Name+' '+($column*4+1)),$font,$brush,[single]($column*160+8),[single]($row*190+10))
            $frame=[CharacterBuilder]::LoadArgb((Join-Path (Join-Path (Join-Path $repoRoot 'Assets\Characters') $profiles[$row].Name) $file))
            try {$graphics.DrawImageUnscaled($frame,$column*160+16,$row*190+42)}finally{$frame.Dispose()}
            $graphics.DrawLine($line,$column*160+12,$row*190+164,$column*160+148,$row*190+164)
        }
    }
    [CharacterBuilder]::Save($previewSheet,$preview)
}finally{$line.Dispose();$brush.Dispose();$font.Dispose();$graphics.Dispose();$previewSheet.Dispose()}
Write-Output "Grounded preview: $preview"
