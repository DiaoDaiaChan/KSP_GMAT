# Build & Deploy Script for KSP-GMAT Mod

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $ScriptDir "src\KSPGMAT"
$Csproj = Join-Path $SrcDir "KSPGMAT.csproj"
$LocalGameData = Join-Path $ScriptDir "GameData\KSPGMAT"
$KspGameData = "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData\KSPGMAT"

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
$LocalTemplates = Join-Path $LocalGameData "templates"
New-Item -ItemType Directory -Force -Path $LocalPlugins | Out-Null
New-Item -ItemType Directory -Force -Path $LocalTemplates | Out-Null

Copy-Item $BuildOutDll $LocalPlugins -Force
Copy-Item $BuildOutPdb $LocalPlugins -Force

$TemplateSource = Join-Path $ScriptDir "scripts\templates\*.script"
Copy-Item $TemplateSource $LocalTemplates -Force

Write-Host "✅ Deployed to local GameData: $LocalGameData" -ForegroundColor Green

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  3. Deploying to KSP Game Folder..." -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

if (Test-Path "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\GameData") {
    $KspPlugins = Join-Path $KspGameData "Plugins"
    $KspTemplates = Join-Path $KspGameData "templates"
    New-Item -ItemType Directory -Force -Path $KspPlugins | Out-Null
    New-Item -ItemType Directory -Force -Path $KspTemplates | Out-Null

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

    Write-Host "✅ Successfully deployed to KSP Steam Installation: $KspGameData" -ForegroundColor Green
} else {
    Write-Host "⚠️ KSP Steam directory not found, skipped external deployment." -ForegroundColor Yellow
}

Write-Host "`n🎉 BUILD & DEPLOYMENT COMPLETE 100%!" -ForegroundColor Cyan
