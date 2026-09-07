@echo off
setlocal enabledelayedexpansion

if exist "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"
)

set CONDA_LIB=C:\Users\43701\miniconda3\envs\kspmga\Library
set KEP3_ROOT=%~dp0..\..\pykep_sourcecode
set KEP3_BUILD=%~dp0..\..\build_kep3

if not exist "GameData\KSPGMAT\native" mkdir "GameData\KSPGMAT\native"

echo [BUILD] Compiling pykep_bridge.cpp with official clean ESA kep3 library...

cl.exe /O2 /LD /std:c++latest /EHsc /utf-8 /DNOMINMAX /DWIN32_LEAN_AND_MEAN ^
    /I"%CONDA_LIB%\include" ^
    /I"%KEP3_ROOT%\include" ^
    /I"%KEP3_BUILD%\include" ^
    "src\native\pykep_native\pykep_bridge.cpp" ^
    /Fe:"GameData\KSPGMAT\native\pykep_native.bin" ^
    /link /LIBPATH:"%CONDA_LIB%\lib" /LIBPATH:"%KEP3_BUILD%\Release" ^
    kep3.lib boost_serialization.lib fmt.lib

if %ERRORLEVEL% neq 0 (
    echo [ERROR] Native C++ compilation failed!
    exit /b 1
)

echo [SUCCESS] pykep_native.bin built successfully!
copy /y "%KEP3_BUILD%\Release\kep3.dll" "GameData\KSPGMAT\native\kep3.bin" >nul
copy /y "%CONDA_LIB%\bin\boost_serialization.dll" "GameData\KSPGMAT\native\boost_serialization.bin" >nul
copy /y "%CONDA_LIB%\bin\fmt.dll" "GameData\KSPGMAT\native\fmt.bin" >nul
copy /y "%CONDA_LIB%\bin\openblas.dll" "GameData\KSPGMAT\native\openblas.bin" >nul 2>nul
copy /y "%CONDA_LIB%\bin\libomp.dll" "GameData\KSPGMAT\native\libomp.bin" >nul 2>nul
copy /y "%CONDA_LIB%\bin\msvcp140.dll" "GameData\KSPGMAT\native\msvcp140.bin" >nul 2>nul
copy /y "%CONDA_LIB%\bin\vcruntime140.dll" "GameData\KSPGMAT\native\vcruntime140.bin" >nul 2>nul
copy /y "%CONDA_LIB%\bin\vcruntime140_1.dll" "GameData\KSPGMAT\native\vcruntime140_1.bin" >nul 2>nul

rem --- Aggressive Native Cleanup (Remove heavy MKL & unused DLLs) ---
del /f /q "GameData\KSPGMAT\native\mkl_*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libiomp5md.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libcrypto*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libssl*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\sqlite3.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\sleef.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libmpfr*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libgmp*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\mp++.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\spdlog.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\tbb12.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\zlib.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\zstd.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\boost_charconv.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\heyoka.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libgcc*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\libwinpthread*.bin" 2>nul
del /f /q "GameData\KSPGMAT\native\keplerian_toolbox.bin" 2>nul
del /f /q pykep_bridge.obj pykep_bridge.exp pykep_bridge.lib 2>nul
del /f /q "GameData\KSPGMAT\native\pykep_native.lib" "GameData\KSPGMAT\native\pykep_native.exp" 2>nul
del /f /q "GameData\KSPGMAT\native\*.dll" 2>nul
del /f /q "GameData\KSPGMAT\Plugins\pykep_native.*" 2>nul

rem --- Auto Deploy to External KSP Game Directory if installed ---
set "KSP_GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod"
if exist "%KSP_GAME_DIR%\GameData\KSPGMAT" (
    echo [DEPLOY] Auto-deploying slim native binaries to KSP game directory...
    if not exist "%KSP_GAME_DIR%\GameData\KSPGMAT\native" mkdir "%KSP_GAME_DIR%\GameData\KSPGMAT\native"
    copy /y "GameData\KSPGMAT\native\*.bin" "%KSP_GAME_DIR%\GameData\KSPGMAT\native\" >nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\mkl_*.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\libiomp5md.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\libcrypto*.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\libssl*.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\sqlite3.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\sleef.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\libmpfr*.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\libgmp*.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\mp++.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\spdlog.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\tbb12.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\zlib.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\zstd.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\boost_charconv.bin" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\native\*.dll" 2>nul
    del /f /q "%KSP_GAME_DIR%\GameData\KSPGMAT\Plugins\pykep_native.*" 2>nul
    echo [DEPLOY] Successfully synced native binaries to KSP!
)
