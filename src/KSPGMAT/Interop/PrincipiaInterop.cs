using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using principia.ksp_plugin_adapter;

namespace KSPGMAT {
    /// <summary>
    /// Robust reflection-based interoperability layer for Principia N-body flight planner and ephemeris.
    /// Handles Principia's internal classes safely without visibility or versioning conflicts.
    /// </summary>
    public static class PrincipiaInterop {
        // Principia's KSP adapter uses this extension value for the Frenet
        // navigation frame. Zero is not a valid enum value in current builds.
        private const int FrenetNavigationFrameExtension = 6000;

        private static PrincipiaPluginAdapter cachedAdapter = null;
        private static bool initialized = false;

        // Cached reflection types & members
        private static Type tInterface;
        private static Type tQP;
        private static Type tBurn;
        private static Type tNavFrame;
        private static Type tStatus;

        private static MethodInfo mCurrentTime;
        private static MethodInfo mHasVessel;
        private static MethodInfo mVesselFromParent;
        private static MethodInfo mCelestialFromParent;
        private static MethodInfo mFlightPlanExists;
        private static MethodInfo mFlightPlanCreate;
        private static MethodInfo mFlightPlanDelete;
        private static MethodInfo mFlightPlanRemove;
        private static MethodInfo mFlightPlanSetDesiredFinalTime;
        private static MethodInfo mFlightPlanGetDesiredFinalTime;
        private static MethodInfo mFlightPlanNumberOfManoeuvres;
        private static MethodInfo mFlightPlanInsert;
        private static MethodInfo mFlightPlanReplace;
        private static MethodInfo mFlightPlanGetManoeuvre;

        private static FieldInfo fQP_Q;
        private static FieldInfo fQP_P;

        private static FieldInfo fBurn_Thrust;
        private static FieldInfo fBurn_Isp;
        private static FieldInfo fBurn_Frame;
        private static FieldInfo fBurn_InitialTime;
        private static FieldInfo fBurn_DeltaV;
        private static FieldInfo fBurn_IsInertiallyFixed;

        private static FieldInfo fNav_Centre;
        private static FieldInfo fNav_Primary;
        private static FieldInfo fNav_Secondary;
        private static FieldInfo fNav_Extension;

        private static FieldInfo fStatus_Error;
        private static FieldInfo fStatus_Message;

        private static FieldInfo fAdapterPlugin;
        private static FieldInfo fUnmodifiedOrbits;

