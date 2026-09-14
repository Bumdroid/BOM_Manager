@echo off
setlocal enabledelayedexpansion
chcp 65001 > nul
cls

echo.
echo ================================================================
echo    Design Automation Portal (설계/DT 자동화 포털) 원키 설치
echo ================================================================
echo.

set "SOURCE_DIR=%~dp0"
set "TARGET_DIR=C:\ISC_DT_Automation"

echo  [1/3] 설치 디렉터리 준비 중...
if not exist "%TARGET_DIR%" (
    mkdir "%TARGET_DIR%" > nul 2>&1
    echo        - 신규 폴더 생성: %TARGET_DIR%
) else (
    echo        - 기존 폴더 확인: %TARGET_DIR%
)

echo.
echo  [2/3] 프로그램 및 최신 모듈 파일 설치 중...

:: 1. ZIP 압축 해제 (배포 패키지가 있는 경우)
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $src = '%SOURCE_DIR%'; $target = '%TARGET_DIR%'; $zip = Get-ChildItem -LiteralPath $src -Filter 'Design_Automation_Portal_*.zip' -File | Select-Object -First 1; if (-not $zip) { $dist = Join-Path $src 'Dist'; if (Test-Path $dist) { $zip = Get-ChildItem -LiteralPath $dist -Filter 'Design_Automation_Portal_*.zip' -File | Select-Object -First 1 } }; if ($zip) { Expand-Archive -LiteralPath $zip.FullName -DestinationPath $target -Force } else { Copy-Item (Join-Path $src '*') $target -Recurse -Force }" > nul 2>&1

:: 2. 하위 dist_standalone 평탄화
if exist "%TARGET_DIR%\dist_standalone" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; Copy-Item 'C:\ISC_DT_Automation\dist_standalone\*' 'C:\ISC_DT_Automation' -Recurse -Force; Remove-Item 'C:\ISC_DT_Automation\dist_standalone' -Recurse -Force" > nul 2>&1
)

:: 3. 레거시 BOM_Manager 파일 정리
if exist "%TARGET_DIR%\Design_Automation_Portal.exe" (
    if exist "%TARGET_DIR%\BOM_Manager.exe" del /f /q "%TARGET_DIR%\BOM_Manager.exe" > nul 2>&1
    if exist "%TARGET_DIR%\BOM_Manager.exe.config" del /f /q "%TARGET_DIR%\BOM_Manager.exe.config" > nul 2>&1
)

:: 4. 언인스톨러 복사
if exist "%SOURCE_DIR%Uninstall_DT_Design.bat" (
    copy /y "%SOURCE_DIR%Uninstall_DT_Design.bat" "%TARGET_DIR%\Uninstall_DT_Design.bat" > nul 2>&1
) else if exist "%SOURCE_DIR%Dist\Uninstall_DT_Design.bat" (
    copy /y "%SOURCE_DIR%Dist\Uninstall_DT_Design.bat" "%TARGET_DIR%\Uninstall_DT_Design.bat" > nul 2>&1
)

echo        - 파일 배치 완료

echo.
echo  [3/3] 바탕화면 바로가기 등록 중...

powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $target = '%TARGET_DIR%'; $exe = Join-Path $target 'Design_Automation_Portal.exe'; $ico = Join-Path $target 'resources\app.ico'; $Wsh = New-Object -ComObject WScript.Shell; $desks = @([System.Environment]::GetFolderPath('Desktop'), (Join-Path $env:USERPROFILE 'Desktop'), (Join-Path $env:USERPROFILE 'OneDrive - ISC\바탕 화면'), (Join-Path $env:USERPROFILE 'OneDrive\바탕 화면'), (Join-Path $env:USERPROFILE 'OneDrive - ISC\Desktop'), (Join-Path $env:USERPROFILE 'OneDrive\Desktop')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique; foreach ($d in $desks) { $lnk = Join-Path $d 'Design Automation Portal.lnk'; if (Test-Path -LiteralPath $lnk) { Remove-Item -LiteralPath $lnk -Force }; $sc = $Wsh.CreateShortcut($lnk); $sc.TargetPath = $exe; $sc.WorkingDirectory = $target; if (Test-Path -LiteralPath $ico) { $sc.IconLocation = $ico + ',0' }; $sc.Description = 'Design Automation Portal - SolidWorks & Automation Suite'; $sc.Save() }" > nul 2>&1

echo        - 바탕화면 바로가기 생성 완료

echo.
echo ================================================================
echo   ✔ 설치가 성공적으로 완료되었습니다!
echo   --------------------------------------------------------------
echo   - 설치 경로    : %TARGET_DIR%
echo   - 실행 파일    : %TARGET_DIR%\Design_Automation_Portal.exe
echo   - 언인스톨러   : %TARGET_DIR%\Uninstall_DT_Design.bat
echo ================================================================
echo.

set /p RUN_NOW=" 지금 바로 프로그램을 실행하시겠습니까? (Y/N, 기본값: Y): "
if /i "%RUN_NOW%"=="N" goto finish

start "" "%TARGET_DIR%\Design_Automation_Portal.exe"

:finish
echo.
echo  창을 닫으려면 아무 키나 누르세요...
pause > nul
endlocal
