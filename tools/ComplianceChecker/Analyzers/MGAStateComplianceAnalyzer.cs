using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    public class MGAStateComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        private static readonly HashSet<string> MGAExemptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "MGAStateManager.cs",
            "MGADefaults.cs",
            "SolarSystemBodies.cs",
            "SolarSystemCatalog.cs",
            "PykepBridge.cs"
        };

        public MGAStateComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            Visit(tree.GetRoot());
            return issues;
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node) {
            base.VisitLiteralExpression(node);

            if (MGAExemptFiles.Contains(fileName)) return;

            bool isMgaScope = filePath.IndexOf("MGA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              fileName.StartsWith("TabMGA", StringComparison.OrdinalIgnoreCase) ||
                              fileName.Contains("MGA");

            if (!isMgaScope) return;

            if (node.IsKind(SyntaxKind.NumericLiteralExpression)) {
                string text = node.Token.ValueText;
                if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double val)) {
                    if (Math.Abs(val - 1.496e8) < 1e5 || Math.Abs(val - 149597870.7) < 1.0) {
                        var lineSpan = node.GetLocation().GetLineSpan();
                        issues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.ERROR,
                            Category = "MGA-HARDCODED-CONST",
                            FilePath = filePath,
                            FileName = fileName,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Message = "检测到硬编码的天文单位 (AU) 数值 " + text + "，违反 MGADefaults 规范",
                            Snippet = node.Parent != null ? node.Parent.ToString() : node.ToString(),
                            Suggestion = "请使用 MGADefaults.AU_KM 替代硬编码数值"
                        });
                    } else if (Math.Abs(val - 1.32712440018e11) < 1e6 || Math.Abs(val - 1.3271244e11) < 1e6) {
                        var lineSpan = node.GetLocation().GetLineSpan();
                        issues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.WARN,
                            Category = "MGA-HARDCODED-MU",
                            FilePath = filePath,
                            FileName = fileName,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Message = "检测到硬编码的太阳引力常数 (Sun Mu) 数值 " + text,
                            Snippet = node.Parent != null ? node.Parent.ToString() : node.ToString(),
                            Suggestion = "请使用 MGADefaults.FallbackSunMu 或 MGADefaults.GetBodyMu(\"Sun\") 替代硬编码数值"
                        });
                    }
                }
            }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
            base.VisitFieldDeclaration(node);

            if (MGAExemptFiles.Contains(fileName)) return;

            if (fileName.StartsWith("GMATMainWindow.TabMGA", StringComparison.OrdinalIgnoreCase)) {
                bool isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
                bool isReadOnly = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));

                if (!isConst && !isReadOnly) {
                    foreach (var variable in node.Declaration.Variables) {
                        string varName = variable.Identifier.Text;
                        string typeName = node.Declaration.Type.ToString();
                        if (typeName.Contains("Texture") || typeName.Contains("Style")) continue;
                        var lineSpan = variable.GetLocation().GetLineSpan();
                        issues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.WARN,
                            Category = "MGA-SCATTERED-UI-FIELD",
                            FilePath = filePath,
                            FileName = fileName,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Message = "在 MGA UI 代码中检测到散落的字段 " + varName + "，未托管在 MGAStateManager 中",
                            Snippet = node.ToString().Trim(),
                            Suggestion = "请将此字段迁移至 MGAStateManager.MGAUIInteractionState 或 MGAViewportModel，并在 UI 类中提供委托属性"
                        });
                    }
                }
            }
        }
    }
}