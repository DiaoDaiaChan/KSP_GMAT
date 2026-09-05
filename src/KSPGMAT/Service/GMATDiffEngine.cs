using System;
using System.Collections.Generic;
using System.Text;

namespace KSPGMAT.Service {
    public enum GMATDiffType {
        Equal,
        Insert,
        Delete,
        Modify
    }

    public class GMATDiffLine {
        public GMATDiffType Type { get; set; }
        public int LineNumberA { get; set; } // Baseline line (1-based, 0 if insert)
        public string ContentA { get; set; }
        public int LineNumberB { get; set; } // User line (1-based, 0 if delete)
        public string ContentB { get; set; }
    }

    public class GMATDiffResult {
        public List<GMATDiffLine> Lines { get; } = new List<GMATDiffLine>();
        public int InsertCount { get; set; }
        public int DeleteCount { get; set; }
        public int ModifyCount { get; set; }
        public bool HasDifferences => (InsertCount + DeleteCount + ModifyCount) > 0;
    }

    /// <summary>
    /// 高性能轻量级逐行 Diff 对比引擎 (LCS / Myers 算法变体)。
    /// 专门用于比较 GMAT 飞船基准干净脚本与用户自由编辑脚本之间的差异。
    /// </summary>
    public static class GMATDiffEngine {
        public static GMATDiffResult ComputeDiff(string textA, string textB) {
            var result = new GMATDiffResult();
            string[] linesA = (textA ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            string[] linesB = (textB ?? "").Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int n = linesA.Length;
            int m = linesB.Length;

            // Compute Longest Common Subsequence (LCS) matrix
            int[,] lcs = new int[n + 1, m + 1];
            for (int i = 0; i < n; i++) {
                for (int j = 0; j < m; j++) {
                    if (linesA[i].Equals(linesB[j], StringComparison.Ordinal)) {
                        lcs[i + 1, j + 1] = lcs[i, j] + 1;
                    } else {
                        lcs[i + 1, j + 1] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                    }
                }
            }

            // Backtrack to extract diff sequence
            var rawDiff = new List<GMATDiffLine>();
            int currA = n;
            int currB = m;

            while (currA > 0 || currB > 0) {
                if (currA > 0 && currB > 0 && linesA[currA - 1].Equals(linesB[currB - 1], StringComparison.Ordinal)) {
                    rawDiff.Add(new GMATDiffLine {
                        Type = GMATDiffType.Equal,
                        LineNumberA = currA,
                        ContentA = linesA[currA - 1],
                        LineNumberB = currB,
                        ContentB = linesB[currB - 1]
                    });
                    currA--;
                    currB--;
                } else if (currB > 0 && (currA == 0 || lcs[currA, currB - 1] >= lcs[currA - 1, currB])) {
                    rawDiff.Add(new GMATDiffLine {
                        Type = GMATDiffType.Insert,
                        LineNumberA = 0,
                        ContentA = "",
                        LineNumberB = currB,
                        ContentB = linesB[currB - 1]
                    });
                    currB--;
                } else if (currA > 0 && (currB == 0 || lcs[currA, currB - 1] < lcs[currA - 1, currB])) {
                    rawDiff.Add(new GMATDiffLine {
                        Type = GMATDiffType.Delete,
                        LineNumberA = currA,
                        ContentA = linesA[currA - 1],
                        LineNumberB = 0,
                        ContentB = ""
                    });
                    currA--;
                }
            }

            rawDiff.Reverse();

            // Post-process consecutive Delete + Insert into Modify for improved UX readability
            for (int k = 0; k < rawDiff.Count; k++) {
                if (k + 1 < rawDiff.Count && rawDiff[k].Type == GMATDiffType.Delete && rawDiff[k + 1].Type == GMATDiffType.Insert) {
                    result.Lines.Add(new GMATDiffLine {
                        Type = GMATDiffType.Modify,
                        LineNumberA = rawDiff[k].LineNumberA,
                        ContentA = rawDiff[k].ContentA,
                        LineNumberB = rawDiff[k + 1].LineNumberB,
                        ContentB = rawDiff[k + 1].ContentB
                    });
                    result.ModifyCount++;
                    k++; // Skip the paired insert
                } else {
                    var line = rawDiff[k];
                    result.Lines.Add(line);
                    if (line.Type == GMATDiffType.Insert) result.InsertCount++;
                    else if (line.Type == GMATDiffType.Delete) result.DeleteCount++;
                }
            }

            return result;
        }
    }
}
