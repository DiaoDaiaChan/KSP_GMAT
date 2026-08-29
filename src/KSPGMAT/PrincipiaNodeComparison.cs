using System;
using System.Collections.Generic;
using UnityEngine;

namespace KSPGMAT {
    public class NodeComparison {
        public int gmatCount;
        public int principiaCount;
        public int matchedCount;
        public int unmatchedGmat;
        public int unmatchedPrincipia;
        public double maxTimeError;
        public double maxDeltaVMagnitudeError;
        public string message;
        public List<string> details = new List<string>();
    }

    public static class PrincipiaNodeComparator {
        public static NodeComparison Compare(List<SolvedBurn> gmat, List<PrincipiaInterop.FlightPlanNode> principia, double principiaTimeAtSolve, double timeTolerance, double deltaVTolerance) {
            NodeComparison result = new NodeComparison();
            result.gmatCount = gmat != null ? gmat.Count : 0;
            result.principiaCount = principia != null ? principia.Count : 0;
            bool[] used = new bool[result.principiaCount];
            for (int i = 0; i < result.gmatCount; i++) {
                SolvedBurn expected = gmat[i]; int best = -1; double bestTime = double.MaxValue;
                for (int j = 0; j < result.principiaCount; j++) {
                    if (used[j] || !principia[j].hasTime) continue;
                    double error = Math.Abs(principia[j].initialTime - (principiaTimeAtSolve + expected.epoch_offset));
                    if (error < bestTime) { bestTime = error; best = j; }
                }
                if (best < 0 || bestTime > timeTolerance) { result.unmatchedGmat++; result.details.Add("GMAT 节点 #" + (i + 1) + " 未找到时间匹配"); continue; }
                used[best] = true; result.matchedCount++; result.maxTimeError = Math.Max(result.maxTimeError, bestTime);
                if (principia[best].hasDeltaV) {
                    double expectedMagnitude = expected.magnitude;
                    double actualMagnitude = principia[best].deltaV.magnitude;
                    result.maxDeltaVMagnitudeError = Math.Max(result.maxDeltaVMagnitudeError, Math.Abs(actualMagnitude - expectedMagnitude));
                    if (Math.Abs(actualMagnitude - expectedMagnitude) > deltaVTolerance) result.details.Add("节点 #" + (i + 1) + " 的 ΔV 幅值超出容差");
                } else {
                    result.details.Add("Principia 节点 #" + (best + 1) + " 未提供 delta_v，跳过向量检查");
                }
            }
            for (int i = 0; i < used.Length; i++) if (!used[i]) result.unmatchedPrincipia++;
            result.message = result.matchedCount + "/" + result.gmatCount + " 个 GMAT 节点已匹配";
            return result;
        }
    }
}
