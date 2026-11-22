@echo off
setlocal
title Packaging Iconic
cd /d "%~dp0"

set "SHARE=%~dp0Share"
set "OUT=%SHARE%\Iconic"
set "LOG=%~dp0package-log.txt"

echo.
echo  Packaging Iconic to share... this takes a minute or two.
echo.

call :pack > "%LOG%" 2>&1
if errorlevel 1 goto :failed

echo  Done! Send this file:  Share\Iconic.zip
echo.
start "" explorer "%SHARE%"
timeout /t 4 >nul
exit /b 0

:pack
if exist "%SHARE%" rmdir /s /q "%SHARE%"
call dotnet publish "src\Iconic.csproj" -c Release -o "%OUT%" --nologo --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=false
if errorlevel 1 exit /b 1
copy /y "share-template\Install Iconic.cmd" "%OUT%\" || exit /b 1
copy /y "share-template\Read Me.txt" "%OUT%\" || exit /b 1
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUT%' -DestinationPath '%SHARE%\Iconic.zip' -Force"
if errorlevel 1 exit /b 1
exit /b 0

:failed
echo  Packaging failed. Tell Claude - the details are in package-log.txt.
echo.
pause
exit /b 1
