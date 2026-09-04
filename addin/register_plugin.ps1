# SolidWorksMLAddin Registration Script
param(
    [switch]$Elevated
)

$guid = "{B7A3E2D1-4C5F-6A7B-8C9D-0E1F2A3B4C5D}"
$clsid = "B7A3E2D1-4C5F-6A7B-8C9D-0E1F2A3B4C5D"
$dllPath = "c:\Temp\BOM_Manager\addin\SolidWorksMLAddin.dll"
$regAsm64 = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "  SolidWorks BOM Manager Add-in (net48) Register" -ForegroundColor Cyan
Write-Host "  GUID: $guid" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 1. HKCU COM CLSID 등록 (사용자 레벨 등록 - 관리자 권한 없어도 즉시 유효)
$hkcuClsid = "HKCU:\Software\Classes\CLSID\{$clsid}"
if (-not (Test-Path $hkcuClsid)) { New-Item -Path $hkcuClsid -Force | Out-Null }
Set-ItemProperty -Path $hkcuClsid -Name "(Default)" -Value "SolidWorksMLAddin.SwAddin"

$inprocKey = "$hkcuClsid\InprocServer32"
if (-not (Test-Path $inprocKey)) { New-Item -Path $inprocKey -Force | Out-Null }
Set-ItemProperty -Path $inprocKey -Name "(Default)" -Value "mscoree.dll"
Set-ItemProperty -Path $inprocKey -Name "ThreadingModel" -Value "Both"
Set-ItemProperty -Path $inprocKey -Name "Class" -Value "SolidWorksMLAddin.SwAddin"
Set-ItemProperty -Path $inprocKey -Name "Assembly" -Value "SolidWorksMLAddin, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
Set-ItemProperty -Path $inprocKey -Name "RuntimeVersion" -Value "v4.0.30319"
Set-ItemProperty -Path $inprocKey -Name "CodeBase" -Value "file:///$dllPath"

$progIdKey = "$hkcuClsid\ProgId"
if (-not (Test-Path $progIdKey)) { New-Item -Path $progIdKey -Force | Out-Null }
Set-ItemProperty -Path $progIdKey -Name "(Default)" -Value "SolidWorksMLAddin.SwAddin"

# 2. SolidWorks HKCU Startup 키 등록
$hkcuSw = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
if (-not (Test-Path $hkcuSw)) { New-Item -Path $hkcuSw -Force | Out-Null }
Set-ItemProperty -Path $hkcuSw -Name "(Default)" -Value 1 -Type DWord

# 3. RegAsm 및 HKLM 등록 시도 (관리자 권한 시)
try {
    if (Test-Path $regAsm64) {
        & $regAsm64 /codebase /tlb $dllPath 2>$null
    }
    $hklmSw = "HKLM:\SOFTWARE\SolidWorks\Addins\$guid"
    if (-not (Test-Path $hklmSw)) { New-Item -Path $hklmSw -Force -ErrorAction SilentlyContinue | Out-Null }
    Set-ItemProperty -Path $hklmSw -Name "(Default)" -Value 1 -Type DWord -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $hklmSw -Name "Title" -Value "BOM Manager V0.0" -Type String -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $hklmSw -Name "Description" -Value "SolidWorks 2021 BOM Manager Automation Add-in" -Type String -ErrorAction SilentlyContinue
    Set-ItemProperty -Path $hklmSw -Name "Default" -Value 1 -Type DWord -ErrorAction SilentlyContinue
} catch { }

Write-Host "====================================================" -ForegroundColor Green
Write-Host "  ✅ SolidWorks 애드인 등록 완료!" -ForegroundColor Green
Write-Host "====================================================" -ForegroundColor Green
