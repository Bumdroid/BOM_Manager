@echo off
chcp 65001 > nul
echo ====================================================
echo   SolidWorksMLAddin (.NET 4.8) 빌드 및 COM 등록
echo ====================================================

set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set SW_DIR=C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS

echo [1/3] SolidWorksMLAddin.dll 컴파일 중...
%CSC% /target:library /out:"c:\Temp\BOM_Manager\addin\SolidWorksMLAddin.dll" /link:"%SW_DIR%\SolidWorks.Interop.sldworks.dll" /link:"%SW_DIR%\SolidWorks.Interop.swconst.dll" /link:"%SW_DIR%\SolidWorks.Interop.swpublished.dll" "c:\Temp\BOM_Manager\addin\SwAddin.cs"

if %ERRORLEVEL% NEQ 0 (
    echo [오류] 컴파일 실패! SolidWorks가 실행 중이면 먼저 종료해 주세요.
    pause
    exit /b %ERRORLEVEL%
)

echo [2/3] RegAsm.exe /codebase /tlb 등록 중...
powershell -NoProfile -ExecutionPolicy Bypass -File "c:\Temp\BOM_Manager\addin\register_plugin.ps1"

echo [3/3] 완료!
pause
