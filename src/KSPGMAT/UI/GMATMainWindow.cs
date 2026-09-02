using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using KSPGMAT.Model;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow : MonoBehaviour {
        private int ideScriptViewMode = 0;
        private Vector2 ideScriptScroll = Vector2.zero;
        private bool showCommandPalette = true;
        // Decluttering & Collapsible Cards State
        private readonly HashSet<int> expandedStepPills = new HashSet<int>();
        private readonly HashSet<int> expandedSolverSettings = new HashSet<int>();
        private readonly HashSet<int> expandedPropSettings = new HashSet<int>();
        private int propCategoryTab = 0;

        // Floating IDE-Style IntelliSense State
        private bool intelliSenseOpen = false;
        private bool intelliSenseDismissed = false;
        private string pendingFocusControl = "";
        private float lastHardwareScanTime = 0f;
        private const float HARDWARE_SCAN_INTERVAL = 0.15f; // 6.7 Hz high-frequency live telemetry (Zero GC, <0.02ms CPU cost)
        private float lastI18nRescanTime = 0f;
        private const float I18N_RESCAN_INTERVAL = 2.0f; // detect newly added language files (e.g. ru.json)
        private int intelliSenseTargetStepIndex = -1;
        private string intelliSenseFieldName = ""; // "achieve" or "vary"
        private Rect intelliSenseAnchorRect = Rect.zero;
        private int intelliSenseSelectedIndex = 0;
        private Vector2 intelliSenseScroll = Vector2.zero;
        private List<IntelliSenseItem> intelliSenseItems = new List<IntelliSenseItem>();
        private string intelliSenseLastQuery = null;

        public static GMATMainWindow Instance { get; private set; }

        private static readonly Queue<Action> mainThreadQueue = new Queue<Action>();

        public static void RunOnMainThread(Action action) {
            if (action == null) return;
            lock (mainThreadQueue) {
                mainThreadQueue.Enqueue(action);
            }
        }

        private bool visible = false;
        private Rect windowRect;
        private bool windowInitialized = false;
        private bool isMaximized = false;
        private Rect preMaximizedRect;
        private bool isResizingWindow = false;
        private Vector2 resizeStartMouse;
        private Rect resizeStartRect;

        private int activeTab = 0; // Default to Tab 0: Craft & Orbit
        private string[] GetTabNames() => new string[] {
            I18n.T("tab_craft", "飞船与动力学"),
            I18n.T("tab_resources", "资源库"),
            I18n.T("tab_mission", "任务控制序列"),
            I18n.T("tab_mga", "深空借力 (MGA)"),
            I18n.T("tab_results", "变轨与轨迹明细"),
            I18n.T("tab_settings", "设置")
        };

        private Vector2 scrollPosCraft = Vector2.zero;
        private Vector2 scrollPosResources = Vector2.zero;
        private Vector2 scrollPosSequence = Vector2.zero;
        private Vector2 scrollPosResults = Vector2.zero;
        private Vector2 rightColScrollPos = Vector2.zero;
        private Vector2 rightErrorScrollPos = Vector2.zero;
        private bool showFullErrorLog = false;

        // Custom GMAT User Variables, Arrays & Strings pool
        private List<GMATUserVariable> userVariables = new List<GMATUserVariable>();
        private List<GMATUserArray> userArrays = new List<GMATUserArray>();
        private List<GMATUserString> userStrings = new List<GMATUserString>();

        // Script Push Inspector & IDE Modal
                private int selectedResourceCategory = 0;

        private ROEngineInfo GetDefaultOrActiveEngine() {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null) {
                var activeList = ROEngineInterop.GetActiveOrIgnitedEngines(v);
                if (activeList != null && activeList.Count > 0) {
                    return activeList[0];
                }
            }
            if (cachedEngines != null && cachedEngines.Count > 0) {
                if (selectedEngineIndex >= 0 && selectedEngineIndex < cachedEngines.Count) {
                    return cachedEngines[selectedEngineIndex];
                }
                return cachedEngines[0];
            }
            return null;
        }

        // Built-in Script IDE & Inspector State (Unified into Tab 1)
        private List<StageData> cachedStages = new List<StageData>();
        private HashSet<int> selectedStageIndices = new HashSet<int> { 0 };
        private List<ROEngineInfo> cachedEngines = new List<ROEngineInfo>();
        private List<VesselTankInfo> cachedTanks = new List<VesselTankInfo>();
        private int selectedEngineIndex {
            get => GMATStateManager.Current.SelectedEngineIndex;
            set => GMATStateManager.Current.SelectedEngineIndex = value;
        }
        private string syncStatusMessage = "";
        private int ideViewMode = 0; // 0 = 高亮预览, 1 = 自由编辑, 2 = 分栏对照
        private string ideCustomBuffer = "";
        private string ideLastLiveScriptSnapshot = "";
        private bool ideHasUnsavedEdits = false;
        private GUIStyle ideEditorStyle;
        private Vector2 ideAutocompleteScroll = Vector2.zero;
        private string ideCurrentWordUnderCursor = "";
        private int ideSelectedCompletionIndex = 0;
        private int ideCursorWordStart = 0;
        private bool isSavingAndSyncing = false;
        private string eventSearchKeyword = "";

        private bool isPropagating = false;
        private float solveProgress = 0.0f;
        private float solveStartTime = 0.0f;
        private float solveTotalTime = 0.0f;
        private string solveStageText = "";
        private int solveCurrentStepIndex = 0;

        // Sub-Window: Spacecraft details
        private bool showCraftDetailsWindow = false;
        private Rect craftDetailsWindowRect;
        private Vector2 scrollPosCraftDetails = Vector2.zero;

        // Sub-Window 1: Simulation Trajectory Viewer
        private bool showSimulationImageWindow = false;
        private Rect simulationImageWindowRect;
        private Texture2D simulationTexture = null;
        private string currentViewMode = "dual";
        private string imageStatusText = "";

        // Sub-Window 2: Propagator & Force Model Config
        private bool showPropagatorConfigWindow = false;
        private Rect propagatorConfigWindowRect;
        private Dictionary<string, PropagatorConfig> propagatorConfigs = PropagatorConfig.CreateDefaultPresets();
        private string selectedPropagatorKey = "LEOProp";
        private Vector2 scrollPosPropConfig = Vector2.zero;

        // Sub-Window 3: Parameter Picker Modal
        private bool showParamPickerModal = false;
        private Rect paramPickerWindowRect;
        private int paramPickerTargetStepIndex = -1;
        private string paramPickerMode = "variable"; // "variable", "achieve", "stop"
        private string paramPickerSearchQuery = "";
        private int paramPickerCategoryIndex = 0;
        private Vector2 paramPickerScrollPos = Vector2.zero;

        // Sub-Window 4: Targeter & Solver Setup Wizard Modal
        private bool showTargeterSetupModal = false;
        private bool showCoordSystemWindow = false;
        private CustomCoordSystemDef editingCoordSystem = new CustomCoordSystemDef();
        private Rect coordSystemWindowRect = new Rect(Screen.width * 0.35f, Screen.height * 0.2f, 480, 520);
        private bool showCsOriginPicker = false;
        private bool showCsAxesPicker = false;
        private bool showCsPrimaryPicker = false;

        // Sub-Window 5: Celestial Body / Planet Picker Modal (MGA 行星际天体弹出选择器)
        private bool showPlanetPickerModal = false;
        private Rect planetPickerWindowRect = new Rect(Screen.width * 0.3f, Screen.height * 0.2f, 520, 480);
        private int planetPickerTargetType = 0; // 0: Departure, 1: Destination, 2: Replace Flyby, 3: Append Flyby
        private int planetPickerFlybyIndex = -1;
        private string planetPickerTitle = "";
        private Vector2 planetPickerScrollPos = Vector2.zero;
        private string planetPickerSearch = "";
        private bool showCsSecondaryPicker = false;
        private bool showCsHelpTooltip = false;
        private Rect targeterSetupWindowRect;
        private int editingTargetStepIndex = -1;
        private string wizardDcName = "DefaultDC";
        private int wizardAlgorithmIndex = 0;
        private static readonly string[] WizardAlgorithmNames = new string[] { "NewtonRaphson", "Broyden", "ModifiedBroyden" };
        private static string[] GetWizardAlgorithmLabels() => new string[] { I18n.T("alg_newton"), I18n.T("alg_broyden"), I18n.T("alg_modbroyden") };
        private int wizardMaxIterations = 50;
        private int wizardDerivativeIndex = 0;
        private static readonly string[] WizardDerivativeNames = new string[] { "ForwardDifference", "CentralDifference" };
        private static string[] GetWizardDerivativeLabels() => new string[] { I18n.T("deriv_forward"), I18n.T("deriv_central") };
        private int wizardSolveModeIndex = 0;
        private static readonly string[] WizardSolveModeNames = new string[] { "Solve", "RunInitialGuess" };
        private static string[] GetWizardSolveModeLabels() => new string[] { I18n.T("mode_solve"), I18n.T("mode_runinit") };
        private int wizardExitModeIndex = 0;
        private static readonly string[] WizardExitModeNames = new string[] { "DiscardAndContinue", "SaveAndContinue", "Stop" };
        private static string[] GetWizardExitModeLabels() => new string[] { I18n.T("exit_discard"), I18n.T("exit_save"), I18n.T("exit_stop") };
        private int wizardTemplateIndex = 0;
        private static string[] GetWizardTemplateLabels() => new string[] {
            I18n.T("tmpl_free"),
            I18n.T("tmpl_apogee"),
            I18n.T("tmpl_period"),
            I18n.T("tmpl_tli"),
            I18n.T("tmpl_inc")
        };

        // Mission Sequence & Results
        private List<MissionStep> missionSequence = new List<MissionStep>();
        private MissionResult latestResult = null;
        private string customScriptPath = "";
        private string exportTemplateName = "custom_mission_preset.script";
        private bool showCustomScriptInput = false;
        private bool showTemplatesFoldout = true;
        private string templateSearchQuery = "";
        private string templateSelectedCategory = "全部";
        private string currentLoadedTemplateName = "";
        private Vector2 templateScrollPos = Vector2.zero;
        private int draggedStepIndex = -1;
        private int stepToMoveUp = -1;
        private int stepToMoveDown = -1;

        // Propagator names and labels
        private static readonly string[] PropagatorNames = new string[] {
            "LEOProp",
            "CislunarProp",
            "LunarNRHOProp"
        };

        private static readonly string[] PropagatorLabels = new string[] {
            "近轨高保真积分 (NearBody)",
            "深空多体巡航积分 (DeepSpace)",
            "复杂三体/Halo积分 (LagrangeHalo)"
        };

        // GUI Styles
        private GUIStyle windowStyle;
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle tabButtonStyle;
        private GUIStyle tabButtonActiveStyle;
        private GUIStyle cardBoxStyle;
        private GUIStyle nestedCardBoxStyle;
        private GUIStyle targetGroupBoxStyle;
        private GUIStyle targetGroupHoverBoxStyle;
        private GUIStyle stepHoverBoxStyle;
        private GUIStyle stepSolvingBoxStyle;
        private GUIStyle stepDraggingBoxStyle;
        private int hoveredStepIndex = -1;
        private int nextHoveredStepIndex = -1;
        private int hoveredBurnIndex = -1;
        private int nextHoveredBurnIndex = -1;
        private GUIStyle labelValueStyle;
        private GUIStyle actionButtonStyle;
        private GUIStyle secondaryButtonStyle;
        private GUIStyle deleteButtonStyle;
        private GUIStyle codeBlockStyle;
        private GUIStyle successButtonStyle;
        private GUIStyle tooltipStyle;

        // Modernized Step Pill & Numeric Styles
        private GUIStyle pillPropStyle;
        private GUIStyle pillVaryStyle;
        private GUIStyle pillAchieveStyle;
        private GUIStyle pillBurnStyle;
        private GUIStyle pillImpulseStyle;
        private GUIStyle pillStageStyle;
        private GUIStyle pillTargetStyle;
        private GUIStyle pillEndTargetStyle;
        private GUIStyle pillGenericStyle;
        private GUIStyle badgeDarkStyle;
        private GUIStyle numericInputStyle;
        private GUIStyle stepLabelInputStyle;
        private GUIStyle gridLabelStyle;
        private GUIStyle sectionHeaderStyle;

        // Colored Step Container Box Styles
        private GUIStyle stepBoxPropStyle;
        private GUIStyle stepBoxVaryStyle;
        private GUIStyle stepBoxAchieveStyle;
        private GUIStyle stepBoxBurnStyle;
        private GUIStyle stepBoxImpulseStyle;
        private GUIStyle stepBoxStageStyle;
        private GUIStyle stepBoxReportStyle;
        private GUIStyle stepBoxEndTargetStyle;

        private bool stylesInitialized = false;

        void Awake() {
            Instance = this;
            try {
                I18n.AutoDetect();
            } catch {}
            solveStageText = I18n.T("solve_stage_ready");
            imageStatusText = I18n.T("img_status_ready");
            planetPickerTitle = I18n.T("mga_picker_default_title");
            mgaStatusText = I18n.T("mga_status_ready");
            porkchopStatusText = I18n.T("solve_stage_ready");
            // Default template: Earth LEO Parking -> Prograde Burn -> Propagate to Apoapsis
            missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Time", duration = 1800.0, label = I18n.T("misc_default_cruise") });
            missionSequence.Add(new MissionStep { type = "FiniteBurn", duration = 60.0, propagator = "LEOProp", burn_direction = "Prograde", label = I18n.T("misc_default_burn") });
            missionSequence.Add(new MissionStep { type = "Propagate", propagator = "LEOProp", stop_type = "Apoapsis", label = I18n.T("misc_default_prop_ap") });
        }

        void Start() {
            GMATClient.CheckConnection((ok, msg) => {});
        }

        void Update() {
            // i18n dynamic language registration: rescan loc/ when its contents changed (e.g. a new ru.json)
            if (Time.realtimeSinceStartup - lastI18nRescanTime > I18N_RESCAN_INTERVAL) {
                lastI18nRescanTime = Time.realtimeSinceStartup;
                try { I18n.RescanIfChanged(); } catch {}
            }

            // High-frequency live telemetry scan (100% automatic, zero user clicking needed)
            if (visible && FlightGlobals.ActiveVessel != null && Time.realtimeSinceStartup - lastHardwareScanTime > HARDWARE_SCAN_INTERVAL) {
                lastHardwareScanTime = Time.realtimeSinceStartup;
                Vessel v = FlightGlobals.ActiveVessel;
                GMATStateManager.Current.SyncFromVessel(v);
                cachedEngines = ROEngineInterop.ScanVesselEngines(v);
                cachedTanks = ROEngineInterop.ScanVesselTanks(v);
                cachedStages = MechJebInterop.GetStages(v);
            }

            if (isPropagating) {
                float elapsed = Time.realtimeSinceStartup - solveStartTime;
                solveTotalTime = elapsed;
                if (elapsed < 0.6f) {
                    solveProgress = Mathf.Lerp(0.05f, 0.22f, elapsed / 0.6f);
                    solveStageText = I18n.T("solve_stage_1");
                    solveCurrentStepIndex = 0;
                } else if (elapsed < 1.5f) {
                    solveProgress = Mathf.Lerp(0.22f, 0.48f, (elapsed - 0.6f) / 0.9f);
                    solveStageText = I18n.T("solve_stage_2");
                    solveCurrentStepIndex = Mathf.Min(1, missionSequence.Count - 1);
                } else if (elapsed < 3.8f) {
                    solveProgress = Mathf.Lerp(0.48f, 0.88f, (elapsed - 1.5f) / 2.3f);
                    solveStageText = I18n.T("solve_stage_3");
                    if (missionSequence.Count > 2) {
                        solveCurrentStepIndex = (int)Mathf.Lerp(1, missionSequence.Count - 1, (elapsed - 1.5f) / 2.3f);
                    }
                } else {
                    solveProgress = Mathf.Min(0.96f, 0.88f + (elapsed - 3.8f) * 0.015f);
                    solveStageText = I18n.T("solve_stage_4");
                    solveCurrentStepIndex = missionSequence.Count - 1;
                }
            }

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
                EnsureWindowInitialized();
                RefreshCraftData();
                LoadSimulationTexture();
            }
        }

        private void EnsureWindowInitialized() {
            if (windowInitialized) return;

            float screenW = Screen.width > 0 ? Screen.width : 1920;
            float screenH = Screen.height > 0 ? Screen.height : 1080;

            float initW = Mathf.Clamp(screenW * 0.72f, 980f, 1360f);
            float initH = Mathf.Clamp(screenH * 0.76f, 640f, 860f);
            float initX = Mathf.Max(20f, (screenW - initW) * 0.5f);
            float initY = Mathf.Max(20f, (screenH - initH) * 0.5f);

            windowRect = new Rect(initX, initY, initW, initH);
            craftDetailsWindowRect = new Rect(initX + 30, initY + 30, Mathf.Min(780f, screenW - 60), Mathf.Min(580f, screenH - 60));
            simulationImageWindowRect = new Rect(initX + 40, initY + 25, Mathf.Min(840f, screenW - 80), Mathf.Min(640f, screenH - 80));
            propagatorConfigWindowRect = new Rect(initX + 25, initY + 20, Mathf.Min(860f, screenW - 50), Mathf.Min(640f, screenH - 50));
            paramPickerWindowRect = new Rect(initX + 50, initY + 40, Mathf.Min(720f, screenW - 100), Mathf.Min(560f, screenH - 100));
            targeterSetupWindowRect = new Rect(initX + 60, initY + 50, Mathf.Min(680f, screenW - 120), Mathf.Min(540f, screenH - 120));

            windowInitialized = true;
        }

        private void ToggleMaximize() {
            if (!isMaximized) {
                preMaximizedRect = windowRect;
                windowRect = new Rect(15, 15, Screen.width - 30, Screen.height - 40);
                isMaximized = true;
            } else {
                windowRect = preMaximizedRect;
                isMaximized = false;
            }
        }

        private void DrawResizeHandle(ref Rect rect, float minW = 980f, float minH = 640f) {
            int controlID = GUIUtility.GetControlID(FocusType.Passive);
            Rect handleRect = new Rect(rect.width - 20, rect.height - 20, 20, 20);
            GUI.Label(handleRect, "<color=#58a6ff><b>◢</b></color>", labelValueStyle);

            Event evt = Event.current;
            switch (evt.type) {
                case EventType.MouseDown:
                    if (handleRect.Contains(evt.mousePosition) && evt.button == 0) {
                        GUIUtility.hotControl = controlID;
                        isResizingWindow = true;
                        resizeStartMouse = evt.mousePosition;
                        resizeStartRect = rect;
                        evt.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID && isResizingWindow) {
                        float deltaX = evt.mousePosition.x - resizeStartMouse.x;
                        float deltaY = evt.mousePosition.y - resizeStartMouse.y;
                        rect.width = Mathf.Max(minW, resizeStartRect.width + deltaX);
                        rect.height = Mathf.Max(minH, resizeStartRect.height + deltaY);
                        evt.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID) {
                        GUIUtility.hotControl = 0;
                        isResizingWindow = false;
                        evt.Use();
                    }
                    break;
            }
        }

        public void RefreshCraftData() {
            try {
                Vessel v = FlightGlobals.ActiveVessel;
                if (v != null) {
                    GMATStateManager.Current.SyncFromVessel(v);
                    cachedStages = GMATStateManager.Current.Stages;
                    if (selectedStageIndices.Count == 0 && cachedStages.Count > 0) {
                        selectedStageIndices.Add(0);
                    }

                    cachedEngines = ROEngineInterop.ScanVesselEngines(v);
                    cachedTanks = ROEngineInterop.ScanVesselTanks(v);
                    selectedEngineIndex = GMATStateManager.Current.SelectedEngineIndex;
                }
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Error in RefreshCraftData: " + ex.Message);
            }
        }

        public void SelectEngineAndSync(int engineIndex) {
            selectedEngineIndex = engineIndex;
            GMATStateManager.Current.SelectedEngineIndex = engineIndex;

            if (FlightGlobals.ActiveVessel != null) {
                if (cachedEngines == null || cachedEngines.Count == 0) {
                    cachedEngines = ROEngineInterop.ScanVesselEngines(FlightGlobals.ActiveVessel);
                }
                if (cachedTanks == null || cachedTanks.Count == 0) {
                    cachedTanks = ROEngineInterop.ScanVesselTanks(FlightGlobals.ActiveVessel);
                }
                if (GMATStateManager.Current.Thrusters == null || GMATStateManager.Current.Thrusters.Count < cachedEngines.Count) {
                    GMATStateManager.Current.SyncFromVessel(FlightGlobals.ActiveVessel);
                    GMATStateManager.Current.SelectedEngineIndex = engineIndex;
                }
            }

            var curThruster = GMATStateManager.Current.GetSelectedThruster();
            if (curThruster == null) return;
            string engId = curThruster.Id;
            string tkId = curThruster.BoundTankId;

            // Synchronize Stage indices to match the active engine
            selectedStageIndices.Clear();
            if (cachedStages != null) {
                for (int s = 0; s < cachedStages.Count; s++) {
                    if (cachedStages[s].stageNumber == curThruster.StageNumber || cachedStages[s].fuelMassTonnes > 0.05) {
                        selectedStageIndices.Add(s);
                    }
                }
            }
            if (selectedStageIndices.Count == 0) selectedStageIndices.Add(0);

            // Synchronize all FiniteBurn and ThrustVectorControl steps
            if (missionSequence != null) {
                for (int s = 0; s < missionSequence.Count; s++) {
                    var step = missionSequence[s];
                    if (step.type == "FiniteBurn") {
                        step.bound_thruster = engId;
                        if (step.bound_thrusters == null) step.bound_thrusters = new List<string>();
                        step.bound_thrusters.Clear();
                        step.bound_thrusters.Add(engId);
                        step.bound_tank = tkId;
                        step.burn_isp = curThruster.IspVac;
                    } else if (step.type == "ThrustVectorControl" || step.type == "VaryThrustDirection") {
                        step.bound_thruster = engId;
                    }
                }
            }
        }

        public MissionStep CreateFiniteBurnStep(string label = null) {
            if (string.IsNullOrEmpty(label)) label = I18n.T("misc_finite_burn_default");
            var curThruster = GMATStateManager.Current.GetSelectedThruster();
            string engName = curThruster != null ? curThruster.Id : "MainEngine";
            string tkName = curThruster != null ? curThruster.BoundTankId : "Tank0";
            var activeEngines = ROEngineInterop.GetActiveOrIgnitedEngines(FlightGlobals.ActiveVessel);
            List<string> clusterList = new List<string>();
            if (activeEngines != null && activeEngines.Count > 0) {
                for (int ae = 0; ae < activeEngines.Count; ae++) {
                    clusterList.Add(GMATParameterRegistry.SanitizeGmatIdentifier(activeEngines[ae].title, "Engine"));
                }
            } else {
                clusterList.Add(engName);
            }
            string cb = (GMATStateManager.Current != null && GMATStateManager.Current.Spacecraft != null) ? GMATStateManager.Current.Spacecraft.CentralBody : "Earth";
            string defaultProp = (propagatorConfigs != null && propagatorConfigs.Count > 0) ? new List<string>(propagatorConfigs.Keys)[0] : (cb == "Earth" ? "LEOProp" : "DeepSpaceProp");
            string burnName = $"FiniteBurn_{((missionSequence != null) ? missionSequence.Count + 1 : 1)}";
            return new MissionStep {
                type = "FiniteBurn",
                burn_name = burnName,
                propagator = defaultProp,
                bound_thruster = engName,
                bound_thrusters = clusterList,
                bound_tank = tkName,
                burn_use_vary_var = true,
                burn_vary_var_name = $"{burnName}_Duration",
                burn_decrement_mass = true,
                burn_isp = curThruster != null ? curThruster.IspVac : 300.0,
                label = clusterList.Count > 1 ? I18n.Format("burn_label_cluster", label, clusterList.Count) : I18n.Format("burn_label_single", label, engName)
            };
        }

        public List<StageData> GetSelectedStagesList() {
            List<StageData> list = new List<StageData>();
            if (cachedStages == null || cachedStages.Count == 0) return list;
            for (int i = 0; i < cachedStages.Count; i++) {
                if (selectedStageIndices.Contains(i)) {
                    list.Add(cachedStages[i]);
                }
            }
            if (list.Count == 0 || (list.Count == 1 && list[0].fuelMassTonnes <= 0.01)) {
                var realFuelStage = cachedStages.Find(s => s.fuelMassTonnes > 0.05);
                if (realFuelStage != null && !list.Contains(realFuelStage)) {
                    list.Add(realFuelStage);
                }
            }
            if (list.Count == 0 && cachedStages.Count > 0) {
                list.Add(cachedStages[0]);
            }
            return list;
        }

        public void LoadSimulationTexture(string path = null) {
            if (string.IsNullOrEmpty(path)) {
                path = Path.Combine(GMATClient.GetOutputDir(), "gmat_orbit_render.png");
            }
            if (System.IO.File.Exists(path)) {
                try {
                    byte[] data = System.IO.File.ReadAllBytes(path);
                    if (simulationTexture == null) simulationTexture = new Texture2D(2, 2);
                    simulationTexture.LoadImage(data);
                    imageStatusText = I18n.Format("img_loaded", simulationTexture.width, simulationTexture.height);
                } catch (Exception ex) {
                    imageStatusText = I18n.Format("img_load_fail", ex.Message);
                }
            } else {
                imageStatusText = I18n.T("img_not_ready");
            }
        }


        void OnGUI() {
            if (!visible) return;
            EnsureWindowInitialized();
            InitStyles();

            // Seamless focus restoration for IntelliSense & ParamPicker
            if (!string.IsNullOrEmpty(pendingFocusControl)) {
                GUI.FocusControl(pendingFocusControl);
                if (Event.current.type == EventType.Repaint) {
                    pendingFocusControl = "";
                }
            }

            // Sync real-time session objects into GMATParameterRegistry (zero hardcoding)
            GMATParameterRegistry.SyncRealtimeSequenceObjects(missionSequence, FlightGlobals.ActiveVessel);

            // IDE IntelliSense Keyboard Navigation (Up/Down with Auto-Scroll, Enter/Tab, Esc)
            if (intelliSenseOpen && intelliSenseItems != null && intelliSenseItems.Count > 0) {
                Event e = Event.current;
                if (e.type == EventType.KeyDown) {
                    if (e.keyCode == KeyCode.DownArrow) {
                        intelliSenseSelectedIndex = (intelliSenseSelectedIndex + 1) % intelliSenseItems.Count;
                        intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * 26f);
                        e.Use();
                    } else if (e.keyCode == KeyCode.UpArrow) {
                        intelliSenseSelectedIndex = (intelliSenseSelectedIndex - 1 + intelliSenseItems.Count) % intelliSenseItems.Count;
                        intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * 26f);
                        e.Use();
                    } else if (e.keyCode == KeyCode.PageDown) {
                        intelliSenseSelectedIndex = Mathf.Min(intelliSenseItems.Count - 1, intelliSenseSelectedIndex + 5);
                        intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * 26f);
                        e.Use();
                    } else if (e.keyCode == KeyCode.PageUp) {
                        intelliSenseSelectedIndex = Mathf.Max(0, intelliSenseSelectedIndex - 5);
                        intelliSenseScroll.y = Mathf.Max(0, (intelliSenseSelectedIndex - 2) * 26f);
                        e.Use();
                    } else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Tab) {
                        if (intelliSenseSelectedIndex >= 0 && intelliSenseSelectedIndex < intelliSenseItems.Count) {
                            ApplyIntelliSense(intelliSenseItems[intelliSenseSelectedIndex]);
                        }
                        e.Use();
                    } else if (e.keyCode == KeyCode.Escape) {
                        intelliSenseOpen = false;
                        intelliSenseDismissed = true;
                        e.Use();
                    }
                }
            }

            // 基线4.1: 全局背景色使用深空暗黑令牌，杜绝白色闪烁 (Zero-White-Flash)
            GUI.backgroundColor = new Color(0.038f, 0.048f, 0.070f, 1.0f);
            // Smooth screen boundary clamping without layout fighting
            windowRect.x = Mathf.Clamp(windowRect.x, 0f, Mathf.Max(0f, Screen.width - windowRect.width));
            windowRect.y = Mathf.Clamp(windowRect.y, 0f, Mathf.Max(0f, Screen.height - windowRect.height));

            windowRect = GUILayout.Window(982301, windowRect, DrawMainWindow, "", windowStyle);

            if (showSimulationImageWindow) {
                simulationImageWindowRect = GUILayout.Window(982302, simulationImageWindowRect, DrawSimulationImageWindow, "", windowStyle,
                    GUILayout.Width(simulationImageWindowRect.width), GUILayout.Height(simulationImageWindowRect.height));
            }

            if (showPropagatorConfigWindow) {
                propagatorConfigWindowRect = GUILayout.Window(982303, propagatorConfigWindowRect, DrawPropagatorConfigWindow, "", windowStyle,
                    GUILayout.Width(propagatorConfigWindowRect.width), GUILayout.Height(propagatorConfigWindowRect.height));
            }

            if (showCraftDetailsWindow) {
                craftDetailsWindowRect = GUILayout.Window(982304, craftDetailsWindowRect, DrawCraftDetailsWindow, "", windowStyle,
                    GUILayout.Width(craftDetailsWindowRect.width), GUILayout.Height(craftDetailsWindowRect.height));
            }

            if (showParamPickerModal) {
                paramPickerWindowRect = GUILayout.Window(982305, paramPickerWindowRect, DrawParamPickerWindow, "", windowStyle,
                    GUILayout.Width(paramPickerWindowRect.width), GUILayout.Height(paramPickerWindowRect.height));
            }

            if (showTargeterSetupModal) {
                targeterSetupWindowRect = GUILayout.Window(982306, targeterSetupWindowRect, DrawTargeterSetupWindow, "", windowStyle,
                    GUILayout.Width(targeterSetupWindowRect.width), GUILayout.Height(targeterSetupWindowRect.height));
            }

            if (showCoordSystemWindow) {
                if (coordSystemWindowRect.width < 450 || coordSystemWindowRect.height < 450) {
                    float cw = Mathf.Clamp(Screen.width * 0.45f, 480f, 650f);
                    float ch = Mathf.Clamp(Screen.height * 0.6f, 480f, 680f);
                    coordSystemWindowRect = new Rect((Screen.width - cw) / 2, (Screen.height - ch) / 2, cw, ch);
                }
                coordSystemWindowRect = GUILayout.Window(982307, coordSystemWindowRect, DrawCoordSystemWindow, "", windowStyle,
                    GUILayout.Width(coordSystemWindowRect.width), GUILayout.Height(coordSystemWindowRect.height));
            }

            if (showPlanetPickerModal) {
                if (planetPickerWindowRect.width < 450 || planetPickerWindowRect.height < 400) {
                    float pw = Mathf.Clamp(Screen.width * 0.45f, 480f, 650f);
                    float ph = Mathf.Clamp(Screen.height * 0.55f, 450f, 600f);
                    planetPickerWindowRect = new Rect((Screen.width - pw) / 2, (Screen.height - ph) / 2, pw, ph);
                }
                planetPickerWindowRect = GUILayout.Window(982308, planetPickerWindowRect, DrawPlanetPickerWindow, "", windowStyle,
                    GUILayout.Width(planetPickerWindowRect.width), GUILayout.Height(planetPickerWindowRect.height));
            }

            // Script Push Modal unified into Tab 1 Built-in IDE

            if (intelliSenseOpen) {
                DrawFloatingIntelliSenseWindow();
            }

            // Global hover tooltip
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip)) {
                Vector2 mousePos = Event.current.mousePosition;
                GUIContent tipContent = new GUIContent(GUI.tooltip);
                Vector2 tipSize = tooltipStyle.CalcSize(tipContent);
                float tw = Mathf.Min(tipSize.x + 20f, 320f);
                float th = tooltipStyle.CalcHeight(tipContent, tw);
                float tx = Mathf.Min(mousePos.x + 15f, Screen.width - tw - 10f);
                float ty = Mathf.Min(mousePos.y + 15f, Screen.height - th - 10f);
                GUI.Label(new Rect(tx, ty, tw, th), tipContent, tooltipStyle);
            }
        }

        private void DrawMainWindow(int windowID) {
            DrawTopBar();
            DrawTabBar();

            Vessel vessel = FlightGlobals.ActiveVessel;

            switch (activeTab) {
                case 0:
                    DrawTabCraftAndOrbit(vessel);
                    break;
                case 1:
                    DrawTabResources();
                    break;
                case 2:
                    DrawTabMissionSequence(vessel);
                    break;
                case 3:
                    DrawTabMGA(vessel);
                    break;
                case 4:
                    DrawTabResults();
                    break;
                case 5:
                    DrawTabSettings();
                    break;
            }

            DrawResizeHandle(ref windowRect, 980f, 680f);
            GUI.DragWindow(new Rect(0, 0, windowRect.width - 150, 32));
        }

        private void DrawTopBar() {
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("topbar_title"), headerStyle);

            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null) {
                GUILayout.Space(6);
                GUILayout.Label(I18n.ColorFormat("topbar_vessel", v.vesselName, v.mainBody.name), labelValueStyle);
            }

            GUILayout.Space(8);
            if (GMATClient.HasFullSpiceEphemeris) {
                string kernelName = GMATClient.DetectedSpiceKernelName;
                GUILayout.Label(I18n.ColorFormat("topbar_spice_loaded", kernelName), labelValueStyle);
            } else {
                double currentJd = 2433647.5 + (Planetarium.GetUniversalTime() / 86400.0);
                bool isBefore1957 = currentJd < 2436116.0;
                if (isBefore1957) {
                    GUILayout.Label(I18n.T("topbar_epoch_pre1957"), labelValueStyle);
                } else {
                    GUILayout.Label(I18n.T("topbar_epoch_post1957"), labelValueStyle);
                }
            }

            GUILayout.FlexibleSpace();

            string statusColor = GMATClient.IsConnected ? "#34d399" : "#f87171";
            string statusIcon = GMATClient.IsConnected ? I18n.T("status_online") : I18n.T("status_offline");
            GUILayout.Label($"<color={statusColor}><b>{statusIcon}</b></color>", labelValueStyle);

            GUILayout.Space(10);
            if (GUILayout.Button(isMaximized ? I18n.T("btn_restore") : I18n.T("btn_maximize"), secondaryButtonStyle, GUILayout.Width(58), GUILayout.Height(22))) {
                ToggleMaximize();
            }

            if (GUILayout.Button("✕", deleteButtonStyle, GUILayout.Width(26), GUILayout.Height(22))) {
                visible = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void DrawTabBar() {
            var tabs = GetTabNames();
            GUILayout.BeginHorizontal();
            for (int i = 0; i < tabs.Length; i++) {
                bool isSelected = (activeTab == i);
                if (GUILayout.Button(tabs[i], isSelected ? tabButtonActiveStyle : tabButtonStyle, GUILayout.Height(28))) {
                    activeTab = i;
                }
                if (Event.current.type == EventType.Repaint && isSelected) {
                    Rect r = GUILayoutUtility.GetLastRect();
                    // Draw sleek 2px neon cyan active bottom highlight
                    GUI.DrawTexture(new Rect(r.x + 2, r.y + r.height - 2, r.width - 4, 2), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.22f, 0.74f, 0.97f, 1f), 0, 0);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

    }
}
