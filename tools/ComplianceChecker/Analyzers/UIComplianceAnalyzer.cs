using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    public class UIComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        public UIComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            Visit(tree.GetRoot());
            return issues;
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) {
            base.VisitObjectCreationExpression(node);

            if (fileName == "GMATMainWindow.Styles.cs") return;

            string typeName = node.Type.ToString();
            if (typeName == "GUIStyle") {
                // 检查是否是在方法内临时分配 (Parent 是 LocalDeclarationStatementSyntax)
                var localDecl = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
                if (localDecl != null) {
                    var lineSpan = node.GetLocation().GetLineSpan();
                    issues.Add(new ComplianceIssue {
                        Severity = IssueSeverity.WARN,
                        Category = "UI-GC-STYLE",
                        FilePath = filePath,
                        FileName = fileName,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Message = "在 UI 渲染路径中检测到局部变量 'new GUIStyle' 实例化，造成每帧 GC 内存抖动",
                        Snippet = localDecl.ToString().Trim(),
                        Suggestion = "请在 GMATMainWindow.Styles.cs 中预热，或使用私有字段进行懒加载单例缓存"
                    });
                }
            }
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node) {
            base.VisitMemberAccessExpression(node);

            string expr = node.ToString();
            if (expr is "GUI.skin.button" or "GUI.skin.box") {
                // 如果是赋值或者 ?? 默认回退则跳过
                if (node.Parent is BinaryExpressionSyntax bin && bin.OperatorToken.IsKind(SyntaxKind.QuestionQuestionToken)) {
                    return;
                }

                var lineSpan = node.GetLocation().GetLineSpan();
                issues.Add(new ComplianceIssue {
                    Severity = IssueSeverity.INFO,
                    Category = "UI-SKIN-STYLE",
                    FilePath = filePath,
                    FileName = fileName,
                    Line = lineSpan.StartLinePosition.Line + 1,
                    Column = lineSpan.StartLinePosition.Character + 1,
                    Message = $"检测到直接裸用原生 {expr} 控件",
                    Snippet = node.Parent?.ToString().Trim() ?? expr,
                    Suggestion = "建议替换为 primaryButtonStyle / secondaryButtonStyle / nestedCardBoxStyle 等统一设计规范样式"
                });
            }
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node) {
            base.VisitInvocationExpression(node);

            if (fileName == "GMATMainWindow.Widgets.cs") return;

            string invokedMethod = node.Expression.ToString();
            if (invokedMethod == "GUILayout.TextField") {
                string fullCall = node.ToString();
                if (fullCall.Contains("SMA") || fullCall.Contains("ECC") || fullCall.Contains("INC") || fullCall.Contains("Alt") || fullCall.Contains("Mass") || fullCall.Contains("Rad") || fullCall.Contains("Step")) {
                    var lineSpan = node.GetLocation().GetLineSpan();
                    issues.Add(new ComplianceIssue {
                        Severity = IssueSeverity.INFO,
                        Category = "UI-DELAYED-INPUT",
                        FilePath = filePath,
                        FileName = fileName,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        Column = lineSpan.StartLinePosition.Character + 1,
                        Message = "检测到裸用 GUILayout.TextField 处理数值参数输入",
                        Snippet = fullCall,
                        Suggestion = "建议使用 GMATMainWindow.Widgets 中的 DelayedFloatField / DelayedDoubleField 避免编辑时光标跳动"
                    });
                }
            }
        }
    }
}
