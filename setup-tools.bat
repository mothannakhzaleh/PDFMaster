@echo off
setlocal EnableDelayedExpansion
title PDFMaster - Setup Tools
cd /d "%~dp0"

set "ROOT=%~dp0"
set "TOOLS=%ROOT%PDFMaster\tools"
set "GS_ROOT=%TOOLS%\gs"
set "GS_EXE="

echo.
echo  ============================================
echo   PDFMaster - Setup bundled tools
echo  ============================================
echo   Copyright (c) 2026 mothannakh
echo.

if not exist "%TOOLS%" mkdir "%TOOLS%"

REM --- Already ready? ---
call :FindGsExe
if defined GS_EXE (
    echo [OK] Ghostscript found:
    echo      !GS_EXE!
    goto :show_tools
)

echo Ghostscript is not set up yet.
echo.

REM --- 1) Installer in tools\ or repo root (e.g. gs10071w64.exe) ---
set "INSTALLER="
for %%F in ("%TOOLS%\gs*.exe") do (
    set "NAME=%%~nxF"
    if /I not "!NAME!"=="gswin64c.exe" (
        set "INSTALLER=%%~fF"
        goto :found_installer
    )
)
for %%F in ("%ROOT%gs*.exe") do (
    set "NAME=%%~nxF"
    if /I not "!NAME!"=="gswin64c.exe" (
        set "INSTALLER=%%~fF"
        goto :found_installer
    )
)
for %%F in ("%USERPROFILE%\Downloads\gs*w64.exe") do (
    set "INSTALLER=%%~fF"
    goto :found_installer
)
for %%F in ("%USERPROFILE%\Downloads\gs*win64*.exe") do (
    set "INSTALLER=%%~fF"
    goto :found_installer
)

:found_installer
if defined INSTALLER (
    echo [1/3] Found installer: %INSTALLER%
    echo       Installing silently to: %GS_ROOT%
    echo       This may take 30-60 seconds...
    echo.

    if exist "%GS_ROOT%" rmdir /s /q "%GS_ROOT%" 2>nul
    mkdir "%GS_ROOT%" 2>nul

    REM NSIS: /S silent, /D= must be last, no quotes, no trailing backslash
    "%INSTALLER%" /S /D=%GS_ROOT%

    echo       Waiting for install to finish...
    timeout /t 8 /nobreak >nul

    call :FindGsExe
    if defined GS_EXE goto :installed_ok

    echo       Retrying search after longer wait...
    timeout /t 15 /nobreak >nul
    call :FindGsExe
    if defined GS_EXE goto :installed_ok

    echo.
    echo WARNING: Installer ran but gswin64c.exe was not found under:
    echo   %GS_ROOT%
    echo Try running the installer manually, then run this script again.
    echo.
)

REM --- 2) Copy from system install (Program Files) ---
echo [2/3] Checking for Ghostscript in Program Files...
set "SYS_GS="
for /d %%D in ("%ProgramFiles%\gs\gs*") do (
    if exist "%%D\bin\gswin64c.exe" set "SYS_GS=%%D\bin\gswin64c.exe"
)
if defined SYS_GS (
    echo       Found: !SYS_GS!
    echo       Copying to bundled tools folder...
    if not exist "%GS_ROOT%\bin" mkdir "%GS_ROOT%\bin"
    xcopy /Y /Q "!SYS_GS!" "%GS_ROOT%\bin\" >nul
    xcopy /Y /Q "%ProgramFiles%\gs\gs*\bin\*.dll" "%GS_ROOT%\bin\" >nul 2>nul
    for /d %%D in ("%ProgramFiles%\gs\gs*") do xcopy /Y /Q "%%D\bin\*.dll" "%GS_ROOT%\bin\" >nul 2>nul
    set "GS_EXE=%GS_ROOT%\bin\gswin64c.exe"
    if exist "!GS_EXE!" goto :installed_ok
)

REM --- 3) Still missing ---
echo [3/3] Ghostscript not available.
echo.
echo HOW TO FIX:
echo   1. Download "Ghostscript 10.x for Windows ^(64 bit^)" AGPL from:
echo      https://ghostscript.com/releases/gsdnld.html
echo   2. Save the installer ^(e.g. gs10071w64.exe^) into:
echo      %TOOLS%
echo   3. Run this script again ^(it will install automatically^)
echo.
echo   OR double-click the installer, then run this script again
echo   ^(it will copy from Program Files^).
echo.
goto :show_tools

:installed_ok
echo.
echo  ============================================
echo   Ghostscript setup complete!
echo  ============================================
echo   !GS_EXE!
echo.
echo   High/Maximum compression in PDFMaster will use this.
echo.
goto :show_tools

:show_tools
echo Tools folder contents:
dir /B "%TOOLS%" 2>nul
if exist "%GS_ROOT%" (
    echo.
    echo gs\ subfolder:
    dir /B /S "%GS_ROOT%\gswin64c.exe" 2>nul
    dir /B "%GS_ROOT%\bin" 2>nul
)
echo.
pause
exit /b 0

REM --- Search for gswin64c.exe under tools\gs ---
:FindGsExe
set "GS_EXE="
if exist "%GS_ROOT%\bin\gswin64c.exe" set "GS_EXE=%GS_ROOT%\bin\gswin64c.exe"
if not defined GS_EXE if exist "%TOOLS%\gswin64c.exe" set "GS_EXE=%TOOLS%\gswin64c.exe"
if not defined GS_EXE (
    for /r "%GS_ROOT%" %%F in (gswin64c.exe) do (
        set "GS_EXE=%%F"
        goto :eof
    )
)
goto :eof
