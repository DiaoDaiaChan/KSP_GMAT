using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KSPGMAT.MGA;
using KSPGMAT.Model;

namespace KSPGMAT.Service {
    /// <summary>
    /// GMAT 真实太阳系多重引力助推脚本自动编译器
    /// (GMAT Solar System MGA B-Plane Targeting Script Compiler)
    /// </summary>
    public static class GMATMGAScriptGenerator {
        
        /// <summary>
        /// 将 MGA 差分进化最优方案编译为 NASA GMAT 标准深空借力脚本
        /// </summary>
        public static string CompileToGmatScript(MGASolution solution) {
            if (solution == null || solution.Legs == null || solution.Legs.Count == 0) return "% No MGA Solution Data";

            var sb = new StringBuilder();
            sb.AppendLine("% =============================================================");
            sb.AppendLine("% NASA GMAT Mission Script - Interplanetary MGA Gravity Assist");
            sb.AppendLine($"% Flyby Chain: {solution.FlybyChainSummary}");
            sb.AppendLine($"% Total Delta-V: {solution.TotalDeltaVKms * 1000.0:F1} m/s | Total Flight Time: {solution.TotalFlightDays:F1} Days");
            sb.AppendLine("% Compiled by KSP_GMAT ESA PyKEP Native Engine");
            sb.AppendLine("% =============================================================\n");

            if (GMATClient.HasFullSpiceEphemeris) {
                sb.AppendLine("% NASA JPL SPICE Ephemeris (Covering 1550-2650 AD, zero-offset full direct)");
                sb.AppendLine("GMAT SolarSystem.EphemerisSource = 'SPICE';");
                sb.AppendLine("GMAT Mars.NAIFId = 4;");
                sb.AppendLine("GMAT Jupiter.NAIFId = 5;");
                sb.AppendLine("GMAT Saturn.NAIFId = 6;");
                sb.AppendLine("GMAT Uranus.NAIFId = 7;");
                sb.AppendLine("GMAT Neptune.NAIFId = 8;");
                sb.AppendLine("GMAT Pluto.NAIFId = 9;\n");
            }

            var leg0 = solution.Legs[0];
            string depBody = leg0.FromBody;

            sb.AppendLine("% -------------------------------------------------------------");
            sb.AppendLine($"% Primary Interplanetary Departure Flight Parameters ({leg0.FromBody} -> {leg0.ToBody})");
            sb.AppendLine($"% Departure Phase Angle (相角)    : {leg0.PhaseAngleDeg:F2} deg");
            sb.AppendLine($"% Hyperbolic Ejection Angle (出射角): {leg0.EjectionAngleDeg:F2} deg");
            sb.AppendLine($"% Ejection Asymptote LAN (出射升交点): {leg0.EjectionLANDeg:F2} deg");
            sb.AppendLine($"% Ejection Inclination (出射倾角)  : {leg0.EjectionInclinationDeg:F2} deg");
            sb.AppendLine($"% Characteristic Energy C3 (特征能量): {leg0.C3Kms2:F3} km^2/s^2");
            sb.AppendLine($"% Hyperbolic Excess Speed (v_inf) : {leg0.VInfDepartureKms.magnitude:F4} km/s");
            sb.AppendLine($"% Departure Ejection Delta-V (脱离): {leg0.EjectionDeltaVKms * 1000.0:F1} m/s");
            sb.AppendLine("% -------------------------------------------------------------\n");

            sb.AppendLine("Create Variable PhaseAngle EjectionAngle EjectionLAN EjectionInc C3_Energy V_Inf;");
            sb.AppendLine($"PhaseAngle = {leg0.PhaseAngleDeg.ToString("F4", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"EjectionAngle = {leg0.EjectionAngleDeg.ToString("F4", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"EjectionLAN = {leg0.EjectionLANDeg.ToString("F4", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"EjectionInc = {leg0.EjectionInclinationDeg.ToString("F4", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"C3_Energy = {leg0.C3Kms2.ToString("F4", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"V_Inf = {leg0.VInfDepartureKms.magnitude.ToString("F4", CultureInfo.InvariantCulture)};\n");

            // Spacecraft definition
            sb.AppendLine("Create Spacecraft Sat;");
            sb.AppendLine("Sat.DateFormat = UTCGregorian;");
            sb.AppendLine($"Sat.Epoch = '{GMATClient.FormatGmatSafeEpoch(leg0.DepartureUT)}';");
            sb.AppendLine("Sat.CoordinateSystem = SunMJ2000Eq;");
            sb.AppendLine("Sat.DisplayStateType = Cartesian;");
            sb.AppendLine($"Sat.X = {leg0.DeparturePosKm.x.ToString("F6", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.Y = {leg0.DeparturePosKm.y.ToString("F6", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.Z = {leg0.DeparturePosKm.z.ToString("F6", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.VX = {leg0.DepartureVelKms.x.ToString("F6", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.VY = {leg0.DepartureVelKms.y.ToString("F6", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.VZ = {leg0.DepartureVelKms.z.ToString("F6", CultureInfo.InvariantCulture)};");
            var sc = GMATStateManager.Current?.Spacecraft;
            double dryMass = sc != null && sc.DryMassKg > 0 ? sc.DryMassKg : 1000.0;
            double dragArea = sc != null && sc.DragArea > 0 ? sc.DragArea : 15.0;
            double cd = sc != null && sc.Cd > 0 ? sc.Cd : 2.2;
            double srpArea = sc != null && sc.SRPArea > 0 ? sc.SRPArea : 15.0;
            double cr = sc != null && sc.Cr > 0 ? sc.Cr : 1.8;

            sb.AppendLine($"Sat.DryMass = {dryMass.ToString("F3", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.DragArea = {dragArea.ToString("F3", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.Cd = {cd.ToString("F3", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.SRPArea = {srpArea.ToString("F3", CultureInfo.InvariantCulture)};");
            sb.AppendLine($"Sat.Cr = {cr.ToString("F3", CultureInfo.InvariantCulture)};\n");

            // Deep Space Propagator
            sb.AppendLine("Create ForceModel DeepSpace_ForceModel;");
            sb.AppendLine("DeepSpace_ForceModel.CentralBody = Sun;");
            sb.AppendLine("DeepSpace_ForceModel.PointMasses = {Sun, Earth, Venus, Mars, Jupiter, Saturn};");
            sb.AppendLine("DeepSpace_ForceModel.Drag = None;");
            sb.AppendLine("DeepSpace_ForceModel.SRP = On;");
            sb.AppendLine("DeepSpace_ForceModel.ErrorControl = RSSStep;\n");

            sb.AppendLine("Create Propagator DeepSpaceProp;");
            sb.AppendLine("DeepSpaceProp.FM = DeepSpace_ForceModel;");
            sb.AppendLine("DeepSpaceProp.Type = PrinceDormand78;");
            sb.AppendLine("DeepSpaceProp.InitialStepSize = 60;");
            sb.AppendLine("DeepSpaceProp.Accuracy = 1e-11;");
            sb.AppendLine("DeepSpaceProp.MinStep = 0.001;");
            sb.AppendLine("DeepSpaceProp.MaxStep = 86400;\n");

            // Differential Corrector
            sb.AppendLine("Create DifferentialCorrector DC_MGA;");
            sb.AppendLine("DC_MGA.ShowProgress = true;");
            sb.AppendLine("DC_MGA.ReportStyle = Normal;");
            sb.AppendLine("DC_MGA.MaximumIterations = 50;\n");

            // Trajectory Report File
            sb.AppendLine("Create ReportFile TrajReport;");
            sb.AppendLine("TrajReport.Filename = 'TrajReport.txt';");
            sb.AppendLine("TrajReport.WriteHeaders = false;\n");

            // Define Impulsive Burn Objects for all Maneuver Legs
            for (int k = 0; k < solution.Legs.Count; k++) {
                var leg = solution.Legs[k];
                string burnName = (k == 0) ? "Burn_Ejection" : $"Burn_Flyby_{k}";
                sb.AppendLine($"Create ImpulsiveBurn {burnName};");
                sb.AppendLine($"{burnName}.CoordinateSystem = Local;");
                sb.AppendLine($"{burnName}.Origin = {leg.FromBody};");
                sb.AppendLine($"{burnName}.Axes = VNC;");
                sb.AppendLine($"{burnName}.Element1 = {(leg.ProgradeMs / 1000.0).ToString("F6", CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{burnName}.Element2 = {(leg.NormalMs / 1000.0).ToString("F6", CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{burnName}.Element3 = {(leg.RadialMs / 1000.0).ToString("F6", CultureInfo.InvariantCulture)};");
                sb.AppendLine($"{burnName}.DecrementMass = true;");
                sb.AppendLine($"{burnName}.Isp = 310;\n");
            }

            // Capture burn at destination
            if (solution.InsertionDeltaVKms > 0.001) {
                var lastLeg = solution.Legs.Last();
                sb.AppendLine("Create ImpulsiveBurn Burn_Insertion;");
                sb.AppendLine("Burn_Insertion.CoordinateSystem = Local;");
                sb.AppendLine($"Burn_Insertion.Origin = {lastLeg.ToBody};");
                sb.AppendLine("Burn_Insertion.Axes = VNC;");
                sb.AppendLine($"Burn_Insertion.Element1 = {(-solution.InsertionDeltaVKms / 1000.0).ToString("F6", CultureInfo.InvariantCulture)};");
                sb.AppendLine("Burn_Insertion.Element2 = 0.0;");
                sb.AppendLine("Burn_Insertion.Element3 = 0.0;");
                sb.AppendLine("Burn_Insertion.DecrementMass = true;");
                sb.AppendLine("Burn_Insertion.Isp = 310;\n");
            }

            // Mission Sequence
            sb.AppendLine("% -------------------------------------------------------------");
            sb.AppendLine("% Mission Sequence Execution");
            sb.AppendLine("% -------------------------------------------------------------");
            sb.AppendLine("BeginMissionSequence;\n");
            sb.AppendLine("Report TrajReport Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ Sat.Sun.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.TotalMass;\n");

            for (int k = 0; k < solution.Legs.Count; k++) {
                var leg = solution.Legs[k];
                string burnName = (k == 0) ? "Burn_Ejection" : $"Burn_Flyby_{k}";
                
                if (k == 0 || leg.FlybyDeltaVKms > 0.001) {
                    sb.AppendLine($"% Maneuver #{k + 1}: {leg.FromBody} (ΔV: {leg.DeltaVKms * 1000.0:F1} m/s)");
                    sb.AppendLine($"Maneuver {burnName}(Sat);");
                    sb.AppendLine("Report TrajReport Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ Sat.Sun.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.TotalMass;\n");
                }

                sb.AppendLine($"% Transfer Leg #{k + 1}: {leg.FromBody} -> {leg.ToBody} ({leg.ToFDays:F1} Days)");
                sb.AppendLine($"Propagate DeepSpaceProp(Sat) {{Sat.ElapsedDays = {leg.ToFDays.ToString("F3", CultureInfo.InvariantCulture)}}};");
                sb.AppendLine("Report TrajReport Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ Sat.Sun.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.TotalMass;\n");
            }

            if (solution.InsertionDeltaVKms > 0.001) {
                var lastLeg = solution.Legs.Last();
                sb.AppendLine($"% Destination Orbit Insertion: {lastLeg.ToBody} (ΔV: {solution.InsertionDeltaVKms:F1} m/s)");
                sb.AppendLine("Maneuver Burn_Insertion(Sat);");
                sb.AppendLine("Report TrajReport Sat.ElapsedSecs Sat.X Sat.Y Sat.Z Sat.VX Sat.VY Sat.VZ Sat.Sun.Altitude Sat.SMA Sat.ECC Sat.INC Sat.RAAN Sat.AOP Sat.TA Sat.TotalMass;\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 导出 MGA 方案为预设脚本模板，存入 GameData/KSPGMAT/templates/
        /// </summary>
        public static string ExportToTemplatesDirectory(MGASolution solution, string templateName = null) {
            try {
                if (string.IsNullOrEmpty(templateName)) {
                    string cleanChain = string.Join("_", solution.Legs.Select(l => l.FromBody)).Replace(" ", "");
                    templateName = $"07_MGA_SolarSystem_{cleanChain}_{solution.Legs.Last().ToBody}.script";
                }

                string scriptContent = CompileToGmatScript(solution);
                string templateDir = GMATTemplateManager.GetPrimaryTemplatesDirectory();
                string outPath = Path.Combine(templateDir, templateName);

                File.WriteAllText(outPath, scriptContent, new UTF8Encoding(false));
                GMATTemplateManager.RefreshTemplates(); // Trigger instant dynamic library reload
                return outPath;
            } catch (Exception ex) {
                KSPGMATLog.Error($"[GMATMGAScriptGenerator] Failed to export MGA template: {ex.Message}");
                return null;
            }
        }
    }
}
