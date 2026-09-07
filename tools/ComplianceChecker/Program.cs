using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using KSPGMAT.Tools.ComplianceChecker.Analyzers;
using KSPGMAT.Tools.ComplianceChecker.Model;

namespace KSPGMAT.Tools.ComplianceChecker {
    public class Program {
        public static int Main(string[] args) {
            Console.OutputEncoding = Encoding.UTF8;

            string projectRoot = "";
            bool strict = false;

            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--project-root" && i + 1 < args.Length) {
                    projectRoot = args[++i];
                } else if (args[i] == "--strict") {
                    strict = true;
                }
            }

            if (string.IsNullOrEmpty(projectRoot)) {
                projectRoot = Directory.GetCurrentDirectory();
            }

            string srcDir = Path.Combine(projectRoot, "src", "KSPGMAT");
            string locDir = Path.Combine(projectRoot, "GameData", "KSPGMAT", "loc");

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("  🛡️  KSP-GMAT 编译级 AST 代码合规自查引擎 (Roslyn Code Compliance Analyzer)");
            Console.WriteLine("==========================================================================================");
            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  源码目录: {srcDir}");
            Console.WriteLine($"  语言目录: {locDir}");
            Console.WriteLine($"  技术架构: Microsoft.CodeAnalysis.CSharp (Roslyn AST 抽象语法树分析)");
            Console.WriteLine($"  基线规范: baseline.md & baseline_agent.md (单一真值源 SSOT / 零硬编码)");
            Console.ResetColor();
            Console.WriteLine();

            if (!Directory.Exists(srcDir)) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ 源码目录未找到: {srcDir}");
                Console.ResetColor();
                return 1;
            }

            // 1. 扫描所有 C# 源码文件 (排除 src/KSPGMAT/obj 和 src/KSPGMAT/bin)
            var allFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories)
                .Where(p => {
                    string rel = p.Substring(srcDir.Length);
                    return !rel.StartsWith(@"\obj\") && !rel.StartsWith("/obj/") &&
                           !rel.StartsWith(@"\bin\") && !rel.StartsWith("/bin/");
                })
                .ToList();

            Console.WriteLine($"  🔍 已索引 C# 源代码文件: {allFiles.Count} 个 (全工程深入穿透，零目录死角)");
            Console.WriteLine();

            var sw = Stopwatch.StartNew();
            var allIssues = new List<ComplianceIssue>();

            // 2. 调度 Roslyn 语法分析器
            foreach (var file in allFiles) {
                string code = File.ReadAllText(file, Encoding.UTF8);
                var tree = CSharpSyntaxTree.ParseText(code, path: file);

                // Analyzer 1: 状态管理与 SSOT
                var stateAnalyzer = new StateComplianceAnalyzer(file);
                allIssues.AddRange(stateAnalyzer.Analyze(tree));

                // Analyzer 2: UI 控件与样式
                var uiAnalyzer = new UIComplianceAnalyzer(file);
                allIssues.AddRange(uiAnalyzer.Analyze(tree));

                // Analyzer 3: 多语言硬编码文本
                var i18nAnalyzer = new I18nComplianceAnalyzer(file);
                allIssues.AddRange(i18nAnalyzer.Analyze(tree));

                // Analyzer 4: 动力学解耦
                var dynAnalyzer = new DynamicsComplianceAnalyzer(file);
                allIssues.AddRange(dynAnalyzer.Analyze(tree));
            }

            // 3. 检测本地化字典完整性
            if (Directory.Exists(locDir)) {
                allIssues.AddRange(I18nComplianceAnalyzer.AuditLocalizationTables(locDir));
            }

            sw.Stop();

            // 4. 统计与分级汇报
            var errors = allIssues.Where(i => i.Severity == IssueSeverity.ERROR).ToList();
            var warns = allIssues.Where(i => i.Severity == IssueSeverity.WARN).ToList();
            var infos = allIssues.Where(i => i.Severity == IssueSeverity.INFO).ToList();

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================================");
            Console.WriteLine("  📋 Roslyn 抽象语法树深度合规审计报告 (AST Summary Report)");
            Console.WriteLine("==========================================================================================");
            Console.ResetColor();

            Console.WriteLine($"  分析文件总数: {allFiles.Count} 个源码文件 + 4 套字典 (用时 {sw.ElapsedMilliseconds} ms)");
            Console.ForegroundColor = errors.Count > 0 ? ConsoleColor.Red : ConsoleColor.Green;
            Console.WriteLine($"  致命违规 (ERROR)  : {errors.Count}");
            Console.ForegroundColor = warns.Count > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.WriteLine($"  建议整改 (WARN)   : {warns.Count}");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  优化提示 (INFO)   : {infos.Count}");
            Console.ResetColor();
            Console.WriteLine();

            // 输出致命错误
            if (errors.Count > 0) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("🚨 阻断性违规项 (CRITICAL ERRORS - 违反基线必须整改):");
                foreach (var err in errors) {
                    Console.WriteLine($"  • [{err.Category}] {err.FileName}:L{err.Line}");
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"    说明: {err.Message}");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"    建议: {err.Suggestion}");
                    Console.ForegroundColor = ConsoleColor.Red;
                }
                Console.ResetColor();
                Console.WriteLine();
            }

            // 分组输出警告
            if (warns.Count > 0) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ 建议整改项 (WARNINGS - 语法树级精准识别):");
                Console.ResetColor();

                var grouped = warns.GroupBy(w => w.Category);
                foreach (var grp in grouped) {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  📁 [{grp.Key}] 共 {grp.Count()} 处");
                    Console.ResetColor();

                    foreach (var issue in grp.Take(3)) {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"    • {issue.FileName}:L{issue.Line} - {issue.Message}");
                        if (!string.IsNullOrEmpty(issue.Snippet)) {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"      语法: {issue.Snippet}");
                        }
                    }
                    if (grp.Count() > 3) {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"      ... 其余 {grp.Count() - 3} 处同类项已收拢");
                    }
                    Console.WriteLine();
                }
            }

            if (errors.Count == 0 && warns.Count == 0) {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("🎉 编译器级 100% 完美合规！语法树内未发现任何游离状态变量、未发现裸露控件与每帧 GC 实例化！");
                Console.ResetColor();
            } else if (errors.Count == 0) {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ 核心基线红线无阻断违规！(建议在日常重构中逐步规范化上述 {warns.Count} 处语法树警告项)");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==========================================================================================");
            Console.ResetColor();
            Console.WriteLine();

            if (strict && errors.Count > 0) {
                return 1;
            }

            return 0;
        }
    }
}
