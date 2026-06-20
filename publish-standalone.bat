@echo off
setlocal EnableDelayedExpansion
title PDFMaster - Publish Standalone
cd /d "%~dp0"

set "ROOT=%~dp0"
set "PROJECT=%ROOT%PDFMaster"
set "OUTPUT=%ROOT%publish"

echo.
echo  ============================================
echo   PDFMaster - Publish Standalone (x64)
echo  ============================================
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 8 SDK.
    pause
    exit /b 1
)

echo [1/3] Cleaning Debug output...
cd /d "%PROJECT%"
dotnet clean PDFMaster.csproj -c Debug >nul 2>&1
if exist "bin\Debug" rmdir /s /q "bin\Debug"
if exist "obj\Debug" rmdir /s /q "obj\Debug"

echo [2/3] Cleaning previous publish folder...
cd /d "%ROOT%"
if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

dotnet clean PDFMaster.csproj -c Release >nul 2>&1

echo [3/3] Publishing Release x64 single-file exe...
cd /d "%PROJECT%"
dotnet publish PDFMaster.csproj -c Release ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o "%OUTPUT%"

if errorlevel 1 (
    echo.
    echo ERROR: Publish failed.
    pause
    exit /b 1
)

echo.
echo  ============================================
echo   Publish complete!
echo  ============================================
echo.
echo   Folder:  %OUTPUT%
echo   Run:     %OUTPUT%\PDFMaster.exe
echo.

if /I "%~1"=="--no-pause" exit /b 0
pause
