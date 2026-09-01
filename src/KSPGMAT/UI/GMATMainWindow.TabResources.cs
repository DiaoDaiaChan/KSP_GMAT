using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private static Dictionary<string, string> smartFieldBuffers = new Dictionary<string, string>();
        private static string activeSmartControlId = null;

        private double DrawDoubleField(string label, double val, float labelW = 110, float fieldW = 90, string format = "auto", string customId = null) {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label)) GUILayout.Label(label, labelValueStyle, GUILayout.Width(labelW));
            
            string ctrlId = !string.IsNullOrEmpty(customId) ? customId : ("res_field_" + (label ?? "num").GetHashCode());
            GUI.SetNextControlName(ctrlId);
            bool isFocused = (GUI.GetNameOfFocusedControl() == ctrlId);

            string currentBuf;
            if (!smartFieldBuffers.TryGetValue(ctrlId, out currentBuf)) {
                currentBuf = FormatSmartDoubleForDisplay(val, format);
                smartFieldBuffers[ctrlId] = currentBuf;
            }

            if (!isFocused) {
                if (activeSmartControlId == ctrlId) {
                    activeSmartControlId = null;
                }
                currentBuf = FormatSmartDoubleForDisplay(val, format);
                smartFieldBuffers[ctrlId] = currentBuf;
            } else {
                // When focused, expand to raw data string if newly entered
                if (activeSmartControlId != ctrlId) {
                    activeSmartControlId = ctrlId;
                    currentBuf = (val == 0.0) ? "0.0" : val.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
                    smartFieldBuffers[ctrlId] = currentBuf;
                }
            }

            string edited = GUILayout.TextField(currentBuf, numericInputStyle ?? GUI.skin.textField, GUILayout.Width(fieldW), GUILayout.Height(20));
            if (edited != currentBuf) {
                smartFieldBuffers[ctrlId] = edited;
                double res;
                if (double.TryParse(edited, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out res)) {
                    val = res;
                }
            }
            GUILayout.EndHorizontal();
            return val;
        }

        private static string FormatSmartDoubleForDisplay(double val, string format = "auto") {
            if (val == 0.0) return "0.0";
            if (format != "auto") {
                return val.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
            }
            double abs = Math.Abs(val);
            if (abs < 1e-4) {
                return val.ToString("E2", System.Globalization.CultureInfo.InvariantCulture); // e.g. 1.00E-11
            }
            if (abs >= 10000.0) {
                return val.ToString("F1", System.Globalization.CultureInfo.InvariantCulture); // e.g. 35786.0
            }
            if (abs >= 100.0) {
                return val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); // e.g. 6282.84
            }
            if (abs >= 1.0) {
                return val.ToString("F2", System.Globalization.CultureInfo.InvariantCulture); // e.g. 2.16
            }
            return val.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture); // e.g. 0.0001
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
            GMATStateManager.Current.EnsureDefaultResources();
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

            var valAlerts = GMATStateManager.Current.ValidationAlerts;
            if (valAlerts != null && valAlerts.Count > 0) {
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#f87171><b>🛡️ GMAT 状态校验器检测到 {valAlerts.Count} 处无效参数并已自动恢复缺省值:</b></color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("✕ 忽略", secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(18))) {
                    valAlerts.Clear();
                }
                GUILayout.EndHorizontal();
                for (int a = 0; a < valAlerts.Count; a++) {
                    GUILayout.Label($"<color=#fca5a5>• {valAlerts[a]}</color>", labelValueStyle);
                }
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

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
                    GMATStateManager.Current.SyncFromVessel(vessel);
                    ScreenMessages.PostScreenMessage("🔄 已从当前飞船全量同步动力学与硬件物理状态！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var scModel = GMATStateManager.Current.Spacecraft;

                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>⚙️ 坐标系与时间历元配置:</b></color>", subHeaderStyle);
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                scModel.DateFormat = DrawTextField("时间格式 (DateFormat):", scModel.DateFormat ?? "UTCGregorian", 160, 130);
                scModel.CoordinateSystem = DrawTextField("主坐标系 (CoordinateSystem):", scModel.CoordinateSystem ?? "EarthMJ2000Eq", 180, 140);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                scModel.DisplayStateType = DrawTextField("状态表示法 (DisplayStateType):", scModel.DisplayStateType ?? "Cartesian", 160, 130);
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>⚖️ 航天器物理质量与气动/光压参数:</b></color>", subHeaderStyle);
                GUILayout.Space(2);
                double effDryMass = scModel.DryMassKg > 0 ? scModel.DryMassKg : totalDryMassKg;
                GUILayout.BeginHorizontal();
                double newDry = DrawDoubleField("干重 DryMass (kg):", effDryMass, 140, 100, "F2", "sc_dry_mass");
                if (newDry != effDryMass) scModel.DryMassKg = newDry;
                GUILayout.Label($"燃料质量 FuelMass: <b>{totalFuelMassKg:F1} kg</b>", labelValueStyle, GUILayout.Width(180));
                GUILayout.Label($"总湿重 WetMass: <b>{(effDryMass + totalFuelMassKg):F1} kg</b>", labelValueStyle);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                scModel.DragArea = DrawDoubleField("迎风面积 DragArea (m²):", scModel.DragArea, 140, 100, "F2", "sc_drag_area");
                scModel.Cd = DrawDoubleField("阻力系数 Cd:", scModel.Cd, 100, 70, "F2", "sc_cd");
                scModel.Cr = DrawDoubleField("光压系数 Cr:", scModel.Cr, 100, 70, "F2", "sc_cr");
                scModel.SRPArea = DrawDoubleField("光压面积 SRPArea (m²):", scModel.SRPArea, 140, 70, "F2", "sc_srp_area");
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                GUILayout.Space(4);
                GUILayout.BeginVertical(nestedCardBoxStyle);
                GUILayout.Label("<color=#facc15><b>🔗 挂载硬件摘要 (Hardware Bindings):</b></color>", subHeaderStyle);
                List<string> attachedTkNames = GMATStateManager.Current.GetAttachedTankIds();
                List<string> attachedEngNames = GMATStateManager.Current.GetAttachedThrusterIds();

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
                        tnk.FuelMassKg = DrawDoubleField("燃料质量 (kg):", tnk.FuelMassKg, 90, 80, "F2", $"tank_fuel_{tk}_{tnk.Id}");
                        tnk.DryMassKg = DrawDoubleField("结构干重 (kg):", tnk.DryMassKg, 90, 70, "F2", $"tank_dry_{tk}_{tnk.Id}");
                        tnk.VolumeM3 = DrawDoubleField("容积 (m³):", tnk.VolumeM3, 70, 60, "F3", $"tank_vol_{tk}_{tnk.Id}");
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
                        eng.ThrustKN = DrawDoubleField("真空推力 (kN):", eng.ThrustKN, 95, 75, "F2", $"thr_thrust_{eg}_{eng.Id}");
                        eng.IspVac = DrawDoubleField("真空比冲 (s):", eng.IspVac, 85, 65, "F1", $"thr_isp_{eg}_{eng.Id}");
                        
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
                    GMATStateManager.Current.Burns.Add(new Model.GMATBurnModel {
                        Name = $"Impulsive{GMATStateManager.Current.Burns.Count + 1}",
                        Origin = "Earth",
                        Axes = "VNB",
                        Element1 = 0.5,
                        Isp = 320.0
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
                        st.thrust_dir1 = DrawDoubleField("V(向):", st.thrust_dir1, 50, 50, "F3", $"fb_v_{i}_{st.burn_name}");
                        st.thrust_dir2 = DrawDoubleField("N(法):", st.thrust_dir2, 50, 50, "F3", $"fb_n_{i}_{st.burn_name}");
                        st.thrust_dir3 = DrawDoubleField("B(径):", st.thrust_dir3, 50, 50, "F3", $"fb_b_{i}_{st.burn_name}");
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
                var impulsiveBurns = GMATStateManager.Current.Burns;
                if (impulsiveBurns != null && impulsiveBurns.Count > 0) {
                    for (int ib = 0; ib < impulsiveBurns.Count; ib++) {
                        var imp = impulsiveBurns[ib];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        imp.Name = DrawTextField("<color=#ef4444><b>机动名称:</b></color>", imp.Name, 80, 120);
                        imp.Origin = DrawTextField("中心天体:", imp.Origin, 70, 80);
                        imp.Axes = DrawTextField("参考坐标系:", imp.Axes, 80, 80);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            impulsiveBurns.RemoveAt(ib);
                            ib--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                        GUILayout.EndHorizontal();

                        GUILayout.BeginHorizontal();
                        imp.Element1 = DrawDoubleField("ΔV1 (km/s):", imp.Element1, 80, 60, "F4", $"imp_v1_{ib}_{imp.Name}");
                        imp.Element2 = DrawDoubleField("ΔV2 (km/s):", imp.Element2, 80, 60, "F4", $"imp_v2_{ib}_{imp.Name}");
                        imp.Element3 = DrawDoubleField("ΔV3 (km/s):", imp.Element3, 80, 60, "F4", $"imp_v3_{ib}_{imp.Name}");
                        imp.Isp = DrawDoubleField("比冲 (s):", imp.Isp, 60, 60, "F1", $"imp_isp_{ib}_{imp.Name}");
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
                    cfg.accuracy = DrawDoubleField("精度/容差:", cfg.accuracy, 80, 90, "E2", $"prop_acc_{pKey}");
                    cfg.minStep = DrawDoubleField("最小步长(s):", cfg.minStep, 90, 60, "F3", $"prop_min_{pKey}");
                    cfg.maxStep = DrawDoubleField("最大步长(s):", cfg.maxStep, 90, 70, "F1", $"prop_max_{pKey}");
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
            // 5. SOLVERS (DifferentialCorrectors)
            // -------------------------------------------------------------
            else if (selectedResourceCategory == 5) {
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#38bdf8><b>⚡ Differential Correctors 差分修正求解器:</b></color>", headerStyle);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("➕ 新建差分求解器", actionButtonStyle, GUILayout.Height(20))) {
                    string newName = $"DC_{GMATStateManager.Current.Solvers.Count + 1}";
                    GMATStateManager.Current.Solvers.Add(new Model.GMATSolverModel {
                        Name = newName,
                        Algorithm = "NewtonRaphson",
                        MaxIterations = 50,
                        DerivativeMethod = "ForwardDifference",
                        ShowProgress = true,
                        ReportStyle = "Normal",
                        ReportFile = $"DifferentialCorrector{newName}.data"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var solvers = GMATStateManager.Current.Solvers;
                for (int d = 0; d < solvers.Count; d++) {
                    var sol = solvers[d];
                    GUILayout.BeginVertical(nestedCardBoxStyle);
                    
                    // Card Title Bar
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#38bdf8><b>🌐 Create DifferentialCorrector {sol.Name};</b></color>", labelValueStyle);
                    GUILayout.FlexibleSpace();
                    if (solvers.Count > 1) {
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            solvers.RemoveAt(d);
                            d--;
                            GUILayout.EndHorizontal();
                            GUILayout.EndVertical();
                            continue;
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);

                    // --- Options Group Box ---
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label("<color=#facc15><b>⚙️ Options 动力学校正内核设置:</b></color>", subHeaderStyle);
                    GUILayout.Space(2);

                    // 1. Algorithm Selector
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("寻优算法 (Algorithm):", labelValueStyle, GUILayout.Width(150));
                    string[] algs = new string[] { "NewtonRaphson", "Broyden", "ModifiedBroyden" };
                    for (int a = 0; a < algs.Length; a++) {
                        bool isCur = (sol.Algorithm == algs[a]);
                        if (GUILayout.Button(algs[a], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.Algorithm = algs[a];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 2. Derivative Method Selector
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("差分求导 (Derivative Method):", labelValueStyle, GUILayout.Width(150));
                    string[] dmList = new string[] { "ForwardDifference", "CentralDifference" };
                    for (int m = 0; m < dmList.Length; m++) {
                        bool isCur = (sol.DerivativeMethod == dmList[m]);
                        string desc = (dmList[m] == "ForwardDifference") ? "ForwardDifference (前向单扰动)" : "CentralDifference (中心双扰动高精)";
                        if (GUILayout.Button(desc, isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.DerivativeMethod = dmList[m];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 3. Max Iterations Field & Quick Presets
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    sol.MaxIterations = DrawIntField("最大迭代 (Max Iterations):", sol.MaxIterations, 150, 60);
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#64748b>预设:</color>", gridLabelStyle, GUILayout.Width(35));
                    int[] iterPresets = new int[] { 25, 50, 100, 200 };
                    for (int ip = 0; ip < iterPresets.Length; ip++) {
                        if (GUILayout.Button(iterPresets[ip].ToString(), secondaryButtonStyle, GUILayout.Width(35), GUILayout.Height(18))) {
                            sol.MaxIterations = iterPresets[ip];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();

                    GUILayout.Space(3);

                    // --- Output Group Box ---
                    GUILayout.BeginVertical(cardBoxStyle);
                    GUILayout.Label("<color=#38bdf8><b>📄 Output 数据输出与收敛报告设置:</b></color>", subHeaderStyle);
                    GUILayout.Space(2);

                    // 1. Show Progress Toggle
                    GUILayout.BeginHorizontal();
                    sol.ShowProgress = GUILayout.Toggle(sol.ShowProgress, " 实时在控制台与输出窗口输出求解进度 (ShowProgress = true)", GUILayout.Height(18));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 2. Report Style Selector
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("报告级别 (Report Style):", labelValueStyle, GUILayout.Width(150));
                    string[] repStyles = new string[] { "Normal", "Concise", "Verbose", "Debug" };
                    for (int rs = 0; rs < repStyles.Length; rs++) {
                        bool isCur = (sol.ReportStyle == repStyles[rs]);
                        if (GUILayout.Button(repStyles[rs], isCur ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                            sol.ReportStyle = repStyles[rs];
                        }
                    }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    // 3. Report File Text Field
                    GUILayout.Space(2);
                    GUILayout.BeginHorizontal();
                    string defRepFile = string.IsNullOrEmpty(sol.ReportFile) ? $"DifferentialCorrector{sol.Name}.data" : sol.ReportFile;
                    sol.ReportFile = DrawTextField("数据报告文件 (Report File):", defRepFile, 150, 220);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();

                    GUILayout.EndVertical();
                    GUILayout.Space(3);
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
                    GMATStateManager.Current.Variables.Add(new Model.GMATVariableModel {
                        Name = $"Var{GMATStateManager.Current.Variables.Count + 1}",
                        InitialValue = 0.0
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button("➕ 添加矩阵数组", actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Arrays.Add(new Model.GMATArrayModel {
                        Name = $"Arr{GMATStateManager.Current.Arrays.Count + 1}",
                        Rows = 3,
                        Cols = 3
                    });
                }
                GUILayout.Space(4);
                if (GUILayout.Button("➕ 添加字符串", actionButtonStyle, GUILayout.Height(20))) {
                    GMATStateManager.Current.Strings.Add(new Model.GMATStringModel {
                        Name = $"Str{GMATStateManager.Current.Strings.Count + 1}",
                        Value = "Value"
                    });
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(4);

                var vars = GMATStateManager.Current.Variables;
                GUILayout.Label("<color=#facc15><b>📌 Variables 标量变量列表:</b></color>", subHeaderStyle);
                if (vars.Count > 0) {
                    for (int v = 0; v < vars.Count; v++) {
                        var uvar = vars[v];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uvar.Name = DrawTextField("<color=#facc15><b>变量名:</b></color>", uvar.Name, 60, 140);
                        uvar.InitialValue = DrawDoubleField("初值:", uvar.InitialValue, 50, 90, "auto", $"uvar_val_{v}_{uvar.Name}");
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            vars.RemoveAt(v);
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

                var arrays = GMATStateManager.Current.Arrays;
                if (arrays.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#34d399><b>📊 Arrays 矩阵数组列表:</b></color>", subHeaderStyle);
                    for (int a = 0; a < arrays.Count; a++) {
                        var uarr = arrays[a];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        uarr.Name = DrawTextField("<color=#34d399><b>数组名:</b></color>", uarr.Name, 60, 140);
                        uarr.Rows = DrawIntField("行数:", uarr.Rows, 50, 40);
                        uarr.Cols = DrawIntField("列数:", uarr.Cols, 50, 40);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            arrays.RemoveAt(a);
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

                var strings = GMATStateManager.Current.Strings;
                if (strings.Count > 0) {
                    GUILayout.Space(4);
                    GUILayout.Label("<color=#a78bfa><b>🔤 Strings 字符串变量列表:</b></color>", subHeaderStyle);
                    for (int st = 0; st < strings.Count; st++) {
                        var ustr = strings[st];
                        GUILayout.BeginVertical(nestedCardBoxStyle);
                        GUILayout.BeginHorizontal();
                        ustr.Name = DrawTextField("<color=#a78bfa><b>变量名:</b></color>", ustr.Name, 60, 140);
                        ustr.Value = DrawTextField("字符串内容:", ustr.Value, 80, 180);
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("❌ 删除", deleteButtonStyle, GUILayout.Height(18), GUILayout.Width(50))) {
                            strings.RemoveAt(st);
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
            GUILayout.Label("<color=#38bdf8><b>💻 GMAT 脚本 IDE 与代码编辑器</b></color>", headerStyle);
            GUILayout.FlexibleSpace();

            string[] ideModes = new string[] { "📄 全脚本", "📦 资源区", "🎯 任务区" };
            for (int m = 0; m < ideModes.Length; m++) {
                bool isM = (ideScriptViewMode == m);
                if (GUILayout.Button(ideModes[m], isM ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    ideScriptViewMode = m;
                    if (string.IsNullOrEmpty(ideCustomBuffer)) {
                        ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    }
                }
            }
            GUILayout.Space(6);

            // 3-Mode Switcher: Preview, Edit, Split View
            string[] viewModes = new string[] { "🎨 高亮预览", "✏️ 自由编辑", "🌓 分栏对照" };
            for (int vm = 0; vm < viewModes.Length; vm++) {
                bool isVm = (ideViewMode == vm);
                if (GUILayout.Button(viewModes[vm], isVm ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(20))) {
                    ideViewMode = vm;
                    if (string.IsNullOrEmpty(ideCustomBuffer)) {
                        ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    }
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            string liveScript = GenerateLiveGmatScript(ideScriptViewMode);
            string currentScriptToUse = (ideViewMode != 0 && !string.IsNullOrEmpty(ideCustomBuffer)) ? ideCustomBuffer : liveScript;

            // Action Toolbar (Save & Sync, Export, External IDE, Copy, Reset, Solve)
            GUILayout.BeginHorizontal(nestedCardBoxStyle);

            GUI.enabled = (ideViewMode != 0) && !isSavingAndSyncing;
            if (GUILayout.Button(isSavingAndSyncing ? "⏳ 正在同步..." : "💾 保存并同步到任务序列", successButtonStyle, GUILayout.Height(22))) {
                SaveAndSyncIdeScriptToSequence(currentScriptToUse, vessel);
            }
            GUI.enabled = true;

            if (GUILayout.Button("💻 外部IDE打开", secondaryButtonStyle, GUILayout.Height(22))) {
                OpenScriptInExternalEditor(currentScriptToUse);
            }
            if (GUILayout.Button("📥 重载外部文件", secondaryButtonStyle, GUILayout.Height(22))) {
                ReloadScriptFromExternalFile();
            }

            if (GUILayout.Button("📋 复制代码", secondaryButtonStyle, GUILayout.Height(22))) {
                GUIUtility.systemCopyBuffer = GMATClient.SanitizeGmatScript(currentScriptToUse);
                ScreenMessages.PostScreenMessage("✅ GMAT 标准合规脚本已复制到剪贴板！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button("💾 导出脚本...", secondaryButtonStyle, GUILayout.Height(22))) {
                try {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string outputDir = GMATClient.GetOutputDir();
                    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                    string fullScript = (ideViewMode == 1) ? currentScriptToUse : GenerateLiveGmatScript(0);
                    string expPath = Path.Combine(outputDir, $"mission_{timestamp}.script");
                    string sanitized = GMATClient.SanitizeGmatScript(fullScript);
                    File.WriteAllText(expPath, sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_latest.script"), sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_run.script"), sanitized, new System.Text.UTF8Encoding(false));
                    ScreenMessages.PostScreenMessage("✅ GMAT 标准合规脚本已导出至: " + expPath, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage("❌ 导出失败: " + ex.Message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (GUILayout.Button("🚀 推送至 GMAT", actionButtonStyle, GUILayout.Height(22))) {
                try {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    string outputDir = GMATClient.GetOutputDir();
                    if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                    string fullScript = (ideViewMode == 1) ? currentScriptToUse : GenerateLiveGmatScript(0);
                    string expPath = Path.Combine(outputDir, $"mission_{timestamp}.script");
                    string sanitized = GMATClient.SanitizeGmatScript(fullScript);
                    File.WriteAllText(expPath, sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_latest.script"), sanitized, new System.Text.UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(outputDir, "mission_run.script"), sanitized, new System.Text.UTF8Encoding(false));
                    GMATClient.OpenGmatDesktop(expPath);
                    ScreenMessages.PostScreenMessage($"🚀 已推送合规脚本 ({Path.GetFileName(expPath)}) 至 GMAT 桌面端！", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception ex) {
                    ScreenMessages.PostScreenMessage("❌ 推送失败: " + ex.Message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (ideViewMode != 0) {
                if (GUILayout.Button("🔄 恢复自动生成", secondaryButtonStyle, GUILayout.Height(22))) {
                    ideCustomBuffer = GenerateLiveGmatScript(ideScriptViewMode);
                    ScreenMessages.PostScreenMessage("🔄 已重置为实时自动生成脚本！", 2.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            if (isPropagating) {
                if (GUILayout.Button("🛑 中断物理求解", deleteButtonStyle, GUILayout.Height(22))) {
                    GMATClient.AbortCurrentExecution();
                    isPropagating = false;
                    solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                    solveStageText = $"⚠️ 用户已手动中断 GMAT 求解 (耗时 {solveTotalTime:F1}s)";
                    solveProgress = 0f;
                    ScreenMessages.PostScreenMessage("⚠️ 已手动中断 GMAT 物理求解进程。", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            } else {
                if (GUILayout.Button((ideViewMode != 0) ? "⚡ 求解编辑脚本" : "⚡ 立即求解", actionButtonStyle, GUILayout.Height(22))) {
                    if (ideViewMode != 0 && !string.IsNullOrEmpty(ideCustomBuffer)) {
                        TriggerPropagationSolve(ideCustomBuffer);
                    } else {
                        TriggerPropagationSolve();
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // =========================================================================
            // MAIN EDITOR / VIEWER AREA (Mode 0: Preview, Mode 1: Edit, Mode 2: Split)
            // =========================================================================
            if (string.IsNullOrEmpty(ideCustomBuffer)) {
                ideCustomBuffer = liveScript;
            }

            if (ideViewMode == 0) {
                // 1. Full Syntax Highlighted Preview (Always real-time live script)
                ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                string[] scriptLines = liveScript.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int l = 0; l < scriptLines.Length; l++) {
                    string rawLine = scriptLines[l];
                    string lineNum = $"<color=#64748b>{l + 1,3}:</color> ";
                    string coloredLine = ColorizeGmatSyntax(rawLine);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(lineNum + coloredLine, codeBlockStyle);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            } else if (ideViewMode == 1) {
                // 2. Full Free Edit Mode with Floating Autocomplete
                GUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                GUI.SetNextControlName("GmatIdeTextArea");
                ideCustomBuffer = GUILayout.TextArea(ideCustomBuffer, ideEditorStyle ?? GUI.skin.textArea, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                GUILayout.EndScrollView();

                // Draw Floating Autocomplete Popup if typing a keyword/parameter
                DrawFloatingAutocompletePopup(vessel);
                GUILayout.EndVertical();
            } else {
                // 3. Split Mode: Left Editable TextArea | Right Real-time Highlighted Preview
                GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
                
                // Left Pane: Editor (50% Width)
                GUILayout.BeginVertical(GUILayout.Width(rightW * 0.49f), GUILayout.ExpandHeight(true));
                GUILayout.Label("<color=#94a3b8><b>✏️ 代码编辑区 (输入即触发补全)</b></color>", labelValueStyle);
                ideScriptScroll = GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                GUI.SetNextControlName("GmatIdeTextArea");
                ideCustomBuffer = GUILayout.TextArea(ideCustomBuffer, ideEditorStyle ?? GUI.skin.textArea, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
                GUILayout.EndScrollView();
                DrawFloatingAutocompletePopup(vessel);
                GUILayout.EndVertical();

                GUILayout.Space(4);

                // Right Pane: Synchronized Syntax Highlighted Output (50% Width)
                GUILayout.BeginVertical(GUILayout.Width(rightW * 0.49f), GUILayout.ExpandHeight(true));
                GUILayout.Label("<color=#38bdf8><b>🎨 实时语法高亮渲染</b></color>", labelValueStyle);
                GUILayout.BeginScrollView(ideScriptScroll, GUILayout.ExpandHeight(true));
                string[] scriptLines = (ideCustomBuffer ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int l = 0; l < scriptLines.Length; l++) {
                    string rawLine = scriptLines[l];
                    string lineNum = $"<color=#64748b>{l + 1,3}:</color> ";
                    string coloredLine = ColorizeGmatSyntax(rawLine);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(lineNum + coloredLine, codeBlockStyle);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制现代 IDE 风格的浮动智能自动补全悬浮窗 (Floating IntelliSense Popup)
        /// 支持方向键 ↑↓ 滚动高亮选择，Enter/Tab 插入，Esc 关闭
        /// </summary>
        private void DrawFloatingAutocompletePopup(Vessel vessel) {
            TextEditor te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (te == null || string.IsNullOrEmpty(ideCustomBuffer)) return;

            int cursor = te.cursorIndex;
            int wordStart;
            string currentWord = GetWordUnderCursor(ideCustomBuffer, cursor, out wordStart);
            ideCurrentWordUnderCursor = currentWord;
            ideCursorWordStart = wordStart;

            if (string.IsNullOrEmpty(currentWord) || currentWord.Length < 1) {
                ideSelectedCompletionIndex = 0;
                return;
            }

            var completions = GMATParameterRegistry.QueryIntelliSense(currentWord, vessel);
            if (completions == null || completions.Count == 0) {
                ideSelectedCompletionIndex = 0;
                return;
            }

            int maxItems = Mathf.Min(completions.Count, 7);
            if (ideSelectedCompletionIndex >= maxItems) ideSelectedCompletionIndex = 0;
            if (ideSelectedCompletionIndex < 0) ideSelectedCompletionIndex = maxItems - 1;

            // Handle Arrow Keys and Enter / Tab / Esc
            Event e = Event.current;
            if (e.type == EventType.KeyDown) {
                if (e.keyCode == KeyCode.DownArrow) {
                    ideSelectedCompletionIndex = (ideSelectedCompletionIndex + 1) % maxItems;
                    e.Use();
                } else if (e.keyCode == KeyCode.UpArrow) {
                    ideSelectedCompletionIndex = (ideSelectedCompletionIndex - 1 + maxItems) % maxItems;
                    e.Use();
                } else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab) {
                    if (ideSelectedCompletionIndex >= 0 && ideSelectedCompletionIndex < completions.Count) {
                        ApplyAutocompleteCompletion(completions[ideSelectedCompletionIndex].InsertText);
                        e.Use();
                        return;
                    }
                } else if (e.keyCode == KeyCode.Escape) {
                    ideCurrentWordUnderCursor = "";
                    e.Use();
                    return;
                }
            }

            // Floating Popup Container
            GUILayout.BeginVertical(nestedCardBoxStyle, GUILayout.Height(Mathf.Min(170, maxItems * 26 + 36)));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=#38bdf8><b>⚡ GMAT 智能补全</b></color> <color=#64748b>(匹配: \"{currentWord}\")</color>", labelValueStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#facc15><size=10>↑↓ 选择, Enter/Tab 插入, Esc 关闭</size></color>", labelValueStyle);
            GUILayout.EndHorizontal();

            ideAutocompleteScroll = GUILayout.BeginScrollView(ideAutocompleteScroll);
            for (int k = 0; k < maxItems; k++) {
                var itm = completions[k];
                bool isSelected = (k == ideSelectedCompletionIndex);
                GUILayout.BeginHorizontal();
                GUILayout.Label(isSelected ? "<color=#38bdf8>▶</color>" : itm.GetIcon(), GUILayout.Width(18));
                if (GUILayout.Button($"<b>{itm.Label}</b>  <color=#38bdf8>[{itm.TypeTag}]</color> <color=#94a3b8>{itm.Doc}</color>", isSelected ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Height(22))) {
                    ApplyAutocompleteCompletion(itm.InsertText);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private string GetWordUnderCursor(string text, int cursorIndex, out int wordStartIndex) {
            wordStartIndex = cursorIndex;
            if (string.IsNullOrEmpty(text) || cursorIndex <= 0 || cursorIndex > text.Length) return "";

            int start = cursorIndex - 1;
            while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '.' || text[start] == '_')) {
                start--;
            }
            wordStartIndex = start + 1;
            int length = cursorIndex - wordStartIndex;
            if (length <= 0) return "";
            return text.Substring(wordStartIndex, length);
        }

        private void ApplyAutocompleteCompletion(string completionText) {
            if (string.IsNullOrEmpty(completionText)) return;
            TextEditor te = GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) as TextEditor;
            if (te != null && !string.IsNullOrEmpty(ideCustomBuffer)) {
                int cursor = te.cursorIndex;
                int wordStart = ideCursorWordStart;
                if (wordStart < 0 || wordStart > ideCustomBuffer.Length) wordStart = cursor;

                string before = ideCustomBuffer.Substring(0, wordStart);
                string after = (cursor <= ideCustomBuffer.Length) ? ideCustomBuffer.Substring(cursor) : "";
                ideCustomBuffer = before + completionText + after;

                // Move cursor directly to the end of inserted word
                int newCursorPos = wordStart + completionText.Length;
                te.cursorIndex = newCursorPos;
                te.selectIndex = newCursorPos;
            } else {
                InsertIntoIdeBuffer(completionText);
            }
        }

        private void OpenScriptInExternalEditor(string scriptText) {
            try {
                string outputDir = GMATClient.GetOutputDir();
                if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);
                string scriptPath = Path.Combine(outputDir, "user_custom_edit.script");
                File.WriteAllText(scriptPath, scriptText, new System.Text.UTF8Encoding(false));

                System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo {
                    FileName = scriptPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                ScreenMessages.PostScreenMessage($"💻 已调用系统 API 与默认 IDE 打开文件: {Path.GetFileName(scriptPath)}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                try {
                    string scriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{scriptPath}\"");
                    ScreenMessages.PostScreenMessage($"📂 已在系统资源管理器中定位脚本文件", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                } catch (Exception) {
                    ScreenMessages.PostScreenMessage($"❌ 打开外部编辑器失败: {ex.Message}", 3.5f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
        }

        private void ReloadScriptFromExternalFile() {
            try {
                string scriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                if (File.Exists(scriptPath)) {
                    string loaded = File.ReadAllText(scriptPath, System.Text.Encoding.UTF8);
                    ideCustomBuffer = loaded;
                    ideViewMode = 1; // Switch to Edit mode
                    SaveAndSyncIdeScriptToSequence(loaded, FlightGlobals.ActiveVessel);
                } else {
                    ScreenMessages.PostScreenMessage("⚠️ 未找到外部临时脚本文件，请先点击【外部IDE打开】", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            } catch (Exception ex) {
                ScreenMessages.PostScreenMessage($"❌ 重载失败: {ex.Message}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void SaveAndSyncIdeScriptToSequence(string scriptContent, Vessel vessel) {
            if (string.IsNullOrEmpty(scriptContent)) return;
            isSavingAndSyncing = true;
            try {
                // Ensure full script is passed to import
                string fullScript = scriptContent;
                if (ideScriptViewMode == 1) { // Resources only
                    fullScript = scriptContent + "\nBeginMissionSequence;\n" + GenerateLiveGmatScript(2);
                } else if (ideScriptViewMode == 2) { // Sequence only
                    fullScript = GenerateLiveGmatScript(1) + "\n" + scriptContent;
                }

                string tempScriptPath = Path.Combine(GMATClient.GetOutputDir(), "user_custom_edit.script");
                File.WriteAllText(tempScriptPath, fullScript, new System.Text.UTF8Encoding(false));

                GMATClient.ImportScript(tempScriptPath, vessel, (ok, result, msg) => {
                    isSavingAndSyncing = false;
                    if (ok && result != null) {
                        ApplyImportResultToState(result);

                        // Retain user edited buffer
                        ideCustomBuffer = scriptContent;

                        ScreenMessages.PostScreenMessage($"✅ 脚本修改已保存！已同步至【资源管理】与【任务序列】({(result.sequence != null ? result.sequence.Count : 0)} 个步骤)。", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        ideCustomBuffer = scriptContent;
                        ScreenMessages.PostScreenMessage("💾 脚本已保存！(已保留在当前编辑器中)", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
            } catch (Exception ex) {
                isSavingAndSyncing = false;
                ScreenMessages.PostScreenMessage("❌ 保存失败: " + ex.Message, 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ApplyImportResultToState(GMATImportResult result) {
            if (result == null) return;

            if (result.sequence != null && result.sequence.Count > 0) {
                missionSequence = result.sequence;
                ClearSolutionResults();
            }

            if (GMATStateManager.Current != null) {
                // 1. Solvers
                if (result.solver_models != null && result.solver_models.Count > 0) {
                    GMATStateManager.Current.Solvers = new List<Model.GMATSolverModel>(result.solver_models);
                } else if (result.solvers != null && result.solvers.Count > 0) {
                    GMATStateManager.Current.Solvers.Clear();
                    for (int s = 0; s < result.solvers.Count; s++) {
                        string sName = result.solvers[s];
                        GMATStateManager.Current.Solvers.Add(new Model.GMATSolverModel {
                            Name = sName,
                            Algorithm = "NewtonRaphson",
                            DerivativeMethod = "ForwardDifference",
                            MaxIterations = 50,
                            ReportStyle = "Normal"
                        });
                    }
                }

                // 2. User Variables
                if (result.user_variables != null && result.user_variables.Count > 0) {
                    GMATStateManager.Current.Variables.Clear();
                    for (int uv = 0; uv < result.user_variables.Count; uv++) {
                        GMATStateManager.Current.Variables.Add(new Model.GMATVariableModel {
                            Name = result.user_variables[uv].name,
                            InitialValue = result.user_variables[uv].value,
                            Description = result.user_variables[uv].description
                        });
                    }
                }

                // 3. User Arrays
                if (result.user_arrays != null && result.user_arrays.Count > 0) {
                    GMATStateManager.Current.Arrays.Clear();
                    for (int ua = 0; ua < result.user_arrays.Count; ua++) {
                        GMATStateManager.Current.Arrays.Add(new Model.GMATArrayModel {
                            Name = result.user_arrays[ua].name,
                            Rows = result.user_arrays[ua].rows,
                            Cols = result.user_arrays[ua].cols,
                            Description = result.user_arrays[ua].description
                        });
                    }
                }

                // 4. User Strings
                if (result.user_strings != null && result.user_strings.Count > 0) {
                    GMATStateManager.Current.Strings.Clear();
                    for (int us = 0; us < result.user_strings.Count; us++) {
                        GMATStateManager.Current.Strings.Add(new Model.GMATStringModel {
                            Name = result.user_strings[us].name,
                            Value = result.user_strings[us].value,
                            Description = result.user_strings[us].description
                        });
                    }
                }

                // 5. Spacecraft & Hardware
                if (result.craft_dry_mass > 0) GMATStateManager.Current.Spacecraft.DryMassKg = result.craft_dry_mass;
                if (result.craft_drag_area > 0) GMATStateManager.Current.Spacecraft.DragArea = result.craft_drag_area;
                if (result.craft_cd > 0) GMATStateManager.Current.Spacecraft.Cd = result.craft_cd;
                if (result.craft_cr > 0) GMATStateManager.Current.Spacecraft.Cr = result.craft_cr;
                if (result.craft_srp_area > 0) GMATStateManager.Current.Spacecraft.SRPArea = result.craft_srp_area;
                if (!string.IsNullOrEmpty(result.craft_epoch)) GMATStateManager.Current.Spacecraft.EpochUtc = result.craft_epoch;
                if (!string.IsNullOrEmpty(result.craft_coord_system)) GMATStateManager.Current.Spacecraft.CoordinateSystem = result.craft_coord_system;

                if (result.craft_x != 0 || result.craft_y != 0 || result.craft_z != 0) {
                    GMATStateManager.Current.Spacecraft.PosKm = new Vector3d(result.craft_x, result.craft_y, result.craft_z);
                    GMATStateManager.Current.Spacecraft.VelKms = new Vector3d(result.craft_vx, result.craft_vy, result.craft_vz);
                }

                if (result.tanks != null && result.tanks.Count > 0) {
                    GMATStateManager.Current.Tanks = result.tanks;
                } else if (FlightGlobals.ActiveVessel != null) {
                    GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                }

                if (result.thrusters != null && result.thrusters.Count > 0) {
                    GMATStateManager.Current.Thrusters = result.thrusters;
                }
            }
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
