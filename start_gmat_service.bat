@echo off
chcp 65001 >nul
title KSP-GMAT Bridge Service
echo ======================================================================
echo   KSP-GMAT Bridge Service (REST API on http://127.0.0.1:8055)
echo   Ensures native high-precision mission design with NASA GMAT R2026a
echo ======================================================================
echo.

REM Check Python environment
python --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Python not found in system PATH.
    echo Please install Python 3.10+ and make sure GMAT API is configured.
    pause
    exit /b 1
)

echo Starting bridge server...
python "%~dp0gmat_service\gmat_bridge_server.py"
pause
