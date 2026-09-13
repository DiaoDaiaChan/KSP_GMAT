using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker.Analyzers {
    /// <summary>
    /// 游离日志合规分析器 (Log Compliance Analyzer)
    /// 基于 Roslyn AST 抽象语法树深度遍历，拦截全工程代码中散落裸写的 UnityEngine.Debug 与 Console 日志输出，
    /// 强制所有日志统一经由 KSPGMATLog 核心中枢分发，确保 KSP 游戏内控制台与 Logs/KSPGMAT.log 导出文件同步且线程安全。
    /// </summary>
    public class LogComplianceAnalyzer : CSharpSyntaxWalker {
        private readonly string filePath;
        private readonly string fileName;
        private readonly List<ComplianceIssue> issues = new List<ComplianceIssue>();

        // 统一日志实现本体豁免
        private static readonly HashSet<string> LogExemptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "KSPGMATLog.cs"
        };

        private static readonly HashSet<string> UnityDebugMethods = new HashSet<string>(StringComparer.Ordinal) {
            "Log",
            "LogWarning",
            "LogError",
            "LogException",
            "LogAssertion",
            "LogFormat",
            "LogWarningFormat",
            "LogErrorFormat"
        };

        private static readonly HashSet<string> ConsoleWriteMethods = new HashSet<string>(StringComparer.Ordinal) {
            "Write",
            "WriteLine"
        };

        public LogComplianceAnalyzer(string filePath) {
            this.filePath = filePath;
            this.fileName = Path.GetFileName(filePath);
        }

        public List<ComplianceIssue> Analyze(SyntaxTree tree) {
            issues.Clear();
            if (LogExemptFiles.Contains(fileName)) return issues;
            Visit(tree.GetRoot());
            return issues;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node) {
            base.VisitInvocationExpression(node);

            if (LogExemptFiles.Contains(fileName)) return;

            // 1. 检查成员方法调用表达式，如 Debug.Log(...), Console.WriteLine(...)
            if (node.Expression is MemberAccessExpressionSyntax memberAccess) {
                string methodName = memberAccess.Name.Identifier.Text;
                string targetExpr = memberAccess.Expression.ToString().Trim();

                // 检查 UnityEngine.Debug 或 Debug 上的日志调用
                if (UnityDebugMethods.Contains(methodName)) {
                    if (targetExpr == "Debug" || targetExpr == "UnityEngine.Debug" ||
                        targetExpr.EndsWith(".Debug", StringComparison.Ordinal)) {
                        ReportIssue(node, $"检测到游离原生日志调用 '{memberAccess}'，违反统一日志系统规范",
                            "请统一通过 KSPGMATLog (如 KSPGMATLog.Info / Warn / Error 或 ILogger) 记录日志，严禁直接调用 UnityEngine.Debug");
                        return;
                    }
                }

                // 检查 Console.Write / Console.WriteLine / Console.Error.WriteLine
                if (ConsoleWriteMethods.Contains(methodName)) {
                    if (targetExpr == "Console" || targetExpr == "System.Console" ||
                        targetExpr == "Console.Error" || targetExpr == "System.Console.Error" ||
                        targetExpr.EndsWith(".Console", StringComparison.Ordinal) ||
                        targetExpr.EndsWith(".Console.Error", StringComparison.Ordinal)) {
                        ReportIssue(node, $"检测到游离控制台裸写调用 '{memberAccess}'，违反统一日志系统规范",
                            "请统一通过 KSPGMATLog (如 KSPGMATLog.Info / Debug / Warn / Error) 记录日志，严禁裸写控制台输出");
                        return;
                    }
                }
            }
            // 2. 检查 MonoBehaviour 的 print(...) 裸调用
            else if (node.Expression is IdentifierNameSyntax identifier) {
                if (identifier.Identifier.Text == "print" && node.ArgumentList.Arguments.Count > 0) {
                    ReportIssue(node, $"检测到游离 MonoBehaviour.print(...) 裸调用，违反统一日志系统规范",
                        "请统一通过 KSPGMATLog 记录日志，严禁调用 MonoBehaviour.print");
                }
            }
        }

        private void ReportIssue(InvocationExpressionSyntax node, string message, string suggestion) {
            var lineSpan = node.GetLocation().GetLineSpan();
            issues.Add(new ComplianceIssue {
                Severity = IssueSeverity.ERROR,
                Category = "ORPHAN-LOG",
                FilePath = filePath,
                FileName = fileName,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                Message = message,
                Snippet = node.ToString(),
                Suggestion = suggestion
            });
        }
    }
}
