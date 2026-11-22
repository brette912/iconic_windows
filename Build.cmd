@echo off
setlocal
title Building Iconic
cd /d "%~dp0"

set "DEST=%LOCALAPPDATA%\Programs\Iconic"
set "LOG=%~dp0build-log.txt"

echo.
echo  Building Iconic... this takes about a minute.
echo.

call :build > "%LOG%" 2>&1
set "RC=%errorlevel%"
if "%RC%"=="2" goto :nosdk
if not "%RC%"=="0" goto :failed

echo  Done! Iconic is installed and its icon is on your desktop.
echo.
start "" "%DEST%\Iconic.exe"
timeout /t 4 >nul
exit /b 0

:build
echo Started %date% %time%
echo Folder: %cd%
echo.
echo --- dotnet location ---
where dotnet
echo --- installed SDKs ---
call dotnet --list-sdks
dotnet --list-sdks 2>nul | findstr /b /l "10." >nul || exit /b 2

taskkill /im Iconic.exe /f >nul 2>nul

echo --- publish ---
call dotnet publish "src\Iconic.csproj" -c Release -o "%DEST%" --nologo
if errorlevel 1 exit /b 1

echo --- desktop shortcut ---
powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $d 'Iconic.lnk')); $s.TargetPath=(Join-Path $env:LOCALAPPDATA 'Programs\Iconic\Iconic.exe'); $s.WorkingDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\Iconic'); $s.Description='Open Iconic'; $s.Save()"
if errorlevel 1 exit /b 1
echo Finished %date% %time%
exit /b 0

:nosdk
echo  The .NET 10 SDK is needed to build Iconic (one-time install).
echo  Run this command, then double-click Build.cmd again:
echo.
echo      winget install Microsoft.DotNet.SDK.10
echo.
pause
exit /b 1

:failed
echo  Build failed. The details are saved in build-log.txt in this folder.
echo  Just tell Claude it failed - Claude can read the log.
echo.
pause
exit /b 1
