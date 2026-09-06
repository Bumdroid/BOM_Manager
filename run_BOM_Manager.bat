@echo off
chcp 65001 > nul
title SolidWorks 2021 - BOM Manager V0.0

echo ===================================================
echo   SolidWorks 2021 - BOM Manager V0.0 실행 중...
echo ===================================================
echo.

if exist "%~dp0BOM_Manager.exe" (
    "%~dp0BOM_Manager.exe" %*
) else (
    dotnet run --project "%~dp0BOMManager.csproj" -- %*
)

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] 프로그램 실행 중 오류가 발생했습니다.
    pause
)
