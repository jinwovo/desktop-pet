$ErrorActionPreference='Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\publish-existing-1.0.0.ps1')
function Assert-Publication([bool]$Condition,[string]$Message) { if (-not $Condition) { throw $Message } }
function Reject-Publication([scriptblock]$Action) { $failed=$false; try { & $Action } catch { $failed=$true }; Assert-Publication $failed 'Unsafe publication input was accepted.' }
$names=@('DesktopPet-1.0.0-win-x64.zip','DesktopPet-1.0.0-win-x64.zip.sha256','DesktopPet-1.0.0-fan-art-packs.zip','DesktopPet-1.0.0-fan-art-packs.zip.sha256')
$assets=@($names | ForEach-Object {[pscustomobject]@{name=$_;size=123;state='uploaded'}})
$release=[pscustomobject]@{tag_name='v1.0.0';draft=$true;prerelease=$false}
Assert-DesktopPetStableAssets $release $assets
Reject-Publication { Assert-DesktopPetStableAssets $release @($assets | Select-Object -First 3) }
$duplicate=@($assets[0],$assets[1],$assets[2],$assets[2])
Reject-Publication { Assert-DesktopPetStableAssets $release $duplicate }
$assets[0].state='open'; Reject-Publication { Assert-DesktopPetStableAssets $release $assets }; $assets[0].state='uploaded'
$assets[0].size=0; Reject-Publication { Assert-DesktopPetStableAssets $release $assets }; $assets[0].size=123
$release.tag_name='v1.0.1'; Reject-Publication { Assert-DesktopPetStableAssets $release $assets }; $release.tag_name='v1.0.0'
$release.prerelease=$true; Reject-Publication { Assert-DesktopPetStableAssets $release $assets }
Write-Output 'PASS Stable publication rejects missing/duplicate/incomplete assets and unauthorized release identity (offline; no network or token)'
