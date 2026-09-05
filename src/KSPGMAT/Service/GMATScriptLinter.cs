using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

        // 极值事件关键字（只能用于 Propagate 停机条件，严禁用于 Achieve）
        private static readonly string[] ExtremaKeywords = new string[] {
            "Apoapsis", "Periapsis", "ApoapsisAlt", "PeriapsisAlt", "Node", "AscendingNode", "DescendingNode"
        };

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
            int targetStartLine = 0;
            int finiteBurnStartLine = 0;

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

                        // (A) 铁律 3：极值事件仅用于 Propagate，严禁用于 Achieve
                        foreach (var ext in ExtremaKeywords) {
                            if (targetParam.IndexOf(ext, StringComparison.OrdinalIgnoreCase) >= 0) {
                                report.Issues.Add(new GMATLintIssue {
                                    LineNumber = lineNum,
                                    Severity = GMATLintSeverity.Error,
                                    RuleId = "LINT_ACHIEVE_EXTREMA",
                                    MessageKey = "ide_lint_achieve_extrema",
                                    DefaultMessage = $"Achieve 不支持极值/交点事件 '{ext}'，这会导致求解器奇异",
                                    Suggestion = "极值事件只能作为 Propagate 停机条件；在 Achieve 中应使用具体物理量（如 SMA、RMAG、Latitude = 0 等）",
                                    RawLine = rawLine
                                });
                                break;
                            }
                        }

                        // (B) 铁律 2：Achieve Tolerance 必须大于 0
                        if (string.IsNullOrEmpty(tolStr) || (double.TryParse(tolStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double tolVal) && tolVal <= 0)) {
                            report.Issues.Add(new GMATLintIssue {
                                LineNumber = lineNum,
                                Severity = GMATLintSeverity.Error,
                                RuleId = "LINT_ACHIEVE_TOL",
                                MessageKey = "ide_lint_achieve_tol",
                                DefaultMessage = "Achieve 容差未设置或小于等于 0",
                                Suggestion = "必须设置有效容差（距离建议 0.5~1 km，速度建议 0.01 km/s，角度建议 0.01°）",
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

            return report;
        }

        /// <summary>
        /// 一键自愈修复脚本：执行纯 ASCII 转换、航天器前缀规范化、Vary 安全上下界修复、Achieve 容差补齐与 Node 别名映射。
        /// 严格保证经过 SanitizeGmatScript 的输出无违规。
        /// </summary>
        public static string FixScript(string rawScript) {
            if (string.IsNullOrEmpty(rawScript)) return rawScript;
            return GMATClient.SanitizeGmatScript(rawScript);
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
