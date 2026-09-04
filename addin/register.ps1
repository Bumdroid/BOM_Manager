# SolidWorks 2021 BOM Manager Add-in Full System Registration Script
$guid = "{B8F89A12-7890-4A56-B123-C9876543210F}"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$dllPath = Join-Path $scriptDir "BOMManagerAddin.dll"
$codebase = "file:///" + ($dllPath -replace "\\", "/")

Write-Host "=== Registering SolidWorks 2021 BOM Manager Add-in ===" -ForegroundColor Cyan
Write-Host "DLL: $dllPath" -ForegroundColor Gray
Write-Host "GUID: $guid" -ForegroundColor Gray

# 1. HKLM Classes CLSID
try {
    $clsidPath = "HKLM:\SOFTWARE\Classes\CLSID\$guid"
    New-Item -Path "$clsidPath\InprocServer32\0.0.0.0" -Force | Out-Null
    New-Item -Path "$clsidPath\ProgId" -Force | Out-Null
    New-Item -Path "$clsidPath\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}" -Force | Out-Null

    Set-ItemProperty -Path $clsidPath -Name "(Default)" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path "$clsidPath\ProgId" -Name "(Default)" -Value "BOMManagerAddin.SwAddin"

    $inproc = "$clsidPath\InprocServer32"
    Set-ItemProperty -Path $inproc -Name "(Default)" -Value "mscoree.dll"
    Set-ItemProperty -Path $inproc -Name "ThreadingModel" -Value "Both"
    Set-ItemProperty -Path $inproc -Name "Class" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path $inproc -Name "Assembly" -Value "BOMManagerAddin, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
    Set-ItemProperty -Path $inproc -Name "RuntimeVersion" -Value "v4.0.30319"
    Set-ItemProperty -Path $inproc -Name "CodeBase" -Value $codebase

    $inprocVer = "$clsidPath\InprocServer32\0.0.0.0"
    Set-ItemProperty -Path $inprocVer -Name "Class" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path $inprocVer -Name "Assembly" -Value "BOMManagerAddin, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
    Set-ItemProperty -Path $inprocVer -Name "RuntimeVersion" -Value "v4.0.30319"
    Set-ItemProperty -Path $inprocVer -Name "CodeBase" -Value $codebase

    # HKLM SolidWorks Addins
    $swAddinPath = "HKLM:\SOFTWARE\SolidWorks\Addins\$guid"
    New-Item -Path $swAddinPath -Force | Out-Null
    Set-ItemProperty -Path $swAddinPath -Name "(Default)" -Value 1 -Type DWord
    Set-ItemProperty -Path $swAddinPath -Name "Title" -Value "BOM Manager V0.0"
    Set-ItemProperty -Path $swAddinPath -Name "Description" -Value "SolidWorks 2021 BOM Manager Automation Add-in"

    # HKLM SolidWorks AddInsStartup
    $swStartupPath = "HKLM:\SOFTWARE\SolidWorks\AddInsStartup\$guid"
    New-Item -Path $swStartupPath -Force | Out-Null
    Set-ItemProperty -Path $swStartupPath -Name "(Default)" -Value 1 -Type DWord

    # Remove old 4F52B4D2 keys
    Remove-Item -Path "HKLM:\SOFTWARE\SolidWorks\Addins\{4F52B4D2-861C-4B9A-99F7-70B0B79E1A34}" -Recurse -ErrorAction SilentlyContinue
    Remove-Item -Path "HKLM:\SOFTWARE\SolidWorks\AddInsStartup\{4F52B4D2-861C-4B9A-99F7-70B0B79E1A34}" -Recurse -ErrorAction SilentlyContinue

    Write-Host "[성공] HKLM 64비트 시스템 레지스트리에 등록 완료!" -ForegroundColor Green
} catch {
    Write-Warning "HKLM 등록 실패 (관리자 권한 필요): $_"
}

# 2. HKCU Classes & SolidWorks Addins (Per-User Fallback)
try {
    $clsidPathCu = "HKCU:\Software\Classes\CLSID\$guid"
    New-Item -Path "$clsidPathCu\InprocServer32\0.0.0.0" -Force | Out-Null
    New-Item -Path "$clsidPathCu\ProgId" -Force | Out-Null
    New-Item -Path "$clsidPathCu\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}" -Force | Out-Null

    Set-ItemProperty -Path $clsidPathCu -Name "(Default)" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path "$clsidPathCu\ProgId" -Name "(Default)" -Value "BOMManagerAddin.SwAddin"

    $inprocCu = "$clsidPathCu\InprocServer32"
    Set-ItemProperty -Path $inprocCu -Name "(Default)" -Value "mscoree.dll"
    Set-ItemProperty -Path $inprocCu -Name "ThreadingModel" -Value "Both"
    Set-ItemProperty -Path $inprocCu -Name "Class" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path $inprocCu -Name "Assembly" -Value "BOMManagerAddin, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
    Set-ItemProperty -Path $inprocCu -Name "RuntimeVersion" -Value "v4.0.30319"
    Set-ItemProperty -Path $inprocCu -Name "CodeBase" -Value $codebase

    $inprocVerCu = "$clsidPathCu\InprocServer32\0.0.0.0"
    Set-ItemProperty -Path $inprocVerCu -Name "Class" -Value "BOMManagerAddin.SwAddin"
    Set-ItemProperty -Path $inprocVerCu -Name "Assembly" -Value "BOMManagerAddin, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
    Set-ItemProperty -Path $inprocVerCu -Name "RuntimeVersion" -Value "v4.0.30319"
    Set-ItemProperty -Path $inprocVerCu -Name "CodeBase" -Value $codebase

    # HKCU SolidWorks Addins
    $swAddinPathCu = "HKCU:\Software\SolidWorks\Addins\$guid"
    New-Item -Path $swAddinPathCu -Force | Out-Null
    Set-ItemProperty -Path $swAddinPathCu -Name "(Default)" -Value 1 -Type DWord
    Set-ItemProperty -Path $swAddinPathCu -Name "Title" -Value "BOM Manager V0.0"
    Set-ItemProperty -Path $swAddinPathCu -Name "Description" -Value "SolidWorks 2021 BOM Manager Automation Add-in"

    # HKCU SolidWorks AddInsStartup
    $swStartupPathCu = "HKCU:\Software\SolidWorks\AddInsStartup\$guid"
    New-Item -Path $swStartupPathCu -Force | Out-Null
    Set-ItemProperty -Path $swStartupPathCu -Name "(Default)" -Value 1 -Type DWord

    Remove-Item -Path "HKCU:\Software\SolidWorks\Addins\{4F52B4D2-861C-4B9A-99F7-70B0B79E1A34}" -Recurse -ErrorAction SilentlyContinue

    Write-Host "[성공] HKCU 사용자 레지스트리에 등록 완료!" -ForegroundColor Green
} catch {
    Write-Warning "HKCU 등록 실패: $_"
}

Write-Host "`n등록이 성공적으로 완료되었습니다!" -ForegroundColor Green
