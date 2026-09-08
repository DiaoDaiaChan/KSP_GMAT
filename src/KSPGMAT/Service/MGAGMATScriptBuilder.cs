using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// GMAT 行星际多天体引力助推 (MGA) 任务脚本自动化构建引擎。
    /// 遵循 baseline_agent.md 铁律（纯 ASCII、SSOT 命名、Vary 安全上界、Achieve 容差、分段动力学力模型），
    /// 将 MGA 求解出的最优借力轨道（包括切入方向、飞掠高度/近拱点半径、B平面参数及各航段）
    /// 自动转换为可直接在 GMAT 官方软件中打开并执行高精度数值积分打靶的完整脚本。
    /// </summary>
    public static class MGAGMATScriptBuilder {
        /// <summary>
        /// 将 MGASolution 一键转换为合规且高保真的 GMAT 官方工程脚本
        /// </summary>
        public static string GenerateMGAScript(MGASolution solution, MGAConfig config = null, string satName = "Sat") {
            if (solution == null || solution.Legs == null || solution.Legs.Count == 0) {
                return "% Error: MGASolution is null or empty.";
            }

            var cfg = config ?? MGAStateManager.Current.Config;
            MGAFlybyDynamicsHelper.EnrichSolutionFlybyDynamics(solution);

            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            string depBody = solution.Legs[0].FromBody;
            string destBody = solution.Legs.Last().ToBody;
            double depEpochUt = solution.DepartureUT;
            string depUtcStr = GMATClient.FormatUtcGregorian(depEpochUt);

            var depPlanet = SolarSystemCatalog.GetPlanet(depBody);
            var destPlanet = SolarSystemCatalog.GetPlanet(destBody);
            double depRadius = depPlanet != null ? depPlanet.RadiusKm : 6378.14;
            double destRadius = destPlanet != null ? destPlanet.RadiusKm : 6051.8;
            double depMu = depPlanet != null ? depPlanet.Mu : 398600.4;
            double destMu = destPlanet != null ? destPlanet.Mu : 324859.0;

            // 收集所有涉及的行星天体名称
            var uniqueBodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { depBody, destBody };
            for (int i = 0; i < solution.Legs.Count; i++) {
                uniqueBodies.Add(solution.Legs[i].FromBody);
                uniqueBodies.Add(solution.Legs[i].ToBody);
            }

            // -----------------------------------------------------------------
            // 1. 脚本头部注释与元数据 (纯 ASCII 字符)
            // -----------------------------------------------------------------
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("% NASA GMAT Interplanetary Multiple Gravity Assist (MGA) Script");
            sb.AppendLine($"% Mission Sequence: {depBody} -> {string.Join(" -> ", solution.Legs.Select(l => l.ToBody))}");
            sb.AppendLine($"% Total Flight Time: {solution.TotalFlightDays:F1} days | Total DeltaV: {solution.TotalDeltaVKms * 1000.0:F1} m/s");
            sb.AppendLine("% Generated automatically by KSP_GMAT Autonomous Trajectory Engine");
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine();

            // -----------------------------------------------------------------
            // 2. 航天器实体定义 (Spacecraft Definition)
            // -----------------------------------------------------------------
            sb.AppendLine($"Create Spacecraft {satName};");
            sb.AppendLine($"{satName}.DateFormat = UTCGregorian;");
            sb.AppendLine($"{satName}.Epoch = '{depUtcStr}';");
            sb.AppendLine($"{satName}.CoordinateSystem = {depBody}MJ2000Eq;");
            sb.AppendLine($"{satName}.DisplayStateType = Keplerian;");

            double parkAlt = cfg.DeparturePeAKm > 0 ? cfg.DeparturePeAKm : MGADefaults.DepartureAltitudeKm;
            double smaPark = depRadius + parkAlt;
            sb.AppendLine(string.Format(inv, "{0}.SMA = {1:F1};", satName, smaPark));
            sb.AppendLine(string.Format(inv, "{0}.ECC = 0.001;", satName));
            sb.AppendLine(string.Format(inv, "{0}.INC = {1:F2};", satName, cfg.DepartureInclinationDeg > 0 ? cfg.DepartureInclinationDeg : 28.5));
            sb.AppendLine(string.Format(inv, "{0}.RAAN = 0.0;", satName));
            sb.AppendLine(string.Format(inv, "{0}.AOP = 0.0;", satName));
            sb.AppendLine(string.Format(inv, "{0}.TA = 0.0;", satName));
            sb.AppendLine();

            // -----------------------------------------------------------------
            // 3. 统一多级坐标系 (Coordinate Systems)
            // -----------------------------------------------------------------
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("% Coordinate Systems Definition");
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("Create CoordinateSystem SunMJ2000Eq;");
            sb.AppendLine("SunMJ2000Eq.Origin = Sun;");
            sb.AppendLine("SunMJ2000Eq.Axes = MJ2000Eq;");
            sb.AppendLine();

            foreach (var bName in uniqueBodies) {
                if (bName.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                    bName.Equals("Sol", StringComparison.OrdinalIgnoreCase)) continue;

                sb.AppendLine($"Create CoordinateSystem {bName}MJ2000Eq;");
                sb.AppendLine($"{bName}MJ2000Eq.Origin = {bName};");
                sb.AppendLine($"{bName}MJ2000Eq.Axes = MJ2000Eq;");
                sb.AppendLine();
            }

            // -----------------------------------------------------------------
            // 4. 分段动力学环境与推进器 (Force Models & Propagators)
            // -----------------------------------------------------------------
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("% Force Models and Numerical Propagators (Multi-scale dynamics)");
            sb.AppendLine("%----------------------------------------------------------------------");

            // 4.1 日心深空巡航力模型 (包含了大行星质点摄动与太阳光压)
            sb.AppendLine("Create ForceModel DeepSpace_FM;");
            sb.AppendLine("DeepSpace_FM.CentralBody = Sun;");
            sb.AppendLine("DeepSpace_FM.PointMasses = {Sun, Mercury, Venus, Earth, Luna, Mars, Jupiter, Saturn, Uranus, Neptune};");
            sb.AppendLine("DeepSpace_FM.SRP = On;");
            sb.AppendLine();
            sb.AppendLine("Create Propagator DeepSpaceProp;");
            sb.AppendLine("DeepSpaceProp.FM = DeepSpace_FM;");
            sb.AppendLine("DeepSpaceProp.Type = PrinceDormand78;");
            sb.AppendLine("DeepSpaceProp.Accuracy = 1e-11;");
            sb.AppendLine("DeepSpaceProp.InitialStepSize = 60.0;");
            sb.AppendLine("DeepSpaceProp.MinStep = 0.001;");
            sb.AppendLine("DeepSpaceProp.MaxStep = 86400.0;");
            sb.AppendLine();

            // 4.2 各近邻行星高精度力模型
            foreach (var bName in uniqueBodies) {
                if (bName.Equals("Sun", StringComparison.OrdinalIgnoreCase) ||
                    bName.Equals("Sol", StringComparison.OrdinalIgnoreCase)) continue;

                sb.AppendLine($"Create ForceModel Near{bName}_FM;");
                sb.AppendLine($"Near{bName}_FM.CentralBody = {bName};");
                sb.AppendLine($"Near{bName}_FM.PrimaryBodies = {{{bName}}};");
                sb.AppendLine($"Near{bName}_FM.PointMasses = {{Sun}};");
                sb.AppendLine($"Near{bName}_FM.SRP = On;");
                sb.AppendLine();
                sb.AppendLine($"Create Propagator Near{bName}Prop;");
                sb.AppendLine($"Near{bName}Prop.FM = Near{bName}_FM;");
                sb.AppendLine($"Near{bName}Prop.Type = PrinceDormand78;");
                sb.AppendLine($"Near{bName}Prop.Accuracy = 1e-11;");
                sb.AppendLine();
            }

            // -----------------------------------------------------------------
            // 5. 目标器与推进机动定义 (Differential Correctors & Maneuvers)
            // -----------------------------------------------------------------
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("% Impulsive Maneuvers & Differential Correctors (Targeting Architecture)");
            sb.AppendLine("%----------------------------------------------------------------------");

            // 5.1 始发脱离脉冲
            sb.AppendLine("Create ImpulsiveBurn EscapeBurn;");
            sb.AppendLine("EscapeBurn.CoordinateSystem = Local;");
            sb.AppendLine($"EscapeBurn.Origin = {depBody};");
            sb.AppendLine("EscapeBurn.Axes = VNB;");
            sb.AppendLine(string.Format(inv, "EscapeBurn.Element1 = {0:F3};", solution.LaunchDeltaVKms));
            sb.AppendLine("EscapeBurn.Element2 = 0.0;");
            sb.AppendLine("EscapeBurn.Element3 = 0.0;");
            sb.AppendLine();

            // 5.2 各飞掠段的深空 TCM 与目标器
            for (int k = 0; k < solution.Legs.Count - 1; k++) {
                string fbName = solution.Legs[k].ToBody;
                string tcmName = $"TCM_{k + 1}_{fbName}";
                string dcName = $"DC_{k + 1}_{fbName}";

                sb.AppendLine($"Create ImpulsiveBurn {tcmName};");
                sb.AppendLine($"{tcmName}.CoordinateSystem = Local;");
                sb.AppendLine($"{tcmName}.Origin = Sun;");
                sb.AppendLine($"{tcmName}.Axes = VNB;");
                sb.AppendLine($"{tcmName}.Element1 = 0.0;");
                sb.AppendLine($"{tcmName}.Element2 = 0.0;");
                sb.AppendLine($"{tcmName}.Element3 = 0.0;");
                sb.AppendLine();

                sb.AppendLine($"Create DifferentialCorrector {dcName};");
                sb.AppendLine($"{dcName}.Algorithm = NewtonRaphson;");
                sb.AppendLine($"{dcName}.MaximumIterations = 50;");
                sb.AppendLine();
            }

            // 5.3 终点到达机动 (MOI 制动捕获)
            sb.AppendLine("Create ImpulsiveBurn MOI;");
            sb.AppendLine("MOI.CoordinateSystem = Local;");
            sb.AppendLine($"MOI.Origin = {destBody};");
            sb.AppendLine("MOI.Axes = VNB;");
            sb.AppendLine("MOI.Element1 = -1.5;");
            sb.AppendLine("MOI.Element2 = 0.0;");
            sb.AppendLine("MOI.Element3 = 0.0;");
            sb.AppendLine();

            sb.AppendLine("Create DifferentialCorrector DC_Capture;");
            sb.AppendLine("DC_Capture.Algorithm = NewtonRaphson;");
            sb.AppendLine("DC_Capture.MaximumIterations = 50;");
            sb.AppendLine();

            // -----------------------------------------------------------------
            // 6. 任务控制序列主流程 (Mission Sequence Command Pipeline)
            // -----------------------------------------------------------------
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("% Mission Sequence Command Pipeline");
            sb.AppendLine("%----------------------------------------------------------------------");
            sb.AppendLine("BeginMissionSequence;");
            sb.AppendLine();

            // Phase 1: 始发加速逃逸
            sb.AppendLine($"% Phase 1: {depBody} Departure and Trans-planetary Injection");
            sb.AppendLine($"Maneuver EscapeBurn({satName});");
            sb.AppendLine($"Propagate Near{depBody}Prop({satName}) {{{satName}.ElapsedDays = 3.0}};");
            sb.AppendLine();

            // Phase 2: 各中间航段巡航、TCM 打靶与借力飞掠
            for (int k = 0; k < solution.Legs.Count - 1; k++) {
                var leg = solution.Legs[k];
                string fbName = leg.ToBody;
                string tcmName = $"TCM_{k + 1}_{fbName}";
                string dcName = $"DC_{k + 1}_{fbName}";
                var fbPl = SolarSystemCatalog.GetPlanet(fbName);
                double fbSoi = fbPl != null && fbPl.SoiKm > 0 && !double.IsInfinity(fbPl.SoiKm) ? fbPl.SoiKm : 1000000.0;
                double midCruiseDays = Math.Max(5.0, leg.ToFDays * 0.5);

                double targetRp = leg.FlybyPeriapsisRadiusKm > 0 ? leg.FlybyPeriapsisRadiusKm : ((fbPl?.RadiusKm ?? 6000.0) + 300.0);
                double targetBt = leg.BPlane != null ? leg.BPlane.BDotTKm : 0.0;

                sb.AppendLine($"% ------------------------------------------------------------------");
                sb.AppendLine($"% Phase {k + 2}: Flyby Assist at {fbName} (Targeting Periapsis & B-Plane)");
                sb.AppendLine($"% ------------------------------------------------------------------");
                sb.AppendLine($"% Cruise toward {fbName} until TCM point");
                sb.AppendLine(string.Format(inv, "Propagate DeepSpaceProp({0}) {{{0}.ElapsedDays = {1:F1}}};", satName, midCruiseDays));
                sb.AppendLine();

                sb.AppendLine($"Target {dcName} {{SolveMode = Solve, ExitMode = SaveAndContinue}};");
                sb.AppendLine($"    Vary {dcName}({tcmName}.Element1 = 0.0, {{Perturbation = 1e-4, MaxStep = 0.1, Lower = -50.0, Upper = 50.0}});");
                sb.AppendLine($"    Vary {dcName}({tcmName}.Element2 = 0.0, {{Perturbation = 1e-4, MaxStep = 0.1, Lower = -50.0, Upper = 50.0}});");
                sb.AppendLine($"    Vary {dcName}({tcmName}.Element3 = 0.0, {{Perturbation = 1e-4, MaxStep = 0.1, Lower = -50.0, Upper = 50.0}});");
                sb.AppendLine($"    ");
                sb.AppendLine($"    Maneuver {tcmName}({satName});");
                sb.AppendLine($"    ");
                sb.AppendLine(string.Format(inv, "    Propagate DeepSpaceProp({0}) {{{0}.{1}.RMAG = {2:F0}}};", satName, fbName, fbSoi));
                sb.AppendLine($"    Propagate Near{fbName}Prop({satName}) {{{satName}.{fbName}.Periapsis}};");
                sb.AppendLine($"    ");
                sb.AppendLine(string.Format(inv, "    Achieve {0}({1}.{2}.RMAG = {3:F1}, {{Tolerance = 1.0}});", dcName, satName, fbName, targetRp));
                sb.AppendLine(string.Format(inv, "    Achieve {0}({1}.{2}.BdotT = {3:F1}, {{Tolerance = 5.0}});", dcName, satName, fbName, targetBt));
                sb.AppendLine($"EndTarget;");
                sb.AppendLine();

                sb.AppendLine($"% Flyby passage and exit from {fbName} SOI");
                sb.AppendLine(string.Format(inv, "Propagate Near{0}Prop({1}) {{{1}.{0}.RMAG = {2:F0}}};", fbName, satName, fbSoi));
                sb.AppendLine();
            }

            // Phase 3: 最终转移段与目标天体到达
            int lastIdx = solution.Legs.Count - 1;
            var lastLeg = solution.Legs[lastIdx];
            var destPl = SolarSystemCatalog.GetPlanet(destBody);
            double destSoi = destPl != null && destPl.SoiKm > 0 && !double.IsInfinity(destPl.SoiKm) ? destPl.SoiKm : 1000000.0;
            double destPeA = cfg.DestinationPeAKm > 0 ? cfg.DestinationPeAKm : MGADefaults.DestinationAltitudeKm;
            double destPeR = destRadius + destPeA;

            sb.AppendLine($"% ------------------------------------------------------------------");
            sb.AppendLine($"% Final Phase: Interplanetary Cruise to {destBody} & Arrival");
            sb.AppendLine($"% ------------------------------------------------------------------");
            sb.AppendLine(string.Format(inv, "Propagate DeepSpaceProp({0}) {{{0}.{1}.RMAG = {2:F0}}};", satName, destBody, destSoi));
            sb.AppendLine($"Propagate Near{destBody}Prop({satName}) {{{satName}.{destBody}.Periapsis}};");
            sb.AppendLine();

            sb.AppendLine($"% Orbit Insertion Braking at {destBody}");
            sb.AppendLine("Target DC_Capture {SolveMode = Solve, ExitMode = SaveAndContinue};");
            sb.AppendLine("    Vary DC_Capture(MOI.Element1 = -1.5, {Perturbation = 0.0001, MaxStep = 0.2, Lower = -5.0, Upper = -0.01});");
            sb.AppendLine($"    Maneuver MOI({satName});");
            sb.AppendLine($"    Propagate Near{destBody}Prop({satName}) {{{satName}.{destBody}.Apoapsis}};");
            sb.AppendLine(string.Format(inv, "    Achieve DC_Capture({0}.{1}.RMAG = {2:F1}, {{Tolerance = 5.0}});", satName, destBody, destPeR * 2.5));
            sb.AppendLine("EndTarget;");
            sb.AppendLine();

            string rawScript = sb.ToString();
            // 自动过 Linter 自愈净化与格式校验
            return GMATScriptLinter.FixScript(rawScript);
        }
    }
}
