using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using KSPGMAT.UI;

namespace KSPGMAT {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class KSPGMATBridge : MonoBehaviour {
        public static KSPGMATBridge Instance { get; private set; }

        private Process pythonProcess = null;

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

            UnityEngine.Debug.Log("[KSPGMAT] Bridge initialized successfully.");
            EnsureBackendRunning();
        }

        private void EnsureBackendRunning() {
            GMATClient.CheckConnection((online, msg) => {
                if (!online) {
                    UnityEngine.Debug.Log("[KSPGMAT] GMAT Backend offline, launching Python service...");
                    LaunchPythonService();
                } else {
                    UnityEngine.Debug.Log("[KSPGMAT] Connected to GMAT Backend: " + msg);
                }
            });
        }

        private void LaunchPythonService() {
            try {
                string serviceScript = KSPGMATSettings.ServiceScriptPath;
                string binDir = KSPGMATSettings.RootPath;

                if (!string.IsNullOrEmpty(binDir) && File.Exists(serviceScript)) {
                    ProcessStartInfo psi = new ProcessStartInfo {
                        FileName = "python",
                        Arguments = $"\"{serviceScript}\"",
                        WorkingDirectory = binDir,
                        UseShellExecute = true, // Prevents inheriting KSP's file handles (like stderr.log)
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    pythonProcess = Process.Start(psi);
                    UnityEngine.Debug.Log("[KSPGMAT] Started Python GMAT Service process (PID: " + pythonProcess.Id + ")");
                } else {
                    UnityEngine.Debug.LogWarning("[KSPGMAT] Service script not found at " + serviceScript);
                }
            } catch (Exception ex) {
                UnityEngine.Debug.LogError("[KSPGMAT] Failed to start Python service: " + ex.Message);
            }
        }

        private void KillPythonService() {
            try {
                if (pythonProcess != null && !pythonProcess.HasExited) {
                    pythonProcess.Kill();
                    pythonProcess.Dispose();
                    pythonProcess = null;
                    UnityEngine.Debug.Log("[KSPGMAT] Python GMAT Service terminated cleanly.");
                }
            } catch {}
        }

        void OnApplicationQuit() {
            KillPythonService();
        }

        void OnDestroy() {
            KillPythonService();
        }
    }
}
