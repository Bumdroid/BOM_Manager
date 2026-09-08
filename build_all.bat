@echo off
setlocal
chcp 65001 > nul
echo ====================================================
echo   Design Automation Portal Standalone Build
echo ====================================================

echo [1/3] Building Visual Studio Solution...
dotnet build BOMManager.sln -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Visual Studio Solution build failed!
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
echo [3/4] Packaging Standalone Distribution (Dist and dist_standalone)...
if not exist "dist_standalone" mkdir "dist_standalone"
if not exist "Dist" mkdir "Dist"

if exist "dist_standalone\*.zip" del /f /q "dist_standalone\*.zip" > nul
if exist "Dist\*.zip" del /f /q "Dist\*.zip" > nul

copy /y "bin\Release\net48\Design_Automation_Portal.exe" "dist_standalone\" > nul
copy /y "bin\Release\net48\Design_Automation_Portal.exe.config" "dist_standalone\" > nul
if exist "lib\*.dll" copy /y "lib\*.dll" "dist_standalone\" > nul

if not exist "dist_standalone\resources" mkdir "dist_standalone\resources"
xcopy /s /y /q "resources\*" "dist_standalone\resources\" > nul
if not exist "dist_standalone\addin" mkdir "dist_standalone\addin"
if exist "addin\*" xcopy /s /y /q "addin\*" "dist_standalone\addin\" > nul
if exist "scratch\gen.exe" (
    "scratch\gen.exe" > nul
) else if exist "scratch\GenerateInstaller.cs" (
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:"scratch\gen.exe" "scratch\GenerateInstaller.cs" > nul
    "scratch\gen.exe" > nul
)
if exist "Install_DT_Design.bat" copy /y "Install_DT_Design.bat" "dist_standalone\" > nul
copy /y "dist_standalone\Design_Automation_Portal.exe" "." > nul

echo.
echo [4/4] Creating Vault Release Package in Dist\ (Zip and Install_DT_Design.bat)...
powershell -NoProfile -Command "Compress-Archive -Path 'dist_standalone\*' -DestinationPath 'Dist\Design_Automation_Portal_Alpha_V0.1.zip' -CompressionLevel Optimal -Force"

if exist "Install_DT_Design.bat" copy /y "Install_DT_Design.bat" "Dist\" > nul

echo.
echo ====================================================
echo   SUCCESS! Design Automation Portal Ready:
echo   - Executable:       Design_Automation_Portal.exe
echo   - Version:          Alpha V0.1
echo   - Dist Folder:      Dist\
echo   - Vault Zip File:   Dist\Design_Automation_Portal_Alpha_V0.1.zip
echo   - One-Key Setup:    Dist\Install_DT_Design.bat
echo ====================================================
echo.
endlocal
