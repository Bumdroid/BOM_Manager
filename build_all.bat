@echo off
setlocal
chcp 65001 > nul
echo ====================================================
echo   SolidWorks BOM Manager C# Standalone Build
echo ====================================================

echo [1/3] Building Visual Studio Solution...
dotnet build BOMManager.sln -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] BOMManager.sln build failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [2/3] Running Unit Tests...
dotnet run --project tests\BOMManagerTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Unit Tests failed!
    exit /b %ERRORLEVEL%
)

echo.
echo [3/3] Packaging Standalone Distribution (dist_standalone)...
if not exist "dist_standalone" mkdir "dist_standalone"
copy /y "bin\Release\net48\BOM_Manager.exe" "dist_standalone\" > nul
copy /y "bin\Release\net48\BOM_Manager.exe.config" "dist_standalone\" > nul
if exist "lib\SolidWorks.Interop.sldworks.dll" copy /y "lib\SolidWorks.Interop.sldworks.dll" "dist_standalone\" > nul
if exist "lib\SolidWorks.Interop.swconst.dll" copy /y "lib\SolidWorks.Interop.swconst.dll" "dist_standalone\" > nul
if exist "lib\SolidWorks.Interop.swpublished.dll" copy /y "lib\SolidWorks.Interop.swpublished.dll" "dist_standalone\" > nul
if exist "lib\Autodesk*.dll" copy /y "lib\Autodesk*.dll" "dist_standalone\" > nul

if not exist "dist_standalone\resources" mkdir "dist_standalone\resources"
xcopy /s /y /q "resources\*" "dist_standalone\resources\" > nul
copy /y "dist_standalone\BOM_Manager.exe" "." > nul

echo.
echo ====================================================
echo   SUCCESS! Standalone C# .NET BOM Manager Ready:
echo   - Main Executable: BOM_Manager.exe
echo   - Dist Folder:     dist_standalone\
echo ====================================================
echo.
endlocal
