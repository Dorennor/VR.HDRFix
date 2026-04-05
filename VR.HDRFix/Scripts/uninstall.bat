@echo off

net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

set "SERVICE_NAME=HDRFixService"

sc stop "%SERVICE_NAME%"
sc delete "%SERVICE_NAME%"
pause