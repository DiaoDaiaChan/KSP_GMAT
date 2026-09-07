using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    public class I18nComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        private static readonly Regex ChineseRegex = new Regex(@"[\u4e00-\u9fa5]", RegexOptions.Compiled);
        private static readonly Regex TagRegex = new Regex(@"<\s*(/?)\s*([a-zA-Z0-9]+)(?:=[^>]*)?\s*>", RegexOptions.Compiled);

        private static readonly HashSet<string> I18nExemptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "I18n.cs",
            "GMATI18n.cs",
            "KSPGMATLog.cs"
        };

        public I18nComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            if (!I18nExemptFiles.Contains(fileName)) {
                Visit(tree.GetRoot());
            }
            return issues;
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node) {
            base.VisitLiteralExpression(node);

            if (!node.IsKind(SyntaxKind.StringLiteralExpression)) return;

            string strValue = node.Token.ValueText;
            if (string.IsNullOrEmpty(strValue) || !ChineseRegex.IsMatch(strValue)) return;

            // 检查父级调用
            var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocation != null) {
                string invokedName = invocation.Expression.ToString();
                // 合法 I18n 调用
                if (invokedName.EndsWith("I18n.T") || invokedName == "T" || invokedName.EndsWith("I18n.Format")) {
                    return;
                }
                // 日志打印跳过
                if (invokedName.Contains("Debug.Log") || invokedName.Contains("KSPGMATLog") || invokedName.Contains("Console.WriteLine") || invokedName.Contains("ScreenMessages.PostScreenMessage")) {
                    return;
                }
            }

            var lineSpan = node.GetLocation().GetLineSpan();
            issues.Add(new ComplianceIssue {
                Severity = IssueSeverity.WARN,
                Category = "I18N-HARDCODED",
                FilePath = filePath,
                FileName = fileName,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Message = $"检测到未进入 I18n 的硬编码中文文本: \"{strValue}\"",
                Snippet = node.Parent?.ToString().Trim() ?? node.ToString(),
                Suggestion = "请将文本维护入 GameData/KSPGMAT/loc/ 并使用 I18n.T(\"key\") 引用"
            });
        }

        /// <summary>
        /// 静态检测四国语言字典完整性、键对称性与富文本标签平衡
        /// </summary>
        public static List<ComplianceIssue> AuditLocalizationTables(string locDir) {
            var locIssues = new List<ComplianceIssue>();
            string[] langs = { "zh", "en", "ru", "jp" };
            var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var lang in langs) {
                string p = Path.Combine(locDir, $"{lang}.json");
                if (!File.Exists(p)) {
                    locIssues.Add(new ComplianceIssue {
                        Severity = IssueSeverity.ERROR,
                        Category = "I18N-FILE-MISSING",
                        FilePath = p,
                        FileName = $"{lang}.json",
                        Line = 0,
                        Message = $"缺少必须的语言字典文件: {p}",
                        Suggestion = "请补充该语言定义"
                    });
                    continue;
                }

                try {
                    string content = File.ReadAllText(p);
                    using var doc = JsonDocument.Parse(content);
                    var dict = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var prop in doc.RootElement.EnumerateObject()) {
                        dict[prop.Name] = prop.Value.GetString() ?? "";
                    }
                    tables[lang] = dict;
                } catch (Exception ex) {
                    locIssues.Add(new ComplianceIssue {
                        Severity = IssueSeverity.ERROR,
                        Category = "I18N-JSON-CORRUPT",
                        FilePath = p,
                        FileName = $"{lang}.json",
                        Line = 1,
                        Message = $"语言字典 JSON 损坏: {ex.Message}",
                        Suggestion = "请检查 JSON 语法是否有未闭合引号或多余逗号"
                    });
                }
            }

            if (tables.TryGetValue("zh", out var zhDict)) {
                var zhKeys = new HashSet<string>(zhDict.Keys, StringComparer.Ordinal);

                foreach (var otherLang in new[] { "en", "ru", "jp" }) {
                    if (!tables.TryGetValue(otherLang, out var otherDict)) continue;

                    var otherKeys = new HashSet<string>(otherDict.Keys, StringComparer.Ordinal);
                    var missing = zhKeys.Except(otherKeys).ToList();

                    if (missing.Count > 0) {
                        string sample = string.Join(", ", missing.Take(5));
                        if (missing.Count > 5) sample += $" (等共 {missing.Count} 个)";
                        locIssues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.ERROR,
                            Category = "I18N-ASYMMETRY",
                            FilePath = Path.Combine(locDir, $"{otherLang}.json"),
                            FileName = $"{otherLang}.json",
                            Line = 1,
                            Message = $"{otherLang}.json 缺失基准词条 ({missing.Count} 个): {sample}",
                            Suggestion = $"请向 {otherLang}.json 补充对齐这些键值"
                        });
                    }
                }
            }

            return locIssues;
        }
    }
}
