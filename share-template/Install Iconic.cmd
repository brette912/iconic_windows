@echo off
title Installing Iconic
set "DEST=%LOCALAPPDATA%\Programs\Iconic"

if not exist "%~dp0Iconic.exe" (
  echo.
  echo  Please right-click Iconic.zip, choose "Extract All", then run
  echo  "Install Iconic" from the extracted folder.
  echo.
  pause
  exit /b 1
)

taskkill /im Iconic.exe /f >nul 2>nul
if not exist "%DEST%" mkdir "%DEST%"
copy /y "%~dp0Iconic.exe" "%DEST%\Iconic.exe" >nul
if errorlevel 1 (
  echo  Couldn't copy Iconic. Close it if it's running and try again.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $d 'Iconic.lnk')); $s.TargetPath=(Join-Path $env:LOCALAPPDATA 'Programs\Iconic\Iconic.exe'); $s.WorkingDirectory=(Join-Path $env:LOCALAPPDATA 'Programs\Iconic'); $s.Description='Open Iconic'; $s.Save()"

start "" "%DEST%\Iconic.exe"
