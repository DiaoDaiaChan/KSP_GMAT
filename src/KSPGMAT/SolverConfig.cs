using System;
using System.Collections.Generic;

namespace KSPGMAT {
    [Serializable]
    public class SolverConfig {
        public string name = "DefaultDC";
        public string algorithm = "NewtonRaphson"; // NewtonRaphson, Broyden, ModifiedBroyden
        public string derivativeMethod = "ForwardDifference"; // ForwardDifference, CentralDifference, BackwardDifference
        public int maxIterations = 50;
        public string solveMode = "Solve"; // Solve, Calculate, RunInitialGuess
        public string exitMode = "SaveAndContinue"; // SaveAndContinue, DiscardAndContinue, Stop
        public string reportStyle = "Normal"; // Normal, Concise, Verbose, Debug
        public bool showProgress = true;
        public bool allowScaleSetting = true;
        public bool isDefault = false;

        public SolverConfig Clone() {
            return (SolverConfig)this.MemberwiseClone();
        }

        /// <summary>
        /// 生成默认求解器预设。
        /// 基线 7 说明: 此处为引导期兜底预设 (NewtonRaphson/Broyden 均为 GMAT 原生算法)，
        /// 权威清单以 gmat_capabilities.json 动态加载为准 (见 GMATParameterRegistry.AvailableSolvers)。
        /// </summary>
        public static Dictionary<string, SolverConfig> CreateDefaultPresets() {
            var dict = new Dictionary<string, SolverConfig>();

            // 1. DefaultDC: Industrial standard NewtonRaphson Differential Corrector
            var def = new SolverConfig {
                name = "DefaultDC",
                algorithm = "NewtonRaphson",
                derivativeMethod = "ForwardDifference",
                maxIterations = 50,
                solveMode = "Solve",
                exitMode = "SaveAndContinue",
                reportStyle = "Normal",
                showProgress = true,
                isDefault = true
            };
            dict["DefaultDC"] = def;

            // 2. PrecisionDC: CentralDifference High-Accuracy Differential Corrector
            var prec = new SolverConfig {
                name = "PrecisionDC",
                algorithm = "NewtonRaphson",
                derivativeMethod = "CentralDifference",
                maxIterations = 100,
                solveMode = "Solve",
                exitMode = "SaveAndContinue",
                reportStyle = "Normal",
                showProgress = true,
                isDefault = true
            };
            dict["PrecisionDC"] = prec;

            // 3. BroydenDC: Quasi-Newton Broyden Secant Method Corrector
            var broy = new SolverConfig {
                name = "BroydenDC",
                algorithm = "Broyden",
                derivativeMethod = "ForwardDifference",
                maxIterations = 75,
                solveMode = "Solve",
                exitMode = "SaveAndContinue",
                reportStyle = "Normal",
                showProgress = true,
                isDefault = true
            };
            dict["BroydenDC"] = broy;

            return dict;
        }

        public string ToJson() {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("{");
            sb.Append($"\"name\": \"{name}\",");
            sb.Append($"\"algorithm\": \"{algorithm}\",");
            sb.Append($"\"derivative_method\": \"{derivativeMethod}\",");
            sb.Append($"\"max_iterations\": {maxIterations},");
            sb.Append($"\"solve_mode\": \"{solveMode}\",");
            sb.Append($"\"exit_mode\": \"{exitMode}\",");
            sb.Append($"\"report_style\": \"{reportStyle}\",");
            sb.Append($"\"show_progress\": {(showProgress ? "true" : "false")}");
            sb.Append("}");
            return sb.ToString();
        }
    }
}
