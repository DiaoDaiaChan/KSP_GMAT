using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    public class DynamicsComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        public DynamicsComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            Visit(tree.GetRoot());
            return issues;
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node) {
            base.VisitBinaryExpression(node);

            // 检查对特定天体名称的硬编码等值判断 (== "Earth" 或 == "Kerbin")
            if (node.IsKind(SyntaxKind.EqualsExpression) || node.IsKind(SyntaxKind.NotEqualsExpression)) {
                string left = node.Left.ToString();
                string right = node.Right.ToString();

                if (left.Contains("centralBody") || left.Contains("planetName") || left.Contains("bodyName") || left.Contains("originBody")) {
                    if (right is "\"Earth\"" or "\"Kerbin\"" or "\"Sun\"" or "\"Moon\"" or "\"Mun\"") {
                        var lineSpan = node.GetLocation().GetLineSpan();
                        issues.Add(new ComplianceIssue {
                            Severity = IssueSeverity.INFO,
                            Category = "CELESTIAL-BRANCH",
                            FilePath = filePath,
                            FileName = fileName,
                            Line = lineSpan.StartLinePosition.Line + 1,
                            Column = lineSpan.StartLinePosition.Character + 1,
                            Message = $"检测到对特定天体名称的直接特化判断分支: {node}",
                            Snippet = node.ToString(),
                            Suggestion = "建议通过 SolarSystemCatalog 或 GMATSchemaRegistry 动态读取天体物理属性，以兼容 RSS / OPM 等星球包"
                        });
                    }
                }
            }
        }
    }
}
