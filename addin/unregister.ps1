# SolidWorks 2021 BOM Manager Add-in Unregistration Script
$guid = "{B8F89A12-7890-4A56-B123-C9876543210F}"

Write-Host "Unregistering SolidWorks 2021 Add-in..." -ForegroundColor Yellow

Remove-Item -Path "HKCU:\Software\Classes\BOMManagerAddin.SwAddin" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\Classes\CLSID\$guid" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\SolidWorks\Addins\$guid" -Recurse -ErrorAction SilentlyContinue
Remove-Item -Path "HKCU:\Software\SolidWorks\AddInsStartup\$guid" -Recurse -ErrorAction SilentlyContinue

Write-Host "[완료] SolidWorks Add-in 등록이 정상적으로 해제되었습니다." -ForegroundColor Green
