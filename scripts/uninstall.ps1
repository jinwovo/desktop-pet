<#
.SYNOPSIS
    install.ps1로 설치한 DesktopPet을 제거한다: 실행 중이면 종료하고, 설치 폴더/바로가기/
    자동 실행 등록을 모두 지운다.
#>
$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "DesktopPet"
$shortcutPath = Join-Path ([Environment]::GetFolderPath("Programs")) "DesktopPet.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

Get-Process -Name "DesktopPet.App" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
    Write-Host "설치 폴더 삭제: $installDir"
}

if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
    Write-Host "시작 메뉴 바로가기 삭제: $shortcutPath"
}

if (Get-ItemProperty -Path $runKey -Name "DesktopPet" -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name "DesktopPet"
    Write-Host "자동 실행 등록 해제"
}

Write-Host "제거 완료."
