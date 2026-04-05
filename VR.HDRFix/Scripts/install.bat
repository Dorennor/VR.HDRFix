@echo off

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

set "SERVICE_NAME=HDRFixService"
set "DISPLAY_NAME=HDR to SDR Converter Service"
set "EXE_NAME=VR.HDRFix.exe"

pushd "%~dp0.."
set "BIN_PATH=%CD%\%EXE_NAME%"
popd

sc create "%SERVICE_NAME%" binPath= "\"%BIN_PATH%\"" start= auto DisplayName= "%DISPLAY_NAME%"
sc failure "%SERVICE_NAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000

sc start "%SERVICE_NAME%"

echo Service installation and recovery configuration complete.
pause