<#
.SYNOPSIS
    DesktopPet를 퍼블리시하고 %LocalAppData%\DesktopPet 에 설치한다(관리자 권한 불필요).

.PARAMETER AddToStartup
    지정하면 HKCU Run 키에 등록해서 로그인할 때 자동 실행되도록 한다. 기본값은 꺼짐.

.EXAMPLE
    .\scripts\install.ps1
    .\scripts\install.ps1 -AddToStartup
#>
param(
    [switch]$AddToStartup
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\DesktopPet.App\DesktopPet.App.csproj"
$installDir = Join-Path $env:LOCALAPPDATA "DesktopPet"
$publishDir = Join-Path $env:TEMP "DesktopPet.publish"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

Write-Host "퍼블리시 중... ($appProject)"
dotnet publish $appProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 실패 (exit $LASTEXITCODE)"
}

if (Test-Path $installDir) {
    Write-Host "기존 설치 제거 중... ($installDir)"
    Remove-Item $installDir -Recurse -Force
}

Write-Host "설치 중... ($installDir)"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item "$publishDir\*" $installDir -Recurse -Force

$exePath = Join-Path $installDir "DesktopPet.App.exe"

# 시작 메뉴 바로가기
$startMenuDir = Join-Path ([Environment]::GetFolderPath("Programs")) ""
$shortcutPath = Join-Path $startMenuDir "DesktopPet.lnk"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = (Join-Path $installDir "Assets\AppIcon.ico")
$shortcut.Save()
Write-Host "시작 메뉴 바로가기 생성: $shortcutPath"

if ($AddToStartup) {
    $runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
    Set-ItemProperty -Path $runKey -Name "DesktopPet" -Value "`"$exePath`""
    Write-Host "로그인 시 자동 실행으로 등록했습니다. (해제하려면 uninstall.ps1 실행)"
}

Write-Host ""
Write-Host "설치 완료: $exePath"
Write-Host "캐릭터 이미지 폴더: $installDir\Assets\Characters\<캐릭터이름>\"
