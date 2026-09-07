using System;
using System.Collections.Generic;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// GMAT 统一脚本处理中枢引擎 (Unified GMAT Script Engine)。
    /// 深度整合全量解析 (Parser)、静态语法巡检与基线自愈 (Linter)、全量保真生成 (Generator)，
    /// 遵循 baseline_agent.md 铁律，实现外部原生脚本与 KSP 任务模型的双向无损往返 (Lossless Roundtrip)。
    /// </summary>
    public static class GMATScriptEngine {
        /// <summary>
        /// 全量解析外部脚本文件并自动过 Linter 预净化与模型纳管
        /// </summary>
        public static GMATImportResult ParseFile(string filePath, Vessel vessel = null) {
            return GMATScriptParser.ParseScriptFile(filePath, vessel);
        }

        /// <summary>
        /// 全量解析外部脚本文本并自动过 Linter 预净化与模型纳管
        /// </summary>
        public static GMATImportResult ParseText(string scriptContent, string scriptName = "ImportedScript", Vessel vessel = null) {
            return GMATScriptParser.ParseScriptText(scriptContent, scriptName, vessel);
        }

        /// <summary>
        /// 基于全量底层状态生成合规的原生 GMAT 脚本，并自动通过 Linter 净化闸门
        /// </summary>
        public static string Generate(Vessel vessel, List<MissionStep> steps, string centralBodyOverride = null, Dictionary<string, PropagatorConfig> propConfigsOverride = null) {
            return GMATScriptGenerator.GenerateScript(vessel, steps, centralBodyOverride, propConfigsOverride);
        }

        /// <summary>
        /// 依据 baseline_agent.md 对指定脚本文本执行静态语法规则检查
        /// </summary>
        public static GMATLintReport Lint(string script) {
            return GMATScriptLinter.Lint(script);
        }

        /// <summary>
        /// 对指定脚本文本执行一键基线自愈修复 (纯 ASCII、Sat. 前缀、机动矢量剥离、Vary/Achieve 容差)
        /// </summary>
        public static string Fix(string rawScript) {
            return GMATScriptLinter.FixScript(rawScript);
        }

        /// <summary>
        /// 单步代码透视即时解析引擎 (Two-Way Binding)
        /// </summary>
        public static bool ParseSingleStep(MissionStep step, string lineScript) {
            return GMATScriptParser.ParseSingleStepScript(step, lineScript);
        }

        /// <summary>
        /// 单步代码透视片段生成
        /// </summary>
        public static string GenerateSingleStep(MissionStep step, string satName = null, string centralBody = null) {
            return GMATScriptGenerator.GenerateStepScript(step, satName, centralBody);
        }
    }
}