        private static void EnsureInitialized() {
            if (initialized) return;
            try {
                Assembly pAsm = typeof(PrincipiaPluginAdapter).Assembly;
                tInterface = pAsm.GetType("principia.ksp_plugin_adapter.Interface");
                tQP = pAsm.GetType("principia.ksp_plugin_adapter.QP");
                tBurn = pAsm.GetType("principia.ksp_plugin_adapter.Burn");
                tNavFrame = pAsm.GetType("principia.ksp_plugin_adapter.NavigationFrameParameters");
                tStatus = pAsm.GetType("principia.ksp_plugin_adapter.Status");

                fAdapterPlugin = typeof(PrincipiaPluginAdapter).GetField("plugin_", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                fUnmodifiedOrbits = typeof(PrincipiaPluginAdapter).GetField("unmodified_orbits_", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                BindingFlags instFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                if (tInterface != null) {
                    mCurrentTime = tInterface.GetMethod("CurrentTime", flags);
                    mHasVessel = tInterface.GetMethod("HasVessel", flags);
                    mVesselFromParent = tInterface.GetMethod("VesselFromParent", flags);
                    mCelestialFromParent = tInterface.GetMethod("CelestialFromParent", flags);
                    mFlightPlanExists = tInterface.GetMethod("FlightPlanExists", flags);
                    mFlightPlanCreate = tInterface.GetMethod("FlightPlanCreate", flags);
                    mFlightPlanDelete = tInterface.GetMethod("FlightPlanDelete", flags);
                    mFlightPlanRemove = tInterface.GetMethod("FlightPlanRemove", flags);
                    mFlightPlanSetDesiredFinalTime = tInterface.GetMethod("FlightPlanSetDesiredFinalTime", flags);
                    mFlightPlanGetDesiredFinalTime = tInterface.GetMethod("FlightPlanGetDesiredFinalTime", flags);
                    mFlightPlanNumberOfManoeuvres = tInterface.GetMethod("FlightPlanNumberOfManoeuvres", flags);
                    mFlightPlanInsert = tInterface.GetMethod("FlightPlanInsert", flags);
                    mFlightPlanReplace = tInterface.GetMethod("FlightPlanReplace", flags);
                    foreach (var method in tInterface.GetMethods(flags)) {
                        if (method.Name == "FlightPlanGetManoeuvre") { mFlightPlanGetManoeuvre = method; break; }
                    }

                    foreach (var m in tInterface.GetMethods(flags)) {
                        if (m.Name.Contains("FlightPlan")) {
                            List<string> pms = new List<string>();
                            foreach (var p in m.GetParameters()) pms.Add(p.ParameterType.Name + " " + p.Name);
                            KSPGMATLog.Info("Principia", $"[API] {m.Name}({string.Join(", ", pms.ToArray())}) -> {m.ReturnType.Name}");
                        }
                    }
                }

                if (tQP != null) {
                    fQP_Q = tQP.GetField("q", instFlags);
                    fQP_P = tQP.GetField("p", instFlags);
                }

                if (tBurn != null) {
                    fBurn_Thrust = tBurn.GetField("thrust_in_kilonewtons", instFlags);
                    fBurn_Isp = tBurn.GetField("specific_impulse_in_seconds_g0", instFlags);
                    fBurn_Frame = tBurn.GetField("frame", instFlags);
                    fBurn_InitialTime = tBurn.GetField("initial_time", instFlags);
                    fBurn_DeltaV = tBurn.GetField("delta_v", instFlags);
                    fBurn_IsInertiallyFixed = tBurn.GetField("is_inertially_fixed", instFlags);
                }

                if (tNavFrame != null) {
                    fNav_Centre = tNavFrame.GetField("centre_index", instFlags);
                    fNav_Primary = tNavFrame.GetField("primary_index", instFlags);
                    fNav_Secondary = tNavFrame.GetField("secondary_index", instFlags);
                    fNav_Extension = tNavFrame.GetField("extension", instFlags);
                }

                if (tStatus != null) {
                    fStatus_Error = tStatus.GetField("error", instFlags);
                    fStatus_Message = tStatus.GetField("message", instFlags);
                }

                initialized = true;
                KSPGMATLog.Info("Principia", "Principia reflection bindings initialized successfully.");
            } catch (Exception ex) {
                KSPGMATLog.Error("Principia", "Error initializing Principia reflection", ex);
            }
        }

        public static PrincipiaPluginAdapter GetAdapter() {
            if (cachedAdapter == null || !cachedAdapter.PluginRunning()) {
                cachedAdapter = UnityEngine.Object.FindObjectOfType<PrincipiaPluginAdapter>();
            }
            return cachedAdapter;
        }

        public static bool IsPrincipiaAvailable() {
            var adapter = GetAdapter();
            return adapter != null && adapter.PluginRunning();
        }

        public static IntPtr GetPlugin() {
            EnsureInitialized();
            var adapter = GetAdapter();
            if (adapter == null || fAdapterPlugin == null) return IntPtr.Zero;
            try {
                object val = fAdapterPlugin.GetValue(adapter);
                return (val is IntPtr ptr) ? ptr : IntPtr.Zero;
            } catch {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// Checks if Principia's C++ core is currently tracking this vessel.
        /// Calling native C++ functions on a vessel not in Principia will abort the process!
        /// </summary>
        public static bool HasVessel(Vessel vessel) {
            if (vessel == null) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || mHasVessel == null) return false;
            try {
                return (bool)mHasVessel.Invoke(null, new object[] { plugin, vessel.id.ToString() });
            } catch {
                return false;
            }
        }

        public static double GetCurrentPrincipiaTime() {
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || mCurrentTime == null) return Planetarium.GetUniversalTime();
            try {
                return (double)mCurrentTime.Invoke(null, new object[] { plugin });
            } catch {
                return Planetarium.GetUniversalTime();
            }
        }

        /// <summary>
        /// Reads vessel position and velocity relative to parent body in Principia's barycentric ICRF.
        /// </summary>
        public static bool GetVesselStateICRF(Vessel vessel, out Vector3d pos_km, out Vector3d vel_kms) {
            pos_km = Vector3d.zero;
            vel_kms = Vector3d.zero;

            if (vessel == null) return false;

            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin != IntPtr.Zero && vessel.mainBody != null && mVesselFromParent != null && HasVessel(vessel)) {
                try {
                    int parentIndex = vessel.mainBody.flightGlobalsIndex;
                    string guid = vessel.id.ToString();
                    object qpObj = mVesselFromParent.Invoke(null, new object[] { plugin, parentIndex, guid });
                    if (qpObj != null && fQP_Q != null && fQP_P != null) {
                        XYZ q = (XYZ)fQP_Q.GetValue(qpObj);
                        XYZ p = (XYZ)fQP_P.GetValue(qpObj);

                        // Convert meters and m/s to km and km/s
                        pos_km = new Vector3d(q.x / 1000.0, q.y / 1000.0, q.z / 1000.0);
                        vel_kms = new Vector3d(p.x / 1000.0, p.y / 1000.0, p.z / 1000.0);
                        KSPGMATLog.Info("Principia", $"Query ICRF: source=Principia(VesselFromParent), r=[{pos_km.x:F2}, {pos_km.y:F2}, {pos_km.z:F2}]km (|r|={pos_km.magnitude:F1}km), v=[{vel_kms.x:F3}, {vel_kms.y:F3}, {vel_kms.z:F3}]km/s (|v|={vel_kms.magnitude:F3}km/s)");
                        return true;
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Error("Principia", "Error querying VesselFromParent", ex);
                }
            }

            // Fallback to stock orbit frame if Principia plugin isn't active or hasn't tracked this vessel
            Vector3d r_ksp = vessel.orbit.pos;
            Vector3d v_ksp = vessel.orbit.vel;
            pos_km = new Vector3d(r_ksp.x / 1000.0, r_ksp.z / 1000.0, r_ksp.y / 1000.0);
            vel_kms = new Vector3d(v_ksp.x / 1000.0, v_ksp.z / 1000.0, v_ksp.y / 1000.0);
            KSPGMATLog.Warn("Principia", $"Query ICRF: source=StockFallback, r=[{pos_km.x:F2}, {pos_km.y:F2}, {pos_km.z:F2}]km, v=[{vel_kms.x:F3}, {vel_kms.y:F3}, {vel_kms.z:F3}]km/s");
            return true;
        }

        /// <summary>
        /// 获取 Principia 内部缓存的天体原始未篡改轨道 (Unmodified Orbit)。
        /// Principia 每帧会用当前时刻瞬时密切根数覆写 body.orbit，
        /// 若要在跨历元时间外推，必须使用 unmodified_orbits_ 才能避免二体开普勒外推发散导致的数十万至十数亿公里偏差。
        /// </summary>
        public static Orbit GetUnmodifiedOrbit(CelestialBody body) {
            if (body == null) return null;
            EnsureInitialized();
            if (fUnmodifiedOrbits != null) {
                try {
                    var dict = fUnmodifiedOrbits.GetValue(null) as Dictionary<CelestialBody, Orbit>;
                    if (dict != null && dict.TryGetValue(body, out Orbit unmod)) {
                        return unmod;
                    }
                } catch { }
            }
            return body.orbit;
        }

        /// <summary>
        /// 查询 Principia 原生 N 体高精度接口在当前帧相对父天体的状态矢量 (仅限当前时刻附近)。
        /// </summary>
        public static bool TryGetCelestialFromParent(CelestialBody body, out Vector3d pos_km, out Vector3d vel_kms) {
            pos_km = Vector3d.zero;
            vel_kms = Vector3d.zero;
            if (body == null || body.referenceBody == null || body.referenceBody == body) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || mCelestialFromParent == null || fQP_Q == null || fQP_P == null) return false;
            try {
                object qpObj = mCelestialFromParent.Invoke(null, new object[] { plugin, body.flightGlobalsIndex });
                if (qpObj != null) {
                    XYZ q = (XYZ)fQP_Q.GetValue(qpObj);
                    XYZ p = (XYZ)fQP_P.GetValue(qpObj);
                    // 标准天体力学惯性坐标 (X=East, Y=North/90deg, Z=Normal/Up)
                    pos_km = new Vector3d(q.x / 1000.0, q.y / 1000.0, q.z / 1000.0);
                    vel_kms = new Vector3d(p.x / 1000.0, p.y / 1000.0, p.z / 1000.0);
                    return true;
                }
            } catch (Exception ex) {
                KSPGMATLog.Error("Principia", "Error querying CelestialFromParent", ex);
            }
            return false;
        }

        /// <summary>
        /// 方案 B: 获得天体相对日心的高精度 ICRF 空间状态 (位置 km, 速度 km/s)。
        /// - 若 ut 处于当前时刻附近 (|ut - CurrentTime| < 1.0)，调用 Principia C++ 原生 CelestialFromParent 级联求和；
        /// - 若 ut 处于任意历史/未来时间，调用 Principia 保存的真初始未篡改轨道 (unmodified_orbits_) 级联外推，
        ///   彻底根除每帧 osculating 密切根数外推产生的 12.8 亿公里虚假偏差！
        /// </summary>
        public static bool TryGetHeliocentricState(CelestialBody body, double ut, out Vector3d pos_km, out Vector3d vel_kms) {
            pos_km = Vector3d.zero;
            vel_kms = Vector3d.zero;
            if (body == null) return false;

            // 太阳自身或根参考系
            if (body.referenceBody == null || body.referenceBody == body || (body.orbit == null && GetUnmodifiedOrbit(body) == null)) {
                return true;
            }

            double curTime = GetCurrentPrincipiaTime();
            bool isCurrentInstant = Math.Abs(ut - curTime) < 1.0;

            Vector3d r_sum = Vector3d.zero;
            Vector3d v_sum = Vector3d.zero;
            CelestialBody curr = body;

            while (curr != null && curr.referenceBody != null && curr.referenceBody != curr) {
                Vector3d r_rel = Vector3d.zero;
                Vector3d v_rel = Vector3d.zero;
                bool gotRel = false;

                // 1. 若为当前帧，优先尝试直读 Principia 原生 N 体真实相对状态
                if (isCurrentInstant && IsPrincipiaAvailable()) {
                    if (TryGetCelestialFromParent(curr, out r_rel, out v_rel)) {
                        gotRel = true;
                    }
                }

                // 2. 否则或原生失败，读取未篡改轨道 (unmodified_orbits_) 沿真实根数与历元稳定外推
                if (!gotRel) {
                    Orbit unmod = GetUnmodifiedOrbit(curr) ?? curr.orbit;
                    if (unmod != null) {
                        Vector3d r_m = unmod.getRelativePositionAtUT(ut);
                        Vector3d v_ms = unmod.getOrbitalVelocityAtUT(ut);
                        // KSP Orbit 输出已处于标准 Z-Up 惯性系 (X=春分点, Y=黄道90°, Z=法向)，严禁交换 Y 与 Z
                        r_rel = new Vector3d(r_m.x / 1000.0, r_m.y / 1000.0, r_m.z / 1000.0);
                        v_rel = new Vector3d(v_ms.x / 1000.0, v_ms.y / 1000.0, v_ms.z / 1000.0);
                        gotRel = true;
                    }
                }

                if (!gotRel) {
                    return false;
                }

                r_sum += r_rel;
                v_sum += v_rel;
                curr = curr.referenceBody;
            }

            pos_km = r_sum;
            vel_kms = v_sum;
            return true;
        }

        /// <summary>
        /// Checks if a flight plan exists for the given vessel.
        /// </summary>
        public static bool HasFlightPlan(Vessel vessel) {
            if (vessel == null || !HasVessel(vessel)) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || mFlightPlanExists == null) return false;
            try {
                return (bool)mFlightPlanExists.Invoke(null, new object[] { plugin, vessel.id.ToString() });
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Creates a Principia Flight Plan if not existing.
        /// </summary>
        public static bool EnsureFlightPlanExists(Vessel vessel, double initialDurationSeconds = 604800.0) {
            if (vessel == null || !HasVessel(vessel)) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || vessel == null) return false;

            string guid = vessel.id.ToString();
            if (!HasFlightPlan(vessel) && mFlightPlanCreate != null) {
                double currentTime = GetCurrentPrincipiaTime();
                double finalTime = currentTime + initialDurationSeconds;
                double massTonnes = Math.Max(0.1, vessel.totalMass);
                mFlightPlanCreate.Invoke(null, new object[] { plugin, guid, finalTime, massTonnes });
                KSPGMATLog.Info("Principia", $"Created new FlightPlan for vessel {vessel.vesselName} (mass: {massTonnes}t, finalTime: {finalTime})");
            }
            return true;
        }

        /// <summary>
        /// Clears all manoeuvre nodes from the Principia flight plan.
        /// </summary>
        public static bool ClearAllFlightPlanNodes(Vessel vessel) {
            if (vessel == null || !HasVessel(vessel)) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero) return false;
            string guid = vessel.id.ToString();
            try {
                if (mFlightPlanNumberOfManoeuvres != null && mFlightPlanRemove != null && HasFlightPlan(vessel)) {
                    int count = (int)mFlightPlanNumberOfManoeuvres.Invoke(null, new object[] { plugin, guid });
                    for (int i = count - 1; i >= 0; i--) {
                        try {
                            mFlightPlanRemove.Invoke(null, new object[] { plugin, guid, i });
                        } catch {}
                    }
                }
                if (mFlightPlanDelete != null && HasFlightPlan(vessel)) {
                    try {
                        mFlightPlanDelete.Invoke(null, new object[] { plugin, guid });
                    } catch {}
                }
                EnsureFlightPlanExists(vessel);
                KSPGMATLog.Info("Principia", $"Cleared all Principia FlightPlan nodes for vessel {vessel.vesselName}");
                return true;
            } catch (Exception ex) {
                KSPGMATLog.Error("Principia", "Error clearing FlightPlan", ex);
                return false;
            }
        }

