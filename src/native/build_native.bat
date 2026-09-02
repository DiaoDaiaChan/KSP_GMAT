@echo off
setlocal enabledelayedexpansion

if exist "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvars64.bat"
) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" (
    call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
)

set CONDA_LIB=C:\Users\43701\miniconda3\envs\kspmga\Library

if not exist "GameData\KSPGMAT\native" mkdir "GameData\KSPGMAT\native"

echo [BUILD] Compiling pykep_bridge.cpp with official ESA keplerian_toolbox library...

cl.exe /O2 /LD /std:c++17 /EHsc /DNOMINMAX /DWIN32_LEAN_AND_MEAN ^
    /I"%CONDA_LIB%\include" ^
    "src\native\pykep_native\pykep_bridge.cpp" ^
    /Fe:"GameData\KSPGMAT\native\pykep_native.bin" ^
    /link /LIBPATH:"%CONDA_LIB%\lib" keplerian_toolbox.lib boost_serialization.lib

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] pykep_native.bin built successfully!
    copy /y "%CONDA_LIB%\bin\keplerian_toolbox.dll" "GameData\KSPGMAT\native\keplerian_toolbox.bin" >nul
    copy /y "%CONDA_LIB%\bin\boost_serialization.dll" "GameData\KSPGMAT\native\boost_serialization.bin" >nul
    copy /y "%CONDA_LIB%\bin\msvcp140.dll" "GameData\KSPGMAT\native\msvcp140.bin" >nul 2>nul
    copy /y "%CONDA_LIB%\bin\vcruntime140.dll" "GameData\KSPGMAT\native\vcruntime140.bin" >nul 2>nul
    copy /y "%CONDA_LIB%\bin\vcruntime140_1.dll" "GameData\KSPGMAT\native\vcruntime140_1.bin" >nul 2>nul
    del /f /q pykep_bridge.obj pykep_bridge.exp pykep_bridge.lib 2>nul
    del /f /q "GameData\KSPGMAT\native\pykep_native.lib" "GameData\KSPGMAT\native\pykep_native.exp" 2>nul
    del /f /q "GameData\KSPGMAT\native\*.dll" 2>nul
    del /f /q "GameData\KSPGMAT\Plugins\pykep_native.*" 2>nul
) else (
    echo [ERROR] Native C++ compilation failed!
    exit /b 1
)
