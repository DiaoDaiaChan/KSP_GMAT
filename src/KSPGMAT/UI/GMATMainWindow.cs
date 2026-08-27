using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPGMAT.UI {
    public class GMATMainWindow : MonoBehaviour {
        public static GMATMainWindow Instance { get; private set; }

        private static readonly Queue<Action> mainThreadQueue = new Queue<Action>();

        public static void RunOnMainThread(Action action) {
            if (action == null) return;
            lock (mainThreadQueue) {
                mainThreadQueue.Enqueue(action);
            }
        }

        private bool visible = false;
        private Rect windowRect = new Rect(80, 60, 880, 660);
        private int activeTab = 1; // Default to Mission Sequence for fast iteration
        private readonly string[] tabNames = new string[] { "🛰️ 飞船与动力学", "📋 任务序列与求解", "📈 变轨与轨迹明细", "⚙️ 设置" };

        private Vector2 scrollPosCraft = Vector2.zero;
        private Vector2 scrollPosSequence = Vector2.zero;
        private Vector2 scrollPosResults = Vector2.zero;

        // Active craft & multi-stage staging state
        private List<StageData> cachedStages = new List<StageData>();
        private HashSet<int> selectedStageIndices = new HashSet<int> { 0 };
        private List<ROEngineInfo> cachedEngines = new List<ROEngineInfo>();
        private int selectedEngineIndex = 0;
        private string syncStatusMessage = "就绪。随时可同步或求解。";
        private bool isPropagating = false;

        // Sub-Window: Spacecraft details
        private bool showCraftDetailsWindow = false;
        private Rect craftDetailsWindowRect = new Rect(100, 60, 720, 540);
        private Vector2 scrollPosCraftDetails = Vector2.zero;

        // Sub-Window 1: Simulation Trajectory Viewer
        private bool showSimulationImageWindow = false;
        private Rect simulationImageWindowRect = new Rect(80, 45, 780, 570);
        private Texture2D simulationTexture = null;
        private string currentViewMode = "dual";
        private string imageStatusText = "就绪。";

        // Sub-Window 2: Propagator & Force Model Config
        private bool showPropagatorConfigWindow = false;
        private Rect propagatorConfigWindowRect = new Rect(70, 35, 800, 590);
        private Dictionary<string, PropagatorConfig> propagatorConfigs = PropagatorConfig.CreateDefaultPresets();
        private string selectedPropagatorKey = "LEOProp";
        private Vector2 scrollPosPropConfig = Vector2.zero;

        // Mission Sequence & Results
        private List<MissionStep> missionSequence = new List<MissionStep>();
        private MissionResult latestResult = null;
        private string customScriptPath = @"D:\nocomp\gmat-win-R2026a\bin\Mission_EarthToLunarNRHO_1988.script";
        private bool showCustomScriptInput = false;
        private int openParamPickerStepIndex = -1;
        private int activeParamCategoryIndex = 0;

        // Propagator names and labels
        private static readonly string[] PropagatorNames = new string[] {
            "LEOProp",
            "CislunarProp",
            "LunarNRHOProp"
        };

        private static readonly string[] PropagatorLabels = new string[] {
            "🪐 近轨高保真积分 (NearBody)",
            "🌌 深空多体巡航积分 (DeepSpace)",
            "🌙 复杂三体/Halo积分 (LagrangeHalo)"
        };

        // Stop condition types and labels
        private static readonly string[] PropagateStopTypes = new string[] {
            "Time",      // ⏱️ 时间(秒)
            "Days",      // 📅 时间(天)
            "Apoapsis",  // 🔺 远拱点(Ap)
            "Periapsis", // 🔻 近拱点(Pe)
            "Altitude",  // 📏 目标高度(km)
            "RMAG",      // 🌐 距心半径(km)
            "TA",        // 📐 真近点角(°)
            "Node"       // 🧭 赤道交点(Node)
        };

        private static readonly string[] PropagateStopLabels = new string[] {
            "⏱️ 时间 (秒)",
            "📅 时间 (天)",
            "🔺 远拱点 (Ap)",
            "🔻 近拱点 (Pe)",
            "📏 目标高度 (km)",
            "🌐 距心半径 (km)",
            "📐 真近点角 (°)",
            "🧭 赤道交点 (Node)"
        };

        private int draggedStepIndex = -1;

        // Target goals and labels for TargetApoapsis / Targeter
        private static readonly string[] TargetGoalTypes = new string[] {
            "Apoapsis",      // 远拱点高度 (Ap Alt)
            "Periapsis",     // 近拱点高度 (Pe Alt)
            "GEO_CAM",       // 🌍 GEO联合变轨(圆化+倾角归零)
            "SMA",           // 半长轴 (SMA)
            "ECC",           // 离心率 (ECC)
            "INC",           // 轨道倾角 (INC)
            "RMAG",          // 距心半径 (RMAG)
            "LunarPerilune"  // 月球近心点 (Luna RMAG)
        };

        private static readonly string[] TargetGoalLabels = new string[] {
            "🔺 远拱点高度 (Ap Alt)",
            "🔻 近拱点高度 (Pe Alt)",
            "🌍 GEO圆化+倾角清零 (CAM)",
            "📏 轨道半长轴 (SMA)",
            "⭕ 轨道离心率 (ECC)",
            "📐 轨道倾角 (INC)",
            "🌐 距心绝对半径 (RMAG)",
            "🌙 月心近心点 (Luna RMAG)"
        };

        // GUI Styles
        private GUIStyle windowStyle;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;
        private GUIStyle cardBoxStyle;
        private GUIStyle labelValueStyle;
        private GUIStyle actionButtonStyle;
        private GUIStyle secondaryButtonStyle;
        private GUIStyle deleteButtonStyle;
        private GUIStyle successButtonStyle;
        private bool stylesInitialized = false;

        void Awake() {
            Instance = this;
            // Default template: Earth LEO Parking -> Prograde Burn -> Propagate to Apoapsis
            missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Time", duration = 1800.0 });
            missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", dv_v = 150.0, dv_n = 0.0, dv_b = 0.0 });
            missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis" });
        }

        void Start() {
            GMATClient.CheckConnection((ok, msg) => {});
        }

        void Update() {
            lock (mainThreadQueue) {
                while (mainThreadQueue.Count > 0) {
                    try {
                        mainThreadQueue.Dequeue()?.Invoke();
                    } catch (Exception ex) {
                        Debug.LogError("[KSPGMAT] Main thread dispatch error: " + ex);
                    }
                }
            }
        }

        public void Toggle() {
            visible = !visible;
            if (visible) {
                RefreshCraftData();
                LoadSimulationTexture();
            }
        }

        public void RefreshCraftData() {
            try {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null) {
                    cachedStages = MechJebInterop.GetStages(v);
                    if (selectedStageIndices.Count == 0 && cachedStages.Count > 0) {
                        selectedStageIndices.Add(0);
                    }

                    // Scan RO engines
                    cachedEngines = ROEngineInterop.ScanVesselEngines(v);
                    if (selectedEngineIndex >= cachedEngines.Count) {
                        selectedEngineIndex = 0;
                    }
                }
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Error in RefreshCraftData: " + ex.Message);
            }
        }

        public List<StageData> GetSelectedStagesList() {
            List<StageData> list = new List<StageData>();
            if (cachedStages == null || cachedStages.Count == 0) return list;
            for (int i = 0; i < cachedStages.Count; i++) {
                if (selectedStageIndices.Contains(i)) {
                    list.Add(cachedStages[i]);
                }
            }
            if (list.Count == 0 && cachedStages.Count > 0) {
                list.Add(cachedStages[0]);
            }
            return list;
        }

        public void LoadSimulationTexture(string path = null) {
            if (string.IsNullOrEmpty(path)) {
                path = @"D:\nocomp\gmat-win-R2026a\output\gmat_orbit_render.png";
            }
            if (System.IO.File.Exists(path)) {
                try {
                    byte[] data = System.IO.File.ReadAllBytes(path);
                    if (simulationTexture == null) simulationTexture = new Texture2D(2, 2);
                    simulationTexture.LoadImage(data);
                    imageStatusText = $"已载入 GMAT 仿真图 ({simulationTexture.width}x{simulationTexture.height})";
                } catch (Exception ex) {
                    imageStatusText = "载入图片失败: " + ex.Message;
                }
            } else {
                imageStatusText = "尚未生成仿真渲染图，请点击求解或重新渲染。";
            }
        }

        private void InitStyles() {
            if (stylesInitialized) return;

            Texture2D darkBg = MakeTex(2, 2, new Color(0.07f, 0.09f, 0.13f, 0.96f));
            Texture2D cardBg = MakeTex(2, 2, new Color(0.12f, 0.15f, 0.21f, 0.92f));
            Texture2D cyanBtn = MakeTex(2, 2, new Color(0.0f, 0.55f, 0.78f, 1.0f));
            Texture2D greenBtn = MakeTex(2, 2, new Color(0.14f, 0.62f, 0.36f, 1.0f));
            Texture2D redBtn = MakeTex(2, 2, new Color(0.72f, 0.22f, 0.22f, 1.0f));

            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = darkBg;
            windowStyle.normal.textColor = Color.white;
            windowStyle.fontSize = 13;
            windowStyle.fontStyle = FontStyle.Bold;

            headerStyle = new GUIStyle(GUI.skin.label);
            headerStyle.fontSize = 13;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = new Color(0.35f, 0.85f, 1.0f);

            subHeaderStyle = new GUIStyle(GUI.skin.label);
            subHeaderStyle.fontSize = 11;
            subHeaderStyle.fontStyle = FontStyle.Bold;
            subHeaderStyle.normal.textColor = new Color(0.85f, 0.85f, 0.60f);

            tabButtonStyle = new GUIStyle(GUI.skin.button);
            tabButtonStyle.normal.background = cardBg;
            tabButtonStyle.normal.textColor = new Color(0.75f, 0.80f, 0.85f);
            tabButtonStyle.fontSize = 11;
            tabButtonStyle.fontStyle = FontStyle.Bold;

            tabButtonActiveStyle = new GUIStyle(tabButtonStyle);
            tabButtonActiveStyle.normal.background = cyanBtn;
            tabButtonActiveStyle.normal.textColor = Color.white;

            cardBoxStyle = new GUIStyle(GUI.skin.box);
            cardBoxStyle.normal.background = cardBg;
            cardBoxStyle.padding = new RectOffset(8, 8, 8, 8);

            labelValueStyle = new GUIStyle(GUI.skin.label);
            labelValueStyle.fontSize = 11;
            labelValueStyle.normal.textColor = new Color(0.90f, 0.92f, 0.95f);

            actionButtonStyle = new GUIStyle(GUI.skin.button);
            actionButtonStyle.normal.background = cyanBtn;
            actionButtonStyle.normal.textColor = Color.white;
            actionButtonStyle.fontSize = 12;
            actionButtonStyle.fontStyle = FontStyle.Bold;

            successButtonStyle = new GUIStyle(GUI.skin.button);
            successButtonStyle.normal.background = greenBtn;
            successButtonStyle.normal.textColor = Color.white;
            successButtonStyle.fontSize = 12;
            successButtonStyle.fontStyle = FontStyle.Bold;

            secondaryButtonStyle = new GUIStyle(GUI.skin.button);
            secondaryButtonStyle.normal.background = cardBg;
            secondaryButtonStyle.normal.textColor = new Color(0.35f, 0.85f, 1.0f);
            secondaryButtonStyle.fontSize = 11;

            deleteButtonStyle = new GUIStyle(GUI.skin.button);
            deleteButtonStyle.normal.background = redBtn;
            deleteButtonStyle.normal.textColor = Color.white;
            deleteButtonStyle.fontSize = 11;
            deleteButtonStyle.fontStyle = FontStyle.Bold;

            stylesInitialized = true;
        }

        private Texture2D MakeTex(int width, int height, Color col) {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        void OnGUI() {
            if (!visible) return;
            InitStyles();

            GUI.backgroundColor = Color.white;
            windowRect = GUILayout.Window(924823, windowRect, DrawWindow, "⚡ GMAT-Principia Mission Planner", windowStyle, GUILayout.Width(880), GUILayout.Height(660));

            if (showCraftDetailsWindow) {
                craftDetailsWindowRect = GUILayout.Window(924824, craftDetailsWindowRect, DrawCraftDetailsWindow, "🛰️ GMAT 同步航天器完整参数快照", windowStyle, GUILayout.Width(720), GUILayout.Height(540));
            }

            if (showSimulationImageWindow) {
                simulationImageWindowRect = GUILayout.Window(924825, simulationImageWindowRect, DrawSimulationImageWindow, "🖼️ GMAT 轨道仿真图像渲染器", windowStyle, GUILayout.Width(780), GUILayout.Height(570));
            }

            if (showPropagatorConfigWindow) {
                propagatorConfigWindowRect = GUILayout.Window(924826, propagatorConfigWindowRect, DrawPropagatorConfigWindow, "⚙️ GMAT 传播子与高精度受力模型深度配置", windowStyle, GUILayout.Width(800), GUILayout.Height(590));
            }
        }

        private void DrawWindow(int windowID) {
            try {
                Vessel vessel = FlightGlobals.ActiveVessel;

                // Top Status Bar
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🛰️ 目标: <b>{(vessel != null ? vessel.vesselName : "无活跃飞船")}</b>", headerStyle);
                GUILayout.FlexibleSpace();
                Color statusColor = GMATClient.IsConnected ? Color.green : Color.red;
                GUI.contentColor = statusColor;
                GUILayout.Label(GMATClient.ServerStatusText, GUILayout.Height(20));
                GUI.contentColor = Color.white;
                if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                    visible = false;
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // Tab Bar
                GUILayout.BeginHorizontal();
                for (int i = 0; i < tabNames.Length; i++) {
                    GUIStyle style = (activeTab == i) ? tabButtonActiveStyle : tabButtonStyle;
                    if (GUILayout.Button(tabNames[i], style, GUILayout.Height(28))) {
                        activeTab = i;
                        if (activeTab == 0) RefreshCraftData();
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                // Tab Content
                switch (activeTab) {
                    case 0:
                        DrawTabCraftAndOrbit(vessel);
                        break;
                    case 1:
                        DrawTabMissionSequence(vessel);
                        break;
                    case 2:
                        DrawTabResultsAndPrincipia(vessel);
                        break;
                    case 3:
                        DrawTabSettings();
                        break;
                }

                GUI.DragWindow();
            } catch (Exception ex) {
                GUILayout.Label("渲染异常: " + ex.Message, labelValueStyle);
                GUI.DragWindow();
            }
        }

        private void DrawTabCraftAndOrbit(Vessel vessel) {
            if (vessel == null) {
                GUILayout.Label("请在飞行场景中选择一艘飞船。", labelValueStyle);
                return;
            }

            // Two-column layout: Left = Orbit & Staging, Right = RO Engines & Tanks
            GUILayout.BeginHorizontal();

            // Left Column (Width: 425px)
            GUILayout.BeginVertical(GUILayout.Width(425));

            // Orbit & ICRF
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("🌍 轨道根数与引力环境 (Principia ICRF)", headerStyle);
            GUILayout.Label($"中心天体: <b>{vessel.mainBody.name}</b> (SOI) | UT: {Planetarium.GetUniversalTime():F1} s", labelValueStyle);

            Vector3d pos_km, vel_kms;
            bool ok = PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);
            if (ok) {
                GUILayout.Label($"ICRF 相对位置: r={pos_km.magnitude:F1} km (X={pos_km.x:F1}, Y={pos_km.y:F1}, Z={pos_km.z:F1})", labelValueStyle);
                GUILayout.Label($"ICRF 速度矢量: v={vel_kms.magnitude:F3} km/s (Vx={vel_kms.x:F3}, Vy={vel_kms.y:F3}, Vz={vel_kms.z:F3})", labelValueStyle);
            } else {
                GUILayout.Label("无法获取 Principia 状态向量，使用备用开普勒根数。", labelValueStyle);
            }

            if (vessel.orbit != null) {
                GUILayout.Label($"半长轴: <b>{vessel.orbit.semiMajorAxis / 1000.0:F1} km</b> | 离心率: <b>{vessel.orbit.eccentricity:F4}</b> | 倾角: <b>{vessel.orbit.inclination:F2}°</b>", labelValueStyle);
                GUILayout.Label($"近拱点高度: <b>{vessel.orbit.PeA / 1000.0:F1} km</b> | 远拱点高度: <b>{vessel.orbit.ApA / 1000.0:F1} km</b>", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // MechJeb Staging with Multi-Stage Checkboxes
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🚀 MechJeb 分级多选 (勾选包含的分级)", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄 刷新", secondaryButtonStyle, GUILayout.Width(50), GUILayout.Height(20))) {
                RefreshCraftData();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"飞船当前总湿重: <b>{vessel.totalMass:F3} 吨</b>", labelValueStyle);
            if (cachedStages != null && cachedStages.Count > 0) {
                double combinedDv = 0.0;
                double combinedFuel = 0.0;
                double combinedStartMass = 0.0;
                double minDry = double.MaxValue;

                for (int i = 0; i < cachedStages.Count; i++) {
                    var st = cachedStages[i];
                    bool isSel = selectedStageIndices.Contains(i);
                    GUILayout.BeginHorizontal();
                    bool newSel = GUILayout.Toggle(isSel, $" <b>Stage {st.stageNumber}</b>: Δv <b>{st.deltaV:F0}</b>m/s | 湿重 {st.wetMassTonnes:F2}t | 燃料 {st.fuelMassTonnes:F2}t | 干重 {st.dryMassTonnes:F2}t", GUILayout.Height(22));
                    if (newSel != isSel) {
                        if (newSel) selectedStageIndices.Add(i);
                        else if (selectedStageIndices.Count > 1) selectedStageIndices.Remove(i);
                    }
                    GUILayout.EndHorizontal();

                    if (selectedStageIndices.Contains(i)) {
                        combinedDv += st.deltaV;
                        combinedFuel += st.fuelMassTonnes;
                        if (st.wetMassTonnes > combinedStartMass) combinedStartMass = st.wetMassTonnes;
                        if (st.dryMassTonnes < minDry) minDry = st.dryMassTonnes;
                    }
                }

                if (minDry == double.MaxValue) minDry = vessel.totalMass * 0.4;
                if (combinedStartMass <= 0) combinedStartMass = vessel.totalMass;

                GUILayout.Space(4);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=#55ffff><b>已选多级组合统计 (Combined Staging Stack):</b></color>", labelValueStyle);
                GUILayout.Label($"总设计 ΔV: <b>{combinedDv:F0} m/s</b> | 初始湿重: <b>{combinedStartMass:F2} 吨</b>", labelValueStyle);
                GUILayout.Label($"可用推进剂: <b>{combinedFuel:F2} 吨</b> | 最终干重: <b>{minDry:F2} 吨</b>", labelValueStyle);
                GUILayout.EndVertical();
            } else {
                GUILayout.Label("未检测到 MechJeb 分级，使用当前飞船整体作为单阶段。", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Right Column (Width: 425px)
            GUILayout.BeginVertical(GUILayout.Width(425));

            // RO Engines & Tanks
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔥 RealismOverhaul 推进系统", headerStyle);
            GUILayout.FlexibleSpace();
            bool roOk = ROEngineInterop.IsRealismOverhaulInstalled();
            GUI.contentColor = roOk ? Color.green : new Color(1f, 0.6f, 0.2f);
            GUILayout.Label(roOk ? "✅ RO/RealFuels 就绪" : "⚠️ 未检测到 RO", GUILayout.Height(20));
            GUI.contentColor = Color.white;
            GUILayout.EndHorizontal();

            if (cachedEngines != null && cachedEngines.Count > 0) {
                GUILayout.Label("选择机动发动机 (自动匹配推进剂储罐与推力参数):", labelValueStyle);
                for (int i = 0; i < cachedEngines.Count; i++) {
                    var eng = cachedEngines[i];
                    bool isSel = (selectedEngineIndex == i);
                    GUI.backgroundColor = isSel ? new Color(0.2f, 0.8f, 0.5f) : Color.white;
                    string engBtn = $"{(isSel ? "🔘 " : "")}[Stage {eng.stageNumber}] {eng.title} ({eng.thrustKN:F1}kN, Isp {eng.ispVac:F0}s)";
                    if (GUILayout.Button(engBtn, GUILayout.Height(24))) {
                        selectedEngineIndex = i;
                    }
                    GUI.backgroundColor = Color.white;
                }

                if (selectedEngineIndex < cachedEngines.Count) {
                    var curEng = cachedEngines[selectedEngineIndex];
                    GUILayout.Space(4);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label($"推进剂组合: <color=#55ffff>{curEng.propellantDesc}</color>", labelValueStyle);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("推进剂质量:", labelValueStyle, GUILayout.Width(75));
                    string sMass = GUILayout.TextField(curEng.fuelMassKg.ToString("F1"), GUILayout.Width(70));
                    double.TryParse(sMass, out curEng.fuelMassKg);
                    GUILayout.Label("kg", labelValueStyle, GUILayout.Width(25));

                    GUILayout.Label("推进剂密度:", labelValueStyle, GUILayout.Width(75));
                    string sRho = GUILayout.TextField(curEng.fuelDensityKgM3.ToString("F1"), GUILayout.Width(70));
                    double.TryParse(sRho, out curEng.fuelDensityKgM3);
                    GUILayout.Label("kg/m³", labelValueStyle, GUILayout.Width(40));
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("储罐容积:", labelValueStyle, GUILayout.Width(75));
                    string sVol = GUILayout.TextField(curEng.tankVolumeM3.ToString("F3"), GUILayout.Width(70));
                    double.TryParse(sVol, out curEng.tankVolumeM3);
                    GUILayout.Label("m³", labelValueStyle, GUILayout.Width(25));

                    GUILayout.Label("调节压力:", labelValueStyle, GUILayout.Width(75));
                    string sPres = GUILayout.TextField(curEng.tankPressureKPa.ToString("F0"), GUILayout.Width(70));
                    double.TryParse(sPres, out curEng.tankPressureKPa);
                    GUILayout.Label("kPa", labelValueStyle, GUILayout.Width(40));
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"真空推力: <b>{curEng.thrustKN:F1} kN</b> | 真空比冲: <b>{curEng.ispVac:F0} s</b>", labelValueStyle);
                    GUILayout.EndVertical();
                }
            } else {
                GUILayout.Label("当前飞船未发现可用发动机，将使用默认动力学参数。", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // Synchronize & Sub-window Button Card
            GUILayout.BeginVertical(cardBoxStyle);
            if (GUILayout.Button("🚀 同步航天器动力学至 GMAT (前往任务序列)", actionButtonStyle, GUILayout.Height(34))) {
                string engTitle = (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) ? cachedEngines[selectedEngineIndex].title : "默认动力";
                syncStatusMessage = $"已同步飞船！中心天体: {vessel.mainBody.name}, 选定 {selectedStageIndices.Count} 个分级, 发动机: {engTitle}";
                activeTab = 1; // jump to mission sequence
            }
            if (GUILayout.Button("🔍 查看已同步航天器详细参数 (快照子界面)", secondaryButtonStyle, GUILayout.Height(26))) {
                showCraftDetailsWindow = true;
            }
            if (!string.IsNullOrEmpty(syncStatusMessage)) {
                GUILayout.Label(syncStatusMessage, labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawTabMissionSequence(Vessel vessel) {
            // Split-Screen Two-Column Layout: Left = Sequence Steps & Presets; Right = Execution Hub & Pinned Results
            GUILayout.BeginHorizontal();

            // ==========================================
            // LEFT COLUMN: Sequence Editor (Width: 535px)
            // ==========================================
            GUILayout.BeginVertical(GUILayout.Width(535));

            // Presets & Import Header
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("📥 任务序列模板与智能导入 (自动对齐当前世界时间)", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(showCustomScriptInput ? "▲ 折叠" : "📂 导入外部脚本...", secondaryButtonStyle, GUILayout.Height(20))) {
                showCustomScriptInput = !showCustomScriptInput;
            }
            GUILayout.EndHorizontal();

            // Quick Preset Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🌙 地月NRHO任务 (1988)", secondaryButtonStyle, GUILayout.Height(24))) {
                ImportScriptFile(@"D:\nocomp\gmat-win-R2026a\bin\Mission_EarthToLunarNRHO_1988.script");
            }
            if (GUILayout.Button("🛰️ GTO/GEO (交点点火+联合变轨消除倾角)", secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Clear();
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Node", label = "Coast to Node" });
                missionSequence.Add(new MissionStep { type = "TargetApoapsis", target_goal = "Apoapsis", target_alt = 35786.0, label = "GTO Injection at Node" });
                missionSequence.Add(new MissionStep { type = "Targeter", target_goal = "GEO_CAM", target_alt = 35786.0, label = "GEO Circularize & Plane Change" });
                ScreenMessages.PostScreenMessage("✅ 已载入 GTO 交点入轨 + GEO 远地点联合圆化与倾角清零模板", 4.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button("🔄 基础轨道积分", secondaryButtonStyle, GUILayout.Height(24))) {
                missionSequence.Clear();
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Periapsis" });
                missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", dv_v = 150.0 });
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis" });
            }
            GUILayout.EndHorizontal();

            if (showCustomScriptInput) {
                GUILayout.Space(3);
                GUILayout.BeginHorizontal();
                customScriptPath = GUILayout.TextField(customScriptPath);
                if (GUILayout.Button("载入", actionButtonStyle, GUILayout.Width(55), GUILayout.Height(22))) {
                    ImportScriptFile(customScriptPath);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // Scrollable Steps List
            scrollPosSequence = GUILayout.BeginScrollView(scrollPosSequence, cardBoxStyle, GUILayout.Height(390));

            if (missionSequence.Count == 0) {
                GUILayout.Label("任务序列为空。请点击下方按钮添加步骤，或点击上方载入模板。", labelValueStyle);
            }

            int stepToMoveUp = -1;
            int stepToMoveDown = -1;
            int stepToDelete = -1;
            int dropTargetIndex = -1;

            for (int i = 0; i < missionSequence.Count; i++) {
                var step = missionSequence[i];
                bool isBeingDragged = (draggedStepIndex == i);

                Color oldBoxColor = GUI.backgroundColor;
                if (isBeingDragged) {
                    GUI.backgroundColor = new Color(0.3f, 0.75f, 1.0f, 0.9f);
                }

                GUILayout.BeginVertical(GUI.skin.box);
                GUI.backgroundColor = oldBoxColor;

                GUILayout.BeginHorizontal();

                // 1. Move Up Button
                GUI.enabled = (i > 0);
                if (GUILayout.Button("▲", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    stepToMoveUp = i;
                }

                // 2. Move Down Button
                GUI.enabled = (i < missionSequence.Count - 1);
                if (GUILayout.Button("▼", secondaryButtonStyle, GUILayout.Width(22), GUILayout.Height(20))) {
                    stepToMoveDown = i;
                }
                GUI.enabled = true;

                // 3. Drag Handle / Drop Target Button
                string dragBtnText = isBeingDragged ? "✊ 释放" : "☰ 拖动";
                if (GUILayout.Button(dragBtnText, isBeingDragged ? successButtonStyle : secondaryButtonStyle, GUILayout.Width(52), GUILayout.Height(20))) {
                    if (draggedStepIndex == i) {
                        draggedStepIndex = -1;
                    } else if (draggedStepIndex >= 0) {
                        dropTargetIndex = i;
                    } else {
                        draggedStepIndex = i;
                    }
                }

                // Drag detection via mouse press on drag handle
                Rect lastDragRect = GUILayoutUtility.GetLastRect();
                if (Event.current.type == EventType.MouseDown && lastDragRect.Contains(Event.current.mousePosition)) {
                    draggedStepIndex = i;
                    Event.current.Use();
                }

                string goalBadge = (step.type == "TargetApoapsis" || step.type == "Targeter") ? $" [{step.target_goal}]" : "";
                string typeBadge = step.type == "Propagate" ? $"🌐 轨道积分 [{step.propagator}]" :
                                   step.type == "ImpulsiveBurn" ? $"💥 脉冲机动 {(string.IsNullOrEmpty(step.label) ? "" : " - " + step.label)}" :
                                   step.type == "FiniteBurn" ? "🔥 有限推力点火" :
                                   (step.type == "TargetApoapsis" || step.type == "Targeter") ? $"🎯 目标变轨求解{goalBadge}" :
                                   step.type == "Equation" ? "📐 状态赋值 (Equation)" : step.type;
                GUILayout.Label($"#{i + 1} <b>{typeBadge}</b>", labelValueStyle);
                GUILayout.FlexibleSpace();
                if (step.type == "Propagate") {
                    if (GUILayout.Button("⚙️ 配置力模型", secondaryButtonStyle, GUILayout.Height(18))) {
                        selectedPropagatorKey = step.propagator;
                        showPropagatorConfigWindow = true;
                    }
                }
                if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(22), GUILayout.Height(18))) {
                    stepToDelete = i;
                }
                GUILayout.EndHorizontal();

                if (step.type == "Propagate") {
                    // Propagator Function Selection Call
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("函数传播子:", labelValueStyle, GUILayout.Width(75));
                    int pIdx = Array.IndexOf(PropagatorNames, step.propagator);
                    if (pIdx < 0) pIdx = 0;

                    if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20))) {
                        pIdx = (pIdx - 1 + PropagatorNames.Length) % PropagatorNames.Length;
                        step.propagator = PropagatorNames[pIdx];
                    }
                    if (GUILayout.Button(PropagatorLabels[pIdx], secondaryButtonStyle, GUILayout.Height(20))) {
                        pIdx = (pIdx + 1) % PropagatorNames.Length;
                        step.propagator = PropagatorNames[pIdx];
                    }
                    if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20))) {
                        pIdx = (pIdx + 1) % PropagatorNames.Length;
                        step.propagator = PropagatorNames[pIdx];
                    }
                    GUILayout.EndHorizontal();

                    // Stop Condition
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("停止条件:", labelValueStyle, GUILayout.Width(75));
                    int curIdx = Array.IndexOf(PropagateStopTypes, step.stop_type);
                    if (curIdx < 0) curIdx = 0;

                    if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20))) {
                        curIdx = (curIdx - 1 + PropagateStopTypes.Length) % PropagateStopTypes.Length;
                        step.stop_type = PropagateStopTypes[curIdx];
                    }
                    if (GUILayout.Button(PropagateStopLabels[curIdx], secondaryButtonStyle, GUILayout.Height(20))) {
                        curIdx = (curIdx + 1) % PropagateStopTypes.Length;
                        step.stop_type = PropagateStopTypes[curIdx];
                    }
                    if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20))) {
                        curIdx = (curIdx + 1) % PropagateStopTypes.Length;
                        step.stop_type = PropagateStopTypes[curIdx];
                    }
                    GUILayout.EndHorizontal();

                    if (step.stop_type == "Time") {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("经过秒数 (s):", labelValueStyle, GUILayout.Width(90));
                        string sVal = GUILayout.TextField(step.duration.ToString("F0"), GUILayout.Width(75));
                        double.TryParse(sVal, out step.duration);
                        GUILayout.Label("(指定秒数后停止)", labelValueStyle);
                        GUILayout.EndHorizontal();
                    } else if (step.stop_type == "Days") {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("经过天数 (d):", labelValueStyle, GUILayout.Width(90));
                        string sVal = GUILayout.TextField(step.duration.ToString("F2"), GUILayout.Width(75));
                        double.TryParse(sVal, out step.duration);
                        GUILayout.Label("(地月转移/星际巡航)", labelValueStyle);
                        GUILayout.EndHorizontal();
                    } else if (step.stop_type == "Apoapsis") {
                        GUILayout.Label("<color=#7bf>▶ 自动积分至轨道最高点 (远地点/远月点)</color>", labelValueStyle);
                    } else if (step.stop_type == "Periapsis") {
                        GUILayout.Label("<color=#7bf>▶ 自动积分至轨道最低点 (近地点/近月点)</color>", labelValueStyle);
                    } else if (step.stop_type == "Altitude") {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("目标高度 (km):", labelValueStyle, GUILayout.Width(90));
                        string sVal = GUILayout.TextField(step.target_val.ToString("F1"), GUILayout.Width(75));
                        double.TryParse(sVal, out step.target_val);
                        GUILayout.Label("(相对海平面/天体表面)", labelValueStyle);
                        GUILayout.EndHorizontal();
                    } else if (step.stop_type == "RMAG") {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("距心半径 (km):", labelValueStyle, GUILayout.Width(90));
                        string sVal = GUILayout.TextField(step.target_val.ToString("F1"), GUILayout.Width(75));
                        double.TryParse(sVal, out step.target_val);
                        GUILayout.Label("(如 42164km GEO, 384400km 月球)", labelValueStyle);
                        GUILayout.EndHorizontal();
                    } else if (step.stop_type == "TA") {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("真近点角 (°):", labelValueStyle, GUILayout.Width(90));
                        string sVal = GUILayout.TextField(step.target_val.ToString("F1"), GUILayout.Width(75));
                        double.TryParse(sVal, out step.target_val);
                        GUILayout.Label("(0°~360°)", labelValueStyle);
                        GUILayout.EndHorizontal();
                    } else if (step.stop_type == "Node") {
                        GUILayout.Label("<color=#7bf>▶ 自动积分至赤道交点 (纬度 Latitude = 0°)</color>", labelValueStyle);
                    }
                } else if (step.type == "Equation") {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("变量: Sat.", labelValueStyle, GUILayout.Width(65));
                    string oldVar = step.variable ?? "";
                    step.variable = GUILayout.TextField(oldVar, GUILayout.Width(150));
                    GUILayout.Label("=", labelValueStyle, GUILayout.Width(15));
                    step.expression = GUILayout.TextField(step.expression ?? "0.0", GUILayout.Width(75));

                    bool isPickerOpen = (openParamPickerStepIndex == i);
                    GUI.backgroundColor = isPickerOpen ? new Color(0.2f, 0.8f, 0.5f) : Color.white;
                    if (GUILayout.Button(isPickerOpen ? "▲ 收起库" : "📋 常用参数库", secondaryButtonStyle, GUILayout.Width(90), GUILayout.Height(20))) {
                        openParamPickerStepIndex = isPickerOpen ? -1 : i;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    // Real-Time Dynamic Autocompletion Suggestions
                    if (!string.IsNullOrEmpty(step.variable) && openParamPickerStepIndex != i) {
                        var matches = GMATParameterRegistry.FindMatches(step.variable, 3);
                        if (matches.Count > 0 && !(matches.Count == 1 && matches[0].Name.Equals(step.variable, StringComparison.OrdinalIgnoreCase))) {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("<color=#55ffff>💡 自动补全:</color>", labelValueStyle, GUILayout.Width(75));
                            for (int m = 0; m < matches.Count; m++) {
                                var match = matches[m];
                                if (GUILayout.Button($"{match.Name}", secondaryButtonStyle, GUILayout.Height(18))) {
                                    step.variable = match.Name;
                                    if (string.IsNullOrEmpty(step.expression) || step.expression == "0.0") {
                                        step.expression = match.Example;
                                    }
                                }
                            }
                            GUILayout.EndHorizontal();
                        }
                    }

                    // Categorized Quick Insert Grid (if opened for this step)
                    if (openParamPickerStepIndex == i) {
                        GUILayout.Space(2);
                        GUILayout.BeginVertical(GUI.skin.box);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("<b>选择参数分类:</b>", subHeaderStyle);
                        GUILayout.FlexibleSpace();
                        for (int c = 0; c < GMATParameterRegistry.Categories.Length; c++) {
                            bool isCatSel = (activeParamCategoryIndex == c);
                            GUI.backgroundColor = isCatSel ? new Color(0.3f, 0.8f, 1.0f) : Color.white;
                            if (GUILayout.Button(GMATParameterRegistry.Categories[c], secondaryButtonStyle, GUILayout.Height(20))) {
                                activeParamCategoryIndex = c;
                            }
                            GUI.backgroundColor = Color.white;
                        }
                        GUILayout.EndHorizontal();

                        // Render parameters in selected category
                        string curCat = GMATParameterRegistry.Categories[activeParamCategoryIndex];
                        var catParams = GMATParameterRegistry.GetByCategory(curCat);
                        GUILayout.Space(2);
                        for (int p = 0; p < catParams.Count; p++) {
                            var item = catParams[p];
                            GUILayout.BeginHorizontal();
                            if (GUILayout.Button($"➕ {item.Name}", secondaryButtonStyle, GUILayout.Width(160), GUILayout.Height(20))) {
                                step.variable = item.Name;
                                if (string.IsNullOrEmpty(step.expression) || step.expression == "0.0") {
                                    step.expression = item.Example;
                                }
                                openParamPickerStepIndex = -1; // close picker after selection
                            }
                            GUILayout.Label($"<color=#aaaaaa>{item.Description} (例: {item.Example})</color>", labelValueStyle);
                            GUILayout.EndHorizontal();
                        }
                        GUILayout.EndVertical();
                    }
                } else if (step.type == "ImpulsiveBurn") {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("顺行ΔV (m/s):", labelValueStyle, GUILayout.Width(85));
                    string sV = GUILayout.TextField(step.dv_v.ToString("F1"), GUILayout.Width(55));
                    double.TryParse(sV, out step.dv_v);

                    GUILayout.Label("法向:", labelValueStyle, GUILayout.Width(35));
                    string sN = GUILayout.TextField(step.dv_n.ToString("F1"), GUILayout.Width(50));
                    double.TryParse(sN, out step.dv_n);

                    GUILayout.Label("径向:", labelValueStyle, GUILayout.Width(35));
                    string sB = GUILayout.TextField(step.dv_b.ToString("F1"), GUILayout.Width(50));
                    double.TryParse(sB, out step.dv_b);
                    GUILayout.EndHorizontal();
                } else if (step.type == "FiniteBurn") {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("点火时间 (s):", labelValueStyle, GUILayout.Width(85));
                    string sDur = GUILayout.TextField(step.duration.ToString("F0"), GUILayout.Width(65));
                    double.TryParse(sDur, out step.duration);
                    GUILayout.Label("(使用选定发动机真实质量流积分)", labelValueStyle);
                    GUILayout.EndHorizontal();
                } else if (step.type == "TargetApoapsis" || step.type == "Targeter") {
                    // 1. Goal Selector
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("求解目标:", labelValueStyle, GUILayout.Width(75));
                    int gIdx = Array.IndexOf(TargetGoalTypes, step.target_goal);
                    if (gIdx < 0) gIdx = 0;

                    if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20))) {
                        gIdx = (gIdx - 1 + TargetGoalTypes.Length) % TargetGoalTypes.Length;
                        step.target_goal = TargetGoalTypes[gIdx];
                    }
                    if (GUILayout.Button(TargetGoalLabels[gIdx], secondaryButtonStyle, GUILayout.Height(20))) {
                        gIdx = (gIdx + 1) % TargetGoalTypes.Length;
                        step.target_goal = TargetGoalTypes[gIdx];
                    }
                    if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20))) {
                        gIdx = (gIdx + 1) % TargetGoalTypes.Length;
                        step.target_goal = TargetGoalTypes[gIdx];
                    }
                    GUILayout.EndHorizontal();

                    // 2. Target Value Input
                    GUILayout.BeginHorizontal();
                    string unitStr = (step.target_goal == "ECC") ? "(0.0~1.0)" : (step.target_goal == "INC") ? "(°)" : "(km)";
                    GUILayout.Label($"目标数值 {unitStr}:", labelValueStyle, GUILayout.Width(100));

                    double curVal = (step.target_goal == "Apoapsis") ? step.target_alt : step.target_val;
                    if (curVal == 0.0 && (step.target_goal == "Apoapsis" || step.target_goal == "GEO_CAM")) curVal = 35786.0;
                    if (curVal == 0.0 && step.target_goal == "Periapsis") curVal = 200.0;
                    if (curVal == 0.0 && step.target_goal == "SMA") curVal = 24368.0;
                    if (curVal == 0.0 && step.target_goal == "LunarPerilune") curVal = 3229.0;

                    string sVal = GUILayout.TextField(curVal.ToString(step.target_goal == "ECC" ? "F4" : "F1"), GUILayout.Width(75));
                    double parsedVal;
                    if (double.TryParse(sVal, out parsedVal)) {
                        step.target_alt = parsedVal;
                        step.target_val = parsedVal;
                    }
                    double totalDv = Math.Sqrt(step.dv_v * step.dv_v + step.dv_n * step.dv_n + step.dv_b * step.dv_b);
                    if (totalDv > 0.1) {
                        if (Math.Abs(step.dv_n) > 0.1 || Math.Abs(step.dv_b) > 0.1) {
                            GUILayout.Label($"<color=#55ff55>▶ 求解: 总ΔV={totalDv:F1} (顺行:{step.dv_v:F1}, 法向:{step.dv_n:F1}) m/s</color>", labelValueStyle);
                        } else {
                            GUILayout.Label($"<color=#55ff55>▶ 求解结果: ΔV = {step.dv_v:F1} m/s</color>", labelValueStyle);
                        }
                    } else {
                        GUILayout.Label("(微分校正器自动求解)", labelValueStyle);
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();

                // Drop target detection when mouse is released on this box
                if (draggedStepIndex >= 0 && draggedStepIndex != i && Event.current.type == EventType.MouseUp) {
                    Rect boxRect = GUILayoutUtility.GetLastRect();
                    if (boxRect.Contains(Event.current.mousePosition)) {
                        dropTargetIndex = i;
                    }
                }

                GUILayout.Space(3);
            }

            // Post-loop reorder executions
            if (dropTargetIndex >= 0 && draggedStepIndex >= 0 && dropTargetIndex != draggedStepIndex) {
                var item = missionSequence[draggedStepIndex];
                missionSequence.RemoveAt(draggedStepIndex);
                missionSequence.Insert(dropTargetIndex, item);
                draggedStepIndex = -1;
                Event.current.Use();
            }

            if (stepToMoveUp > 0) {
                var temp = missionSequence[stepToMoveUp];
                missionSequence[stepToMoveUp] = missionSequence[stepToMoveUp - 1];
                missionSequence[stepToMoveUp - 1] = temp;
            } else if (stepToMoveDown >= 0 && stepToMoveDown < missionSequence.Count - 1) {
                var temp = missionSequence[stepToMoveDown];
                missionSequence[stepToMoveDown] = missionSequence[stepToMoveDown + 1];
                missionSequence[stepToMoveDown + 1] = temp;
            } else if (stepToDelete >= 0 && stepToDelete < missionSequence.Count) {
                missionSequence.RemoveAt(stepToDelete);
                if (draggedStepIndex == stepToDelete) draggedStepIndex = -1;
            }

            GUILayout.EndScrollView();

            GUILayout.Space(4);

            // Add Step Buttons Bar
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("➕ 积分", secondaryButtonStyle, GUILayout.Height(26))) {
                missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Time", duration = 3600.0 });
            }
            if (GUILayout.Button("➕ 脉冲", secondaryButtonStyle, GUILayout.Height(26))) {
                missionSequence.Add(new MissionStep { type = "ImpulsiveBurn", dv_v = 150.0 });
            }
            if (GUILayout.Button("➕ 方程赋值", secondaryButtonStyle, GUILayout.Height(26))) {
                missionSequence.Add(new MissionStep { type = "Equation", variable = "EarthMoonRot.X", expression = "-173.0" });
            }
            if (GUILayout.Button("➕ 有限推力", secondaryButtonStyle, GUILayout.Height(26))) {
                missionSequence.Add(new MissionStep { type = "FiniteBurn", duration = 60.0 });
            }
            if (GUILayout.Button("🎯 目标求解", secondaryButtonStyle, GUILayout.Height(26))) {
                missionSequence.Add(new MissionStep { type = "TargetApoapsis", target_alt = 35786.0 });
            }
            if (GUILayout.Button("🗑️ 清空", deleteButtonStyle, GUILayout.Width(45), GUILayout.Height(26))) {
                missionSequence.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(8);

            // =======================================================
            // RIGHT COLUMN: Pinned Execution & Live Results (315px)
            // (PERMANENTLY PINNED - NEVER REQUIRES SCROLLING!)
            // =======================================================
            GUILayout.BeginVertical(GUILayout.Width(315));

            // Pinned Execution Box
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("⚡ 任务执行中枢 (Pinned)", headerStyle);

            GUI.enabled = !isPropagating && vessel != null;
            string runBtnText = isPropagating ? "⏳ GMAT 任务序列求解中..." : "▶ 启动 GMAT 任务序列求解";
            if (GUILayout.Button(runBtnText, actionButtonStyle, GUILayout.Height(40))) {
                isPropagating = true;
                List<StageData> selectedStages = GetSelectedStagesList();
                ROEngineInfo selectedEngine = (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) ? cachedEngines[selectedEngineIndex] : null;
                GMATClient.PropagateMission(vessel, selectedStages, selectedEngine, missionSequence, propagatorConfigs, (success, result) => {
                    isPropagating = false;
                    latestResult = result;
                    if (success) {
                        if (result.updated_sequence != null && result.updated_sequence.Count > 0) {
                            for (int k = 0; k < Math.Min(missionSequence.Count, result.updated_sequence.Count); k++) {
                                var newStep = result.updated_sequence[k];
                                var curStep = missionSequence[k];
                                if (newStep.duration > 0) curStep.duration = newStep.duration;
                                if (curStep.type == "ImpulsiveBurn" || curStep.type == "TargetApoapsis") {
                                    curStep.dv_v = newStep.dv_v;
                                    curStep.dv_n = newStep.dv_n;
                                    curStep.dv_b = newStep.dv_b;
                                }
                            }
                        }
                        if (!string.IsNullOrEmpty(result.render_image_path)) {
                            LoadSimulationTexture(result.render_image_path);
                        }
                        ScreenMessages.PostScreenMessage("✅ GMAT 实时地月打靶求解成功！发射窗口与机动数值已自动更新至当前世界时间。", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                    } else {
                        ScreenMessages.PostScreenMessage("❌ GMAT 求解失败: " + result.error, 5.0f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
            }
            GUI.enabled = true;

            GUILayout.Label(isPropagating ? "<color=#ffff55>GMAT 引擎计算中...</color>" : (latestResult != null && latestResult.success ? "<color=#55ff55>● 求解完成</color>" : "● 就绪"), labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // Sub-Windows Entry Buttons
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("🪟 专属分析子界面", headerStyle);
            if (GUILayout.Button("🖼️ 查看 GMAT 仿真轨迹图像", actionButtonStyle, GUILayout.Height(30))) {
                LoadSimulationTexture();
                showSimulationImageWindow = true;
            }
            if (GUILayout.Button("⚙️ 传播子与受力模型深度配置", secondaryButtonStyle, GUILayout.Height(26))) {
                showPropagatorConfigWindow = true;
            }
            if (GUILayout.Button("🛰️ 查看航天器已同步参数快照", secondaryButtonStyle, GUILayout.Height(26))) {
                showCraftDetailsWindow = true;
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // Pinned Live Solution Summary Box
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("📊 最新轨道求解结果", headerStyle);

            if (latestResult != null && latestResult.success) {
                GUILayout.Label($"最终半长轴 (SMA): <b>{latestResult.final_sma:F1} km</b>", labelValueStyle);
                GUILayout.Label($"最终离心率 (ECC): <b>{latestResult.final_ecc:F4}</b>", labelValueStyle);
                GUILayout.Label($"最终倾角 (INC): <b>{latestResult.final_inc:F2}°</b>", labelValueStyle);
                GUILayout.Label($"生成机动点火: <b>{latestResult.solved_burns.Count} 个</b>", labelValueStyle);
            } else if (latestResult != null && !latestResult.success) {
                GUILayout.Label($"<color=#ff5555>求解报错: {latestResult.error}</color>", labelValueStyle);
            } else {
                GUILayout.Label("点击上方按钮启动计算，结果将实时显示于此。", labelValueStyle);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // Direct Actions Box
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("🔗 一键联动快捷操作", headerStyle);

            GUI.enabled = (latestResult != null && latestResult.success && latestResult.solved_burns.Count > 0);
            if (GUILayout.Button("🔥 一键写入所有机动至 Principia", successButtonStyle, GUILayout.Height(34))) {
                WriteBurnsToPrincipia(vessel);
            }
            GUI.enabled = (latestResult != null && latestResult.success);

            if (GUILayout.Button("🖥️ 在 GMAT 桌面端打开 3D 渲染", secondaryButtonStyle, GUILayout.Height(28))) {
                if (!string.IsNullOrEmpty(latestResult.script_path)) {
                    GMATClient.OpenGmatDesktop(latestResult.script_path);
                    ScreenMessages.PostScreenMessage("正在启动 GMAT 桌面端 GUI...", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawSimulationImageWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🖼️ GMAT 轨道仿真图像渲染器 (Trajectory 2D/3D Dual-Perspective View)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showSimulationImageWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // View Mode Toolbar
            GUILayout.BeginHorizontal();
            GUILayout.Label("切换视图:", labelValueStyle, GUILayout.Width(65));
            string[] modes = new string[] { "dual", "planar", "target" };
            string[] modeLabels = new string[] { "🌐 双视角 (平面+3D)", "🪐 轨道全景 (2D)", "🎯 目标端点特写" };
            string bodyName = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.mainBody.name : "Earth";
            double bodyRadiusKm = FlightGlobals.ActiveVessel != null ? (FlightGlobals.ActiveVessel.mainBody.Radius / 1000.0) : 6378.14;

            for (int i = 0; i < modes.Length; i++) {
                bool isCur = (currentViewMode == modes[i]);
                GUI.backgroundColor = isCur ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                if (GUILayout.Button(modeLabels[i], secondaryButtonStyle, GUILayout.Height(24))) {
                    currentViewMode = modes[i];
                    GMATClient.RenderTrajectory(currentViewMode, bodyName, bodyRadiusKm, (ok, path) => {
                        if (ok) LoadSimulationTexture(path);
                    });
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄 重新渲染", actionButtonStyle, GUILayout.Width(90), GUILayout.Height(24))) {
                GMATClient.RenderTrajectory(currentViewMode, bodyName, bodyRadiusKm, (ok, path) => {
                    if (ok) LoadSimulationTexture(path);
                });
            }
            if (GUILayout.Button("🖥️ GMAT 桌面端交互 3D", secondaryButtonStyle, GUILayout.Width(140), GUILayout.Height(24))) {
                if (latestResult != null && !string.IsNullOrEmpty(latestResult.script_path)) {
                    GMATClient.OpenGmatDesktop(latestResult.script_path);
                } else {
                    GMATClient.OpenGmatDesktop(@"D:\nocomp\gmat-win-R2026a\output\mission_run.script");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Image Viewport
            GUILayout.BeginVertical(GUI.skin.box);
            if (simulationTexture != null) {
                GUILayout.Box(simulationTexture, GUILayout.Width(750), GUILayout.Height(420));
            } else {
                GUILayout.BeginVertical(GUILayout.Height(420));
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#888888>尚未载入仿真渲染图。请点击右上角 [ 🔄 重新渲染 ] 或在主界面运行求解。</color>", labelValueStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"状态: <color=#55ffff>{imageStatusText}</color> | 坐标系: ICRF J2000 | 星历: JPL DE405/DE421", labelValueStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕ 关闭图像窗口", secondaryButtonStyle, GUILayout.Width(110), GUILayout.Height(24))) {
                showSimulationImageWindow = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private void DrawPropagatorConfigWindow(int windowID) {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>⚙️ GMAT 传播子与高精度受力模型深度配置 (Propagator & Force Models)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Propagator Selector Tabs
            GUILayout.BeginHorizontal();
            string[] propKeys = new string[] { "LEOProp", "CislunarProp", "LunarNRHOProp" };
            string[] propTabLabels = new string[] { "🪐 近轨高保真 (NearBody)", "🌌 深空多体巡航 (DeepSpace)", "🌙 复杂三体/Halo (LagrangeHalo)" };
            for (int i = 0; i < propKeys.Length; i++) {
                bool isSel = (selectedPropagatorKey == propKeys[i]);
                GUI.backgroundColor = isSel ? new Color(0.2f, 0.7f, 0.9f) : Color.white;
                if (GUILayout.Button(propTabLabels[i], isSel ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(26))) {
                    selectedPropagatorKey = propKeys[i];
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (!propagatorConfigs.ContainsKey(selectedPropagatorKey)) {
                propagatorConfigs[selectedPropagatorKey] = new PropagatorConfig { name = selectedPropagatorKey };
            }
            var cfg = propagatorConfigs[selectedPropagatorKey];

            scrollPosPropConfig = GUILayout.BeginScrollView(scrollPosPropConfig, GUILayout.Height(430));

            // Two-column layout: Left = Integrator, Right = Force Model
            GUILayout.BeginHorizontal();

            // Left Column: Numerical Integrator (380px)
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(380));
            GUILayout.Label("🔢 数值积分器核心参数 (Numerical Integrator)", headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("积分器类型 (Type):", labelValueStyle, GUILayout.Width(130));
            string[] intTypes = new string[] { "RungeKutta89", "PrinceDormand78", "RungeKutta68", "RungeKutta56" };
            int curIntIdx = Array.IndexOf(intTypes, cfg.integratorType);
            if (curIntIdx < 0) curIntIdx = 0;
            if (GUILayout.Button(cfg.integratorType, secondaryButtonStyle, GUILayout.Height(22))) {
                curIntIdx = (curIntIdx + 1) % intTypes.Length;
                cfg.integratorType = intTypes[curIntIdx];
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("初始步长 (InitialStepSize):", labelValueStyle, GUILayout.Width(160));
            string sInit = GUILayout.TextField(cfg.initialStepSize.ToString("F1"), GUILayout.Width(70));
            double.TryParse(sInit, out cfg.initialStepSize);
            GUILayout.Label("秒", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("积分容差 (Accuracy):", labelValueStyle, GUILayout.Width(160));
            string sAcc = GUILayout.TextField(cfg.accuracy.ToString("E1"), GUILayout.Width(70));
            double.TryParse(sAcc, out cfg.accuracy);
            GUILayout.Label("(标准 1e-11)", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("最小步长 (MinStep):", labelValueStyle, GUILayout.Width(160));
            string sMin = GUILayout.TextField(cfg.minStep.ToString("F4"), GUILayout.Width(70));
            double.TryParse(sMin, out cfg.minStep);
            GUILayout.Label("秒", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("最大步长 (MaxStep):", labelValueStyle, GUILayout.Width(160));
            string sMax = GUILayout.TextField(cfg.maxStep.ToString("F0"), GUILayout.Width(70));
            double.TryParse(sMax, out cfg.maxStep);
            GUILayout.Label("秒", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("最大重试 (MaxStepAttempts):", labelValueStyle, GUILayout.Width(160));
            string sAtt = GUILayout.TextField(cfg.maxStepAttempts.ToString(), GUILayout.Width(70));
            int.TryParse(sAtt, out cfg.maxStepAttempts);
            GUILayout.Label("次", labelValueStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("误差控制 (ErrorControl):", labelValueStyle, GUILayout.Width(160));
            if (GUILayout.Button(cfg.errorControl, secondaryButtonStyle, GUILayout.Height(20))) {
                cfg.errorControl = (cfg.errorControl == "RSSStep") ? "RSSState" : "RSSStep";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<color=#888888>说明：RungeKutta89 为 NASA/JPL 深空标准积分器，结合自适应步长控制可保证地月轨道毫米级积分保真度。</color>", labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Right Column: Force Model & Perturbations (380px)
            GUILayout.BeginVertical(cardBoxStyle, GUILayout.Width(380));
            GUILayout.Label("🌌 受力模型与空间摄动 (Force Model)", headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("中心天体 (CentralBody):", labelValueStyle, GUILayout.Width(150));
            string[] bodies = new string[] { "Earth", "Luna", "Sun" };
            int bIdx = Array.IndexOf(bodies, cfg.centralBody);
            if (bIdx < 0) bIdx = 0;
            if (GUILayout.Button(cfg.centralBody, secondaryButtonStyle, GUILayout.Height(22))) {
                bIdx = (bIdx + 1) % bodies.Length;
                cfg.centralBody = bodies[bIdx];
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Label("<b>质点引力源 (Point Masses):</b>", subHeaderStyle);
            GUILayout.BeginHorizontal();
            cfg.gravityEarth = GUILayout.Toggle(cfg.gravityEarth, " 地球 (Earth)");
            cfg.gravityLuna = GUILayout.Toggle(cfg.gravityLuna, " 月球 (Luna)");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            cfg.gravitySun = GUILayout.Toggle(cfg.gravitySun, " 太阳 (Sun)");
            cfg.gravityJupiter = GUILayout.Toggle(cfg.gravityJupiter, " 木星 (Jupiter)");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            cfg.gravityVenus = GUILayout.Toggle(cfg.gravityVenus, " 金星 (Venus)");
            cfg.gravityMars = GUILayout.Toggle(cfg.gravityMars, " 火星 (Mars)");
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>非引力与其他摄动:</b>", subHeaderStyle);

            cfg.enableSRP = GUILayout.Toggle(cfg.enableSRP, " 启用太阳光辐射压 (SRP)");
            if (cfg.enableSRP) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("  太阳常数 (Flux):", labelValueStyle, GUILayout.Width(130));
                string sFlux = GUILayout.TextField(cfg.srpFlux.ToString("F1"), GUILayout.Width(65));
                double.TryParse(sFlux, out cfg.srpFlux);
                GUILayout.Label("W/m²", labelValueStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("大气阻力 (Drag):", labelValueStyle, GUILayout.Width(130));
            string[] drags = new string[] { "None", "Exponential", "MSISE90" };
            int dIdx = Array.IndexOf(drags, cfg.dragModel);
            if (dIdx < 0) dIdx = 0;
            if (GUILayout.Button(cfg.dragModel, secondaryButtonStyle, GUILayout.Height(20))) {
                dIdx = (dIdx + 1) % drags.Length;
                cfg.dragModel = drags[dIdx];
            }
            GUILayout.EndHorizontal();

            cfg.enableRelativity = GUILayout.Toggle(cfg.enableRelativity, " 开启广义相对论修正 (Relativity)");

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();

            GUILayout.Space(6);

            // Bottom Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("💾 保存并应用到 GMAT 求解配置", successButtonStyle, GUILayout.Height(30))) {
                ScreenMessages.PostScreenMessage("✅ 传播子与受力模型参数已保存！下次求解将生效。", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                showPropagatorConfigWindow = false;
            }
            if (GUILayout.Button("🔄 恢复 NASA 默认参数", secondaryButtonStyle, GUILayout.Width(160), GUILayout.Height(30))) {
                propagatorConfigs = PropagatorConfig.CreateDefaultPresets();
                ScreenMessages.PostScreenMessage("已重置所有传播子为 NASA/JPL 默认高精度参数。", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            if (GUILayout.Button("✕ 关闭", secondaryButtonStyle, GUILayout.Width(70), GUILayout.Height(30))) {
                showPropagatorConfigWindow = false;
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private void DrawCraftDetailsWindow(int windowID) {
            Vessel vessel = FlightGlobals.ActiveVessel;
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>🛰️ GMAT 同步航天器完整参数快照 (Spacecraft Parameters)</b>", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(24), GUILayout.Height(20))) {
                showCraftDetailsWindow = false;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            scrollPosCraftDetails = GUILayout.BeginScrollView(scrollPosCraftDetails);

            if (vessel != null) {
                // Section 1: Basic & Orbit
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🌍 航天器身份与轨道引力环境", subHeaderStyle);
                GUILayout.Label($"飞船名称: <b>{vessel.vesselName}</b> | 活跃中心天体 (SOI): <b>{vessel.mainBody.name}</b>", labelValueStyle);
                double ut = Planetarium.GetUniversalTime();
                GUILayout.Label($"当前游戏世界时间 UT: <b>{ut:F2} s</b>", labelValueStyle);

                Vector3d pos_km, vel_kms;
                PrincipiaInterop.GetVesselStateICRF(vessel, out pos_km, out vel_kms);
                GUILayout.Label($"空间惯性系位置 (ICRF): X=<b>{pos_km.x:F2}</b> km, Y=<b>{pos_km.y:F2}</b> km, Z=<b>{pos_km.z:F2}</b> km (r=<b>{pos_km.magnitude:F1}</b> km)", labelValueStyle);
                GUILayout.Label($"空间惯性系速度 (ICRF): VX=<b>{vel_kms.x:F3}</b> km/s, VY=<b>{vel_kms.y:F3}</b> km/s, VZ=<b>{vel_kms.z:F3}</b> km/s (v=<b>{vel_kms.magnitude:F3}</b> km/s)", labelValueStyle);
                if (vessel.orbit != null) {
                    GUILayout.Label($"开普勒根数: 半长轴 SMA=<b>{vessel.orbit.semiMajorAxis / 1000.0:F1} km</b>, 离心率 ECC=<b>{vessel.orbit.eccentricity:F4}</b>, 倾角 INC=<b>{vessel.orbit.inclination:F2}°</b>", labelValueStyle);
                    GUILayout.Label($"近拱点高度: <b>{vessel.orbit.PeA / 1000.0:F1} km</b>, 远拱点高度: <b>{vessel.orbit.ApA / 1000.0:F1} km</b>", labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 2: Staging Stack
                GUILayout.BeginVertical(cardBoxStyle);
                var selStages = GetSelectedStagesList();
                GUILayout.Label($"🚀 多级火箭质量与 ΔV 组合 (已选 {selStages.Count} 个分级)", subHeaderStyle);
                double sumDv = 0, sumFuel = 0, maxWet = 0, minDry = double.MaxValue;
                foreach (var st in selStages) {
                    sumDv += st.deltaV;
                    sumFuel += st.fuelMassTonnes;
                    if (st.wetMassTonnes > maxWet) maxWet = st.wetMassTonnes;
                    if (st.dryMassTonnes < minDry) minDry = st.dryMassTonnes;
                    GUILayout.Label($"  - <b>Stage {st.stageNumber}</b>: ΔV=<b>{st.deltaV:F0} m/s</b> | 湿重=<b>{st.wetMassTonnes:F2}t</b> | 燃料=<b>{st.fuelMassTonnes:F2}t</b> | 干重=<b>{st.dryMassTonnes:F2}t</b>", labelValueStyle);
                }
                if (minDry == double.MaxValue) minDry = vessel.totalMass * 0.4;
                if (maxWet <= 0) maxWet = vessel.totalMass;
                GUILayout.Space(2);
                GUILayout.Label($"<color=#55ffff>组合初始湿重: <b>{maxWet:F2} 吨</b> | 最终干重: <b>{minDry:F2} 吨</b> | 总推进剂: <b>{sumFuel:F2} 吨</b> | 总设计ΔV: <b>{sumDv:F0} m/s</b></color>", labelValueStyle);
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 3: RO Engine & Tank
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🔥 RealismOverhaul 推进系统动力学", subHeaderStyle);
                if (cachedEngines != null && selectedEngineIndex < cachedEngines.Count) {
                    var eng = cachedEngines[selectedEngineIndex];
                    GUILayout.Label($"主发动机: <b>{eng.title}</b> | 真空推力: <b>{eng.thrustKN:F1} kN</b> | 真空比冲: <b>{eng.ispVac:F0} s</b>", labelValueStyle);
                    GUILayout.Label($"推进剂组元: <color=#55ffff>{eng.propellantDesc}</color>", labelValueStyle);
                    GUILayout.Label($"推进剂混合密度: <b>{eng.fuelDensityKgM3:F1} kg/m³</b> | 储罐容积: <b>{eng.tankVolumeM3:F3} m³</b> | 调节压力: <b>{eng.tankPressureKPa:F0} kPa</b>", labelValueStyle);
                } else {
                    GUILayout.Label("使用通用动力学推力模型 (50.0 kN, Isp 310 s)", labelValueStyle);
                }
                GUILayout.EndVertical();

                GUILayout.Space(6);

                // Section 4: GMAT Force Models
                GUILayout.BeginVertical(cardBoxStyle);
                GUILayout.Label("🌌 GMAT 动力学模型与传播子配置", subHeaderStyle);
                GUILayout.Label("• <b>NearBodyForceModel</b>: 中心天体动态自适应，多体引力摄动 {中心天体, 伴星, 太阳, 木星}, 太阳光压 SRP 开启", labelValueStyle);
                GUILayout.Label("• <b>DeepSpaceForceModel</b>: 行星际深空多体巡航动力学模型，多星体点质量与光压扰动", labelValueStyle);
                GUILayout.Label("• <b>LEOProp / NearBody</b>: 中心天体近轨RungeKutta89自适应高精度积分器", labelValueStyle);
                GUILayout.Label("• <b>CislunarProp / DeepSpace</b>: 行星际/多体巡航高效率RungeKutta89积分器 (最大步长 21600s / 6小时)", labelValueStyle);
                GUILayout.Label("• <b>LunarNRHOProp / LagrangeHalo</b>: 复杂三体/Halo晕轮轨道专用高精度保真积分器", labelValueStyle);
                GUILayout.EndVertical();
            } else {
                GUILayout.Label("无活跃飞船数据。", labelValueStyle);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(6);
            if (GUILayout.Button("✕ 关闭快照", secondaryButtonStyle, GUILayout.Height(28))) {
                showCraftDetailsWindow = false;
            }
            GUI.DragWindow();
        }

        private void WriteBurnsToPrincipia(Vessel vessel) {
            if (vessel == null || latestResult == null || latestResult.solved_burns.Count == 0) return;
            try {
                KSPGMATLog.Section("WRITE BURNS TO PRINCIPIA STARTED");
                KSPGMATLog.Info($"Target Vessel: {vessel.vesselName} (ID: {vessel.id})");
                double currentPTime = PrincipiaInterop.GetCurrentPrincipiaTime();
                double ut = Planetarium.GetUniversalTime();
                KSPGMATLog.Info($"Time Baseline: PrincipiaTime={currentPTime:F3}s, Game UT={ut:F3}s, Offset={currentPTime - ut:F3}s");

                // Dynamically calculate flight plan duration to just cover the mission plus 1.5 days buffer
                // This prevents Principia from attempting a 30-day integration that exceeds its 1000 step limit (Error Code 8)
                double lastBurnOffset = latestResult.solved_burns[latestResult.solved_burns.Count - 1].epoch_offset;
                double totalMissionDuration = Math.Max(86400.0 * 2.0, lastBurnOffset + 86400.0 * 1.5);
                PrincipiaInterop.EnsureFlightPlanExists(vessel, totalMissionDuration);
                KSPGMATLog.Info($"Flight plan requested duration: {totalMissionDuration / 86400.0:F2} days (covers last burn at +{lastBurnOffset / 86400.0:F2}d)");

                for (int i = 0; i < latestResult.solved_burns.Count; i++) {
                    var b = latestResult.solved_burns[i];
                    double burnTime = currentPTime + b.epoch_offset;
                    Vector3d dv = new Vector3d(b.dv_v, b.dv_n, b.dv_b);
                    double thrust = b.thrust_kn > 0 ? b.thrust_kn : 50.0;
                    double isp = b.isp > 0 ? b.isp : 310.0;
                    KSPGMATLog.Info($"Writing Maneuver #{i+1} [{b.name}] on {b.origin}: ScheduledTime={burnTime:F2}s (+{b.epoch_offset:F1}s / +{b.epoch_offset/86400.0:F2}d), ΔV=({b.dv_v:F1}, {b.dv_n:F1}, {b.dv_b:F1})m/s, Mag={b.magnitude:F1}m/s, Thrust={thrust:F1}kN, Isp={isp:F1}s");
                    bool ok = PrincipiaInterop.InsertOrUpdateBurn(vessel, burnTime, dv, thrust, isp, i, isFrenet: true, originBodyName: b.origin);
                    if (ok) {
                        KSPGMATLog.Success($"  -> Maneuver #{i+1} [{b.name}] successfully written into Principia flight plan.");
                    } else {
                        KSPGMATLog.Error($"  -> Failed to write Maneuver #{i+1} [{b.name}] into Principia!");
                    }
                }
                KSPGMATLog.Section("WRITE BURNS TO PRINCIPIA COMPLETED");
                ScreenMessages.PostScreenMessage("✅ 已成功将 GMAT 机动按真实时序写入 Principia 飞行计划！请在地图视图中查看。", 5.0f, ScreenMessageStyle.UPPER_CENTER);
            } catch (Exception ex) {
                KSPGMATLog.Error($"Exception writing burns to Principia: {ex}");
                ScreenMessages.PostScreenMessage("❌ 写入 Principia 失败: " + ex.Message, 5.0f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private void ImportScriptFile(string path) {
            Vessel v = FlightGlobals.ActiveVessel;
            KSPGMATLog.Section("GMAT SCRIPT IMPORT STARTED");
            KSPGMATLog.Info($"Importing Script: {path}");
            GMATClient.ImportScript(path, v, (ok, steps, msg) => {
                if (ok && steps != null) {
                    missionSequence = steps;
                    syncStatusMessage = $"已成功导入 {steps.Count} 个步骤！来自: {System.IO.Path.GetFileName(path)}";
                    KSPGMATLog.Success($"Script imported successfully: {steps.Count} steps parsed.");
                    for (int s = 0; s < steps.Count; s++) {
                        var st = steps[s];
                        KSPGMATLog.Info($"  Step {s+1}: {st.type} '{st.label}' (prop={st.propagator}, stop={st.stop_type}, dur={st.duration:F1})");
                    }
                    ScreenMessages.PostScreenMessage($"✅ 成功导入任务脚本: {System.IO.Path.GetFileName(path)} ({steps.Count} 个步骤)", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                } else {
                    KSPGMATLog.Error($"Script import failed: {msg}");
                    ScreenMessages.PostScreenMessage($"❌ 脚本导入失败: {msg}", 4.0f, ScreenMessageStyle.UPPER_CENTER);
                }
                KSPGMATLog.Section("GMAT SCRIPT IMPORT COMPLETED");
            });
        }

        private void DrawTabResultsAndPrincipia(Vessel vessel) {
            scrollPosResults = GUILayout.BeginScrollView(scrollPosResults);

            if (latestResult == null) {
                GUILayout.Label("尚未运行任务序列计算。请前往 [任务序列与求解] 标签页点击求解。", labelValueStyle);
                GUILayout.EndScrollView();
                return;
            }

            if (!latestResult.success) {
                GUILayout.Label($"❌ 求解失败: {latestResult.error}", labelValueStyle);
                GUILayout.EndScrollView();
                return;
            }

            // Results Card
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("📊 GMAT 积分与最终轨道状态", headerStyle);
            GUILayout.Label($"最终半长轴 (SMA): <b>{latestResult.final_sma:F2} km</b>", labelValueStyle);
            GUILayout.Label($"最终离心率 (ECC): <b>{latestResult.final_ecc:F4}</b> | 倾角 (INC): <b>{latestResult.final_inc:F2}°</b>", labelValueStyle);
            GUILayout.Label($"升交点赤经 (RAAN): <b>{latestResult.final_raan:F2}°</b> | 近拱点角 (AOP): <b>{latestResult.final_aop:F2}°</b>", labelValueStyle);
            GUILayout.Label($"最终纪元: {latestResult.final_epoch} | 剩余质量: <b>{latestResult.final_mass:F1} kg</b>", labelValueStyle);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // Solved Burns List
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label($"🔥 求解生成的变轨机动明细 ({latestResult.solved_burns.Count} 个)", headerStyle);

            for (int i = 0; i < latestResult.solved_burns.Count; i++) {
                var b = latestResult.solved_burns[i];
                GUILayout.BeginVertical(GUI.skin.box);
                double tDays = b.epoch_offset / 86400.0;
                string nameTxt = string.IsNullOrEmpty(b.name) ? $"机动 #{i + 1}" : b.name;
                GUILayout.Label($"<b>{nameTxt}</b> [{b.type}] ({b.origin}): 点火时序 t=+{b.epoch_offset:F0}s (+{tDays:F2}天)", subHeaderStyle);
                GUILayout.Label($"  - 总速度增量 |ΔV| = <b>{b.magnitude:F1} m/s</b>", labelValueStyle);
                GUILayout.Label($"  - 顺行: <b>{b.dv_v:F1} m/s</b>, 法向: <b>{b.dv_n:F1} m/s</b>, 径向: <b>{b.dv_b:F1} m/s</b>", labelValueStyle);
                if (b.type == "Finite") {
                    GUILayout.Label($"  - 燃烧时间: {b.duration:F1} s, 消耗燃料: {b.fuel_burned:F1} kg", labelValueStyle);
                }
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.EndVertical();

            GUILayout.Space(10);

            // Action: Sync to Principia Flight Plan
            if (GUILayout.Button("🔥 自动写回 Principia 飞行计划 (Flight Plan)", successButtonStyle, GUILayout.Height(36))) {
                WriteBurnsToPrincipia(vessel);
            }

            GUILayout.Space(6);

            // Sub-Windows Quick Triggers
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🖼️ 查看 GMAT 仿真渲染图像", actionButtonStyle, GUILayout.Height(30))) {
                LoadSimulationTexture();
                showSimulationImageWindow = true;
            }
            if (GUILayout.Button("🌐 在 GMAT 桌面程序中打开并渲染 3D 轨迹", secondaryButtonStyle, GUILayout.Height(30))) {
                if (!string.IsNullOrEmpty(latestResult.script_path)) {
                    GMATClient.OpenGmatDesktop(latestResult.script_path);
                    ScreenMessages.PostScreenMessage("正在启动 GMAT 桌面端 GUI...", 3.0f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private void DrawTabSettings() {
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("🔧 后台服务设置与星历校准", headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("服务地址:", labelValueStyle, GUILayout.Width(80));
            GMATClient.ServerUrl = GUILayout.TextField(GMATClient.ServerUrl, GUILayout.Width(220));
            if (GUILayout.Button("测试连接", secondaryButtonStyle, GUILayout.Width(80))) {
                GMATClient.CheckConnection((ok, msg) => {});
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"连接状态: {GMATClient.ServerStatusText}", labelValueStyle);
            GUILayout.Label($"Principia 状态: {(PrincipiaInterop.IsPrincipiaAvailable() ? "✅ 已就绪" : "❌ 未加载")}", labelValueStyle);
            GUILayout.Label($"MechJeb2 状态: {(MechJebInterop.IsMechJebAvailable() ? "✅ 已就绪" : "❌ 未加载")}", labelValueStyle);
            GUILayout.Label($"RealismOverhaul (RO) / RealFuels: {(ROEngineInterop.IsRealismOverhaulInstalled() ? "✅ 已深度联动" : "⚠️ 未检测到")}", labelValueStyle);

            GUILayout.Space(8);
            GUILayout.Label("Sol 星历与纪元配置:", headerStyle);
            GUILayout.Label("游戏纪元: JD 2433647.5 (1951-01-01 00:00:00 TT)", labelValueStyle);
            GUILayout.Label("GMAT 星历基准: JPL DE405 / DE421 (ICRF 轴向完全对齐)", labelValueStyle);

            GUILayout.EndVertical();
        }
    }
}
