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
        # Clean up any leftover DLLs in KspRoot to keep the game directory clean and neat
        @("boost_serialization.dll", "keplerian_toolbox.dll", "pykep_native.dll") | ForEach-Object {
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
        Copy-Item (Join-Path $LocalMissions "*.json") $KspMissions -Force
    }

    Write-Host "✅ Successfully deployed to KSP Steam Installation: $KspGameData" -ForegroundColor Green
} else {
    Write-Host "⚠️ KSP Steam directory not found, skipped external deployment." -ForegroundColor Yellow
}

Write-Host "`n🎉 BUILD & DEPLOYMENT COMPLETE 100%!" -ForegroundColor Cyan
