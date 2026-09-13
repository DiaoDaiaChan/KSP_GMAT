using System;
using System.IO;
using UnityEngine;
using KSPGMAT.UI;

namespace KSPGMAT {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPGMATBridge : MonoBehaviour {
        public static KSPGMATBridge Instance { get; private set; }

        void Awake() {
            Instance = this;
            DontDestroyOnLoad(this);

            KSPGMATLog.Init();
            KSPGMATLog.Info("Bridge", "KSPGMAT plugin initializing...");

            if (gameObject.GetComponent<GMATMainWindow>() == null) {
                gameObject.AddComponent<GMATMainWindow>();
            }

            if (!ROEngineInterop.IsRealismOverhaulInstalled()) {
                KSPGMATLog.Warn("Bridge", "RealismOverhaul / RealFuels is required for realistic propellant density and engine models!");
            } else {
                KSPGMATLog.Info("Bridge", "RealismOverhaul & RealFuels detected. Propellant densities enabled.");
            }

            KSPGMATLog.Info("Bridge", "Bridge initialized (CLI mode, no background server).");
            // Verify CLI availability
            GMATClient.CheckConnection((online, msg) => {
                if (online) {
                    KSPGMATLog.Info("Bridge", "GMAT engine ready: " + msg);
                } else {
                    KSPGMATLog.Warn("Bridge", "GMAT engine not available: " + msg);
                }
            });
        }

        void OnDestroy() {
            KSPGMATLog.Flush();
        }
    }
}
