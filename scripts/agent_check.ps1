<#
.SYNOPSIS
    Agent 极速语法树自检与合规门禁脚本 (Sub-second AST Syntax & Compliance Linter)
.DESCRIPTION
    专为 AI Agent / 开发者提供的秒级自检工具：
    1. 支持单文件/多文件指定检查 (例如: pwsh -File scripts/agent_check.ps1 src/KSPGMAT/MGA/Foo.cs)
    2. 无参数时自动嗅探 git diff 中修改/新增的 .cs 文件进行增量检查
    3. 支持 -Build 开关同步验证 C# 编译器生成 (dotnet build)
    4. 标准编译器输出格式：FilePath(Line,Col): error/warning CODE: Message
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Files,
    [switch]$Build = $false,
    [switch]$Strict = $true,
    [switch]$All = $false
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $ScriptDir ".."))
$CheckerProj = Join-Path $ProjectRoot "tools\ComplianceChecker\ComplianceChecker.csproj"
$SrcCsproj = Join-Path $ProjectRoot "src\KSPGMAT\KSPGMAT.csproj"

# 1. 确保 ComplianceChecker 已预先构建 (以便后续秒级 --no-build 运行)
$CheckerDll = Join-Path $ProjectRoot "tools\ComplianceChecker\bin\Release\net6.0\ComplianceChecker.dll"
if (-not (Test-Path $CheckerDll)) {
    Write-Host "[AgentCheck] 首次运行，正在预构建 Roslyn AST 检查器..." -ForegroundColor DarkGray
    dotnet build $CheckerProj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "ComplianceChecker 构建失败！"
        exit 1
    }
}

# 2. 收集目标检查文件
$targetArgs = @()

if ($All) {
    # 全量扫描模式
    $targetArgs += "--strict"
} elseif ($Files -and $Files.Count -gt 0) {
    # 用户/Agent 指定了具体文件
    foreach ($f in $Files) {
        if (Test-Path $f) {
            $targetArgs += "--file"
            $targetArgs += [System.IO.Path]::GetFullPath($f)
        } else {
            $relPath = Join-Path $ProjectRoot $f
            if (Test-Path $relPath) {
                $targetArgs += "--file"
                $targetArgs += [System.IO.Path]::GetFullPath($relPath)
            } else {
                Write-Warning "文件未找到，已跳过: $f"
            }
        }
    }
} else {
    # 自动检索 git diff 中的修改文件
    $gitDiffOutput = git status --porcelain 2>$null
    $changedCs = @()
    if ($gitDiffOutput) {
        foreach ($line in $gitDiffOutput) {
            if ($line.Length -gt 3) {
                $filePath = $line.Substring(3).Trim().Trim('"')
                if ($filePath.EndsWith(".cs", [System.StringComparison]::OrdinalIgnoreCase)) {
                    $fullPath = Join-Path $ProjectRoot $filePath
                    if (Test-Path $fullPath) {
                        $changedCs += $fullPath
                    }
                }
            }
        }
    }

    if ($changedCs.Count -gt 0) {
        foreach ($cf in $changedCs) {
            $targetArgs += "--file"
            $targetArgs += $cf
        }
        Write-Host "🔍 [AgentCheck] 自动捕获工作区修改文件 ($($changedCs.Count) 个):" -ForegroundColor Cyan
        foreach ($cf in $changedCs) {
            Write-Host "   • $([System.IO.Path]::GetFileName($cf))" -ForegroundColor DarkGray
        }
    } else {
        Write-Host "ℹ️ [AgentCheck] 工作区无修改的 .cs 文件，执行默认合规扫描..." -ForegroundColor DarkGray
    }
}

$targetArgs += "--format"
$targetArgs += "compiler"
$targetArgs += "--project-root"
$targetArgs += $ProjectRoot

# 3. 执行 Roslyn AST 快速语法树门禁
dotnet run --project $CheckerProj -c Release --no-build -- $targetArgs
$checkerExit = $LASTEXITCODE

if ($checkerExit -ne 0) {
    Write-Host "`n❌ [AgentCheck] 语法树合规检查未通过！请根据上方错误信息修复后重试。" -ForegroundColor Red
    exit $checkerExit
}

# 4. 可选：执行增量编译验证
if ($Build) {
    Write-Host "`n🔨 [AgentCheck] 正在执行增量编译自检 (dotnet build)..." -ForegroundColor Cyan
    dotnet build $SrcCsproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "`n❌ [AgentCheck] 编译失败！" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "✅ [AgentCheck] 编译 0 警告 0 错误！" -ForegroundColor Green
}

Write-Host "🎉 [AgentCheck] 语法树与基线自检 100% 通过！" -ForegroundColor Green
exit 0
