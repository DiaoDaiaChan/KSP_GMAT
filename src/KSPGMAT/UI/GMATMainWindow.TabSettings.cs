using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void DrawTabSettings() {
            // Language & Localization Selection Bar
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("🌐 语言与区域设置 (Localization & Language)", headerStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label("界面语言 (UI Language):", labelValueStyle, GUILayout.Width(160));
            bool isZh = (I18n.Current == I18n.Lang.ZH);
            if (GUILayout.Button(isZh ? "● 🇨🇳 中文 (简体)" : "🇨🇳 中文 (简体)", isZh ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(24))) {
                I18n.SetLang(I18n.Lang.ZH);
            }
            if (GUILayout.Button(!isZh ? "● 🇺🇸 English" : "🇺🇸 English", !isZh ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(130), GUILayout.Height(24))) {
                I18n.SetLang(I18n.Lang.EN);
            }
            if (GUILayout.Button("🔄 自动识别 (Auto Detect)", secondaryButtonStyle, GUILayout.Width(150), GUILayout.Height(24))) {
                I18n.AutoDetect();
                ScreenMessages.PostScreenMessage($"🌐 已自动识别语言: {(I18n.Current == I18n.Lang.ZH ? "中文 (ZH)" : "English (EN)")}", 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);

            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label("⚙️ GMAT 引擎路径设置", headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("GMAT Bin 路径:", labelValueStyle, GUILayout.Width(120));
            GMATClient.GmatBinPath = DrawDelayedTextField("settings_gmat_bin", GMATClient.GmatBinPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Python 解释器:", labelValueStyle, GUILayout.Width(120));
            GMATClient.CustomPythonPath = DrawDelayedTextField("settings_python_bin", GMATClient.CustomPythonPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("🔄 检查 GMAT 引擎状态", secondaryButtonStyle, GUILayout.Height(28))) {
                GMATClient.CheckConnection((ok, msg) => {
                    ScreenMessages.PostScreenMessage(ok ? "✅ GMAT 引擎就绪: " + msg : "❌ 引擎不可用: " + msg, 4.0f, ScreenMessageStyle.UPPER_CENTER);
                });
            }
            GUILayout.EndVertical();
        }
    }
}
