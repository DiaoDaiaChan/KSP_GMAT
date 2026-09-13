using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using KSPGMAT;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void DrawTabSettings() {
            // Language & Localization Selection Bar (languages auto-registered from loc/*.json)
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label(I18n.T("settings_locale_header"), headerStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_ui_lang"), labelValueStyle, GUILayout.Width(160));
            foreach (var code in I18n.AvailableLanguages) {
                bool active = string.Equals(I18n.CurrentCode, code, StringComparison.OrdinalIgnoreCase);
                string label = (active ? "● " : "") + I18n.GetNativeName(code);
                if (GUILayout.Button(label, active ? tabButtonActiveStyle : secondaryButtonStyle, GUILayout.Width(140), GUILayout.Height(24))) {
                    I18n.SetLangCode(code);
                }
            }
            if (GUILayout.Button(I18n.T("lang_auto"), secondaryButtonStyle, GUILayout.Width(150), GUILayout.Height(24))) {
                I18n.AutoDetect();
                ScreenMessages.PostScreenMessage(I18n.Format("lang_detected", I18n.GetNativeName(I18n.CurrentCode)), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);

            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.Label(I18n.T("settings_gmat_path"), headerStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_bin_path"), labelValueStyle, GUILayout.Width(120));
            GMATClient.GmatBinPath = DrawDelayedTextField("settings_gmat_bin", GMATClient.GmatBinPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_py_path"), labelValueStyle, GUILayout.Width(140));
            GMATClient.CustomPythonPath = DrawDelayedTextField("settings_python_bin", GMATClient.CustomPythonPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_spice_path"), labelValueStyle, GUILayout.Width(140));
            string curBsp = KSPGMATSettings.SpiceBspPath;
            string newBsp = DrawDelayedTextField("settings_spice_bsp", curBsp, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            if (newBsp != curBsp) {
                KSPGMATSettings.SpiceBspPath = newBsp;
            }
            GUILayout.EndHorizontal();

            if (GMATClient.HasFullSpiceEphemeris) {
                GUILayout.Label(I18n.Format("settings_spice_loaded", GMATClient.DetectedSpiceKernelName, GMATClient.DetectedSpiceKernelPath), labelValueStyle);
            } else {
                GUILayout.Label(I18n.T("settings_spice_missing"), labelValueStyle);
            }

            GUILayout.Space(6);
            if (GUILayout.Button(I18n.T("settings_check_engine"), secondaryButtonStyle, GUILayout.Height(28))) {
                GMATClient.CheckConnection((ok, msg) => {
                    ScreenMessages.PostScreenMessage(I18n.Format(ok ? "settings_engine_ok" : "settings_engine_fail", msg), 4.0f, ScreenMessageStyle.UPPER_CENTER);
                });
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.BeginVertical(cardBoxStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_log_header", "系统运行日志与诊断 (System Logs & Diagnostics)"), headerStyle);
            GUILayout.FlexibleSpace();
            string logBadgeColor = KSPGMATLog.IsFileLoggingEnabled ? "#34d399" : "#f59e0b";
            GUILayout.Label($"<color={logBadgeColor}><b>[● {KSPGMATLog.CurrentLogLevel} | {(KSPGMATLog.IsFileLoggingEnabled ? "Active" : "Disabled")}]</b></color>", labelValueStyle);
            GUILayout.EndHorizontal();

            string currentLogPath = KSPGMATLog.GetLogFilePath();
            GUILayout.BeginHorizontal();
            GUILayout.Label(I18n.T("settings_log_path", "当前日志文件:"), labelValueStyle, GUILayout.Width(110));
            GUILayout.TextField(currentLogPath, stepLabelInputStyle ?? GUI.skin.textField, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(I18n.T("settings_btn_open_log_folder", "📂 打开日志所在目录 (Open in Explorer)"), secondaryButtonStyle, GUILayout.Height(26))) {
                OpenLogFileOrFolder();
            }
            if (GUILayout.Button(I18n.T("settings_btn_flush_log", "💾 立即刷新写入缓冲区 (Flush)"), secondaryButtonStyle, GUILayout.Width(220), GUILayout.Height(26))) {
                KSPGMATLog.Flush();
                ScreenMessages.PostScreenMessage(I18n.T("log_flushed_msg", "日志缓冲区已立即刷新写入磁盘"), 3.0f, ScreenMessageStyle.UPPER_CENTER);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
