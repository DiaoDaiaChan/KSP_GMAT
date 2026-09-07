using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    public enum GMATLintSeverity {
        Info,
        Warning,
        Error
    }

    public class GMATLintIssue {
        public int LineNumber { get; set; }
        public GMATLintSeverity Severity { get; set; }
        public string RuleId { get; set; }
        public string MessageKey { get; set; }
        public string DefaultMessage { get; set; }
        public string Suggestion { get; set; }
        public string RawLine { get; set; }

        public override string ToString() {
            return $"[L{LineNumber}] [{Severity}] {RuleId}: {DefaultMessage}";
        }
    }

    public class GMATLintReport {
        public List<GMATLintIssue> Issues { get; } = new List<GMATLintIssue>();
        public int ErrorCount => Issues.FindAll(i => i.Severity == GMATLintSeverity.Error).Count;
        public int WarningCount => Issues.FindAll(i => i.Severity == GMATLintSeverity.Warning).Count;
        public int InfoCount => Issues.FindAll(i => i.Severity == GMATLintSeverity.Info).Count;
        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarningCount > 0;
        public bool IsClean => Issues.Count == 0;
    }

    /// <summary>
    /// NASA GMAT 脚本前置静态分析与自愈门禁引擎。
    /// 严格遵守 baseline_agent.md 铁律（纯 ASCII、SSOT 命名、Vary 安全上界、Achieve 容差、极值事件仅限 Propagate）。
    /// </summary>
    public static class GMATScriptLinter {
        private static readonly Regex CraftPrefixRegex = new Regex(@"\bSat\d+\.", RegexOptions.Compiled);
        private static readonly Regex VaryOptionRegex = new Regex(@"Vary\s+[A-Za-z0-9_]+\s*\([^=]+=[^,]+,\s*\{([^}]+)\}\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex AchieveRegex = new Regex(@"Achieve\s+[A-Za-z0-9_]+\s*\(\s*([^=]+)=\s*([^,]+)(?:,\s*\{\s*Tolerance\s*=\s*([^}]+)\s*\})?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 极值事件判断统一走 SSOT 单一真值源: GMATSchemaRegistry.Instance.IsExtremumParameter

        public static GMATLintReport Lint(string script) {
            var report = new GMATLintReport();
            if (string.IsNullOrEmpty(script)) {
                report.Issues.Add(new GMATLintIssue {
                    LineNumber = 1,
                    Severity = GMATLintSeverity.Warning,
                    RuleId = "LINT_EMPTY_SCRIPT",
                    MessageKey = "ide_lint_empty",
                    DefaultMessage = "脚本内容为空",
                    Suggestion = "请插入或编写 GMAT 任务脚本"
                });
                return report;
            }

            string[] lines = script.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int targetDepth = 0;
            int finiteBurnDepth = 0;
            int optimizeDepth = 0;
            int targetStartLine = 0;
            int finiteBurnStartLine = 0;
            int optimizeStartLine = 0;

            HashSet<string> declaredObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++) {
                int lineNum = i + 1;
                string rawLine = lines[i];
                string trimmed = rawLine.Trim();

                // 1. 检查非 ASCII 字符 (baseline 2 rule 1)
                for (int c = 0; c < rawLine.Length; c++) {
                    if (rawLine[c] > 127) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Error,
                            RuleId = "LINT_NON_ASCII",
                            MessageKey = "ide_lint_non_ascii",
                            DefaultMessage = $"包含非 ASCII 字符 ('{rawLine[c]}')",
                            Suggestion = "移除中文或特殊字符，GMAT 求解引擎仅接受纯 7-bit US-ASCII",
                            RawLine = rawLine
                        });
                        break;
                    }
                }

                // 跳过纯注释与空行
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("%")) {
                    continue;
                }

                // 剥离行内注释
                string codeLine = trimmed;
                int commentIdx = trimmed.IndexOf('%');
                if (commentIdx >= 0) {
                    codeLine = trimmed.Substring(0, commentIdx).Trim();
                }

                // 2. 检查旧前缀 SatX. (baseline 2 rule 2)
                if (CraftPrefixRegex.IsMatch(codeLine)) {
                    report.Issues.Add(new GMATLintIssue {
                        LineNumber = lineNum,
                        Severity = GMATLintSeverity.Warning,
                        RuleId = "LINT_CRAFT_PREFIX",
                        MessageKey = "ide_lint_craft_prefix",
                        DefaultMessage = "检测到带有数字后缀的旧航天器前缀 (如 Sat1. / Sat0.)",
                        Suggestion = "按基线规约统一修正为 Sat.",
                        RawLine = rawLine
                    });
                }

                // 3. 检查大括号匹配
                int openBraces = CountChars(codeLine, '{');
                int closeBraces = CountChars(codeLine, '}');
                if (openBraces != closeBraces && !codeLine.StartsWith("Target ", StringComparison.OrdinalIgnoreCase) && !codeLine.StartsWith("Propagate ", StringComparison.OrdinalIgnoreCase) && !codeLine.StartsWith("Vary ", StringComparison.OrdinalIgnoreCase) && !codeLine.StartsWith("Achieve ", StringComparison.OrdinalIgnoreCase)) {
                    report.Issues.Add(new GMATLintIssue {
                        LineNumber = lineNum,
                        Severity = GMATLintSeverity.Warning,
                        RuleId = "LINT_UNBALANCED_BRACES",
                        MessageKey = "ide_lint_braces",
                        DefaultMessage = "行内大括号可能未闭合",
                        Suggestion = "检查 { } 配对",
                        RawLine = rawLine
                    });
                }

                // 检查分号 (GMAT 指令行必须以分号结尾，控制块除外)
                if (!codeLine.EndsWith(";") && !codeLine.EndsWith("{") && !codeLine.EndsWith("}") &&
                    !codeLine.StartsWith("BeginMissionSequence", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("Target ", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("EndTarget", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("Optimize ", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("EndOptimize", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("BeginScript", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("EndScript", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("If ", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("Else", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("EndIf", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("While ", StringComparison.OrdinalIgnoreCase) &&
                    !codeLine.StartsWith("EndWhile", StringComparison.OrdinalIgnoreCase)) {
                    report.Issues.Add(new GMATLintIssue {
                        LineNumber = lineNum,
                        Severity = GMATLintSeverity.Info,
                        RuleId = "LINT_MISSING_SEMICOLON",
                        MessageKey = "ide_lint_semicolon",
                        DefaultMessage = "语句末尾缺失分号 ';'",
                        Suggestion = "在指令末尾追加分号 ';'",
                        RawLine = rawLine
                    });
                }

                // 4. 资源定义阶段 (Create ...)
                if (codeLine.StartsWith("Create ", StringComparison.OrdinalIgnoreCase)) {
                    var parts = codeLine.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3) {
                        for (int p = 2; p < parts.Length; p++) {
                            string objName = parts[p].Trim().TrimEnd(';');
                            if (!string.IsNullOrEmpty(objName) && !objName.Equals(";")) {
                                declaredObjects.Add(objName);
                            }
                        }
                    }
                }

                // 5. Target / EndTarget 块跟踪
                if (codeLine.StartsWith("Target ", StringComparison.OrdinalIgnoreCase)) {
                    targetDepth++;
                    targetStartLine = lineNum;
                } else if (codeLine.StartsWith("EndTarget", StringComparison.OrdinalIgnoreCase)) {
                    targetDepth--;
                    if (targetDepth < 0) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Error,
                            RuleId = "LINT_ORPHAN_ENDTARGET",
                            MessageKey = "ide_lint_orphan_endtarget",
                            DefaultMessage = "孤立的 EndTarget 指令，没有配对的 Target 块",
                            Suggestion = "移除多余的 EndTarget",
                            RawLine = rawLine
                        });
                        targetDepth = 0;
                    }
                }

                // 6. BeginFiniteBurn / EndFiniteBurn 块跟踪
                if (codeLine.StartsWith("BeginFiniteBurn ", StringComparison.OrdinalIgnoreCase)) {
                    finiteBurnDepth++;
                    finiteBurnStartLine = lineNum;
                } else if (codeLine.StartsWith("EndFiniteBurn", StringComparison.OrdinalIgnoreCase)) {
                    finiteBurnDepth--;
                    if (finiteBurnDepth < 0) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Error,
                            RuleId = "LINT_ORPHAN_ENDFINITEBURN",
                            MessageKey = "ide_lint_orphan_endburn",
                            DefaultMessage = "孤立的 EndFiniteBurn 指令，没有配对的 BeginFiniteBurn",
                            Suggestion = "移除多余的 EndFiniteBurn",
                            RawLine = rawLine
                        });
                        finiteBurnDepth = 0;
                    }
                }

                // 6.1 Optimize / EndOptimize 块跟踪
                if (codeLine.StartsWith("Optimize ", StringComparison.OrdinalIgnoreCase)) {
                    optimizeDepth++;
                    optimizeStartLine = lineNum;
                } else if (codeLine.StartsWith("EndOptimize", StringComparison.OrdinalIgnoreCase)) {
                    optimizeDepth--;
                    if (optimizeDepth < 0) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Error,
                            RuleId = "LINT_ORPHAN_ENDOPTIMIZE",
                            MessageKey = "ide_lint_orphan_endoptimize",
                            DefaultMessage = "孤立的 EndOptimize 指令，没有配对的 Optimize 块",
                            Suggestion = "移除多余的 EndOptimize",
                            RawLine = rawLine
                        });
                        optimizeDepth = 0;
                    }
                }

                // 6.2 检查机动属性被错误附加航天器前缀 (如 Sat.TLI_Burn.Element1)
                if (Regex.IsMatch(codeLine, @"\bSat\.[A-Za-z0-9_]+\.Element[123]\b", RegexOptions.IgnoreCase)) {
                    report.Issues.Add(new GMATLintIssue {
                        LineNumber = lineNum,
                        Severity = GMATLintSeverity.Error,
                        RuleId = "LINT_BURN_PREFIX_ERROR",
                        MessageKey = "ide_lint_burn_prefix_err",
                        DefaultMessage = "机动矢量属性不可附加航天器前缀 (如 Sat.TLI_Burn.Element1 会导致 GMAT 解释器抛出 Element1 异常)",
                        Suggestion = "剥离 Sat. 前缀，直接使用 <BurnName>.Element1",
                        RawLine = rawLine
                    });
                }

                // 7. Vary 语句校验 (baseline 2 rule 3)
                if (codeLine.StartsWith("Vary ", StringComparison.OrdinalIgnoreCase)) {
                    var vMatch = VaryOptionRegex.Match(codeLine);
                    if (vMatch.Success) {
                        string opts = vMatch.Groups[1].Value;
                        double low = double.NaN, up = double.NaN, pert = double.NaN;

                        var lowM = Regex.Match(opts, @"Lower\s*=\s*([^,}]+)");
                        if (lowM.Success) double.TryParse(lowM.Groups[1].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out low);

                        var upM = Regex.Match(opts, @"Upper\s*=\s*([^,}]+)");
                        if (upM.Success) double.TryParse(upM.Groups[1].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out up);

                        var pertM = Regex.Match(opts, @"Perturbation\s*=\s*([^,}]+)");
                        if (pertM.Success) double.TryParse(pertM.Groups[1].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pert);

                        if (!double.IsNaN(low) && !double.IsNaN(up)) {
                            if (up <= low) {
                                report.Issues.Add(new GMATLintIssue {
                                    LineNumber = lineNum,
                                    Severity = GMATLintSeverity.Error,
                                    RuleId = "LINT_VARY_BOUNDS",
                                    MessageKey = "ide_lint_vary_bounds",
                                    DefaultMessage = $"Vary 上界 Upper ({up}) 必须严格大于下界 Lower ({low})",
                                    Suggestion = $"修正 Upper 为更大值 (如 {Math.Max(up, low + 100)})",
                                    RawLine = rawLine
                                });
                            }
                        }

                        if (!double.IsNaN(pert) && pert <= 0) {
                            report.Issues.Add(new GMATLintIssue {
                                LineNumber = lineNum,
                                Severity = GMATLintSeverity.Error,
                                RuleId = "LINT_VARY_PERT",
                                MessageKey = "ide_lint_vary_pert",
                                DefaultMessage = "Vary 扰动量 Perturbation 必须大于 0",
                                Suggestion = "按基线将 Perturbation 修正为 0.0001",
                                RawLine = rawLine
                            });
                        }
                    }
                }

                // 8. Achieve 语句校验 (baseline 2 rule 3 & baseline 3 极值校验)
                if (codeLine.StartsWith("Achieve ", StringComparison.OrdinalIgnoreCase)) {
                    var achMatch = AchieveRegex.Match(codeLine);
                    if (achMatch.Success) {
                        string targetParam = achMatch.Groups[1].Value.Trim();
                        string tolStr = achMatch.Groups.Count > 3 ? achMatch.Groups[3].Value.Trim() : "";

                        // (A) 铁律 3：极值事件仅用于 Propagate，严禁用于 Achieve (SSOT 接入 GMATSchemaRegistry)
                        if (GMATSchemaRegistry.Instance.IsExtremumParameter(targetParam)) {
                            report.Issues.Add(new GMATLintIssue {
                                LineNumber = lineNum,
                                Severity = GMATLintSeverity.Error,
                                RuleId = "LINT_ACHIEVE_EXTREMA",
                                MessageKey = "ide_lint_achieve_extrema",
                                DefaultMessage = $"Achieve 不支持极值/交点事件 '{targetParam}'，这会导致求解器奇异",
                                Suggestion = "极值事件只能作为 Propagate 停机条件；在 Achieve 中应使用具体物理量（如 SMA、RMAG、Latitude = 0 等）",
                                RawLine = rawLine
                            });
                        }

                        // (B) 铁律 2：Achieve Tolerance 必须大于 0
                        double recTol = GMATSchemaRegistry.Instance.GetAuthoritativeTolerance(targetParam, 0.1);
                        if (string.IsNullOrEmpty(tolStr) || (double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double tolVal) && tolVal <= 0)) {
                            report.Issues.Add(new GMATLintIssue {
                                LineNumber = lineNum,
                                Severity = GMATLintSeverity.Error,
                                RuleId = "LINT_ACHIEVE_TOL",
                                MessageKey = "ide_lint_achieve_tol",
                                DefaultMessage = $"Achieve 容差未设置或小于等于 0 (官方权威推荐容差: {recTol})",
                                Suggestion = $"必须设置有效容差，建议使用官方权威容差: {{Tolerance = {recTol.ToString("G", CultureInfo.InvariantCulture)}}}",
                                RawLine = rawLine
                            });
                        }
                    }
                }

                // 9. Propagate 语句校验 (停机条件 Node 自愈检查)
                if (codeLine.StartsWith("Propagate ", StringComparison.OrdinalIgnoreCase)) {
                    if (Regex.IsMatch(codeLine, @"\b(?:Node|AscendingNode|DescendingNode)\b", RegexOptions.IgnoreCase)) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Warning,
                            RuleId = "LINT_NODE_EXTREMA",
                            MessageKey = "ide_lint_node_extrema",
                            DefaultMessage = "检测到赤道交点停机别名 (Node / AscendingNode)，GMAT 官方语法规范要求使用 Latitude = 0.0",
                            Suggestion = "修改为 Sat.<CentralBody>.Latitude = 0.0",
                            RawLine = rawLine
                        });
                    }
                }

                // 10. Python 函数调用校验
                if (codeLine.IndexOf("Python.", StringComparison.OrdinalIgnoreCase) >= 0 || codeLine.StartsWith("CallPythonFunction", StringComparison.OrdinalIgnoreCase)) {
                    if (!codeLine.Contains("(") || !codeLine.Contains(")")) {
                        report.Issues.Add(new GMATLintIssue {
                            LineNumber = lineNum,
                            Severity = GMATLintSeverity.Error,
                            RuleId = "LINT_PYTHON_SYNTAX",
                            MessageKey = "ide_lint_python_syntax",
                            DefaultMessage = "Python 脚本调用缺少圆括号或参数列表不完整",
                            Suggestion = "确保语法为 [outputs] = Python.Module.Function(inputs); 或 Python.Module.Function(inputs);",
                            RawLine = rawLine
                        });
                    }
                }
            }

            // 10. 文件末尾检查块未闭合
            if (targetDepth > 0) {
                report.Issues.Add(new GMATLintIssue {
                    LineNumber = lines.Length,
                    Severity = GMATLintSeverity.Error,
                    RuleId = "LINT_UNCLOSED_TARGET",
                    MessageKey = "ide_lint_unclosed_target",
                    DefaultMessage = $"存在未闭合的 Target 求解块 (起始于第 {targetStartLine} 行)",
                    Suggestion = "在任务序列合适位置添加 'EndTarget;' 闭合求解组"
                });
            }

            if (finiteBurnDepth > 0) {
                report.Issues.Add(new GMATLintIssue {
                    LineNumber = lines.Length,
                    Severity = GMATLintSeverity.Error,
                    RuleId = "LINT_UNCLOSED_FINITE_BURN",
                    MessageKey = "ide_lint_unclosed_burn",
                    DefaultMessage = $"存在未闭合的 BeginFiniteBurn 点火块 (起始于第 {finiteBurnStartLine} 行)",
                    Suggestion = "在推进后添加 'EndFiniteBurn BurnName(Sat);' 闭合点火"
                });
            }

            if (optimizeDepth > 0) {
                report.Issues.Add(new GMATLintIssue {
                    LineNumber = lines.Length,
                    Severity = GMATLintSeverity.Error,
                    RuleId = "LINT_UNCLOSED_OPTIMIZE",
                    MessageKey = "ide_lint_unclosed_optimize",
                    DefaultMessage = $"存在未闭合的 Optimize 优化块 (起始于第 {optimizeStartLine} 行)",
                    Suggestion = "在任务序列合适位置添加 'EndOptimize;' 闭合优化求解块"
                });
            }

            // 11. 检查是否存在 3D 轨迹视口 (OrbitView)
            if (!Regex.IsMatch(script, @"\bCreate\s+(?:OrbitView|GroundTrackPlot|OpenGlPlot)\b", RegexOptions.IgnoreCase)) {
                report.Issues.Add(new GMATLintIssue {
                    LineNumber = 1,
                    Severity = GMATLintSeverity.Info,
                    RuleId = "LINT_NO_OUTPUT_ORBITVIEW",
                    MessageKey = "ide_lint_no_orbitview",
                    DefaultMessage = "脚本中未定义 3D 轨道视图 (OrbitView)，在 GMAT GUI 中运行将无法直观观察航天器轨迹与变轨过程",
                    Suggestion = "建议使用 FixScript 自动补全当前 SOI 与目标天体的 3D 轨道视图"
                });
            }

            return report;
        }

        /// <summary>
        /// 一键自愈修复脚本：执行纯 ASCII 转换、航天器前缀规范化、机动矢量前缀剥离、Vary 安全上下界修复、Achieve 容差补齐、Node 别名映射与自动补全 3D 轨道视图。
        /// 严格保证经过 SanitizeGmatScript 的输出无违规 (baseline_agent.md 铁律)。
        /// </summary>
        public static string FixScript(string rawScript) {
            if (string.IsNullOrEmpty(rawScript)) return rawScript;
            string s = GMATClient.SanitizeGmatScript(rawScript);
            // 剥离被错误附加航天器前缀的机动矢量属性: Sat.TLI_Burn.Element1 -> TLI_Burn.Element1
            s = Regex.Replace(s, @"\bSat\.([A-Za-z0-9_]+\.Element[123])\b", "$1", RegexOptions.IgnoreCase);
            // 自动为缺失容差的 Achieve 指令补齐官方权威推荐容差
            s = Regex.Replace(s, @"Achieve\s+([A-Za-z0-9_]+)\s*\(\s*([^=]+)=\s*([^,);]+)\s*\);", m => {
                string dc = m.Groups[1].Value.Trim();
                string param = m.Groups[2].Value.Trim();
                string val = m.Groups[3].Value.Trim();
                double authTol = GMATSchemaRegistry.Instance.GetAuthoritativeTolerance(param, 0.1);
                return $"Achieve {dc}({param} = {val}, {{Tolerance = {authTol.ToString("G", CultureInfo.InvariantCulture)}}});";
            }, RegexOptions.IgnoreCase);
            // 自动为没有 Output 的脚本注入当前 SOI 与目标天体的 3D OrbitView 视图
            s = EnsureOrbitView(s);
            return s;
        }

        /// <summary>
        /// 自动为没有 Output / OrbitView 的脚本注入当前 SOI 或符合条件的 3D 轨道视图，便于直接在 GMAT GUI 中观看
        /// </summary>
        public static string EnsureOrbitView(string script) {
            if (string.IsNullOrWhiteSpace(script)) return script;
            if (Regex.IsMatch(script, @"\bCreate\s+(?:OrbitView|GroundTrackPlot|OpenGlPlot)\b", RegexOptions.IgnoreCase)) {
                return script; // 已有视图，无需注入
            }

            // 1. 探测航天器名称
            string satName = "Sat";
            var satMatch = Regex.Match(script, @"\bCreate\s+Spacecraft\s+([A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase);
            if (satMatch.Success) satName = satMatch.Groups[1].Value.Trim();

            // 2. 探测主中心天体 (如 Earth, Saturn, Luna)
            string centralBody = "Earth";
            var cbMatch = Regex.Match(script, @"\.CentralBody\s*=\s*([A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase);
            if (cbMatch.Success) {
                centralBody = cbMatch.Groups[1].Value.Trim();
            } else {
                var csMatch = Regex.Match(script, @"\.CoordinateSystem\s*=\s*([A-Za-z0-9_]+)MJ2000Eq\b", RegexOptions.IgnoreCase);
                if (csMatch.Success) centralBody = csMatch.Groups[1].Value.Trim();
            }
            centralBody = GMATSchemaRegistry.Instance.NormalizeCelestialName(centralBody);

            // 3. 扫描脚本中涉及的其他引力天体 (Secondary Bodies)
            HashSet<string> secondaryBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pmMatch = Regex.Match(script, @"\.PointMasses\s*=\s*\{([^}]+)\}", RegexOptions.IgnoreCase);
            if (pmMatch.Success) {
                string[] pms = pmMatch.Groups[1].Value.Split(',');
                foreach (var pm in pms) {
                    string norm = GMATSchemaRegistry.Instance.NormalizeCelestialName(pm.Trim());
                    if (!string.IsNullOrEmpty(norm) && !norm.Equals(centralBody, StringComparison.OrdinalIgnoreCase) && !norm.Equals("Sun", StringComparison.OrdinalIgnoreCase)) {
                        secondaryBodies.Add(norm);
                    }
                }
            }

            var origMatches = Regex.Matches(script, @"\.Origin\s*=\s*([A-Za-z0-9_]+)\b", RegexOptions.IgnoreCase);
            foreach (Match om in origMatches) {
                string norm = GMATSchemaRegistry.Instance.NormalizeCelestialName(om.Groups[1].Value.Trim());
                if (!string.IsNullOrEmpty(norm) && !norm.Equals(centralBody, StringComparison.OrdinalIgnoreCase) && !norm.Equals("Sun", StringComparison.OrdinalIgnoreCase)) {
                    secondaryBodies.Add(norm);
                }
            }

            var bodyDotMatches = Regex.Matches(script, $@"\b{satName}\.([A-Za-z0-9_]+)\.(?:RMAG|Altitude|SMA|ECC|INC|Latitude)\b", RegexOptions.IgnoreCase);
            foreach (Match bm in bodyDotMatches) {
                string norm = GMATSchemaRegistry.Instance.NormalizeCelestialName(bm.Groups[1].Value.Trim());
                if (!string.IsNullOrEmpty(norm) && !norm.Equals(centralBody, StringComparison.OrdinalIgnoreCase) && !norm.Equals("Sun", StringComparison.OrdinalIgnoreCase)) {
                    secondaryBodies.Add(norm);
                }
            }

            // 4. 构造 OrbitView 定义块与配套 CoordinateSystem
            var viewSb = new StringBuilder();
            viewSb.AppendLine("% -------------------------------------------------------------");
            viewSb.AppendLine("% 3D Visualization OrbitView (Auto-synthesized for GMAT GUI viewing)");
            viewSb.AppendLine("% -------------------------------------------------------------");

            string mainCs = $"{centralBody}MJ2000Eq";
            if (!Regex.IsMatch(script, $@"\bCreate\s+CoordinateSystem\s+{mainCs}\b", RegexOptions.IgnoreCase) && !GMATSchemaRegistry.Instance.IsDefaultBuiltinCoordSystem(mainCs)) {
                viewSb.AppendLine($"Create CoordinateSystem {mainCs};");
                viewSb.AppendLine($"{mainCs}.Origin = {centralBody};");
                viewSb.AppendLine($"{mainCs}.Axes = MJ2000Eq;\n");
            }

            bool isEarthMoon = centralBody.Equals("Earth", StringComparison.OrdinalIgnoreCase) && (secondaryBodies.Contains("Luna") || secondaryBodies.Contains("Moon"));
            double[] vpv;
            if (centralBody.Equals("Sun", StringComparison.OrdinalIgnoreCase)) vpv = new double[] { 0, 0, 500000000 };
            else if (centralBody.Equals("Jupiter", StringComparison.OrdinalIgnoreCase) || centralBody.Equals("Saturn", StringComparison.OrdinalIgnoreCase)) vpv = new double[] { 0, 0, 4000000 };
            else if (isEarthMoon) vpv = new double[] { 0, 0, 500000 };
            else if (centralBody.Equals("Earth", StringComparison.OrdinalIgnoreCase)) vpv = new double[] { 0, 0, 35000 };
            else if (centralBody.Equals("Luna", StringComparison.OrdinalIgnoreCase) || centralBody.Equals("Moon", StringComparison.OrdinalIgnoreCase)) vpv = new double[] { 0, 0, 10000 };
            else vpv = new double[] { 0, 0, 30000 };

            var primaryAdd = new List<string> { satName, centralBody };
            foreach (var b in secondaryBodies) {
                if (!primaryAdd.Contains(b)) primaryAdd.Add(b);
            }

            string mainViewName = isEarthMoon ? "EarthView" : "DefaultOrbitView";
            viewSb.AppendLine($"Create OrbitView {mainViewName};");
            viewSb.AppendLine($"{mainViewName}.SolverIterations = Current;");
            viewSb.AppendLine($"{mainViewName}.UpperLeft = [ 0.002 0 ];");
            viewSb.AppendLine($"{mainViewName}.Size = [ 0.8 0.85 ];");
            viewSb.AppendLine($"{mainViewName}.RelativeZOrder = 0;");
            viewSb.AppendLine($"{mainViewName}.Maximized = false;");
            viewSb.AppendLine($"{mainViewName}.Add = {{{string.Join(", ", primaryAdd)}}};");
            viewSb.AppendLine($"{mainViewName}.CoordinateSystem = {mainCs};");
            string drawArray = string.Join(" ", Enumerable.Repeat("true", primaryAdd.Count));
            viewSb.AppendLine($"{mainViewName}.DrawObject = [ {drawArray} ];");
            viewSb.AppendLine($"{mainViewName}.DataCollectFrequency = 1;");
            viewSb.AppendLine($"{mainViewName}.UpdatePlotFrequency = 50;");
            viewSb.AppendLine($"{mainViewName}.NumPointsToRedraw = 0;");
            viewSb.AppendLine($"{mainViewName}.ShowPlot = true;");
            viewSb.AppendLine($"{mainViewName}.MaxPlotPoints = 30000;");
            viewSb.AppendLine($"{mainViewName}.ViewPointReference = {centralBody};");
            viewSb.AppendLine($"{mainViewName}.ViewPointVector = [ {vpv[0]} {vpv[1]} {vpv[2]} ];");
            viewSb.AppendLine($"{mainViewName}.ViewDirection = {centralBody};");
            viewSb.AppendLine($"{mainViewName}.ViewUpCoordinateSystem = {mainCs};");
            viewSb.AppendLine($"{mainViewName}.ViewUpAxis = Z;\n");

            string targetBody = secondaryBodies.FirstOrDefault(b => b.Equals("Luna", StringComparison.OrdinalIgnoreCase) || b.Equals("Moon", StringComparison.OrdinalIgnoreCase) || b.Equals("Mars", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(targetBody)) {
                string tgtCs = $"{targetBody}MJ2000Eq";
                if (!Regex.IsMatch(script, $@"\bCreate\s+CoordinateSystem\s+{tgtCs}\b", RegexOptions.IgnoreCase) && !GMATSchemaRegistry.Instance.IsDefaultBuiltinCoordSystem(tgtCs)) {
                    viewSb.AppendLine($"Create CoordinateSystem {tgtCs};");
                    viewSb.AppendLine($"{tgtCs}.Origin = {targetBody};");
                    viewSb.AppendLine($"{tgtCs}.Axes = MJ2000Eq;\n");
                }
                string tgtViewName = $"{targetBody}View";
                double[] tgtVpv = targetBody.Equals("Luna", StringComparison.OrdinalIgnoreCase) || targetBody.Equals("Moon", StringComparison.OrdinalIgnoreCase)
                    ? new double[] { 0, 0, 8000 } : new double[] { 0, 0, 20000 };

                viewSb.AppendLine($"Create OrbitView {tgtViewName};");
                viewSb.AppendLine($"{tgtViewName}.SolverIterations = Current;");
                viewSb.AppendLine($"{tgtViewName}.UpperLeft = [ 0.4 0 ];");
                viewSb.AppendLine($"{tgtViewName}.Size = [ 0.5 0.5 ];");
                viewSb.AppendLine($"{tgtViewName}.RelativeZOrder = 0;");
                viewSb.AppendLine($"{tgtViewName}.Maximized = false;");
                viewSb.AppendLine($"{tgtViewName}.Add = {{{satName}, {targetBody}}};");
                viewSb.AppendLine($"{tgtViewName}.CoordinateSystem = {tgtCs};");
                viewSb.AppendLine($"{tgtViewName}.DrawObject = [ true true ];");
                viewSb.AppendLine($"{tgtViewName}.DataCollectFrequency = 1;");
                viewSb.AppendLine($"{tgtViewName}.UpdatePlotFrequency = 50;");
                viewSb.AppendLine($"{tgtViewName}.NumPointsToRedraw = 0;");
                viewSb.AppendLine($"{tgtViewName}.ShowPlot = true;");
                viewSb.AppendLine($"{tgtViewName}.MaxPlotPoints = 30000;");
                viewSb.AppendLine($"{tgtViewName}.ViewPointReference = {targetBody};");
                viewSb.AppendLine($"{tgtViewName}.ViewPointVector = [ {tgtVpv[0]} {tgtVpv[1]} {tgtVpv[2]} ];");
                viewSb.AppendLine($"{tgtViewName}.ViewDirection = {targetBody};");
                viewSb.AppendLine($"{tgtViewName}.ViewUpCoordinateSystem = {tgtCs};");
                viewSb.AppendLine($"{tgtViewName}.ViewUpAxis = Z;\n");
            }

            int seqIdx = script.IndexOf("BeginMissionSequence;", StringComparison.OrdinalIgnoreCase);
            if (seqIdx >= 0) {
                return script.Insert(seqIdx, viewSb.ToString() + "\n");
            } else {
                return script + "\n" + viewSb.ToString();
            }
        }

        private static int CountChars(string text, char target) {
            int count = 0;
            for (int i = 0; i < text.Length; i++) {
                if (text[i] == target) count++;
            }
            return count;
        }
    }
}
