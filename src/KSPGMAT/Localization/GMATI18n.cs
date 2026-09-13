using System;

namespace KSPGMAT {
    /// <summary>
    /// Forwarding wrapper for I18n.
    /// </summary>
    public static class GMATI18n {
        public static string T(string key, string fallback = null) => I18n.T(key, fallback);
        public static string Format(string key, params object[] args) => I18n.Format(key, args);
        public static string TF(string key, params object[] args) => I18n.TF(key, args);
    }
}
