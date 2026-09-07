# Build & Deploy Script for KSP-GMAT Mod

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $ScriptDir "src\KSPGMAT"
$Csproj = Join-Path $SrcDir "KSPGMAT.csproj"
$LocalGameData = Join-Path $ScriptDir "GameData\KSPGMAT"
$KspRoot = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod"
$KspGameData = Join-Path $KspRoot "GameData\KSPGMAT"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  1. Building KSPGMAT (Release)..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

dotnet build $Csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  1.1 Compiling & Deploying Slim Native Binaries..." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$BuildNativeBat = Join-Path $ScriptDir "src\native\build_native.bat"
if (Test-Path $BuildNativeBat) {
    & cmd.exe /c $BuildNativeBat
}

$ComplianceProj = Join-Path $ScriptDir "tools\ComplianceChecker\ComplianceChecker.csproj"
if (Test-Path $ComplianceProj) {
    dotnet build $ComplianceProj -c Release --nologo -v q
}

$BuildOutDll = Join-Path $SrcDir "bin\Release\net48\KSPGMAT.dll"
$BuildOutPdb = Join-Path $SrcDir "bin\Release\net48\KSPGMAT.pdb"

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  2. Deploying to Local GameData..." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

$LocalPlugins = Join-Path $LocalGameData "Plugins"
$LocalNative = Join-Path $LocalGameData "native"
$LocalTemplates = Join-Path $LocalGameData "templates"
$LocalLoc = Join-Path $LocalGameData "loc"
New-Item -ItemType Directory -Force -Path $LocalPlugins | Out-Null
New-Item -ItemType Directory -Force -Path $LocalNative | Out-Null
New-Item -ItemType Directory -Force -Path $LocalTemplates | Out-Null
New-Item -ItemType Directory -Force -Path $LocalLoc | Out-Null

# Clean any unmanaged native binaries from GameData to prevent KSP AssemblyLoader crash
Get-ChildItem -Path $LocalGameData -Recurse -Filter "pykep_native.dll" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $LocalPlugins -Filter "pykep_native.*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Copy-Item $BuildOutDll $LocalPlugins -Force
Copy-Item $BuildOutPdb $LocalPlugins -Force

$TemplateSource = Join-Path $LocalTemplates "*.script"

Write-Host "✅ Deployed to local GameData: $LocalGameData" -ForegroundColor Green

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  3. Deploying to KSP Game Folder..." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

