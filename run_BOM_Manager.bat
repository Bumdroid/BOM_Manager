@echo off
chcp 65001 > nul
title SolidWorks 2021 - BOM Manager V0.0

echo ===================================================
echo   SolidWorks 2021 - BOM Manager V0.0 실행 중...
echo ===================================================
echo.

python "%~dp0run.py" %*

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] 프로그램 실행 중 오류가 발생했습니다.
    pause
)
