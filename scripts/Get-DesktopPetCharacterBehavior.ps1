# Shared authored presets used only when rebuilding bundled/optional sample metadata.
function Set-DesktopPetCharacterBehavior {
    param([Parameter(Mandatory=$true)]$Definition,[Parameter(Mandatory=$true)][string]$Name,[switch]$OnlyIfMissing)
    if ($OnlyIfMissing) {
        if ($Definition -is [Collections.IDictionary] -and $Definition.Contains('behavior')) { return }
        if ($Definition -isnot [Collections.IDictionary] -and $null -ne $Definition.PSObject.Properties['behavior']) { return }
    }
    $path=Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets\CharacterBehaviors.json'
    $presets=[IO.File]::ReadAllText($path) | ConvertFrom-Json
    $entry=$presets.PSObject.Properties[$Name]
    if ($null -eq $entry) { return }
    if ($Definition -is [Collections.IDictionary]) { $Definition['behavior']=$entry.Value }
    else { $Definition | Add-Member -NotePropertyName behavior -NotePropertyValue $entry.Value -Force }
}
