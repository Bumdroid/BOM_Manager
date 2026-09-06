@echo off
chcp 65001 > nul
echo ====================================================
echo   SolidWorks BOM Manager 100%% C# 전체 빌드
echo ====================================================

echo [1/3] BOM Manager WPF 애플리케이션 빌드 중...
dotnet build BOMManager.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [오류] BOMManager.csproj 빌드 실패!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] SolidWorks COM Add-in DLL 빌드 중...
dotnet build addin\BOMManagerAddin.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [오류] BOMManagerAddin.csproj 빌드 실패!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] 단위 테스트 실행 및 검증 중...
dotnet run --project tests\BOMManagerTests.csproj --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [오류] 단위 테스트 실패!
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ====================================================
echo   모든 C# 모듈 빌드 및 검증 완료! (BOM_Manager.exe)
echo ====================================================
echo.
pause
