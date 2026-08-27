using System;
using UnityEngine;
using KSP.UI.Screens;

namespace KSPGMAT.UI {
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class ToolbarRegistration : MonoBehaviour {
        private ApplicationLauncherButton launcherButton;

        void Start() {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            if (ApplicationLauncher.Ready) {
                OnAppLauncherReady();
            }
        }

        void OnDestroy() {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            if (launcherButton != null) {
                ApplicationLauncher.Instance.RemoveModApplication(launcherButton);
            }
        }

        private void OnAppLauncherReady() {
            if (launcherButton != null) return;

            Texture2D icon = CreateToolbarIcon();
            launcherButton = ApplicationLauncher.Instance.AddModApplication(
                onTrue: () => { if (GMATMainWindow.Instance != null) GMATMainWindow.Instance.Toggle(); },
                onFalse: () => { if (GMATMainWindow.Instance != null) GMATMainWindow.Instance.Toggle(); },
                onHover: null,
                onHoverOut: null,
                onEnable: null,
                onDisable: null,
                visibleInScenes: ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                texture: icon
            );
        }

        private Texture2D CreateToolbarIcon() {
            // Procedural crisp neon GMAT icon
            int w = 38, h = 38;
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color bg = new Color(0.1f, 0.12f, 0.18f, 0.9f);
            Color border = new Color(0.0f, 0.8f, 1.0f, 1.0f);
            Color textCol = Color.white;

            for (int y = 0; y < h; y++) {
                for (int x = 0; x < w; x++) {
                    if (x < 2 || x >= w - 2 || y < 2 || y >= h - 2) {
                        tex.SetPixel(x, y, border);
                    } else {
                        tex.SetPixel(x, y, bg);
                    }
                }
            }

            // Draw a subtle orbit ring inside
            float cx = w / 2f, cy = h / 2f, r = 12f;
            for (float angle = 0; angle < 360f; angle += 1f) {
                float rad = angle * Mathf.Deg2Rad;
                int px = (int)(cx + r * Mathf.Cos(rad));
                int py = (int)(cy + (r * 0.5f) * Mathf.Sin(rad));
                if (px >= 0 && px < w && py >= 0 && py < h) {
                    tex.SetPixel(px, py, new Color(0.0f, 0.9f, 0.9f, 0.8f));
                }
            }

            tex.Apply();
            return tex;
        }
    }
}
