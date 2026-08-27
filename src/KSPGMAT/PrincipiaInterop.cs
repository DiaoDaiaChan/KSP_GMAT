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
        private static MethodInfo mFlightPlanExists;
        private static MethodInfo mFlightPlanCreate;
        private static MethodInfo mFlightPlanSetDesiredFinalTime;
        private static MethodInfo mFlightPlanGetDesiredFinalTime;
        private static MethodInfo mFlightPlanNumberOfManoeuvres;
        private static MethodInfo mFlightPlanInsert;
        private static MethodInfo mFlightPlanReplace;

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

                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                BindingFlags instFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                if (tInterface != null) {
                    mCurrentTime = tInterface.GetMethod("CurrentTime", flags);
                    mHasVessel = tInterface.GetMethod("HasVessel", flags);
                    mVesselFromParent = tInterface.GetMethod("VesselFromParent", flags);
                    mFlightPlanExists = tInterface.GetMethod("FlightPlanExists", flags);
                    mFlightPlanCreate = tInterface.GetMethod("FlightPlanCreate", flags);
                    mFlightPlanSetDesiredFinalTime = tInterface.GetMethod("FlightPlanSetDesiredFinalTime", flags);
                    mFlightPlanGetDesiredFinalTime = tInterface.GetMethod("FlightPlanGetDesiredFinalTime", flags);
                    mFlightPlanNumberOfManoeuvres = tInterface.GetMethod("FlightPlanNumberOfManoeuvres", flags);
                    mFlightPlanInsert = tInterface.GetMethod("FlightPlanInsert", flags);
                    mFlightPlanReplace = tInterface.GetMethod("FlightPlanReplace", flags);

                    foreach (var m in tInterface.GetMethods(flags)) {
                        if (m.Name.Contains("FlightPlan")) {
                            List<string> pms = new List<string>();
                            foreach (var p in m.GetParameters()) pms.Add(p.ParameterType.Name + " " + p.Name);
                            KSPGMATLog.Info($"[Principia API] {m.Name}({string.Join(", ", pms.ToArray())}) -> {m.ReturnType.Name}");
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
                Debug.Log("[KSPGMAT] Principia reflection bindings initialized successfully.");
            } catch (Exception ex) {
                Debug.LogWarning("[KSPGMAT] Error initializing Principia reflection: " + ex.Message);
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
                        KSPGMATLog.Info($"[PrincipiaInterop] Query ICRF: source=Principia(VesselFromParent), r=[{pos_km.x:F2}, {pos_km.y:F2}, {pos_km.z:F2}]km (|r|={pos_km.magnitude:F1}km), v=[{vel_kms.x:F3}, {vel_kms.y:F3}, {vel_kms.z:F3}]km/s (|v|={vel_kms.magnitude:F3}km/s)");
                        return true;
                    }
                } catch (Exception ex) {
                    KSPGMATLog.Warn("[PrincipiaInterop] Error querying VesselFromParent: " + ex.Message);
                }
            }

            // Fallback to stock orbit frame if Principia plugin isn't active or hasn't tracked this vessel
            Vector3d r_ksp = vessel.orbit.pos;
            Vector3d v_ksp = vessel.orbit.vel;
            pos_km = new Vector3d(r_ksp.x / 1000.0, r_ksp.z / 1000.0, r_ksp.y / 1000.0);
            vel_kms = new Vector3d(v_ksp.x / 1000.0, v_ksp.z / 1000.0, v_ksp.y / 1000.0);
            KSPGMATLog.Warn($"[PrincipiaInterop] Query ICRF: source=StockFallback, r=[{pos_km.x:F2}, {pos_km.y:F2}, {pos_km.z:F2}]km, v=[{vel_kms.x:F3}, {vel_kms.y:F3}, {vel_kms.z:F3}]km/s");
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
                Debug.Log($"[KSPGMAT] Created new FlightPlan for vessel {vessel.vesselName} (mass: {massTonnes}t, finalTime: {finalTime})");
            }
            return true;
        }

        /// <summary>
        /// Inserts or replaces a burn in Principia's Flight Plan.
        /// </summary>
        public static bool InsertOrUpdateBurn(Vessel vessel, double burnInitialTime, Vector3d deltaV_mps, double thrust_kN, double isp_s, int burnIndex = 0, bool isFrenet = true, string originBodyName = null) {
            if (vessel == null || !HasVessel(vessel)) {
                Debug.LogWarning("[KSPGMAT] Cannot insert burn: Vessel is not tracked by Principia.");
                return false;
            }
            EnsureInitialized();
            IntPtr plugin = GetPlugin();
            if (plugin == IntPtr.Zero || vessel == null || tBurn == null || tNavFrame == null) return false;

            string guid = vessel.id.ToString();
            EnsureFlightPlanExists(vessel);

            // Only extend final time by a reasonable buffer (1.5 days past this burn) to prevent hitting Principia's 1000 step limit
            if (mFlightPlanGetDesiredFinalTime != null && mFlightPlanSetDesiredFinalTime != null) {
                try {
                    double desiredFinalTime = (double)mFlightPlanGetDesiredFinalTime.Invoke(null, new object[] { plugin, guid });
                    if (burnInitialTime + 43200.0 > desiredFinalTime) {
                        double newFinal = burnInitialTime + 86400.0 * 1.5;
                        mFlightPlanSetDesiredFinalTime.Invoke(null, new object[] { plugin, guid, newFinal });
                        KSPGMATLog.Info($"[PrincipiaInterop] Extended flight plan desired final time to {newFinal:F2}s");
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
            fNav_Extension.SetValue(navFrame, isFrenet ? 6000 : 0);

            // Create Burn object
            object burn = Activator.CreateInstance(tBurn);
            fBurn_InitialTime.SetValue(burn, burnInitialTime);
            fBurn_Thrust.SetValue(burn, Math.Max(0.001, thrust_kN));
            fBurn_Isp.SetValue(burn, Math.Max(1.0, isp_s));
            fBurn_Frame.SetValue(burn, navFrame);
            fBurn_IsInertiallyFixed.SetValue(burn, !isFrenet);

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

            if (err == 0) {
                KSPGMATLog.Success($"[PrincipiaInterop] Burn {burnIndex} inserted successfully! (time={burnInitialTime:F2}s, dv=({deltaV_mps.x:F1}, {deltaV_mps.y:F1}, {deltaV_mps.z:F1})m/s, origin={targetBody.name})");
            } else {
                KSPGMATLog.Error($"[PrincipiaInterop] Burn {burnIndex} failed with code {err}: {msg}");
            }

            return err == 0;
        }
    }
}
