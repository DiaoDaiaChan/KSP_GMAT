using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using KSPGMAT.Model;
using KSPGMAT.Service;

namespace KSPGMAT.UI {
    public partial class GMATMainWindow {
        private void TriggerPropagationSolve(string rawCustomScript = null) {
            Vessel vessel = FlightGlobals.ActiveVessel;
            activeTab = 2; // Switch to Mission Sequence (Tab 2)
            isPropagating = true;
            solveStartTime = Time.realtimeSinceStartup;
            solveProgress = 0.05f;
            solveStageText = I18n.T("solve_stage_serialize");

            List<StageData> selectedStages = GetSelectedStagesList();
            ROEngineInfo selectedEngine = GetDefaultOrActiveEngine();

            string cBody = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth";
            string scriptToSend = !string.IsNullOrEmpty(rawCustomScript) 
                ? rawCustomScript 
                : GMATScriptGenerator.GenerateScript(vessel, missionSequence, cBody, propagatorConfigs);

            GMATClient.PropagateMissionWithRawScript(vessel, selectedStages, selectedEngine, missionSequence, propagatorConfigs, scriptToSend, (success, result) => {
                isPropagating = false;
                solveProgress = success ? 1.0f : 0.0f;
                solveTotalTime = Time.realtimeSinceStartup - solveStartTime;
                latestResult = result;
                SyncSolvedValuesToSequence(result);
                if (success && result != null) {
                    syncStatusMessage = I18n.Format("solve_success_msg", solveTotalTime);
                    if (result.trajectory != null && result.trajectory.Count > 0) {
                        simulationTexture = GMATOrbitCanvas.RenderTrajectoryTexture(result.trajectory, cBody);
                    }
                    if (result.solved_burns != null && result.solved_burns.Count > 0) {
                        activeTab = 3; // Switch to Results tab
                    }
                } else {
                    string errInner = (result != null ? (result.error ?? I18n.T("solve_unknown_error")) : I18n.T("solve_comm_error"));
                    syncStatusMessage = I18n.Format("solve_not_converged", errInner);
                }
            });
        }

        private string GenerateLiveGmatScript(int mode = 0) {
            Vessel v = FlightGlobals.ActiveVessel;
            string cBody = GMATStateManager.Current.Spacecraft.CentralBody ?? "Earth";
            return GMATScriptGenerator.GenerateScript(v, missionSequence, cBody, propagatorConfigs);
        }

        private static readonly Regex GmatSyntaxRegex = new Regex(
            @"\b(BeginMissionSequence|DifferentialCorrector|ChemicalThruster|BeginFiniteBurn|CoordinateSystem|EndFiniteBurn|ChemicalTank|FiniteBurn|Propagator|ForceModel|Spacecraft|Propagate|EndTarget|Achieve|Report|Target|Create|Vary|Sat)\b",
            RegexOptions.Compiled);

        private string ColorizeGmatSyntax(string line) {
            if (string.IsNullOrEmpty(line)) return "";
            if (line.TrimStart().StartsWith("%")) {
                return $"<color=#64748b><i>{line}</i></color>";
            }
            return GmatSyntaxRegex.Replace(line, match => {
                string kw = match.Value;
                string col = (kw == "Create" || kw == "BeginMissionSequence" || kw == "Target" || kw == "EndTarget") ? "#f43f5e" :
                             (kw == "Sat" || kw == "Spacecraft") ? "#38bdf8" :
                             (kw == "ChemicalTank" || kw == "ChemicalThruster" || kw == "FiniteBurn" || kw == "BeginFiniteBurn" || kw == "EndFiniteBurn") ? "#facc15" :
                             (kw == "Propagate" || kw == "Propagator" || kw == "Vary" || kw == "Achieve") ? "#34d399" : "#a78bfa";
                return $"<color={col}><b>{kw}</b></color>";
            });
        }
    }
}