        public static bool SetFlightPlanFinalTime(Vessel vessel, double finalTime) {
            if (vessel == null || !HasVessel(vessel)) return false;
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || mFlightPlanSetDesiredFinalTime == null) return false;
            try {
                object status = mFlightPlanSetDesiredFinalTime.Invoke(null, new object[] { plugin, vessel.id.ToString(), finalTime });
                if (status != null && fStatus_Error != null) return (int)fStatus_Error.GetValue(status) == 0;
                return true;
            } catch (Exception ex) {
                KSPGMATLog.Error("Principia", "Failed to set flight plan final time", ex);
                return false;
            }
        }

        /// <summary>
        /// Inserts or replaces a burn in Principia's Flight Plan.
        /// </summary>
        public static bool InsertOrUpdateBurn(Vessel vessel, double burnInitialTime, Vector3d deltaV_mps, double thrust_kN, double isp_s, int burnIndex = 0, bool isFrenet = true, string originBodyName = null) {
            if (vessel == null || !HasVessel(vessel)) {
                KSPGMATLog.Warn("Principia", "Cannot insert burn: Vessel is not tracked by Principia.");
                return false;
            }
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || tBurn == null || tNavFrame == null ||
                fNav_Centre == null || fNav_Primary == null || fNav_Secondary == null || fNav_Extension == null ||
                fBurn_Thrust == null || fBurn_Isp == null || fBurn_Frame == null || fBurn_InitialTime == null ||
                fBurn_DeltaV == null || fBurn_IsInertiallyFixed == null) {
                KSPGMATLog.Error("Principia", "Burn write rejected: Principia adapter fields are unavailable.");
                return false;
            }

