using System;
using UnityEngine;

namespace KSPGMAT {
    public static class KSPGMATLog {
        private const string TAG = "[KSPGMAT]";

        public static void Info(string message) {
            Debug.Log($"{TAG} [INFO] {message}");
        }

        public static void Success(string message) {
            Debug.Log($"{TAG} [SUCCESS] {message}");
        }

        public static void Warn(string message) {
            Debug.LogWarning($"{TAG} [WARN] {message}");
        }

        public static void Error(string message) {
            Debug.LogError($"{TAG} [ERROR] {message}");
        }

        public static void Section(string title) {
            Debug.Log($"{TAG} ==================== {title} ====================");
        }
    }
}
