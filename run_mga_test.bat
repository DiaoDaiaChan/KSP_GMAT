    @echo off
setlocal
echo [RUN] Starting MGA Offline GUI Alignment Test Runner...
dotnet run --project tests\MGADebugRunner\MGADebugRunner.csproj
if %ERRORLEVEL% equ 0 (
    echo.
    echo [SUCCESS] Offline calculation complete! 100%% identical to in-game GUI!
) else (
    echo.
    echo [ERROR] Test failed with error level %ERRORLEVEL%
)