            if (double.IsNaN(burnInitialTime) || double.IsInfinity(burnInitialTime) ||
                double.IsNaN(thrust_kN) || double.IsInfinity(thrust_kN) ||
                double.IsNaN(isp_s) || double.IsInfinity(isp_s) ||
                double.IsNaN(deltaV_mps.x) || double.IsInfinity(deltaV_mps.x) ||
                double.IsNaN(deltaV_mps.y) || double.IsInfinity(deltaV_mps.y) ||
                double.IsNaN(deltaV_mps.z) || double.IsInfinity(deltaV_mps.z)) {
                KSPGMATLog.Error("Principia", "Burn write rejected: non-finite burn data.");
                return false;
            }

            // The adapter currently rejects extension 0 in NavigationFrameParameters
            // with a native CHECK failure. Keep inertially-fixed burns out of this
            // write path until a valid adapter frame for them is available.
            if (!isFrenet) {
                KSPGMATLog.Error("Principia", "Burn write rejected: only the valid Frenet navigation frame is supported.");
                return false;
            }

            string guid = vessel.id.ToString();
            EnsureFlightPlanExists(vessel);

            // Keep the requested horizon close to the last burn. A multi-day horizon can
            // exceed Principia's 1000-step limit before the manoeuvre is inserted.
            if (mFlightPlanGetDesiredFinalTime != null && mFlightPlanSetDesiredFinalTime != null) {
                try {
                    double desiredFinalTime = (double)mFlightPlanGetDesiredFinalTime.Invoke(null, new object[] { plugin, guid });
                    if (burnInitialTime + 21600.0 > desiredFinalTime) {
                        double newFinal = burnInitialTime + 21600.0;
                        mFlightPlanSetDesiredFinalTime.Invoke(null, new object[] { plugin, guid, newFinal });
                        KSPGMATLog.Info("Principia", $"Extended flight plan desired final time to {newFinal:F2}s");
                    }
                } catch {}
            }

