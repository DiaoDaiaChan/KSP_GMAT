using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private double DrawDoubleField(string label, double val, float labelW = 110, float fieldW = 90) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            string txt = GUILayout.TextField(val.ToString("G", System.Globalization.CultureInfo.InvariantCulture), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW));
            double res;
            if (double.TryParse(txt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out res)) {
                val = res;
            }
            GUILayout.EndHorizontal();
            return val;
        }

        private string DrawTextField(string label, string val, float labelW = 110, float fieldW = 120) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            string txt = GUILayout.TextField(val ?? "", stepLabelInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW));
            GUILayout.EndHorizontal();
            return txt;
        }

        private int DrawIntField(string label, int val, float labelW = 110, float fieldW = 60) {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            string txt = GUILayout.TextField(val.ToString(), numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW));
            int res;
            if (int.TryParse(txt, out res)) {
                val = res;
            }
            GUILayout.EndHorizontal();
            return val;
        }

        private void EnsureDefaultResourceState() {
            if (userCustomSolvers == null) userCustomSolvers = new List<CustomSolverDef>();
            if (userCustomSolvers.Count == 0) {
                userCustomSolvers.Add(new CustomSolverDef { name = "DCGTO", algorithm = "NewtonRaphson", maxIterations = 50, derivativeMethod = "ForwardDifference" });
                userCustomSolvers.Add(new CustomSolverDef { name = "DCGEO", algorithm = "NewtonRaphson", maxIterations = 50, derivativeMethod = "ForwardDifference" });
            }
            if (userCustomVariables == null) userCustomVariables = new List<UserVariableDef>();
            if (userCustomVariables.Count == 0) {
                userCustomVariables.Add(new UserVariableDef { name = "GTODuration", initialValue = 138.0 });
                userCustomVariables.Add(new UserVariableDef { name = "GEODuration", initialValue = 80.0 });
            }
        }

        /// <summary>
        /// GMAT Resources Library Tab: Reconstructed to mirror the complete official GMAT Resources Tree.
        /// Covers Spacecraft, Hardware (Tanks & Thrusters), Burns, Propagators, Coordinate Systems,
        /// Solvers, Outputs, Variables/Arrays/Strings, and Event Locators with full live preview and Export/Import.
        /// </summary>
        private void DrawTabResources() {
            Vessel vessel = FlightGlobals.ActiveVessel;
            float availW = windowRect.width - 36f;
            float leftW = availW * 0.52f;
            float rightW = availW - leftW - 12f;

            // Compute true spacecraft mass budget from vessel
            double totalWetMassKg = (vessel != null) ? vessel.totalMass * 1000.0 : 0.0;
            double totalFuelMassKg = 0.0;
            if (cachedTanks != null && cachedTanks.Count > 0) {
                for (int t = 0; t < cachedTanks.Count; t++) {
                    totalFuelMassKg += cachedTanks[t].fuelMassKg;
                }
            } else if (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) {
                totalFuelMassKg = cachedEngines[selectedEngineIndex].fuelMassKg;
            }
            double totalDryMassKg = Math.Max(100.0, totalWetMassKg - totalFuelMassKg);

            GUILayout.BeginHorizontal();

            // =========================================================================
            // LEFT COLUMN: DYNAMIC GMAT RESOURCES EXPLORER (TREE INSPECTOR)
            // =========================================================================
            GUILayout.BeginVertical(GUILayout.Width(leftW));

            // Folder Categories Bar (Matching Official GMAT Desktop Tree)
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#38bdf8><b>📁 GMAT 原生资源配置树 (Resources Explorer)</b></color>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄 刷新飞船数据", secondaryButtonStyle, GUILayout.Height(20))) {
                cachedEngines = ROEngineInterop.ScanVesselEngines(vessel);
                cachedTanks = ROEngineInterop.ScanVesselTanks(vessel);
                ScreenMessages.PostScreenMessage("✅ 已重新抓取当前飞船推进硬件与储罐数据！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            string[] treeCategories = new string[] {
                "🛰️ 航天器", "🔧 硬件储罐/引擎", "🔥 点火对象",
                "🌐 传播子", "📐 坐标系", "⚡ 求解器",
                "📊 输出与报告", "🔢 变量/数组/字符串", "🌌 空间事件"
            };

            GUILayout.BeginHorizontal();
            for (int tc = 0; tc < 5; tc++) {
                bool isCur = (selectedResourceCategory == tc);
                if (GUILayout.Button(treeCategories[tc], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    selectedResourceCategory = tc;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            for (int tc = 5; tc < treeCategories.Length; tc++) {
                bool isCur = (selectedResourceCategory == tc);
                if (GUILayout.Button(treeCategories[tc], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    selectedResourceCategory = tc;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            EnsureDefaultResourceState();

            scrollPosResources = GUILayout.BeginScrollView(scrollPosResources, GUILayout.ExpandHeight(true));

            // -------------------------------------------------------------
            // 0. SPACECRAFT (Spacecraft Sat)
            // -------------------------------------------------------------
            if (selectedResourceCategory == 0) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🛰️ Spacecraft 航天器原生对象 (Sat):</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("🔄 飞船物理状态同步", secondaryButtonStyle, GUILayout.Height(20))) {
                    customSatDryMass = -1.0;
                    ScreenMessages.PostScreenMessage("🔄 已重置航天器质量为游戏当前飞船实测值！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>⚙️ 坐标系与时间历元配置:</b></color>", subHeaderStyle);
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                customSatDateFormat = DrawTextField("时间格式 (DateFormat):", customSatDateFormat, 160, 130);
                customSatCoordinateSystem = DrawTextField("主坐标系 (CoordinateSystem):", customSatCoordinateSystem, 180, 140);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                customSatDisplayStateType = DrawTextField("状态表示法 (DisplayStateType):", customSatDisplayStateType, 160, 130);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>⚖️ 航天器物理质量与气动/光压参数:</b></color>", subHeaderStyle);
                GUILayout.Space(2);
                double effDryMass = customSatDryMass >= 0 ? customSatDryMass : totalDryMassKg;
                GUILayout.BeginHorizontal();
                double newDry = DrawDoubleField("干重 DryMass (kg):", effDryMass, 140, 100);
                if (newDry != effDryMass) customSatDryMass = newDry;
                GUILayout.Label($"燃料质量 FuelMass: <b>{totalFuelMassKg:F1} kg</b>", labelValueStyle, GUILayout.Width(180));
                GUILayout.Label($"总湿重 WetMass: <b>{(effDryMass + totalFuelMassKg):F1} kg</b>", labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                customSatDragArea = DrawDoubleField("迎风面积 DragArea (m²):", customSatDragArea, 140, 100);
                customSatCd = DrawDoubleField("阻力系数 Cd:", customSatCd, 100, 70);
                customSatCr = DrawDoubleField("光压系数 Cr:", customSatCr, 100, 70);
                customSatSRPArea = DrawDoubleField("光压面积 SRPArea (m²):", customSatSRPArea, 140, 70);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>🔗 挂载硬件摘要 (Hardware Bindings):</b></color>", subHeaderStyle);
                List<string> attachedTkNames = new List<string>();
                if (cachedTanks != null) {
                    for (int t = 0; t < cachedTanks.Count; t++) attachedTkNames.Add(GMATParameterRegistry.SanitizeGmatIdentifier(cachedTanks[t].id, "Tank"));
                }
                if (attachedTkNames.Count == 0) attachedTkNames.Add("MainTank");

                List<string> attachedEngNames = new List<string>();
                if (cachedEngines != null) {
                    for (int e = 0; e < cachedEngines.Count; e++) attachedEngNames.Add(GMATParameterRegistry.SanitizeGmatIdentifier(cachedEngines[e].title, "Engine"));
                }
                if (attachedEngNames.Count == 0) attachedEngNames.Add("MainEngine");

                GUILayout.Label($"挂载储罐: <b>{{{string.Join(", ", attachedTkNames.ToArray())}}}</b>", labelValueStyle);
                GUILayout.Label($"挂载推力器: <b>{{{string.Join(", ", attachedEngNames.ToArray())}}}</b>", labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 1. HARDWARE: CHEMICAL TANKS & THRUSTERS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 1) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🔧 Hardware 推进储罐与发动机硬件:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新建储罐", actionButtonStyle, GUILayout.Height(20))) {
                    int tkIdx = GMATStateManager.Current.Tanks.Count;
                    GMATStateManager.Current.Tanks.Add(new GMATTankModel {
                        Id = $"Tank{tkIdx}",
                        StageNumber = tkIdx,
                        FuelMassKg = 1000.0,
                        DryMassKg = 100.0,
                        VolumeM3 = 2.0
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button("➕ 新建推力器", actionButtonStyle, GUILayout.Height(20))) {
                    int egIdx = GMATStateManager.Current.Thrusters.Count + 1;
                    string defTk = GMATStateManager.Current.Tanks.Count > 0 ? GMATStateManager.Current.Tanks[0].Id : "Tank0";
                    GMATStateManager.Current.Thrusters.Add(new GMATThrusterModel {
                        Id = $"EngineCustom{egIdx}",
                        RawTitle = $"Custom Thruster {egIdx}",
                        ThrustKN = 50.0,
                        IspVac = 350.0,
                        StageNumber = 0,
                        BoundTankId = defTk
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label("<color=#facc15><b>⛽ Chemical Tanks 推进储罐列表:</b></color>", subHeaderStyle);
                var curTanks = GMATStateManager.Current.Tanks;
                if (curTanks != null && curTanks.Count > 0) {
                    for (int tk = 0; tk < curTanks.Count; tk++) {
                        var tnk = curTanks[tk];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        tnk.Id = DrawTextField("<color=#facc15><b>储罐 ID:</b></color>", tnk.Id, 70, 100);
                        tnk.StageNumber = DrawIntField("分级 (Stage):", tnk.StageNumber, 80, 35);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            curTanks.RemoveAt(tk);
                            tk--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        tnk.FuelMassKg = DrawDoubleField("燃料质量 (kg):", tnk.FuelMassKg, 90, 80);
                        tnk.DryMassKg = DrawDoubleField("结构干重 (kg):", tnk.DryMassKg, 90, 70);
                        tnk.VolumeM3 = DrawDoubleField("容积 (m³):", tnk.VolumeM3, 70, 60);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label("<color=#64748b><i>暂无储罐对象，点击上方「➕ 新建储罐」添加。</i></color>", labelValueStyle);
                }

                GUILayout.Space(6);
                GUILayout.Label("<color=#38bdf8><b>🚀 Chemical Thrusters 发动机推力器列表:</b></color>", subHeaderStyle);
                var curThrusters = GMATStateManager.Current.Thrusters;
                if (curThrusters != null && curThrusters.Count > 0) {
                    for (int eg = 0; eg < curThrusters.Count; eg++) {
                        var eng = curThrusters[eg];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        eng.Id = DrawTextField("<color=#34d399><b>推力器 ID:</b></color>", eng.Id, 85, 140);
                        eng.StageNumber = DrawIntField("所属分级:", eng.StageNumber, 75, 35);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            curThrusters.RemoveAt(eg);
                            eg--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        eng.ThrustKN = DrawDoubleField("真空推力 (kN):", eng.ThrustKN, 95, 75);
                        eng.IspVac = DrawDoubleField("真空比冲 (s):", eng.IspVac, 85, 65);
                        
                        // Bound Tank Switcher
                        GUILayout.Label($"供油储罐: <b>{{{eng.BoundTankId}}}</b>", labelValueStyle, GUILayout.Width(130));
                        if (curTanks.Count > 1) {
                            if (GUILayout.Button("切换", secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(45))) {
                                int curTkIdx = curTanks.FindIndex(t => t.Id == eng.BoundTankId);
                                int nextTkIdx = (curTkIdx + 1) % curTanks.Count;
                                eng.BoundTankId = curTanks[nextTkIdx].Id;
                            }
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label("<color=#64748b><i>暂无推力器对象，点击上方「➕ 新建推力器」添加。</i></color>", labelValueStyle);
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 2. BURNS: FINITE BURNS & IMPULSES
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 2) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🔥 Burns 点火机动对象:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新增脉冲点火 (Impulsive)", actionButtonStyle, GUILayout.Height(20))) {
                    userImpulsiveBurns.Add(new CustomImpulsiveBurnDef {
                        name = $"Impulsive{userImpulsiveBurns.Count + 1}",
                        origin = "Earth",
                        axes = "VNB",
                        element1 = 0.5,
                        isp = 320.0
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label("<color=#f97316><b>🔥 FiniteBurns 有限推力机动列表 (与任务序列同步):</b></color>", subHeaderStyle);
                bool hasBurns = false;
                for (int i = 0; i < missionSequence.Count; i++) {
                    var st = missionSequence[i];
                    if (st.type == "FiniteBurn") {
                        hasBurns = true;
                        string bName = string.IsNullOrEmpty(st.burn_name) ? $"Finite_{i + 1}" : st.burn_name;
                        string boundEng = string.IsNullOrEmpty(st.bound_thruster) ? (cachedEngines != null && cachedEngines.Count > 0 ? GMATParameterRegistry.SanitizeGmatIdentifier(cachedEngines[0].title, "Engine") : "MainEngine") : st.bound_thruster;
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        st.burn_name = DrawTextField("<color=#f97316><b>点火对象名:</b></color>", bName, 90, 140);
                        st.bound_thruster = DrawTextField("绑定推力器:", boundEng, 90, 140);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        st.burn_direction = DrawTextField("点火方向模式:", st.burn_direction, 100, 100);
                        st.thrust_dir1 = DrawDoubleField("V(向):", st.thrust_dir1, 50, 50);
                        st.thrust_dir2 = DrawDoubleField("N(法):", st.thrust_dir2, 50, 50);
                        st.thrust_dir3 = DrawDoubleField("B(径):", st.thrust_dir3, 50, 50);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }
                if (!hasBurns) {
                    GUILayout.Label("<color=#64748b><i>当前任务序列中暂无 FiniteBurn 有限推力机动对象。</i></color>", labelValueStyle);
                }

                GUILayout.Space(6);
                GUILayout.Label("<color=#ef4444><b>💥 ImpulsiveBurns 脉冲机动列表 (瞬时速度增量):</b></color>", subHeaderStyle);
                if (userImpulsiveBurns != null && userImpulsiveBurns.Count > 0) {
                    for (int ib = 0; ib < userImpulsiveBurns.Count; ib++) {
                        var imp = userImpulsiveBurns[ib];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        imp.name = DrawTextField("<color=#ef4444><b>机动名称:</b></color>", imp.name, 80, 120);
                        imp.origin = DrawTextField("中心天体:", imp.origin, 70, 80);
                        imp.axes = DrawTextField("参考坐标系:", imp.axes, 80, 80);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            userImpulsiveBurns.RemoveAt(ib);
                            ib--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        imp.element1 = DrawDoubleField("ΔV1 (km/s):", imp.element1, 80, 60);
                        imp.element2 = DrawDoubleField("ΔV2 (km/s):", imp.element2, 80, 60);
                        imp.element3 = DrawDoubleField("ΔV3 (km/s):", imp.element3, 80, 60);
                        imp.isp = DrawDoubleField("比冲 (s):", imp.isp, 60, 60);
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label("<color=#64748b><i>暂无脉冲点火对象，点击上方「➕ 新增脉冲点火」添加。</i></color>", labelValueStyle);
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 3. PROPAGATORS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 3) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🌐 Propagators 轨道积分器与力模型管理器:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新建传播子", actionButtonStyle, GUILayout.Height(20))) {
                    string newKey = $"CustomProp_{propagatorConfigs.Count + 1}";
                    propagatorConfigs[newKey] = new PropagatorConfig {
                        integratorType = "RungeKutta89",
                        accuracy = 1e-11,
                        minStep = 0.001,
                        maxStep = 2700.0,
                        centralBody = "Earth",
                        gravityModel = "JGM2",
                        enableSRP = true,
                        dragAtmosphereModel = "None"
                    };
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                List<string> pKeys = new List<string>(propagatorConfigs.Keys);
                for (int pr = 0; pr < pKeys.Count; pr++) {
                    string pKey = pKeys[pr];
                    var cfg = propagatorConfigs[pKey];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#38bdf8><b>🌐 Create Propagator {pKey};</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("⚙️ 高级力模型配置", secondaryButtonStyle, GUILayout.Height(18))) {
                        selectedPropagatorKey = pKey;
                        showPropagatorConfigWindow = true;
                    }
                    if (pKeys.Count > 1) {
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            propagatorConfigs.Remove(pKey);
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            break;
                        }
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    cfg.integratorType = DrawTextField("积分器类型:", cfg.integratorType, 80, 130);
                    cfg.accuracy = DrawDoubleField("精度/容差:", cfg.accuracy, 80, 90);
                    cfg.minStep = DrawDoubleField("最小步长(s):", cfg.minStep, 90, 60);
                    cfg.maxStep = DrawDoubleField("最大步长(s):", cfg.maxStep, 90, 70);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    cfg.centralBody = DrawTextField("主引力天体:", cfg.centralBody, 80, 80);
                    cfg.gravityModel = DrawTextField("引力场模型:", cfg.gravityModel, 80, 80);
                    cfg.dragAtmosphereModel = DrawTextField("大气模型:", cfg.dragAtmosphereModel, 70, 90);
                    GUILayout.Label($"光压 (SRP): <b>{(cfg.enableSRP ? "开启" : "关闭")}</b>", labelValueStyle, GUILayout.Width(100));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 4. COORDINATE SYSTEMS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 4) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>📐 Coordinate Systems 坐标系管理器:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新建坐标系...", actionButtonStyle, GUILayout.Height(20))) {
                    showCoordSystemWindow = true;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label("<color=#a78bfa><b>🌐 GMAT 原生标准坐标系:</b></color>", subHeaderStyle);
                string[] coords = new string[] { "EarthMJ2000Eq", "EarthFixed", "MoonInertial", "EarthMoonRot", "SunEcliptic" };
                string[] descs = new string[] {
                    "地球 J2000 赤道惯性系 (Origin: Earth, Axes: MJ2000Eq) [GMAT 默认主参考系]",
                    "地球地固旋转坐标系 (Origin: Earth, Axes: BodyFixed) [用于地面站与发射场分析]",
                    "月球惯性参考系 (Origin: Luna, Axes: BodyInertial) [用于环月与着陆打靶]",
                    "地月旋转脉动坐标系 (Origin: Luna, Axes: ObjectReferenced, Primary: Earth) [NRHO/晕轨道核心]",
                    "日心黄道惯性坐标系 (Origin: Sun, Axes: MJ2000Ec) [用于深空引力助推]"
                };
                for (int cs = 0; cs < coords.Length; cs++) {
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.Label($"<color=#a78bfa><b>📐 Create CoordinateSystem {coords[cs]};</b></color>", labelValueStyle);
                    GUILayout.Label($"<color=#94a3b8>{descs[cs]}</color>", labelValueStyle);
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                if (GMATParameterRegistry.UserCoordinateSystems.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#facc15><b>⚙️ 用户自定义坐标系:</b></color>", subHeaderStyle);
                    for (int u = 0; u < GMATParameterRegistry.UserCoordinateSystems.Count; u++) {
                        var ucs = GMATParameterRegistry.UserCoordinateSystems[u];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        ucs.name = DrawTextField("<color=#facc15><b>坐标系名:</b></color>", ucs.name, 70, 120);
                        ucs.origin = DrawTextField("中心天体:", ucs.origin, 70, 80);
                        ucs.axes = DrawTextField("类型/轴系:", ucs.axes, 70, 100);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            GMATParameterRegistry.DeleteCoordinateSystem(ucs.name);
                            break;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 5. SOLVERS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 5) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>⚡ Differential Correctors 差分修正求解器:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新建差分求解器", actionButtonStyle, GUILayout.Height(20))) {
                    userCustomSolvers.Add(new CustomSolverDef {
                        name = $"DC{userCustomSolvers.Count + 1}",
                        algorithm = "NewtonRaphson",
                        maxIterations = 50,
                        derivativeMethod = "ForwardDifference",
                        reportStyle = "Normal"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                for (int d = 0; d < userCustomSolvers.Count; d++) {
                    var sol = userCustomSolvers[d];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    sol.name = DrawTextField("<color=#f59e0b><b>求解器标识:</b></color>", sol.name, 90, 120);
                    sol.algorithm = DrawTextField("核心算法:", sol.algorithm, 70, 120);
                    GUILayout.FlexibleSpace();
                    if (userCustomSolvers.Count > 1) {
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            userCustomSolvers.RemoveAt(d);
                            d--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    sol.maxIterations = DrawIntField("最大迭代次数:", sol.maxIterations, 100, 60);
                    sol.derivativeMethod = DrawTextField("求导差分法:", sol.derivativeMethod, 90, 140);
                    sol.reportStyle = DrawTextField("报告详细度:", sol.reportStyle, 80, 80);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 6. SUBSCRIBERS & REPORTS
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 6) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("<color=#38bdf8><b>📊 Subscribers & Reports 数据输出与可视化报告:</b></color>", headerStyle);
                GUILayout.Space(4);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#38bdf8><b>📄 Create ReportFile TrajReport;</b></color>", labelValueStyle);
                GUILayout.Label("输出文件: <b>output/mission_traj.txt</b> | 实时记录航天器全轨道历元位置与速度向量", labelValueStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#38bdf8><b>📄 Create ReportFile FiniteBurnReport;</b></color>", labelValueStyle);
                GUILayout.Label("输出文件: <b>output/mission_traj.txt.finite.txt</b> | 记录有限推力点火积分前后的质量与动力学状态", labelValueStyle);
                GUILayout.EndVertical();
                GUILayout.Space(2);

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#38bdf8><b>🖼️ Create OrbitView EarthInertialView;</b></color>", labelValueStyle);
                GUILayout.Label("渲染驱动: <b>orbit_renderer.py (高保真三维正交双视窗轨迹绘图)</b>", labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 7. VARIABLES, ARRAYS & STRINGS (REAL DECLARED OBJECTS ONLY)
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 7) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🔢 User Variables, Arrays & Strings 用户变量/数组/字符串:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 添加标量变量", actionButtonStyle, GUILayout.Height(20))) {
                    userCustomVariables.Add(new UserVariableDef {
                        name = $"Var{userCustomVariables.Count + 1}",
                        initialValue = 0.0
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button("➕ 添加矩阵数组", actionButtonStyle, GUILayout.Height(20))) {
                    userCustomArrays.Add(new UserArrayDef {
                        name = $"Arr{userCustomArrays.Count + 1}",
                        rows = 3,
                        cols = 3
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button("➕ 添加字符串", actionButtonStyle, GUILayout.Height(20))) {
                    userCustomStrings.Add(new UserStringDef {
                        name = $"Str{userCustomStrings.Count + 1}",
                        value = "Value"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.Label("<color=#facc15><b>📌 Variables 标量变量列表:</b></color>", subHeaderStyle);
                if (userCustomVariables.Count > 0) {
                    for (int v = 0; v < userCustomVariables.Count; v++) {
                        var uvar = userCustomVariables[v];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uvar.name = DrawTextField("<color=#facc15><b>变量名:</b></color>", uvar.name, 60, 140);
                        uvar.initialValue = DrawDoubleField("初值:", uvar.initialValue, 50, 90);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            userCustomVariables.RemoveAt(v);
                            v--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                } else {
                    GUILayout.Label("<color=#64748b><i>暂无标量变量，点击上方按钮添加。</i></color>", labelValueStyle);
                }

                if (userCustomArrays.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#34d399><b>📊 Arrays 矩阵数组列表:</b></color>", subHeaderStyle);
                    for (int a = 0; a < userCustomArrays.Count; a++) {
                        var uarr = userCustomArrays[a];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uarr.name = DrawTextField("<color=#34d399><b>数组名:</b></color>", uarr.name, 60, 140);
                        uarr.rows = DrawIntField("行数:", uarr.rows, 50, 40);
                        uarr.cols = DrawIntField("列数:", uarr.cols, 50, 40);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            userCustomArrays.RemoveAt(a);
                            a--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                if (userCustomStrings.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#a78bfa><b>🔤 Strings 字符串变量列表:</b></color>", subHeaderStyle);
                    for (int st = 0; st < userCustomStrings.Count; st++) {
                        var ustr = userCustomStrings[st];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        ustr.name = DrawTextField("<color=#a78bfa><b>变量名:</b></color>", ustr.name, 60, 140);
                        ustr.value = DrawTextField("字符串内容:", ustr.value, 80, 180);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            userCustomStrings.RemoveAt(st);
                            st--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        GUILayout.Space(2);
                    }
                }

                GUILayout.EndVertical();
            }

            // -------------------------------------------------------------
            // 8. SPACE EVENTS & PARAMETER CATALOG
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 8) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>🌌 Space Events 开普勒轨道几何事件与打靶约束库:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                GUILayout.BeginHorizontal(nestedCardBoxStyle);
                eventSearchKeyword = DrawTextField("🔍 搜索参数/事件:", eventSearchKeyword, 120, 200);
                if (!string.IsNullOrEmpty(eventSearchKeyword) && GUILayout.Button("✖ 清空", secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                    eventSearchKeyword = "";
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                string[] evts = new string[] {
                    "Sat.Earth.Apoapsis (远地点事件)", "Sat.Earth.Periapsis (近地点事件)",
                    "Sat.Earth.Latitude = 0.0 (赤道升/降交点事件)", "Sat.Luna.Periapsis (近月点事件)",
                    "Sat.Earth.SMA = 42164.14 (GEO 同步半长轴)", "Sat.Earth.Altitude = 35786.0 (GTO 远地点高度)",
                    "Sat.EarthMJ2000Eq.VZ = 0.0 (赤纬速度=0 消除倾角)", "Sat.Earth.ECC = 0.0 (圆化轨道)",
                    "Sat.Earth.INC = 0.0 (0度赤道共面)", "Sat.Earth.RAAN (升交点赤经)",
                    "Sat.Earth.AOP (近地点幅角)", "Sat.Earth.TA (真近点角)",
                    "Sat.FuelMass (推进剂剩余质量)", "Sat.C3Energy (逃逸特征能 C3)"
                };

                for (int e = 0; e < evts.Length; e++) {
                    string ev = evts[e];
                    if (!string.IsNullOrEmpty(eventSearchKeyword) && ev.IndexOf(eventSearchKeyword, StringComparison.OrdinalIgnoreCase) < 0) {
                        continue;
                    }

                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#38bdf8><b>🎯 {ev}</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();

                    string cleanParam = ev.Split(' ')[0];
                    if (cleanParam.Contains("=")) cleanParam = cleanParam.Split('=')[0].Trim();

                    if (GUILayout.Button("🎯 插入打靶目标", actionButtonStyle, GUILayout.Height(18))) {
                        missionSequence.Add(new MissionStep {
                            type = "Achieve",
                            label = $"约束 {cleanParam}",
                            achieve_param = cleanParam,
                            achieve_val = ev.Contains("=") ? double.Parse(ev.Split('=')[1].Split(' ')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0.0,
                            achieve_tolerance = 0.5,
                            dc_name = GetActiveSolverNames()[0]
                        });
                        ScreenMessages.PostScreenMessage($"✅ 已插入打靶约束目标: {cleanParam}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    GUILayout.Space(2);
                    if (GUILayout.Button("⏱️ 插入停止条件", secondaryButtonStyle, GUILayout.Height(18))) {
                        missionSequence.Add(new MissionStep {
                            type = "Propagate",
                            label = $"积分至 {cleanParam}",
                            propagator = "LEOProp",
                            stop_type = cleanParam,
                            target_val = ev.Contains("=") ? double.Parse(ev.Split('=')[1].Split(' ')[0].Trim(), System.Globalization.CultureInfo.InvariantCulture) : 0.0
                        });
                        ScreenMessages.PostScreenMessage($"✅ 已插入积分停止条件: {cleanParam}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // =========================================================================
            // RIGHT COLUMN: UNIFIED BUILT-IN GMAT SCRIPT IDE & LIVE INSPECTOR
            // =========================================================================
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(rightW));

            // IDE Header & Mode Switcher
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#38bdf8><b>💻 GMAT 脚本 IDE 与代码生成器</b></color>", headerStyle);
            GUILayout.FlexibleSpace();

            string[] ideModes = new string[] { "📄 全脚本", "📦 资源区", "🎯 任务区" };
            for (int m = 0; m < ideModes.Length; m++) {
                bool isM = (ideScriptViewMode == m);
                if (GUILayout.Button(ideModes[m], isM ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    ideScriptViewMode = m;
                    if (ideEditMode) {
                        ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    }
                }
            }
            GUILayout.Space(6);
            if (GUILayout.Button(ideEditMode ? "🎨 高亮预览" : "✏️ 自由编辑", ideEditMode ? tabButtonActiveStyle : actionButtonStyle, GUILayout.Height(20))) {
                ideEditMode = !ideEditMode;
                if (ideEditMode && string.IsNullOrEmpty(ideCustomBuffer)) {
                    ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            string liveScript = GenerateLiveGmatScript(ideScriptViewMode);
            string currentScriptToUse = (ideEditMode && !string.IsNullOrEmpty(ideCustomBuffer)) ? ideCustomBuffer : liveScript;

            // Action Toolbar
            GUILayout.BeginHorizontal(nestedCardBoxStyle);

            if (GUILayout.Button("📋 复制代码", secondaryButtonStyle, GUILayout.Height(22))) {
                GUIUtility.systemCopyBuffer = currentScriptToUse;
                ScreenMessages.PostScreenMessage("✅ GMAT 脚本已复制到系统剪贴板！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button("💾 导出脚本...", secondaryButtonStyle, GUILayout.Height(22))) {
                try {
                    string expPath = Path.Combine(GMATClient.GetOutputDir(), "mission_run.script");
                    File.WriteAllText(expPath, currentScriptToUse, new System.Text.UTF8Encoding(false));
                    ScreenMessages.PostScreenMessage("✅ 脚本已导出至: " + expPath, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage("❌ 导出失败: " + ex.Message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (GUILayout.Button("🚀 推送至 GMAT", actionButtonStyle, GUILayout.Height(22))) {
                try {
                    string expPath = Path.Combine(GMATClient.GetOutputDir(), "mission_run.script");
                    File.WriteAllText(expPath, currentScriptToUse, new System.Text.UTF8Encoding(false));
                    GMATClient.OpenGmatDesktop(expPath);
                    ScreenMessages.PostScreenMessage("🚀 已推送至 GMAT 桌面端！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage("❌ 推送失败: " + ex.Message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (ideEditMode) {
                if (GUILayout.Button("🔄 重新生成", secondaryButtonStyle, GUILayout.Height(22))) {
                    ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    ScreenMessages.PostScreenMessage("🔄 已重新同步生成实时脚本！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (GUILayout.Button(ideEditMode ? "⚡ 求解编辑脚本" : "⚡ 立即求解", successButtonStyle, GUILayout.Height(22))) {
                if (ideEditMode && !string.IsNullOrEmpty(ideCustomBuffer)) {
                    TriggerPropagationSolve(ideCustomBuffer);
                } else {
                    TriggerPropagationSolve();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // =========================================================================
            // IDE INTELLISENSE & PARAMETER AUTO-COMPLETION DRAWER
            // =========================================================================
            GUILayout.BeginVertical(nestedCardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>⚡ {I18n.T("ide_intellisense_header")}</b></color>", subHeaderStyle);
            GUILayout.FlexibleSpace();
            string toggleText = showIdeIntelliSenseDrawer ? "💡 展开补全 ▴" : "💡 收起补全 ▾";
            if (GUILayout.Button(toggleText, secondaryButtonStyle, GUILayout.Height(18))) {
                showIdeIntelliSenseDrawer = !showIdeIntelliSenseDrawer;
            }
            GUILayout.EndHorizontal();

            if (showIdeIntelliSenseDrawer) {
                GUILayout.Space(2);

                // Category Chips Bar
                string[] catTabs = new string[] { "🌟 全部", "🛰️ 航天器", "🌍 天体坐标", "🔧 推进硬件", "🌐 传播子", "⚡ 求解器", "📜 常用命令" };
                GUILayout.BeginHorizontal();
                for (int c = 0; c < catTabs.Length; c++) {
                    bool isCat = (selectedIdeCategoryTab == c);
                    if (GUILayout.Button(catTabs[c], isCat ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(19))) {
                        selectedIdeCategoryTab = c;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(2);

                // Search input
                GUILayout.BeginHorizontal();
                ideSearchKeyword = DrawTextField(I18n.T("ide_search_label"), ideSearchKeyword, 140, 160);
                if (!string.IsNullOrEmpty(ideSearchKeyword) && GUILayout.Button(I18n.T("ide_clear_search"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                    ideSearchKeyword = "";
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(2);

                // Query parameter items
                string queryText = string.IsNullOrEmpty(ideSearchKeyword) ? (
                    selectedIdeCategoryTab == 1 ? "Sat." :
                    selectedIdeCategoryTab == 2 ? "Earth." :
                    selectedIdeCategoryTab == 3 ? "Tank" :
                    selectedIdeCategoryTab == 4 ? "Prop" :
                    selectedIdeCategoryTab == 5 ? "DC" :
                    selectedIdeCategoryTab == 6 ? "Propagate" : ""
                ) : ideSearchKeyword;

                var intelliList = GMATParameterRegistry.QueryIntelliSense(queryText, vessel);
                if (intelliList != null && intelliList.Count > 0) {
                    int maxShow = Mathf.Min(intelliList.Count, 12);
                    ideIntelliSenseScrollPos = GUILayout.BeginScrollView(ideIntelliSenseScrollPos, GUILayout.Height(100));
                    for (int k = 0; k < maxShow; k++) {
                        var itm = intelliList[k];
                        GUILayout.BeginHorizontal(cardBoxStyle);
                        GUILayout.Label(itm.GetIcon(), GUILayout.Width(18));
                        GUILayout.Label($"<b>{itm.Label}</b>", labelValueStyle, GUILayout.Width(140));
                        GUILayout.Label($"<color=#38bdf8>{itm.TypeTag}</color>", labelValueStyle, GUILayout.Width(90));
                        GUILayout.Label($"<color=#94a3b8>{itm.Doc}</color>", labelValueStyle);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(I18n.T("ide_btn_insert"), actionButtonStyle, GUILayout.Height(18), GUILayout.Width(75))) {
                            InsertIntoIdeBuffer(itm.InsertText);
                        }
                        if (GUILayout.Button(I18n.T("ide_btn_copy"), secondaryButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            GUIUtility.systemCopyBuffer = itm.InsertText;
                            ScreenMessages.PostScreenMessage($"✅ 已复制: {itm.InsertText}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                        }
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                } else {
                    GUILayout.Label("<color=#64748b><i>未找到匹配的 GMAT 参数或命令，请调整搜索关键词。</i></color>", labelValueStyle);
                }

                GUILayout.Space(2);

                // Quick Snippets Toolbar
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#facc15><b>{I18n.T("ide_snippets_header")}</b></color>", labelValueStyle, GUILayout.Width(80));
                if (GUILayout.Button("+ 积分 (Propagate)", secondaryButtonStyle, GUILayout.Height(18))) {
                    InsertIntoIdeBuffer("Propagate LEOProp(Sat) {Sat.Earth.Apoapsis};");
                }
                if (GUILayout.Button("+ 有限点火 (Burn)", secondaryButtonStyle, GUILayout.Height(18))) {
                    InsertIntoIdeBuffer("BeginFiniteBurn FiniteBurn(Sat);\nPropagate LEOProp(Sat) {Sat.ElapsedSecs = 60.0};\nEndFiniteBurn FiniteBurn(Sat);");
                }
                if (GUILayout.Button("+ 打靶组 (Target)", secondaryButtonStyle, GUILayout.Height(18))) {
                    InsertIntoIdeBuffer("Target DCGTO;\n    Vary DCGTO(GTODuration = 138.0, {Lower = 0.0, Upper = 500.0, MaxStep = 10.0});\n    BeginFiniteBurn GTOBurn(Sat);\n    Propagate LEOProp(Sat) {Sat.ElapsedSecs = GTODuration};\n    EndFiniteBurn GTOBurn(Sat);\n    Achieve DCGTO(Sat.Earth.Altitude = 35786.0, {Tolerance = 0.5});\nEndTarget;");
                }
                if (GUILayout.Button("+ 变量 (Vary)", secondaryButtonStyle, GUILayout.Height(18))) {
                    InsertIntoIdeBuffer("Vary DCGTO(GTODuration = 138.0, {Lower = 0.0, Upper = 500.0, MaxStep = 5.0});");
                }
                if (GUILayout.Button("+ 约束 (Achieve)", secondaryButtonStyle, GUILayout.Height(18))) {
                    InsertIntoIdeBuffer("Achieve DCGTO(Sat.Earth.SMA = 42164.14, {Tolerance = 0.1});");
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
            GUILayout.Space(4);

            // Code View Body (Editable TextArea vs Line-numbered Syntax Highlighted View)
            ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
            if (ideEditMode) {
                if (ideCustomBuffer == null) ideCustomBuffer = liveScript;
                ideCustomBuffer = GUILayout.TextArea(ideCustomBuffer, ideEditorStyle ?? GUI.skin.textArea, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            } else {
                string[] scriptLines = liveScript.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int l = 0; l < scriptLines.Length; l++) {
                    string rawLine = scriptLines[l];
                    string lineNum = $"<color=#64748b>{l + 1,3}:</color> ";
                    string coloredLine = ColorizeGmatSyntax(rawLine);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(lineNum + coloredLine, codeBlockStyle);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void InsertIntoIdeBuffer(string textToInsert) {
            if (string.IsNullOrEmpty(textToInsert)) return;
            if (ideCustomBuffer == null) ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);

            if (!ideCustomBuffer.EndsWith("\n") && !ideCustomBuffer.EndsWith("\r")) {
                ideCustomBuffer += "\n";
            }
            ideCustomBuffer += textToInsert + "\n";
            ScreenMessages.PostScreenMessage($"✅ 已插入: {textToInsert.Split('\n')[0]}", 2.0f, ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
