using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KSPGMAT.MGA;

namespace KSPGMAT.Model {
    /// <summary>
    /// 标准化储罐数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public class GMATTankModel {
        public string Id { get; set; } = "Tank0";
        public int StageNumber { get; set; } = 0;
        public double FuelMassKg { get; set; } = 0.0;
        public double DryMassKg { get; set; } = 0.0;
        public double VolumeM3 { get; set; } = 1.0;
        public double FuelDensityKgM3 { get; set; } = 1000.0;
        public string PressureModel { get; set; } = "PressureRegulated";
        public bool AllowNegativeFuelMass { get; set; } = true;
        public string PropellantType { get; set; } = "LiquidFuel";
    }

    /// <summary>
    /// 标准化推力器与发动机数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public class GMATThrusterModel {
        public string Id { get; set; } = "MainEngine";
        public string RawTitle { get; set; } = "Main Engine";
        public int StageNumber { get; set; } = 0;
        public double ThrustKN { get; set; } = 100.0;
        public double IspVac { get; set; } = 300.0;
        public string BoundTankId { get; set; } = "Tank0"; // 唯一标准映射 (Tank0, Tank1...)
        public double ThrustDirection1 { get; set; } = 1.0; // V / 切向
        public double ThrustDirection2 { get; set; } = 0.0; // N / 副法向
        public double ThrustDirection3 { get; set; } = 0.0; // B / 径向
        public bool DecrementMass { get; set; } = true;
        public bool IsActiveOrIgnited { get; set; } = false;
        public string PropellantDesc { get; set; } = "LiquidFuel+Oxidizer";
        public double FuelDensityKgM3 { get; set; } = 1000.0;
        public string CoordinateSystem { get; set; } = "Local";
        public string Origin { get; set; } = "Earth";
        public string Axes { get; set; } = "VNB";
        public double DutyCycle { get; set; } = 1.0;
        public double ThrustScaleFactor { get; set; } = 1.0;
        public double GravitationalAccel { get; set; } = 9.80665;
    }

    /// <summary>
    /// 标准化航天器状态数据模型 (Single Source of Truth)
    /// </summary>
    [Serializable]
    public class GMATSpacecraftModel {
        public string Name { get; set; } = "Sat";
        public string DateFormat { get; set; } = "UTCGregorian";
        public string EpochUtc { get; set; } = "01 Jan 1951 12:00:00.000";
        public double UniversalTime { get; set; } = 0.0;
        public string CoordinateSystem { get; set; } = "EarthMJ2000Eq";
        public string DisplayStateType { get; set; } = "Cartesian";
        public double DryMassKg { get; set; } = 850.0; // 官方 GMAT 缺省 850kg
        public double TotalMassKg { get; set; } = 5000.0;
        public double DragArea { get; set; } = 15.0;  // 官方 GMAT 缺省 15m²
        public double Cd { get; set; } = 2.2;         // 官方 GMAT 缺省 2.2
        public double Cr { get; set; } = 1.8;         // 官方 GMAT 缺省 1.8
        public double SRPArea { get; set; } = 15.0;   // 基线 1.2.1: SRPArea 默认 15.0m²
        public Vector3d PosKm { get; set; } = Vector3d.zero;
        public Vector3d VelKms { get; set; } = Vector3d.zero;
        public string CentralBody { get; set; } = "Earth";
    }

    [Serializable]
    public class GMATVariableModel {
        public string Name { get; set; } = "Var1";
        public double InitialValue { get; set; } = 0.0;
        public string Description { get; set; } = I18n.T("sv_desc_scalar");
    }

    [Serializable]
    public class GMATArrayModel {
        public string Name { get; set; } = "Arr1";
        public int Rows { get; set; } = 3;
        public int Cols { get; set; } = 3;
        public string Description { get; set; } = I18n.T("sv_desc_array");
    }

    [Serializable]
    public class GMATStringModel {
        public string Name { get; set; } = "Str1";
        public string Value { get; set; } = "";
        public string Description { get; set; } = I18n.T("sv_desc_string");
    }

    [Serializable]
    public class GMATSolverModel {
        public string Name { get; set; } = "DefaultDC";
        public string Algorithm { get; set; } = "NewtonRaphson";
        public string DerivativeMethod { get; set; } = "ForwardDifference";
        public int MaxIterations { get; set; } = 50;
        public bool ShowProgress { get; set; } = true;
        public string ReportStyle { get; set; } = "Normal";
        public string ReportFile { get; set; } = "";
    }

    [Serializable]
    public class GMATBurnModel {
        public string Name { get; set; } = "Impulsive1";
        public string Type { get; set; } = "ImpulsiveBurn";
        public string Origin { get; set; } = "Earth";
        public string Axes { get; set; } = "VNB";
        public string BoundThruster { get; set; } = "MainEngine";
        public double Element1 { get; set; } = 0.0;
        public double Element2 { get; set; } = 0.0;
        public double Element3 { get; set; } = 0.0;
        public double Isp { get; set; } = 300.0;
        public bool DecrementMass { get; set; } = false;
    }

    [Serializable]
    public class GMATReportFileModel {
        public string Name { get; set; } = "TrajReport";
        public string Filename { get; set; } = "TrajReport.txt";
        public bool WriteHeaders { get; set; } = false;
    }

    /// <summary>
    /// 标准化多级火箭推进与质量预算快照
    /// </summary>
    public class GMATStageBudget {
        public int StageNumber { get; set; } = 0;
        public string EngineTitle { get; set; } = "";
        public double DeltaV { get; set; } = 0.0;
        public double WetMassTonnes { get; set; } = 0.0;
        public double DryMassTonnes { get; set; } = 0.0;
        public double FuelMassTonnes { get; set; } = 0.0;
        public double ThrustKN { get; set; } = 0.0;
        public double IspVac { get; set; } = 0.0;
        public string PropellantDesc { get; set; } = "";
        public double FuelDensityKgM3 { get; set; } = 1000.0;
    }

    /// <summary>
    /// NASA GMAT API 官方参数标准缺省值字典 (Single Source of Default Values)
    /// </summary>
    public static class GMATDefaults {
        public static class Spacecraft {
            public const double DryMassKg = 850.0;
            public const double TotalMassKg = 5000.0;
            public const double DragArea = 15.0;
            public const double Cd = 2.2;
            public const double Cr = 1.8;
            public const double SRPArea = 15.0;   // 基线 1.2.1: SRPArea 默认 15.0m²
            public const string DateFormat = "UTCGregorian";
            public const string CoordinateSystem = "EarthMJ2000Eq";
            public const string DisplayStateType = "Cartesian";
        }

        public static class Tank {
            public const double VolumeM3 = 10.0;
            public const double FuelDensityKgM3 = 1000.0;
            public const double FuelMassKg = 1000.0;
            public const double DryMassKg = 100.0;
            public const string PressureModel = "PressureRegulated";
        }

        public static class Thruster {
            public const double ThrustKN = 100.0;
            public const double IspVac = 300.0;
            public const double GravitationalAccel = 9.80665;
            public const double DutyCycle = 1.0;
            public const double ThrustScaleFactor = 1.0;
            public const string CoordinateSystem = "Local";
            public const string Axes = "VNB";
        }

        public static class Propagator {
            public const double Accuracy = 1e-11;
            public const double InitialStepSize = 60.0;
            public const double MinStep = 0.001;
            public const double MaxStep = 2700.0;
            public const int MaxStepAttempts = 50;
            public const string IntegratorType = "RungeKutta89";
            public const string ErrorControl = "RSSStep";
        }

        public static class Solver {
            public const int MaxIterations = 50;
            public const string Algorithm = "NewtonRaphson";
            public const string DerivativeMethod = "ForwardDifference";
            public const double Perturbation = 0.0001;
            public const double MaxStep = 10.0;
        }

        public static class Step {
            public const double AchieveTolerance = 0.1;
            public const double VaryPerturbation = 0.0001;
            public const double VaryMaxStep = 10.0;
            public const double StopTolerance = 1e-5;
        }
    }

    /// <summary>
    /// 全局底层状态管理核心引擎 (Single Source of Truth)
    /// </summary>
    public class GMATStateManager {
        private static GMATStateManager _instance;
        public static GMATStateManager Current => _instance ?? (_instance = new GMATStateManager());

        public MGAStateManager MGA => MGAStateManager.Current;

        public GMATSpacecraftModel Spacecraft { get; set; } = new GMATSpacecraftModel();
        public List<GMATTankModel> Tanks { get; set; } = new List<GMATTankModel>();
        public List<GMATThrusterModel> Thrusters { get; set; } = new List<GMATThrusterModel>();
        public List<GMATVariableModel> Variables { get; set; } = new List<GMATVariableModel>();
        public List<GMATArrayModel> Arrays { get; set; } = new List<GMATArrayModel>();
        public List<GMATStringModel> Strings { get; set; } = new List<GMATStringModel>();
        public List<GMATSolverModel> Solvers { get; set; } = new List<GMATSolverModel>();
        public List<GMATBurnModel> Burns { get; set; } = new List<GMATBurnModel>();
        public List<GMATReportFileModel> ReportFiles { get; set; } = new List<GMATReportFileModel>();
        public List<StageData> Stages { get; private set; } = new List<StageData>();
        public int SelectedEngineIndex { get; set; } = 0;
        public List<int> SelectedStageIndices { get; set; } = new List<int>();

        public List<string> ValidationAlerts { get; } = new List<string>();

        /// <summary>
        /// 针对整个动力学与硬件状态执行非零与合法性校验，自动恢复 GMAT 官方缺省值并记录警告
        /// </summary>
        public bool ValidateAndSanitizeState(Dictionary<string, PropagatorConfig> propConfigs = null, List<MissionStep> steps = null) {
            ValidationAlerts.Clear();
            bool hasAutoFixed = false;

            // 1. Spacecraft Validation (DryMass 必须 > 0; 迎风面积/光压/阻力系数可为 0)
            if (Spacecraft != null) {
                if (Spacecraft.DryMassKg <= 0.0) {
                    Spacecraft.DryMassKg = GMATDefaults.Spacecraft.DryMassKg;
                    ValidationAlerts.Add(I18n.Format("sv_alert_drymass", GMATDefaults.Spacecraft.DryMassKg));
                    hasAutoFixed = true;
                }
                if (Spacecraft.TotalMassKg <= 0.0) {
                    Spacecraft.TotalMassKg = Math.Max(Spacecraft.DryMassKg + 100.0, GMATDefaults.Spacecraft.TotalMassKg);
                }
            }

            // 2. Tanks Validation (Volume/FuelDensity 必须 > 0; 燃料量 FuelMass 可为 0)
            if (Tanks != null) {
                for (int i = 0; i < Tanks.Count; i++) {
                    var tk = Tanks[i];
                    if (tk.VolumeM3 <= 0.0) {
                        tk.VolumeM3 = GMATDefaults.Tank.VolumeM3;
                        ValidationAlerts.Add(I18n.Format("sv_alert_tank_volume", tk.Id, GMATDefaults.Tank.VolumeM3));
                        hasAutoFixed = true;
                    }
                    if (tk.FuelDensityKgM3 <= 0.0) {
                        tk.FuelDensityKgM3 = GMATDefaults.Tank.FuelDensityKgM3;
                        ValidationAlerts.Add(I18n.Format("sv_alert_tank_density", tk.Id, GMATDefaults.Tank.FuelDensityKgM3));
                        hasAutoFixed = true;
                    }
                }
            }

            // 3. Thrusters Validation (Isp 必须 > 0; 推力 ThrustKN 可为 0)
            if (Thrusters != null) {
                for (int i = 0; i < Thrusters.Count; i++) {
                    var thr = Thrusters[i];
                    if (thr.IspVac <= 0.0) {
                        thr.IspVac = GMATDefaults.Thruster.IspVac;
                        ValidationAlerts.Add(I18n.Format("sv_alert_thr_isp", thr.Id, GMATDefaults.Thruster.IspVac));
                        hasAutoFixed = true;
                    }
                    if (thr.GravitationalAccel <= 0.0) {
                        thr.GravitationalAccel = GMATDefaults.Thruster.GravitationalAccel;
                    }
                    double dirMag = Math.Sqrt(thr.ThrustDirection1 * thr.ThrustDirection1 + thr.ThrustDirection2 * thr.ThrustDirection2 + thr.ThrustDirection3 * thr.ThrustDirection3);
                    if (dirMag < 1e-6) {
                        thr.ThrustDirection1 = 1.0;
                        thr.ThrustDirection2 = 0.0;
                        thr.ThrustDirection3 = 0.0;
                        ValidationAlerts.Add(I18n.Format("sv_alert_thr_dir", thr.Id));
                        hasAutoFixed = true;
                    }
                }
            }

            // 4. Propagators Validation (Accuracy/MinStep/MaxStep/InitialStepSize 必须 > 0)
            if (propConfigs != null && propConfigs.Count > 0) {
                foreach (var kvp in propConfigs) {
                    var p = kvp.Value;
                    if (p.accuracy <= 0.0) {
                        p.accuracy = GMATDefaults.Propagator.Accuracy;
                        ValidationAlerts.Add(I18n.Format("sv_alert_prop_accuracy", p.name, GMATDefaults.Propagator.Accuracy));
                        hasAutoFixed = true;
                    }
                    if (p.minStep <= 0.0) {
                        p.minStep = GMATDefaults.Propagator.MinStep;
                        ValidationAlerts.Add(I18n.Format("sv_alert_prop_minstep", p.name, GMATDefaults.Propagator.MinStep));
                        hasAutoFixed = true;
                    }
                    if (p.maxStep <= 0.0 || p.maxStep < p.minStep) {
                        p.maxStep = Math.Max(GMATDefaults.Propagator.MaxStep, p.minStep * 10.0);
                        ValidationAlerts.Add(I18n.Format("sv_alert_prop_maxstep", p.name, p.maxStep));
                        hasAutoFixed = true;
                    }
                    if (p.initialStepSize <= 0.0) {
                        p.initialStepSize = GMATDefaults.Propagator.InitialStepSize;
                        ValidationAlerts.Add(I18n.Format("sv_alert_prop_initstep", p.name, GMATDefaults.Propagator.InitialStepSize));
                        hasAutoFixed = true;
                    }
                    if (p.maxStepAttempts < 1) {
                        p.maxStepAttempts = GMATDefaults.Propagator.MaxStepAttempts;
                    }
                }
            }

            // 5. Mission Steps Validation (Tolerance / Perturbation 必须 > 0; InitialValue/Upper/Lower 可为 0)
            if (steps != null && steps.Count > 0) {
                for (int s = 0; s < steps.Count; s++) {
                    var st = steps[s];
                    if (st.type == "Achieve") {
                        if (st.achieve_tolerance <= 0.0) {
                            st.achieve_tolerance = GMATDefaults.Step.AchieveTolerance;
                            ValidationAlerts.Add(I18n.Format("sv_alert_step_achieve", s + 1, GMATDefaults.Step.AchieveTolerance));
                            hasAutoFixed = true;
                        }
                    } else if (st.type == "Vary") {
                        if (st.vary_perturbation == 0.0) {
                            st.vary_perturbation = GMATDefaults.Step.VaryPerturbation;
                            ValidationAlerts.Add(I18n.Format("sv_alert_step_vary", s + 1, GMATDefaults.Step.VaryPerturbation));
                            hasAutoFixed = true;
                        }
                        if (st.vary_max_step <= 0.0) {
                            st.vary_max_step = GMATDefaults.Step.VaryMaxStep;
                        }
                    }
                }
            }

            return hasAutoFixed;
        }

        private GMATStateManager() {
            EnsureDefaultResources();
        }

        public void EnsureDefaultResources() {
            if (Solvers.Count == 0) {
                Solvers.Add(new GMATSolverModel { Name = "DC_GTO", Algorithm = "NewtonRaphson", DerivativeMethod = "ForwardDifference", MaxIterations = 50 });
                Solvers.Add(new GMATSolverModel { Name = "DC_GEO", Algorithm = "NewtonRaphson", DerivativeMethod = "ForwardDifference", MaxIterations = 50 });
            }
            if (Variables.Count == 0) {
                Variables.Add(new GMATVariableModel { Name = "GTO_Duration", InitialValue = 120.0, Description = I18n.T("sv_GTO_Duration") });
                Variables.Add(new GMATVariableModel { Name = "GEO_Duration", InitialValue = 70.0, Description = I18n.T("sv_GEO_Duration") });
            }
            if (ReportFiles.Count == 0) {
                ReportFiles.Add(new GMATReportFileModel { Name = "TrajReport", Filename = "TrajReport.txt", WriteHeaders = false });
            }
        }

        /// <summary>
        /// 从 KSP 当前飞船同步全船硬件、动力学与分级数据 (单一同步入口)
        /// </summary>
        public void SyncFromVessel(Vessel v) {
            if (v == null) return;

            // 1. 同步航天器动力学状态 (统一约定变量名 Sat)
            Spacecraft.Name = "Sat";
            string cbName = v.mainBody != null ? v.mainBody.bodyName : "Earth";
            if (cbName == "Kerbin") cbName = "Earth";
            else if (cbName == "Mun") cbName = "Luna";
            else if (cbName == "Minmus") cbName = "Minmus";
            Spacecraft.CentralBody = cbName;
            Spacecraft.CoordinateSystem = $"{cbName}MJ2000Eq";
            Spacecraft.UniversalTime = Planetarium.GetUniversalTime();

            // 精确计算 1951 年起算历元 (消除 1960 年截断)
            double jdVal = EphemerisTimeSystem.UtToJd(Spacecraft.UniversalTime);
            Spacecraft.EpochUtc = GMATClient.JdToUtcGregorian(jdVal);

            // 获取 ICRF 6 状态矢量
            Vector3d pKm = Vector3d.zero;
            Vector3d vKms = Vector3d.zero;
            PrincipiaInterop.GetVesselStateICRF(v, out pKm, out vKms);
            Spacecraft.PosKm = pKm;
            Spacecraft.VelKms = vKms;
            Spacecraft.TotalMassKg = v.GetTotalMass() * 1000.0;

            // 2. 扫描整船储罐并规范化命名 (Tank0, Tank1, ...)
            Tanks.Clear();
            var rawTanks = ROEngineInterop.ScanVesselTanks(v);
            if (rawTanks != null && rawTanks.Count > 0) {
                for (int i = 0; i < rawTanks.Count; i++) {
                    var rt = rawTanks[i];
                    Tanks.Add(new GMATTankModel {
                        Id = $"Tank{i}",
                        StageNumber = rt.stageNumber,
                        FuelMassKg = rt.fuelMassKg,
                        DryMassKg = rt.dryMassKg,
                        VolumeM3 = rt.maxVolumeM3 > 0.001 ? rt.maxVolumeM3 : 1.0,
                        FuelDensityKgM3 = 1000.0,
                        PropellantType = !string.IsNullOrEmpty(rt.resourceSummary) ? rt.resourceSummary : "ChemicalFuel"
                    });
                }
            } else {
                // 回退默认储罐
                Tanks.Add(new GMATTankModel {
                    Id = "Tank0",
                    StageNumber = 0,
                    FuelMassKg = 1000.0,
                    DryMassKg = 200.0,
                    VolumeM3 = 1.0
                });
            }

            // 计算航天器总干重
            double totalFuelKg = 0;
            foreach (var tk in Tanks) totalFuelKg += tk.FuelMassKg;
            Spacecraft.DryMassKg = Math.Max(50.0, Spacecraft.TotalMassKg - totalFuelKg);

            // 3. 扫描整船发动机并智能匹配供油储罐
            Thrusters.Clear();
            var rawEngines = ROEngineInterop.ScanVesselEngines(v);
            var activeEngines = ROEngineInterop.GetActiveOrIgnitedEngines(v);
            HashSet<string> activeTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeEngines != null) {
                foreach (var ae in activeEngines) activeTitles.Add(ae.title);
            }

            int bestEngineIndex = 0;
            if (rawEngines != null && rawEngines.Count > 0) {
                for (int e = 0; e < rawEngines.Count; e++) {
                    var re = rawEngines[e];
                    string engId = GMATParameterRegistry.SanitizeGmatIdentifier(re.title, "Engine");

                    // 智能供油匹配：匹配相同分级或容量最大的对应储罐
                    string boundTkId = "Tank0";
                    for (int t = 0; t < Tanks.Count; t++) {
                        if (Tanks[t].StageNumber == re.stageNumber) {
                            boundTkId = Tanks[t].Id;
                            break;
                        }
                    }
                    if (boundTkId == "Tank0" && Tanks.Count > 1) {
                        // 若同分级未明确匹配，则将高推力主发匹配到较大容量储罐
                        if (re.thrustKN > 10.0) {
                            int maxTkIdx = 0;
                            double maxFuel = 0;
                            for (int t = 0; t < Tanks.Count; t++) {
                                if (Tanks[t].FuelMassKg > maxFuel) {
                                    maxFuel = Tanks[t].FuelMassKg;
                                    maxTkIdx = t;
                                }
                            }
                            boundTkId = Tanks[maxTkIdx].Id;
                        }
                    }

                    bool isAct = activeTitles.Contains(re.title);
                    if (isAct && bestEngineIndex == 0 && e > 0) {
                        bestEngineIndex = e;
                    }

                    Thrusters.Add(new GMATThrusterModel {
                        Id = engId,
                        RawTitle = re.title,
                        StageNumber = re.stageNumber,
                        ThrustKN = re.thrustKN,
                        IspVac = re.ispVac,
                        BoundTankId = boundTkId,
                        IsActiveOrIgnited = isAct,
                        PropellantDesc = re.propellantDesc,
                        FuelDensityKgM3 = re.fuelDensityKgM3
                    });
                }
            } else {
                Thrusters.Add(new GMATThrusterModel {
                    Id = "MainEngine",
                    RawTitle = "Main Engine",
                    StageNumber = 0,
                    ThrustKN = 100.0,
                    IspVac = 300.0,
                    BoundTankId = "Tank0"
                });
            }

            // 4. 优先选择激活/主发动机
            if (SelectedEngineIndex < 0 || SelectedEngineIndex >= Thrusters.Count) {
                SelectedEngineIndex = bestEngineIndex;
            }

            // 5. 扫描分级
            Stages = MechJebInterop.GetStages(v);
        }

        /// <summary>
        /// 获取当前激活/选定发动机对应的多级火箭推进与质量预算快照 (用于右侧看板与各卡片)
        /// </summary>
        public GMATStageBudget GetActiveStageBudget() {
            var curEng = GetSelectedThruster();
            var budget = new GMATStageBudget();
            if (curEng == null) return budget;

            budget.StageNumber = curEng.StageNumber;
            budget.EngineTitle = curEng.RawTitle;
            budget.ThrustKN = curEng.ThrustKN;
            budget.IspVac = curEng.IspVac;
            budget.PropellantDesc = curEng.PropellantDesc;
            budget.FuelDensityKgM3 = curEng.FuelDensityKgM3;

            // 找到绑定的储罐获取精确燃料量
            var tk = GetTank(curEng.BoundTankId);
            budget.FuelMassTonnes = (tk != null) ? tk.FuelMassKg / 1000.0 : 0.0;
            budget.WetMassTonnes = Spacecraft.TotalMassKg / 1000.0;
            budget.DryMassTonnes = Math.Max(0.01, budget.WetMassTonnes - budget.FuelMassTonnes);

            // 采用 MechJeb 原生分级 ΔV (数据中继，符合零托管计算铁律；不再自编齐奥尔科夫斯基公式)
            double mechjebDv = 0.0;
            if (Stages != null) {
                for (int s = 0; s < Stages.Count; s++) {
                    if (Stages[s].stageNumber == curEng.StageNumber && Stages[s].deltaV > 0.0) {
                        mechjebDv = Stages[s].deltaV;
                        break;
                    }
                }
            }
            budget.DeltaV = mechjebDv;

            return budget;
        }

        public GMATThrusterModel GetSelectedThruster() {
            if (Thrusters.Count == 0) return null;
            if (SelectedEngineIndex >= 0 && SelectedEngineIndex < Thrusters.Count) {
                return Thrusters[SelectedEngineIndex];
            }
            return Thrusters[0];
        }

        public GMATTankModel GetTank(string tankId) {
            if (string.IsNullOrEmpty(tankId)) return Tanks.Count > 0 ? Tanks[0] : null;
            return Tanks.Find(t => t.Id.Equals(tankId, StringComparison.OrdinalIgnoreCase)) ?? (Tanks.Count > 0 ? Tanks[0] : null);
        }

        public GMATThrusterModel GetThruster(string thrusterId) {
            if (string.IsNullOrEmpty(thrusterId)) return Thrusters.Count > 0 ? Thrusters[0] : null;
            return Thrusters.Find(th => th.Id.Equals(thrusterId, StringComparison.OrdinalIgnoreCase)) ?? (Thrusters.Count > 0 ? Thrusters[0] : null);
        }

        /// <summary>
        /// 获取当前航天器标准标识符 (统一变量管理)
        /// </summary>
        public string GetSpacecraftName() {
            return !string.IsNullOrEmpty(Spacecraft.Name) ? Spacecraft.Name : "Sat";
        }

        /// <summary>
        /// 标准化构造挂载在航天器上的推力器全限定属性路径 (如 Sat.RL10.ThrustDirection1)
        /// 遵循 GMAT 统一对象模型层次规范，杜绝硬编码
        /// </summary>
        public string GetQualifiedThrusterProperty(string thrusterId, string propertyName) {
            string sc = GetSpacecraftName();
            string th = !string.IsNullOrEmpty(thrusterId) ? thrusterId : (Thrusters.Count > 0 ? Thrusters[0].Id : "MainEngine");
            return $"{sc}.{th}.{propertyName}";
        }

        /// <summary>
        /// 标准化构造航天器全限定属性路径 (如 Sat.Earth.SMA, Sat.X)
        /// </summary>
        public string GetQualifiedSpacecraftProperty(string propertyPath) {
            string sc = GetSpacecraftName();
            if (string.IsNullOrEmpty(propertyPath)) return sc;
            if (propertyPath.StartsWith(sc + ".", StringComparison.OrdinalIgnoreCase)) return propertyPath;
            return $"{sc}.{propertyPath}";
        }

        public List<string> GetAttachedTankIds() {
            List<string> list = new List<string>();
            for (int i = 0; i < Tanks.Count; i++) {
                if (!list.Contains(Tanks[i].Id)) list.Add(Tanks[i].Id);
            }
            if (list.Count == 0) list.Add("Tank0");
            return list;
        }

        public List<string> GetAttachedThrusterIds() {
            List<string> list = new List<string>();
            for (int i = 0; i < Thrusters.Count; i++) {
                if (!list.Contains(Thrusters[i].Id)) list.Add(Thrusters[i].Id);
            }
            if (list.Count == 0) list.Add("MainEngine");
            return list;
        }

        /// <summary>
        /// 全流程 GMAT 脚本合规净化过滤器 (Pure-ASCII & GMAT Compliance Filter)
        /// 基线 1.4 四条子规则：
        ///   1) 纯 ASCII 字符流强制转换 (过滤中文与 Emoji)；
        ///   2) 航天器标识符前缀自愈 (正则 \bSat\d+\. 对齐为 Sat.)；
        ///   3) 打靶区间与容差防退化守护 (Upper/Lower 安全边界、Tolerance 按物理量赋容差)；
        ///   4) 终止条件合规化 (Sat.Earth.Node → Sat.Earth.Latitude = 0.0)。
        /// 基线 1.5：第三方异构脚本别名归一化 (Tianwen/Change5/europaclipper/Juno → Sat; maintank/MMH → TankX)。
        /// </summary>
        public static string SanitizeGmatScript(string rawScript) {
            if (rawScript == null) return "";
            if (rawScript.Length == 0) return "";

            // 1) 纯 ASCII 字符流强制转换 (过滤中文与 Emoji，Tab 转 4 空格)
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            using (System.IO.StringReader reader = new System.IO.StringReader(rawScript)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    System.Text.StringBuilder lineSb = new System.Text.StringBuilder();
                    for (int i = 0; i < line.Length; i++) {
                        char c = line[i];
                        if (c >= 32 && c < 127) lineSb.Append(c);
                        else if (c == '\t') lineSb.Append("    ");
                    }
                    sb.AppendLine(lineSb.ToString());
                }
            }
            string script = sb.ToString();

            // 2) 第三方异构脚本别名归一化 (基线 1.5)
            script = NormalizeExternalAliases(script);

            // 3) 航天器标识符前缀自愈 (基线 1.4-2): Sat\d+. → Sat.
            script = System.Text.RegularExpressions.Regex.Replace(script, @"\bSat\d+\.", "Sat.");

            // 4) 终止条件合规化 (基线 1.4-4): X.Y.Node / X.Y.AscendingNode / X.Y.DescendingNode → X.Y.Latitude = 0.0
            script = System.Text.RegularExpressions.Regex.Replace(
                script,
                @"\b([A-Za-z_][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)\.(?:Node|AscendingNode|DescendingNode)\b",
                "$1.$2.Latitude = 0.0");

            // 5) 打靶区间与容差防退化守护 (基线 1.4-3): 逐行处理 Vary / Achieve
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            using (System.IO.StringReader reader = new System.IO.StringReader(script)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    result.AppendLine(SanitizeTargetingLine(line));
                }
            }
            return result.ToString();
        }

        /// <summary>
        /// 第三方异构脚本别名归一化 (基线 1.5)。
        /// 主航天器别名 → Sat；外部储罐别名 → Tank0/Tank1。
        /// 注：外部求解器 (DC1/NLP/VF13ad1) 为合法 GMAT 求解器名，按基线应"注册进候选池"而非改写文本，
        /// 其运行时纳管由求解器注册表负责。
        /// </summary>
        private static string NormalizeExternalAliases(string script) {
            string s = System.Text.RegularExpressions.Regex.Replace(
                script,
                @"\b(Tianwen|Change5|europaclipper|Juno)\b",
                "Sat",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(
                s,
                @"\bmaintank\b",
                "Tank0",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\bMMH\b", "Tank1");
            return s;
        }

        /// <summary>
        /// 逐行打靶指令 (Vary / Achieve) 的防退化守护 (基线 1.4-3)。
        /// </summary>
        private static string SanitizeTargetingLine(string line) {
            // --- Vary: Lower/Upper 安全边界 + Perturbation 兜底 ---
            var varyMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"^\s*Vary\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^=]+?)\s*=\s*([^,]+?)\s*,\s*\{(.*)\}\s*\)\s*;");
            if (varyMatch.Success) {
                string solver = varyMatch.Groups[1].Value;
                string variable = varyMatch.Groups[2].Value.Trim();
                string init = varyMatch.Groups[3].Value.Trim();
                string options = varyMatch.Groups[4].Value;
                string newOptions = GuardVaryOptions(options);
                if (newOptions != options) {
                    return string.Format(CultureInfo.InvariantCulture, "Vary {0}({1} = {2}, {{{3}}});", solver, variable, init, newOptions);
                }
                return line;
            }

            // --- Achieve: Tolerance 防退化 (按物理量赋予收敛容差) ---
            var achieveMatch = System.Text.RegularExpressions.Regex.Match(
                line, @"^\s*Achieve\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^=]+?)\s*=\s*([^,]+?)\s*,\s*\{(.*)\}\s*\)\s*;");
            if (achieveMatch.Success) {
                string solver = achieveMatch.Groups[1].Value;
                string variable = achieveMatch.Groups[2].Value.Trim();
                string target = achieveMatch.Groups[3].Value.Trim();
                string options = achieveMatch.Groups[4].Value;
                string newOptions = GuardAchieveOptions(variable, options);
                if (newOptions != options) {
                    return string.Format(CultureInfo.InvariantCulture, "Achieve {0}({1} = {2}, {{{3}}});", solver, variable, target, newOptions);
                }
                return line;
            }

            return line;
        }

        private static double GetNumberOption(string options, string key) {
            var m = System.Text.RegularExpressions.Regex.Match(options, @"\b" + key + @"\s*=\s*([^\s,}]+)");
            if (!m.Success) return double.NaN;
            double v;
            return double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        private static string FormatNum(double v) {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Vary 区间防退化守护：Perturbation<=0 → 0.0001；Upper==0 或 Upper<=Lower → 自动修正安全上界。
        /// </summary>
        private static string GuardVaryOptions(string options) {
            double lower = GetNumberOption(options, "Lower");
            double upper = GetNumberOption(options, "Upper");
            double perturb = GetNumberOption(options, "Perturbation");
            string result = options;

            if (!double.IsNaN(perturb) && perturb <= 0.0) {
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\bPerturbation\s*=\s*[^\s,}]+", "Perturbation = 0.0001");
            }
            if (!double.IsNaN(lower) && !double.IsNaN(upper)) {
                double safeUpper = (upper == 0.0 || upper <= lower) ? lower + Math.Max(Math.Abs(lower) * 0.1, 1.0) : upper;
                if (safeUpper != upper) {
                    result = System.Text.RegularExpressions.Regex.Replace(result, @"\bUpper\s*=\s*[^\s,}]+", "Upper = " + FormatNum(safeUpper));
                }
            }
            return result;
        }

        /// <summary>
        /// Achieve 容差防退化守护：Tolerance<=0 时按物理量赋予收敛容差
        /// (距离量 0.5~1.0 km、速度量 0.01 km/s、角度量 0.01 deg)。
        /// </summary>
        private static string GuardAchieveOptions(string variable, string options) {
            double tol = GetNumberOption(options, "Tolerance");
            if (double.IsNaN(tol) || tol > 0.0) return options;

            string v = variable.ToUpperInvariant();
            double tolVal;
            if (v.Contains("VEL") || v.Contains("VMAG") || v.Contains("C3") || v.Contains("ENERGY") || v.Contains("MASSFLOW")) {
                tolVal = 0.01;   // 速度量 km/s
            } else if (v.Contains("INC") || v.Contains("RAAN") || v.Contains("AOP") || v.Contains("LATITUDE") ||
                       v.Contains("LONGITUDE") || v.Contains("DEC") || v.Contains("AZI") || v.Contains("FPA") ||
                       v.Contains("BETAANGLE") || v.Contains("TLONG") || v.Contains("MEANANOMALY") ||
                       v == "TA" || v == "MA" || v == "EA" || v == "HA") {
                tolVal = 0.01;   // 角度量 deg
            } else {
                tolVal = 0.5;    // 距离量 km (0.5~1.0)
            }
            return System.Text.RegularExpressions.Regex.Replace(options, @"\bTolerance\s*=\s*[^\s,}]+", "Tolerance = " + FormatNum(tolVal));
        }
    }
}
