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
sc start "%SERVICE_NAME%"
pause