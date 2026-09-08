@echo off
setlocal
chcp 949 > nul

echo ================================================================
echo   [DT 운영관리팀] Design Automation Portal 원키(One-Key) 설치
echo ================================================================
echo.

set "SOURCE_DIR=%~dp0"
set "TARGET_DIR=C:\ISC_DT_Automation"

powershell -NoProfile -ExecutionPolicy Bypass -Command "& { $src = $env:SOURCE_DIR; $target = $env:TARGET_DIR; Write-Host '[1/3] 설치 대상 폴더 준비 중...'; if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null; Write-Host ('  - 대상 폴더 생성: ' + $target); } else { Write-Host ('  - 기존 대상 폴더 확인: ' + $target); } Write-Host ''; Write-Host '[2/3] 프로그램 압축 해제 및 설치 파일 복사 중...'; $zip = Get-ChildItem -Path $src -Filter 'Design_Automation_Portal_*.zip' -File -ErrorAction SilentlyContinue | Select-Object -First 1; if (-not $zip) { $zip = Get-ChildItem -Path (Join-Path $src 'Dist') -Filter 'Design_Automation_Portal_*.zip' -File -ErrorAction SilentlyContinue | Select-Object -First 1; } if (-not $zip) { $zip = Get-ChildItem -Path $src -Filter 'BOM_Manager_*.zip' -File -ErrorAction SilentlyContinue | Select-Object -First 1; } if (-not $zip) { $zip = Get-ChildItem -Path (Join-Path $src 'Dist') -Filter 'BOM_Manager_*.zip' -File -ErrorAction SilentlyContinue | Select-Object -First 1; } if ($zip) { Write-Host ('  - 배포 압축 파일 발견: ' + $zip.FullName); Write-Host ('  - ' + $target + ' 으로 자동 압축 해제 중...'); Expand-Archive -LiteralPath $zip.FullName -DestinationPath $target -Force; Write-Host '  - 압축 해제 완료!'; } else { Write-Host '  - 배포 폴더의 파일을 직접 복사합니다...'; Copy-Item -Path (Join-Path $src '*') -Destination $target -Recurse -Force; Write-Host '  - 파일 복사 완료!'; } $subStandalone = Join-Path $target 'dist_standalone'; if (Test-Path $subStandalone) { Copy-Item -Path (Join-Path $subStandalone '*') -Destination $target -Recurse -Force; Remove-Item -Path $subStandalone -Recurse -Force -ErrorAction SilentlyContinue; } $portalExe = Join-Path $target 'Design_Automation_Portal.exe'; $oldExe = Join-Path $target 'BOM_Manager.exe'; $oldCfg = Join-Path $target 'BOM_Manager.exe.config'; if (Test-Path $portalExe) { if (Test-Path $oldExe) { Remove-Item $oldExe -Force -ErrorAction SilentlyContinue; } if (Test-Path $oldCfg) { Remove-Item $oldCfg -Force -ErrorAction SilentlyContinue; } } $targetRes = Join-Path $target 'resources'; $srcRes = Join-Path $src 'resources'; if (-not (Test-Path $targetRes) -and (Test-Path $srcRes)) { Copy-Item -Path $srcRes -Destination $target -Recurse -Force; } Write-Host ''; Write-Host '[3/3] 윈도우 바탕화면에 바로가기 아이콘 생성 중...'; $exePath = if (Test-Path $portalExe) { $portalExe } else { $oldExe }; $icoPath = Join-Path $target 'resources\app.ico'; $WshShell = New-Object -ComObject WScript.Shell; $deskFolders = @([System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::Desktop), (Join-Path $env:USERPROFILE 'Desktop'), (Join-Path $env:USERPROFILE 'OneDrive - ISC\바탕 화면'), (Join-Path $env:USERPROFILE 'OneDrive\바탕 화면')) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique; foreach ($desk in $deskFolders) { $lnkPath = Join-Path $desk 'Design Automation Portal.lnk'; if (Test-Path $lnkPath) { Remove-Item $lnkPath -Force -ErrorAction SilentlyContinue; } $sc = $WshShell.CreateShortcut($lnkPath); $sc.TargetPath = $exePath; $sc.WorkingDirectory = $target; if (Test-Path $icoPath) { $sc.IconLocation = ($icoPath + ',0'); } else { $sc.IconLocation = ($exePath + ',0'); } $sc.Description = 'Design Automation Portal (Alpha V0.0) - SolidWorks & Automation Suite'; $sc.Save(); Write-Host ('  - 바탕화면 바로가기 등록 완료: ' + $lnkPath); } }"

echo.
echo ================================================================
echo   [완료] 바탕화면 바로가기 및 설치가 성공적으로 완료되었습니다!
echo   - 설치 경로: %TARGET_DIR%
echo   - 실행 파일: %TARGET_DIR%\Design_Automation_Portal.exe
echo   - 바탕화면 [Design Automation Portal] 아이콘으로 실행하세요.
echo ================================================================
echo.

set /p RUN_NOW="지금 바로 프로그램을 실행하시겠습니까? (Y/N, 기본값: Y): "
if /i "%RUN_NOW%"=="N" goto finish

start "" "%TARGET_DIR%\Design_Automation_Portal.exe"

:finish
echo.
echo 창을 닫으려면 아무 키나 누르세요...
pause > nul
endlocal