            // Determine reference body (vessel's current mainBody, or specific origin body like Luna/Moon)
            CelestialBody targetBody = vessel.mainBody;
            if (!string.IsNullOrEmpty(originBodyName) && FlightGlobals.Bodies != null) {
                CelestialBody b = FlightGlobals.Bodies.Find(x => 
                    x.name.Equals(originBodyName, StringComparison.OrdinalIgnoreCase) ||
                    (originBodyName.Equals("Luna", StringComparison.OrdinalIgnoreCase) && x.name.Equals("Moon", StringComparison.OrdinalIgnoreCase)) ||
                    (originBodyName.Equals("Moon", StringComparison.OrdinalIgnoreCase) && x.name.Equals("Luna", StringComparison.OrdinalIgnoreCase))
                );
                if (b != null) targetBody = b;
            }

            // Create NavFrame object
            object navFrame = Activator.CreateInstance(tNavFrame);
            fNav_Centre.SetValue(navFrame, targetBody.flightGlobalsIndex);
            fNav_Primary.SetValue(navFrame, targetBody.flightGlobalsIndex);
            fNav_Secondary.SetValue(navFrame, targetBody.flightGlobalsIndex);
            fNav_Extension.SetValue(navFrame, FrenetNavigationFrameExtension);

            // Create Burn object
            object burn = Activator.CreateInstance(tBurn);
            fBurn_InitialTime.SetValue(burn, burnInitialTime);
            fBurn_Thrust.SetValue(burn, Math.Max(0.001, thrust_kN));
            fBurn_Isp.SetValue(burn, Math.Max(1.0, isp_s));
            fBurn_Frame.SetValue(burn, navFrame);
            fBurn_IsInertiallyFixed.SetValue(burn, false);

