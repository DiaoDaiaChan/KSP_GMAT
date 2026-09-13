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

            if (gameObject.GetComponent<GMATMainWindow>() == null) {
                gameObject.AddComponent<GMATMainWindow>();
            }

            if (!ROEngineInterop.IsRealismOverhaulInstalled()) {
                UnityEngine.Debug.LogWarning("[KSPGMAT] WARNING: RealismOverhaul / RealFuels is required for realistic propellant density and engine models!");
            } else {
                UnityEngine.Debug.Log("[KSPGMAT] RealismOverhaul & RealFuels detected. Propellant densities enabled.");
            }

            UnityEngine.Debug.Log("[KSPGMAT] Bridge initialized (CLI mode, no background server).");
            // Verify CLI availability
            GMATClient.CheckConnection((online, msg) => {
                if (online) {
                    UnityEngine.Debug.Log("[KSPGMAT] GMAT engine ready: " + msg);
                } else {
                    UnityEngine.Debug.LogWarning("[KSPGMAT] GMAT engine not available: " + msg);
                }
            });
        }
    }
}