if (Test-Path $KspRoot) {
    $KspPlugins = Join-Path $KspGameData "Plugins"
    $KspNative = Join-Path $KspGameData "native"
    $KspTemplates = Join-Path $KspGameData "templates"
    $KspLoc = Join-Path $KspGameData "loc"
    New-Item -ItemType Directory -Force -Path $KspPlugins | Out-Null
    New-Item -ItemType Directory -Force -Path $KspNative | Out-Null
    New-Item -ItemType Directory -Force -Path $KspTemplates | Out-Null
    New-Item -ItemType Directory -Force -Path $KspLoc | Out-Null

    # Clean any unmanaged DLLs from GameData to prevent KSP AssemblyLoader crash
    Get-ChildItem -Path $KspGameData -Recurse -Filter "*.dll" | Where-Object { $_.DirectoryName -notmatch "Plugins" } | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $KspPlugins -Filter "*.old" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $KspPlugins -Filter "*.bak" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $KspPlugins -Filter "pykep_native.*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $KspPlugins -Filter "keplerian_toolbox.*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $KspPlugins -Filter "boost_serialization.*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

    $TargetDll = Join-Path $KspPlugins "KSPGMAT.dll"
    $TargetPdb = Join-Path $KspPlugins "KSPGMAT.pdb"

    try {
        Copy-Item $BuildOutDll $TargetDll -Force -ErrorAction Stop
        Copy-Item $BuildOutPdb $TargetPdb -Force -ErrorAction Stop
    } catch {
        Write-Host "⚠️ KSP is currently running, replacing active DLL via backup swap..." -ForegroundColor Yellow
        $BackupDll = Join-Path $KspPlugins "KSPGMAT.dll.old"
        $BackupPdb = Join-Path $KspPlugins "KSPGMAT.pdb.old"
        if (Test-Path $BackupDll) { Remove-Item $BackupDll -Force -ErrorAction SilentlyContinue }
        if (Test-Path $BackupPdb) { Remove-Item $BackupPdb -Force -ErrorAction SilentlyContinue }

        Move-Item $TargetDll $BackupDll -Force -ErrorAction SilentlyContinue
        Move-Item $TargetPdb $BackupPdb -Force -ErrorAction SilentlyContinue

        Copy-Item $BuildOutDll $TargetDll -Force
        Copy-Item $BuildOutPdb $TargetPdb -Force
    }

    # Deploy native binaries exclusively as .bin inside GameData/KSPGMAT/native (100% clean, no files scattered to root)
    if (Test-Path $LocalNative) {
        Get-ChildItem -Path $LocalNative -File | Where-Object { $_.Extension -eq ".bin" } | ForEach-Object {
            $dest = Join-Path $KspNative $_.Name
            Copy-Item $_.FullName $dest -Force -ErrorAction SilentlyContinue
        }
        # Clean up legacy heavy MKL and unused files
        @("mkl_core.3.bin", "mkl_def.3.bin", "mkl_intel_thread.3.bin", "libiomp5md.bin", "libcrypto-3-x64.bin", "libssl-3-x64.bin", "sqlite3.bin", "sleef.bin", "libmpfr-6.bin", "libgmp-10.bin", "mp++.bin", "spdlog.bin", "tbb12.bin", "zlib.bin", "zstd.bin", "boost_charconv.bin", "heyoka.bin", "keplerian_toolbox.bin") | ForEach-Object {
            $fLocal = Join-Path $LocalNative $_
            if (Test-Path $fLocal) { Remove-Item $fLocal -Force -ErrorAction SilentlyContinue }
            $fKsp = Join-Path $KspNative $_
            if (Test-Path $fKsp) { Remove-Item $fKsp -Force -ErrorAction SilentlyContinue }
        }
        # Clean up any leftover DLLs in KspRoot to keep the game directory clean and neat
        @("boost_serialization.dll", "keplerian_toolbox.dll", "pykep_native.dll", "openblas.dll", "libomp.dll", "kep3.dll") | ForEach-Object {
            $leftover = Join-Path $KspRoot $_
            if (Test-Path $leftover) { Remove-Item $leftover -Force -ErrorAction SilentlyContinue }
        }
    }
    Copy-Item $TemplateSource $KspTemplates -Force

    # Also copy Textures and Config if present
    if (Test-Path (Join-Path $LocalGameData "Textures")) {
        Copy-Item (Join-Path $LocalGameData "Textures") $KspGameData -Recurse -Force
    }
    if (Test-Path (Join-Path $LocalGameData "Config")) {
        Copy-Item (Join-Path $LocalGameData "Config") $KspGameData -Recurse -Force
    }
    if (Test-Path (Join-Path $LocalGameData "gmat_capabilities.json")) {
        Copy-Item (Join-Path $LocalGameData "gmat_capabilities.json") $KspGameData -Force
    }
    if (Test-Path $LocalLoc) {
        Copy-Item (Join-Path $LocalLoc "*.json") $KspLoc -Force
    }
    $LocalMissions = Join-Path $LocalGameData "Missions"
    $KspMissions = Join-Path $KspGameData "Missions"
    if (Test-Path $LocalMissions) {
        New-Item -ItemType Directory -Force -Path $KspMissions | Out-Null
        @("BepiColombo_SEP_LowThrust.json", "Cassini_1DSM_1997.json", "Cassini_Huygens_1997.json", "Galileo_VEEGA_1989.json", "Juno_EEJ_Ballistic_2011.json", "Juno_EEJ_MGA1DSM.json", "Mariner_10_1973.json", "New_Horizons_2006.json", "Rosetta_EEMEB_2004.json", "Voyager_2_GrandTour_1977.json") | ForEach-Object {
            $oldf = Join-Path $KspMissions $_
            if (Test-Path $oldf) { Remove-Item $oldf -Force -ErrorAction SilentlyContinue }
        }
        Copy-Item (Join-Path $LocalMissions "*.json") $KspMissions -Force
    }

    Write-Host "✅ Successfully deployed to KSP Steam Installation: $KspGameData" -ForegroundColor Green
} else {
    Write-Host "⚠️ KSP Steam directory not found, skipped external deployment." -ForegroundColor Yellow
}

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  4. Running Roslyn AST Code Sanity & Compliance Check..." -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta

$ComplianceProj = Join-Path $ScriptDir "tools\ComplianceChecker\ComplianceChecker.csproj"
if (Test-Path $ComplianceProj) {
    dotnet run --project $ComplianceProj -c Release --no-build -- --project-root $ScriptDir --strict
    if ($LASTEXITCODE -ne 0) {
        # 如果未提前构建则完整运行
        dotnet run --project $ComplianceProj -c Release -- --project-root $ScriptDir --strict
    }
} else {
    $ComplianceScript = Join-Path $ScriptDir "scripts\check_code_compliance.ps1"
    if (Test-Path $ComplianceScript) {
        & $ComplianceScript -ProjectRoot $ScriptDir
    }
}

Write-Host "`n🎉 BUILD & DEPLOYMENT COMPLETE 100%!" -ForegroundColor Cyan