            XYZ dv = new XYZ();
            dv.x = deltaV_mps.x;
            dv.y = deltaV_mps.y;
            dv.z = deltaV_mps.z;
            fBurn_DeltaV.SetValue(burn, dv);

            int numManoeuvres = 0;
            if (mFlightPlanNumberOfManoeuvres != null) {
                numManoeuvres = (int)mFlightPlanNumberOfManoeuvres.Invoke(null, new object[] { plugin, guid });
            }

            object status = null;
            if (burnIndex < numManoeuvres && mFlightPlanReplace != null) {
                status = mFlightPlanReplace.Invoke(null, new object[] { plugin, guid, burn, burnIndex });
            } else if (mFlightPlanInsert != null) {
                status = mFlightPlanInsert.Invoke(null, new object[] { plugin, guid, burn, numManoeuvres });
            }

            int err = 0;
            string msg = "";
            if (status != null && fStatus_Error != null) {
                err = (int)fStatus_Error.GetValue(status);
                msg = fStatus_Message != null ? (string)fStatus_Message.GetValue(status) : "";
            }

            // Check if the manoeuvre is now present in the flight plan
            int afterCount = 0;
            if (mFlightPlanNumberOfManoeuvres != null) {
                try {
                    afterCount = (int)mFlightPlanNumberOfManoeuvres.Invoke(null, new object[] { plugin, guid });
                } catch {}
            }

