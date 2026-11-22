@echo off
title Installing .NET 10 SDK
cd /d "%~dp0"
echo.
echo  Installing the .NET 10 SDK (one time). Click Yes if Windows asks.
echo.
winget install --id Microsoft.DotNet.SDK.10 --exact --source winget --accept-package-agreements --accept-source-agreements > "%~dp0sdk-install-log.txt" 2>&1
type "%~dp0sdk-install-log.txt"
echo.
echo  Finished. You can close this window and tell Claude.
echo.
pause
