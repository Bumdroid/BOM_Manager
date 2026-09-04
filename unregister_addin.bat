@echo off
chcp 65001 > nul
title SolidWorks 2021 - BOM Manager Add-in 등록 해제

echo ===================================================
echo   SolidWorks 2021 상단 탭 메뉴 애드인 등록 해제
echo ===================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0addin\unregister.ps1"

echo.
pause