            bool writtenSuccessfully = (err == 0) || (afterCount > numManoeuvres) || (burnIndex < afterCount);

            if (writtenSuccessfully) {
                KSPGMATLog.Success("Principia", $"Burn {burnIndex} inserted/confirmed in Principia! (time={burnInitialTime:F2}s, dv=({deltaV_mps.x:F1}, {deltaV_mps.y:F1}, {deltaV_mps.z:F1})m/s, origin={targetBody.name})");
            } else {
                KSPGMATLog.Warn("Principia", $"Burn {burnIndex} status code {err}: {msg}");
            }

            return writtenSuccessfully;
        }

        /// <summary>
        /// 基线 5.3: 变质量冲量质心提前量 (用于有限点火写入 Principia 脉冲节点)。
        /// 恒推力有限点火下推进剂线性耗尽，冲量质心为质量加权平均时刻：
        ///   Δt_lead = t_burn · (m0/2 − m_prop/3) / (m0 − m_prop/2),  m_prop = m0 − m_final
        /// 节点实际写入历元 t_node = t_ignition + Δt_lead；相比粗糙对半 t_burn/2 精度更高。
        /// 说明: 基线文档解析式 (1/β − t_burn·m_f/m_prop) 代数上恒等于 t_burn，故此处采用其
        /// 物理本意 (质量加权冲量质心时刻) 实现，避免退化恒等式。
        /// </summary>
        /// <param name="burnDuration">点火时长 t_burn (s)</param>
        /// <param name="mass0">点火初质量 m0 (kg)</param>
        /// <param name="massFinal">关机末质量 m_f (kg)</param>
        public static double ComputeBurnCentroidLead(double burnDuration, double mass0, double massFinal) {
            if (burnDuration <= 0.0 || mass0 <= 0.0) return 0.0;
            double mf = Math.Max(massFinal, 0.0);
            if (mf > mass0) mf = mass0;
            double mProp = mass0 - mf;
            if (mProp <= 0.0) return burnDuration * 0.5;
            double denom = mass0 - mProp * 0.5;
            if (denom <= 0.0) return burnDuration * 0.5;
            return burnDuration * (mass0 * 0.5 - mProp / 3.0) / denom;
        }

        public class FlightPlanNode {
            public double initialTime;
            public Vector3d deltaV;
            public double duration;
            public double thrust_kn;
            public double isp;
            public double initial_mass_tonnes;
            public bool hasTime;
            public bool hasDeltaV;
            public bool hasDuration;
        }

        public static bool TryReadFlightPlanNodes(Vessel vessel, out List<FlightPlanNode> nodes, out string message) {
            nodes = new List<FlightPlanNode>(); message = "";
            if (vessel == null || !HasVessel(vessel)) { message = "Principia 未跟踪当前飞船"; return false; }
            if (!HasFlightPlan(vessel)) { message = "当前飞船尚未在游戏中创建 Principia 飞行计划"; return false; }
            EnsureInitialized(); IntPtr plugin = GetPlugin(); string guid = vessel.id.ToString();
            if (plugin == IntPtr.Zero || mFlightPlanNumberOfManoeuvres == null || mFlightPlanGetManoeuvre == null) { message = "当前 Principia 版本未暴露 FlightPlanGetManoeuvre"; return false; }
            try {
                int count = (int)mFlightPlanNumberOfManoeuvres.Invoke(null, new object[] { plugin, guid });
                for (int i = 0; i < count; i++) {
                    object manoeuvre = InvokeManoeuvreGetter(plugin, guid, i);
                    if (manoeuvre == null) continue;
                    FlightPlanNode node = new FlightPlanNode();
                    object nestedBurn = ReadMember(manoeuvre, new string[] { "burn", "manoeuvre", "navigation_manoeuvre" });
                    node.hasTime = TryReadDouble(manoeuvre, new string[] { "initial_time", "time", "burn_initial_time" }, out node.initialTime);
                    if (!node.hasTime && nestedBurn != null) node.hasTime = TryReadDouble(nestedBurn, new string[] { "initial_time", "time", "burn_initial_time" }, out node.initialTime);
                    object dv = ReadMember(manoeuvre, new string[] { "delta_v", "deltaV" }) ?? (nestedBurn != null ? ReadMember(nestedBurn, new string[] { "delta_v", "deltaV" }) : null);
                    node.hasDeltaV = TryReadVector(dv, out node.deltaV);

                    TryReadDouble(manoeuvre, new string[] { "duration", "burn_duration" }, out node.duration);
                    if (nestedBurn != null) {
                        TryReadDouble(nestedBurn, new string[] { "thrust_in_kilonewtons", "thrust" }, out node.thrust_kn);
                        TryReadDouble(nestedBurn, new string[] { "specific_impulse_in_seconds_g0", "specific_impulse", "isp" }, out node.isp);
                    }
                    TryReadDouble(manoeuvre, new string[] { "initial_mass_in_tonnes", "initial_mass" }, out node.initial_mass_tonnes);

                    if (node.duration <= 0.0 && node.thrust_kn > 0.0 && node.isp > 0.0 && node.initial_mass_tonnes > 0.0 && node.hasDeltaV) {
                        node.duration = node.initial_mass_tonnes * 1000.0 * (node.isp * 9.80665) * (1.0 - Math.Exp(-node.deltaV.magnitude / (node.isp * 9.80665))) / (node.thrust_kn * 1000.0);
                    }
                    node.hasDuration = node.duration > 0.0;
                    nodes.Add(node);
                }
                message = "已读取 " + nodes.Count + " 个 Principia manoeuvre 节点";
                return true;
            } catch (Exception ex) {
                KSPGMATLog.Error("Principia", "读取 Principia Flight Plan 失败", ex);
                message = "读取 Principia Flight Plan 失败: " + ex.Message;
                return false;
            }
        }

        private static object InvokeManoeuvreGetter(IntPtr plugin, string guid, int index) {
            ParameterInfo[] parameters = mFlightPlanGetManoeuvre.GetParameters(); object[] args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++) {
                Type type = parameters[i].ParameterType;
                Type valueType = type.IsByRef ? type.GetElementType() : type;
                if (valueType == typeof(IntPtr)) args[i] = plugin;
                else if (valueType == typeof(string)) args[i] = guid;
                else if (valueType == typeof(int)) args[i] = index;
                else if (valueType == typeof(long)) args[i] = (long)index;
                else args[i] = valueType.IsValueType ? Activator.CreateInstance(valueType) : null;
            }
            object result = mFlightPlanGetManoeuvre.Invoke(null, args);
            if (HasNodeData(result)) return result;
            for (int i = 0; i < parameters.Length; i++) if (parameters[i].ParameterType.IsByRef && HasNodeData(args[i])) return args[i];
            return null;
        }

        private static bool HasNodeData(object value) {
            return value != null && (ReadMember(value, new string[] { "initial_time", "time", "burn_initial_time", "delta_v", "deltaV" }) != null || ReadMember(value, new string[] { "burn", "manoeuvre" }) != null);
        }

        private static object ReadMember(object instance, string[] names) {
            if (instance == null) return null;
            Type type = instance.GetType(); BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (string name in names) {
                FieldInfo field = type.GetField(name, flags); if (field != null) return field.GetValue(instance);
                PropertyInfo property = type.GetProperty(name, flags); if (property != null && property.CanRead) return property.GetValue(instance, null);
            }
            return null;
        }

        private static bool TryReadDouble(object instance, string[] names, out double value) {
            value = 0.0; object member = ReadMember(instance, names); if (member == null) return false;
            try { value = Convert.ToDouble(member); return true; } catch { return false; }
        }

        private static bool TryReadVector(object instance, out Vector3d value) {
            value = Vector3d.zero; if (instance == null) return false;
            double x, y, z; if (!TryReadDouble(instance, new string[] { "x", "X" }, out x) || !TryReadDouble(instance, new string[] { "y", "Y" }, out y) || !TryReadDouble(instance, new string[] { "z", "Z" }, out z)) return false;
            value = new Vector3d(x, y, z); return true;
        }
    }
}
