using System;
using UnityEngine;

namespace KSPGMAT {
    public static class KSPGMATLog {
        private const string TAG = "[KSPGMAT]";

        public static void Info(string message) {
            try { Debug.Log($"{TAG} [INFO] {message}"); } catch { Console.WriteLine($"{TAG} [INFO] {message}"); }
        }

        public static void Success(string message) {
            try { Debug.Log($"{TAG} [SUCCESS] {message}"); } catch { Console.WriteLine($"{TAG} [SUCCESS] {message}"); }
        }

        public static void Warn(string message) {
            try { Debug.LogWarning($"{TAG} [WARN] {message}"); } catch { Console.WriteLine($"{TAG} [WARN] {message}"); }
        }

        public static void Error(string message) {
            try { Debug.LogError($"{TAG} [ERROR] {message}"); } catch { Console.Error.WriteLine($"{TAG} [ERROR] {message}"); }
        }

        public static void Section(string title) {
            try { Debug.Log($"{TAG} ==================== {title} ===================="); } catch { Console.WriteLine($"{TAG} ==================== {title} ===================="); }
        }
    }
}
