@echo off
setlocal enabledelayedexpansion
chcp 65001 > nul
cls

set "TARGET_DIR=C:\ISC_DT_Automation"
set "APP_NAME=Design Automation Portal"

echo.
echo ================================================================
echo    %APP_NAME% (설계/DT 자동화 포털) 삭제
echo ================================================================
echo.
echo   - 대상 프로그램 : %APP_NAME%
echo   - 설치 경로     : %TARGET_DIR%
echo.

set /p CONFIRM=" 정말 프로그램을 삭제(언인스톨)하시겠습니까? (Y/N, 기본값: Y): "
if /i "%CONFIRM%"=="N" (
    echo.
    echo  [취소] 언인스톨 작업이 취소되었습니다.
    echo  창을 닫으려면 아무 키나 누르세요...
    pause > nul
    goto :eof
)

set "CLEAN_APPDATA=N"
if exist "%APPDATA%\BOMManager" (
    echo.
    set /p CLEAN_APPDATA=" 사용자 설정 및 캐시(Vault 계정/스프링 설정 등)도 삭제하시겠습니까? (Y/N, 기본값: N): "
) else if exist "%APPDATA%\Common_Draw" (
    echo.
    set /p CLEAN_APPDATA=" 사용자 설정 및 캐시(Vault 계정/스프링 설정 등)도 삭제하시겠습니까? (Y/N, 기본값: N): "
)

echo.
echo  [1/4] 실행 중인 프로그램 프로세스 안전 종료 중...
taskkill /f /im Design_Automation_Portal.exe > nul 2>&1
taskkill /f /im BOM_Manager.exe > nul 2>&1
echo        - 프로세스 종료 완료

echo.
echo  [2/4] 바탕화면 및 시작 메뉴 바로가기 아이콘 정리 중...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $targets = @('Design Automation Portal.lnk', 'BOM_Manager.lnk'); $folders = @([System.Environment]::GetFolderPath('Desktop'), [System.Environment]::GetFolderPath('CommonDesktop'), (Join-Path $env:USERPROFILE 'Desktop'), (Join-Path $env:USERPROFILE 'OneDrive - ISC\바탕 화면'), (Join-Path $env:USERPROFILE 'OneDrive\바탕 화면'), (Join-Path $env:USERPROFILE 'OneDrive - ISC\Desktop'), (Join-Path $env:USERPROFILE 'OneDrive\Desktop'), [System.Environment]::GetFolderPath('Programs'), [System.Environment]::GetFolderPath('CommonPrograms')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique; foreach ($f in $folders) { foreach ($name in $targets) { $lnk = Join-Path $f $name; if (Test-Path -LiteralPath $lnk) { Remove-Item -LiteralPath $lnk -Force } } }" > nul 2>&1
echo        - 바로가기 삭제 완료

echo.
echo  [3/4] 프로그램 설치 폴더 삭제 중...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; $target = '%TARGET_DIR%'; $currentScript = '%~f0'; if (Test-Path -LiteralPath $target) { $items = Get-ChildItem -LiteralPath $target -Force | Where-Object { $_.FullName -ne $currentScript }; foreach ($item in $items) { Remove-Item -LiteralPath $item.FullName -Recurse -Force }; if ($currentScript.StartsWith($target, [System.StringComparison]::OrdinalIgnoreCase)) { $cmd = 'Start-Sleep -Seconds 1; for ($i=0; $i -lt 10; $i++) { if (-not (Test-Path ''C:\ISC_DT_Automation'')) { break }; try { Remove-Item ''C:\ISC_DT_Automation'' -Recurse -Force; break } catch { Start-Sleep -Milliseconds 500 } }'; Start-Process powershell.exe -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -Command ' + [char]34 + $cmd + [char]34) -WindowStyle Hidden } else { Remove-Item -LiteralPath $target -Recurse -Force } }" > nul 2>&1
echo        - 설치 폴더 삭제 완료

echo.
echo  [4/4] 사용자 설정 및 캐시 데이터 정리 중...
if /i "%CLEAN_APPDATA%"=="Y" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; if (Test-Path (Join-Path $env:APPDATA 'BOMManager')) { Remove-Item (Join-Path $env:APPDATA 'BOMManager') -Recurse -Force }; if (Test-Path (Join-Path $env:APPDATA 'Common_Draw')) { Remove-Item (Join-Path $env:APPDATA 'Common_Draw') -Recurse -Force }" > nul 2>&1
    echo        - 사용자 캐시 데이터 삭제 완료
) else (
    echo        - 사용자 설정 데이터 보존
)

echo.
echo ================================================================
echo   ✔ %APP_NAME% 삭제(언인스톨)가 완료되었습니다!
echo   --------------------------------------------------------------
echo   - 대상 폴더    : %TARGET_DIR% (삭제 완료)
echo   - 바로가기     : 바탕화면 및 시작 메뉴 바로가기 제거 완료
echo ================================================================
echo.
echo  창을 닫으려면 아무 키나 누르세요...
pause > nul
endlocal
