@echo off
chcp 65001 > nul
echo ====================================================
echo   SolidWorks BOM Manager Addin 등록
echo ====================================================
powershell -NoProfile -ExecutionPolicy Bypass -File "c:\Temp\BOM_Manager\addin\register_plugin.ps1"
pause
