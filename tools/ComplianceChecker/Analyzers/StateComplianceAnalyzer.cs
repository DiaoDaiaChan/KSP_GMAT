using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    public class StateComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        private static readonly HashSet<string> StateExemptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "GMATStateManager.cs",
            "MGAStateManager.cs",
            "ToolbarRegistration.cs",
            "KSPGMATBridge.cs",
            "PykepBridge.cs",
            "SolarSystemBodies.cs"
        };

        public StateComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            Visit(tree.GetRoot());
            return issues;
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
            base.VisitMemberAccessExpression(node);

            if (StateExemptFiles.Contains(fileName)) return;

            string exprText = node.ToString();
            // 检查 FlightGlobals.ActiveVessel 物理属性裸读
            if (exprText.StartsWith("FlightGlobals.ActiveVessel.") || exprText.Contains(".FlightGlobals.ActiveVessel.")) {
                string memberName = node.Name.Identifier.Text;
                if (memberName is "totalMass" or "orbit" or "GetTotalMass" or "altitude" or "terrainAltitude" or "mainBody" or "FindPartModules") {
                    var lineSpan = node.GetLocation().GetLineSpan();
                    issues.Add(new ComplianceIssue {
                        Severity = IssueSeverity.WARN,
                        Category = "SSOT-BYPASS",
                        FilePath = filePath,
                        FileName = fileName,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Message = $"检测到直接访问 FlightGlobals.ActiveVessel.{memberName}，未走统一 StateManager",
                        Snippet = node.ToString(),
                        Suggestion = "请统一通过 GMATStateManager.Current 或 MGAStateManager.Current 访问飞船物理状态"
                    });
                }
            }
        }

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node) {
            base.VisitFieldDeclaration(node);

            if (StateExemptFiles.Contains(fileName) || fileName == "KSPGMATSettings.cs" || fileName == "GMATMainWindow.Styles.cs") return;

            // 检查 Tab 业务类中私设 public static 状态
            bool isPublic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
            bool isStatic = node.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword));
            bool isConst = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword));
            bool isReadOnly = node.Modifiers.Any(m => m.IsKind(SyntaxKind.ReadOnlyKeyword));

            if (isPublic && isStatic && !isConst && !isReadOnly) {
                foreach (var variable in node.Declaration.Variables) {
                    string varName = variable.Identifier.Text;
                    if (!varName.Contains("Style") && !varName.Contains("Texture") && !varName.StartsWith("On") && varName != "Instance" && varName != "Lock") {
                        var lineSpan = variable.GetLocation().GetLineSpan();
                        issues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.WARN,
                            Category = "FREE-STATE",
                            FilePath = filePath,
                            FileName = fileName,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Message = $"检测到游离的 public static 状态变量 '{varName}'",
                            Snippet = node.ToString().Trim(),
                            Suggestion = "跨组件或持久化状态应集中归入 GMATStateManager / MGAStateManager 托管"
                        });
                    }
                }
            }
        }
    }
}
