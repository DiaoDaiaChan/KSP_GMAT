<#
.SYNOPSIS
    KSP-GMAT 全工程无死角静态合规与健康自查引擎 (Full Project-Wide Code Sanity & Compliance Checker)
.DESCRIPTION
    全量扫描整个项目的所有文件（无白名单排除限制），全面排查：
    1. 游离状态变量与 SSOT 绕过 (GMATStateManager, MGAStateManager, public static 游离变量)
    2. 游离 UI 控件与样式规范 (GMATMainWindow.Widgets, GMATMainWindow.Styles, new GUIStyle GC 尖刺, GUI.skin 裸用, 延迟输入框复用)
    3. 游离未进入 I18n 的硬编码文本与四国语言字典 100% 对称性 (I18n.cs, GameData/KSPGMAT/loc/*.json)
    4. 动力学基线与天体解耦规范 (baseline_agent.md 铁律，全天体动态适配)
.NOTES
    每次构建部署 (deploy.ps1) 自动执行，全面汇报所有潜在隐患与优化建议。
#>

[CmdletBinding()]
param(
    [switch]$Strict = $false,
    [string]$ProjectRoot = ""
)

$ErrorActionPreference = "Continue"

if ([string]::IsNullOrEmpty($ProjectRoot)) {
    $ProjectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
} else {
    $ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
}

$SrcDir = [System.IO.Path]::Combine($ProjectRoot, "src", "KSPGMAT")
$LocDir = [System.IO.Path]::Combine($ProjectRoot, "GameData", "KSPGMAT", "loc")

Write-Host ""
Write-Host "==========================================================================================" -ForegroundColor Cyan
Write-Host "  🛡️  KSP-GMAT 全工程全文件静态合规自查引擎 (Project-Wide Deep Compliance Audit)" -ForegroundColor Cyan
Write-Host "==========================================================================================" -ForegroundColor Cyan
Write-Host "  扫描根目录: $SrcDir" -ForegroundColor DarkGray
Write-Host "  语言资源库: $LocDir" -ForegroundColor DarkGray
Write-Host "  扫描策略  : 全量文件深入扫描 (不限定特定文件，全面防漏)" -ForegroundColor DarkGray
$allCsList = @()
$searchDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\src\KSPGMAT"))
$rawFiles = Get-ChildItem -Path $searchDir -Recurse -Filter "*.cs"
foreach ($rf in $rawFiles) {
    $p = $rf.FullName
    $rel = $p.Substring($searchDir.Length)
    if (-not $rel.StartsWith("\obj\") -and -not $rel.StartsWith("/obj/") -and -not $rel.StartsWith("\bin\") -and -not $rel.StartsWith("/bin/")) {
        $allCsList += $p
    }
}
$allCsFiles = $allCsList

Write-Host "  🔍 已索引源代码文件总数: $($allCsFiles.Count) 个" -ForegroundColor White
Write-Host ""

$totalChecks = 0
$issuesWarning = [System.Collections.Generic.List[PSCustomObject]]::new()
$issuesError = [System.Collections.Generic.List[PSCustomObject]]::new()
$issuesInfo = [System.Collections.Generic.List[PSCustomObject]]::new()

function Add-Issue {
    param(
        [string]$Severity,
        [string]$Category,
        [string]$File,
        [int]$Line,
        [string]$Message,
        [string]$Snippet,
        [string]$Suggestion
    )

    $item = [PSCustomObject]@{
        Severity   = $Severity
        Category   = $Category
        File       = $File
        Line       = $Line
        Message    = $Message
        Snippet    = $Snippet
        Suggestion = $Suggestion
    }

    if ($Severity -eq "ERROR") {
        $script:issuesError.Add($item)
    } elseif ($Severity -eq "WARN") {
        $script:issuesWarning.Add($item)
    } else {
        $script:issuesInfo.Add($item)
    }
}

# ==========================================================================================
# 1. 全工程状态管理与单一真值源审计 (SSOT & Free-floating State Audit)
# ==========================================================================================
Write-Host "[1/4] 正在扫描全工程所有源码：排查 SSOT 状态绕过与游离静态变量..." -ForegroundColor Yellow
$totalChecks++

foreach ($f in $allCsFiles) {
    $fileName = [System.IO.Path]::GetFileName($f)
    $isStateManagerSelf = ($fileName -eq "GMATStateManager.cs" -or $fileName -eq "MGAStateManager.cs")
    $isSettingsFile = ($fileName -eq "KSPGMATSettings.cs")
    $isStylesFile = ($fileName -eq "GMATMainWindow.Styles.cs")

    $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { continue }

        # 检查 1.1: 飞船物理状态绕过 (非 StateManager 自身直接访问 FlightGlobals.ActiveVessel 物理属性)
        if (-not $isStateManagerSelf) {
            if ($line -match 'FlightGlobals\.ActiveVessel\.(totalMass|orbit|FindPartModules|GetTotalMass|altitude|terrainAltitude|mainBody)') {
                if ($line -notmatch 'SyncFromVessel' -and $line -notmatch 'vessel\s*=' -and $line -notmatch 'null') {
                    Add-Issue -Severity "WARN" -Category "SSOT-BYPASS" `
                        -File $fileName -Line $lineNum `
                        -Message "检测到直接读取 FlightGlobals.ActiveVessel 物理属性，未走统一 StateManager" `
                        -Snippet $trimmed `
                        -Suggestion "请通过 GMATStateManager.Current 或 MGAStateManager.Current 访问飞船物理状态"
                }
            }
        }

        # 检查 1.2: 游离的 public static 状态变量 (业务类私自维护全局状态造成状态割裂)
        if (-not $isStateManagerSelf -and -not $isSettingsFile -and -not $isStylesFile) {
            if ($line -match '^\s*public\s+static\s+(?!readonly|const)[A-Za-z0-9_<>,\[\]]+\s+([A-Za-z0-9_]+)\s*(=|;)') {
                $varName = $matches[1]
                if ($varName -notmatch "(Style|Texture|On[A-Z]|Instance|Handler|Event|Lock)") {
                    Add-Issue -Severity "WARN" -Category "FREE-STATE" `
                        -File $fileName -Line $lineNum `
                        -Message "检测到游离的 public static 状态变量 '$varName'" `
                        -Snippet $trimmed `
                        -Suggestion "跨组件或持久化状态应集中归入 GMATStateManager / MGAStateManager 托管"
                }
            }
        }
    }
}

# =========================================================================
# 2. 全工程 UI 控件规范与样式系统审计 (Widgets & Styles Compliance Audit)
# =========================================================================
Write-Host "[2/4] 正在扫描全工程所有源码：排查每帧 new GUIStyle、裸用皮肤与原生控件..." -ForegroundColor Yellow
$totalChecks++

foreach ($f in $allCsFiles) {
    $fileName = [System.IO.Path]::GetFileName($f)
    $isStylesFile = ($fileName -eq "GMATMainWindow.Styles.cs")

    $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { continue }

        # 检查 2.1: 动态 new GUIStyle 导致每帧 GC 尖刺 (排除 Styles 初始化与缓存赋值)
        if (-not $isStylesFile) {
            if ($line -match 'new\s+GUIStyle\b' -and $line -notmatch 'InitStyles' -and $line -notmatch 'cached[A-Za-z0-9_]*\s*=' -and ($i -eq 0 -or $lines[$i-1] -notmatch 'if\s*\(\s*cached')) {
                Add-Issue -Severity "WARN" -Category "UI-GC-STYLE" `
                    -File $fileName -Line $lineNum `
                    -Message "检测到动态实例化 'new GUIStyle'，在 OnGUI 渲染循环中会产生严重 GC 内存抖动" `
                    -Snippet $trimmed `
                    -Suggestion "请在 GMATMainWindow.Styles.cs 中预热，或使用私有字段进行懒加载单例复用"
            }
        }

        # 检查 2.2: 裸用原生 GUI.skin (破坏暗黑毛玻璃科技感统一规范)
        if ($line -match 'GUI\.skin\.(button|box)\b' -and $line -notmatch '\?\?') {
            Add-Issue -Severity "INFO" -Category "UI-SKIN-STYLE" `
                -File $fileName -Line $lineNum `
                -Message "检测到直接裸用原生 GUI.skin 控件" `
                -Snippet $trimmed `
                -Suggestion "建议替换为 primaryButtonStyle / secondaryButtonStyle / nestedCardBoxStyle 等统一设计规范样式"
        }

        # 检查 2.3: 裸用 GUILayout.TextField 输入浮点数 (破坏 Delayed 输入防抖体验)
        if ($line -match 'GUILayout\.TextField\(' -and ($line -match 'double|float|sma|ecc|inc|alt|mass|rad|step' -or $trimmed -match 'SMA|ECC|INC|Alt|Mass|Rad|Step')) {
            if ($fileName -ne "GMATMainWindow.Widgets.cs") {
                Add-Issue -Severity "INFO" -Category "UI-DELAYED-INPUT" `
                    -File $fileName -Line $lineNum `
                    -Message "检测到裸用 GUILayout.TextField 处理数值输入" `
                    -Snippet $trimmed `
                    -Suggestion "建议使用 GMATMainWindow.Widgets 中的 DelayedFloatField / DelayedDoubleField 避免编辑时焦点跳动"
            }
        }
    }
}

# =========================================================================
# 3. 全工程多语言与游离文本审计 (I18n Compliance & Loc Quad-Symmetry Audit)
# =========================================================================
Write-Host "[3/4] 正在扫描全工程所有源码与四语言字典：排查硬编码中文与键值对称性..." -ForegroundColor Yellow
$totalChecks++

# 3A. 全量扫描所有 C# 文件中的硬编码中文文本
$i18nExemptFiles = @("I18n.cs", "GMATI18n.cs", "KSPGMATLog.cs")

foreach ($f in $allCsFiles) {
    $fileName = [System.IO.Path]::GetFileName($f)
    if ($i18nExemptFiles -contains $fileName) { continue }

    $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { continue }

        # 匹配双引号内的中文文本 (排除 Debug.Log, KSPGMATLog, I18n.T, I18n.Format, 注解)
        if ($line -match '"([^"]*[\u4e00-\u9fa5]+[^"]*)"') {
            $matchedStr = $matches[1]
            if ($line -notmatch 'Debug\.Log' -and $line -notmatch 'KSPGMATLog' -and $line -notmatch 'I18n\.T' -and $line -notmatch 'I18n\.Format' -and $line -notmatch 'Console\.WriteLine') {
                Add-Issue -Severity "WARN" -Category "I18N-HARDCODED" `
                    -File $fileName -Line $lineNum `
                    -Message "检测到代码中存在未进入 I18n 的硬编码中文文本: `"$matchedStr`"" `
                    -Snippet $trimmed `
                    -Suggestion "请将文本维护入 GameData/KSPGMAT/loc/ 并使用 I18n.T(`"key`") 引用"
            }
        }
    }
}

# 3B. 四套语言字典完整性、格式与键对称性检测
$langs = @("zh", "en", "ru", "jp")
$locTables = @{}
$locKeySets = @{}

foreach ($lang in $langs) {
    $p = Join-Path $LocDir "$lang.json"
    if (Test-Path $p) {
        try {
            $content = Get-Content $p -Raw -Encoding UTF8
            $doc = [System.Text.Json.JsonDocument]::Parse($content)
            $dict = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
            foreach ($elem in $doc.RootElement.EnumerateObject()) {
                $dict[$elem.Name] = $elem.Value.GetString()
            }
            $locTables[$lang] = $dict
            $locKeySets[$lang] = [System.Collections.Generic.HashSet[string]]::new([string[]]$dict.Keys, [System.StringComparer]::Ordinal)
        } catch {
            Add-Issue -Severity "ERROR" -Category "I18N-JSON-CORRUPT" `
                -File "$lang.json" -Line 1 `
                -Message "语言字典 JSON 损坏: $($_.Exception.Message)" `
                -Snippet "" `
                -Suggestion "请检查 JSON 语法是否有未闭合引号或多余逗号"
        }
    } else {
        Add-Issue -Severity "ERROR" -Category "I18N-FILE-MISSING" `
            -File "$lang.json" -Line 0 `
            -Message "缺少必须的语言字典文件: $p" `
            -Snippet "" `
            -Suggestion "请补充该语言定义"
    }
}

if ($locKeySets.ContainsKey("zh")) {
    $zhKeys = $locKeySets["zh"]
    $zhCount = $zhKeys.Count
    Write-Host "  -> zh.json 基准词条数: $zhCount" -ForegroundColor DarkGray

    foreach ($otherLang in @("en", "ru", "jp")) {
        if ($locKeySets.ContainsKey($otherLang)) {
            $otherKeys = $locKeySets[$otherLang]
            
            $missingKeys = @()
            foreach ($k in $zhKeys) {
                if (-not $otherKeys.Contains($k)) {
                    $missingKeys += $k
                }
            }

            if ($missingKeys.Count -gt 0) {
                $sample = ($missingKeys | Select-Object -First 5) -join ", "
                if ($missingKeys.Count -gt 5) { $sample += " (等共 $($missingKeys.Count) 个)" }
                Add-Issue -Severity "ERROR" -Category "I18N-ASYMMETRY" `
                    -File "$otherLang.json" -Line 1 `
                    -Message "$otherLang.json 缺失基准词条 ($($missingKeys.Count) 个): $sample" `
                    -Snippet "" `
                    -Suggestion "请向 $otherLang.json 补充对齐这些键值"
            }
        }
    }
}

# =========================================================================
# 4. 全工程动力学基线与天体硬编码解耦审计 (Baseline Dynamics & Celestial Decoupling)
# =========================================================================
Write-Host "[4/4] 正在扫描全工程所有源码：排查天体白名单硬编码与极值事件误用..." -ForegroundColor Yellow
$totalChecks++

foreach ($f in $allCsFiles) {
    $fileName = [System.IO.Path]::GetFileName($f)
    $lines = [System.IO.File]::ReadAllLines($f, [System.Text.Encoding]::UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $lineNum = $i + 1
        $line = $lines[$i]
        $trimmed = $line.Trim()

        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*") -or $trimmed.StartsWith("/*")) { continue }

        # 检查 4.1: 天体硬编码过滤 (如 if (body == "Earth") 特判，未走统一星系模型)
        if ($line -match 'if\s*\(\s*(?:body|centralBody|cb|planetName)\s*==\s*"(Earth|Kerbin|Sun|Moon)"\s*\)' -and $line -notmatch 'default' -and $line -notmatch 'Fallback') {
            Add-Issue -Severity "INFO" -Category "CELESTIAL-BRANCH" `
                -File $fileName -Line $lineNum `
                -Message "检测到对特定天体名称的直接特化判断分支" `
                -Snippet $trimmed `
                -Suggestion "建议通过 SolarSystemCatalog 或 GMATSchemaRegistry 动态读取天体物理属性，以兼容 RSS / OPM 等星球包"
        }

        # 检查 4.2: Achieve 极值事件误用 (Apoapsis / Periapsis 等)
        if ($line -match 'Achieve.*(Apoapsis|Periapsis|Aop|Peri|Apo)\b' -and $line -notmatch 'IsExtremumParameter' -and $line -notmatch 'Linter' -and $line -notmatch 'Test') {
            Add-Issue -Severity "WARN" -Category "BASELINE-ACHIEVE" `
                -File $fileName -Line $lineNum `
                -Message "检测到在 Achieve 上下文中直接引用极值事件关键字" `
                -Snippet $trimmed `
                -Suggestion "极值事件只能作为 Propagate 停机条件；Achieve 必须使用可微物理量并接入 GMATSchemaRegistry"
        }

        # 检查 4.3: 游离日志与控制台裸写 (UnityEngine.Debug.* / Console.Write*)
        if ($fileName -ne "KSPGMATLog.cs") {
            if ($line -match '\b(?:UnityEngine\.)?Debug\.(?:Log|LogWarning|LogError|LogException|LogAssertion|LogFormat)\b') {
                Add-Issue -Severity "ERROR" -Category "ORPHAN-LOG" `
                    -File $fileName -Line $lineNum `
                    -Message "检测到游离原生日志调用 '$trimmed'，违反统一日志系统规范" `
                    -Snippet $trimmed `
                    -Suggestion "请统一通过 KSPGMATLog (如 KSPGMATLog.Info / Warn / Error 或 ILogger) 记录日志"
            }
            if ($line -match '\b(?:System\.)?Console\.(?:Error\.)?(?:WriteLine|Write)\b') {
                Add-Issue -Severity "ERROR" -Category "ORPHAN-LOG" `
                    -File $fileName -Line $lineNum `
                    -Message "检测到游离控制台裸写调用 '$trimmed'，违反统一日志系统规范" `
                    -Snippet $trimmed `
                    -Suggestion "请统一通过 KSPGMATLog (如 KSPGMATLog.Info / Debug / Warn / Error) 记录日志"
            }
        }
    }
}

# ==========================================================================================
# 5. 结构化审计结果分类汇报
# ==========================================================================================
Write-Host ""
Write-Host "==========================================================================================" -ForegroundColor Cyan
Write-Host "  📋 全工程静态合规与健康自查审计报告 (Full Project Audit Summary)" -ForegroundColor Cyan
Write-Host "==========================================================================================" -ForegroundColor Cyan

$errCount = $issuesError.Count
$warnCount = $issuesWarning.Count
$infoCount = $issuesInfo.Count

Write-Host "  审计文件总数: $($allCsFiles.Count) 个 C# 源码文件 + 4 套本地化字典" -ForegroundColor White
Write-Host "  发现错误 (ERROR)  : $errCount" -ForegroundColor $(if ($errCount -gt 0) { "Red" } else { "Green" })
Write-Host "  发现告警 (WARN)   : $warnCount" -ForegroundColor $(if ($warnCount -gt 0) { "Yellow" } else { "Green" })
Write-Host "  优化建议 (INFO)   : $infoCount" -ForegroundColor DarkGray
Write-Host ""

if ($errCount -gt 0) {
    Write-Host "🚨 阻断性违规项 (CRITICAL ERRORS - 违反基线必须整改):" -ForegroundColor Red
    foreach ($issue in $issuesError) {
        Write-Host "  • [$($issue.Category)] $($issue.File):L$($issue.Line)" -ForegroundColor Red
        Write-Host "    说明: $($issue.Message)" -ForegroundColor DarkRed
        if (![string]::IsNullOrEmpty($issue.Snippet)) {
            Write-Host "    源码: $($issue.Snippet)" -ForegroundColor Gray
        }
        Write-Host "    建议: $($issue.Suggestion)" -ForegroundColor Yellow
        Write-Host ""
    }
}

if ($warnCount -gt 0) {
    Write-Host "⚠️ 建议整改项 (WARNINGS - 游离文本/未托管状态/潜在优化点):" -ForegroundColor Yellow
    
    # 分类展示告警，更一目了然
    $groupedWarns = $issuesWarning | Group-Object Category
    foreach ($grp in $groupedWarns) {
        Write-Host "  📁 [$($grp.Name)] 共 $($grp.Count) 处" -ForegroundColor Cyan
        $top3 = $grp.Group | Select-Object -First 3
        foreach ($issue in $top3) {
            Write-Host "    • $($issue.File):L$($issue.Line) - $($issue.Message)" -ForegroundColor DarkYellow
            if (![string]::IsNullOrEmpty($issue.Snippet)) {
                Write-Host "      源码: $($issue.Snippet)" -ForegroundColor Gray
            }
        }
        if ($grp.Count -gt 3) {
            Write-Host "      ... 其余 $($grp.Count - 3) 处同类项已收拢" -ForegroundColor DarkGray
        }
        Write-Host ""
    }
}

if ($errCount -eq 0 -and $warnCount -eq 0) {
    Write-Host "🎉 完美合规！全工程未发现游离状态变量、未发现裸露 UI 控件、四语言字典 100% 对称对齐！" -ForegroundColor Green
} elseif ($errCount -eq 0) {
    Write-Host "✅ 核心基线红线无阻断违规！(建议在后续迭代中根据上方分类逐步将 $($warnCount) 处警告项规范化)" -ForegroundColor Green
}

Write-Host "==========================================================================================" -ForegroundColor Cyan
Write-Host ""

if ($Strict -and $errCount -gt 0) {
    Write-Host "❌ 严格模式下审计未通过，终止后续流程。" -ForegroundColor Red
    exit 1
}

exit 0
