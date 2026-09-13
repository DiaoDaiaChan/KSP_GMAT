using System;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KSPGMAT {
    /// <summary>
    /// Log severity levels.
    /// </summary>
    public enum LogLevel {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warn = 3,
        Error = 4,
        Fatal = 5
    }

    /// <summary>
    /// Contextual logger interface for module-scoped logging.
    /// </summary>
    public interface ILogger {
        string Tag { get; }
        void Trace(string message);
        void Debug(string message);
        void Info(string message);
        void Success(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, Exception ex);
        void Fatal(string message);
        void Fatal(string message, Exception ex);
        void Section(string title);
        ILogger ForContext(string subTag);
    }

    /// <summary>
    /// Unified logging system for KSPGMAT supporting Unity engine, standalone file, console, and screen output.
    /// Thread-safe for multi-threaded solvers (DE, MAB, MCTS, Archipelago).
    /// </summary>
    public static class KSPGMATLog {
        private const string DEFAULT_TAG = "KSPGMAT";

        private static readonly object _fileLock = new object();
        private static StreamWriter _fileWriter;
        private static string _resolvedLogFilePath;
        private static bool _initialized;

        private static LogLevel _currentLogLevel = LogLevel.Info;
        private static bool _fileLoggingEnabled = true;

        private static Action<string, float> _screenMessageDelegate;
        private static bool _screenMessageResolved;

        /// <summary>
        /// Active log level filter.
        /// </summary>
        public static LogLevel CurrentLogLevel {
            get => _currentLogLevel;
            set => _currentLogLevel = value;
        }

        /// <summary>
        /// Whether file logging is enabled.
        /// </summary>
        public static bool IsFileLoggingEnabled {
            get => _fileLoggingEnabled;
            set => _fileLoggingEnabled = value;
        }

        static KSPGMATLog() {
            Init();
        }

        /// <summary>
        /// Explicit initialization to rotate logs, emit session banner, and ensure readiness.
        /// </summary>
        public static void Init() {
            lock (_fileLock) {
                EnsureFileInitialized();
            }
        }

        public static void SetLogLevel(LogLevel level) {
            _currentLogLevel = level;
        }

        public static void SetFileLoggingEnabled(bool enabled) {
            _fileLoggingEnabled = enabled;
        }

        public static string GetLogFilePath() {
            lock (_fileLock) {
                EnsureFileInitialized();
                return _resolvedLogFilePath ?? string.Empty;
            }
        }

        public static void Flush() {
            lock (_fileLock) {
                try {
                    _fileWriter?.Flush();
                } catch { }
            }
        }

        #region Context Logger Factory

        public static ILogger ForContext(string tag) {
            return new ContextLogger(tag);
        }

        public static ILogger ForContext<T>() {
            return ForContext(typeof(T).Name);
        }

        private class ContextLogger : ILogger {
            private readonly string _tag;
            public string Tag => _tag;

            public ContextLogger(string tag) {
                _tag = string.IsNullOrEmpty(tag) ? DEFAULT_TAG : tag;
            }

            public void Trace(string message) => KSPGMATLog.Trace(_tag, message);
            public void Debug(string message) => KSPGMATLog.Debug(_tag, message);
            public void Info(string message) => KSPGMATLog.Info(_tag, message);
            public void Success(string message) => KSPGMATLog.Success(_tag, message);
            public void Warn(string message) => KSPGMATLog.Warn(_tag, message);
            public void Error(string message) => KSPGMATLog.Error(_tag, message);
            public void Error(string message, Exception ex) => KSPGMATLog.Error(_tag, message, ex);
            public void Fatal(string message) => KSPGMATLog.Fatal(_tag, message);
            public void Fatal(string message, Exception ex) => KSPGMATLog.Fatal(_tag, message, ex);
            public void Section(string title) => KSPGMATLog.Section(_tag, title);
            public ILogger ForContext(string subTag) => new ContextLogger(string.IsNullOrEmpty(_tag) ? subTag : $"{_tag}:{subTag}");
        }

        #endregion

        #region Public Logging API

        public static void Trace(string message) => Log(LogLevel.Trace, DEFAULT_TAG, message, null);
        public static void Trace(string tag, string message) => Log(LogLevel.Trace, tag, message, null);

        public static void Debug(string message) => Log(LogLevel.Debug, DEFAULT_TAG, message, null);
        public static void Debug(string tag, string message) => Log(LogLevel.Debug, tag, message, null);

        public static void Info(string message) => Log(LogLevel.Info, DEFAULT_TAG, message, null);
        public static void Info(string tag, string message) => Log(LogLevel.Info, tag, message, null);

        public static void Success(string message) => Log(LogLevel.Info, DEFAULT_TAG, message, null, isSuccess: true);
        public static void Success(string tag, string message) => Log(LogLevel.Info, tag, message, null, isSuccess: true);

        public static void Warn(string message) => Log(LogLevel.Warn, DEFAULT_TAG, message, null);
        public static void Warn(string tag, string message) => Log(LogLevel.Warn, tag, message, null);

        public static void Error(string message) => Log(LogLevel.Error, DEFAULT_TAG, message, null);
        public static void Error(string tag, string message) => Log(LogLevel.Error, tag, message, null);
        public static void Error(string message, Exception ex) => Log(LogLevel.Error, DEFAULT_TAG, message, ex);
        public static void Error(string tag, string message, Exception ex) => Log(LogLevel.Error, tag, message, ex);
        public static void Error(Exception ex) => Log(LogLevel.Error, DEFAULT_TAG, ex?.Message ?? "Exception", ex);

        public static void Fatal(string message) => Log(LogLevel.Fatal, DEFAULT_TAG, message, null);
        public static void Fatal(string tag, string message) => Log(LogLevel.Fatal, tag, message, null);
        public static void Fatal(string message, Exception ex) => Log(LogLevel.Fatal, DEFAULT_TAG, message, ex);
        public static void Fatal(string tag, string message, Exception ex) => Log(LogLevel.Fatal, tag, message, ex);
        public static void Fatal(Exception ex) => Log(LogLevel.Fatal, DEFAULT_TAG, ex?.Message ?? "Fatal Exception", ex);

        public static void Section(string title) => LogSection(DEFAULT_TAG, title);
        public static void Section(string tag, string title) => LogSection(tag, title);

        /// <summary>
        /// Post on-screen message in flight scene with safe fallback.
        /// </summary>
        public static void Screen(string message, float duration = 3f) {
            try {
                PostScreenMessageInternal(message, duration);
            } catch { }
            Info("Screen", message);
        }

        #endregion

        #region Core Pipeline Dispatch

        private static void LogSection(string tag, string title) {
            if (_currentLogLevel > LogLevel.Info) return;

            string effectiveTag = string.IsNullOrEmpty(tag) ? DEFAULT_TAG : tag;
            string banner = $"==================== {title} ====================";

            // 1. Unity
            LogToUnity(LogLevel.Info, effectiveTag, banner, null, false);

            // 2. Console
            try {
                Console.WriteLine($"[{DEFAULT_TAG}] {banner}");
            } catch { }

            // 3. File
            if (_fileLoggingEnabled) {
                string line = FormatFileLine(LogLevel.Info, effectiveTag, banner, null, false);
                WriteToFile(line, immediateFlush: false);
            }
        }

        private static void Log(LogLevel level, string tag, string message, Exception ex, bool isSuccess = false) {
            if (level < _currentLogLevel) return;

            string effectiveTag = string.IsNullOrEmpty(tag) ? DEFAULT_TAG : tag;
            string msg = message ?? string.Empty;

            // 1. Unity Engine Channel
            LogToUnity(level, effectiveTag, msg, ex, isSuccess);

            // 2. Console Channel
            LogToConsole(level, effectiveTag, msg, ex, isSuccess);

            // 3. File Channel
            if (_fileLoggingEnabled) {
                string fileLine = FormatFileLine(level, effectiveTag, msg, ex, isSuccess);
                WriteToFile(fileLine, immediateFlush: level >= LogLevel.Warn);
            }
        }

        private static bool? _unityDebugAvailable;

        private static void LogToUnity(LogLevel level, string tag, string msg, Exception ex, bool isSuccess) {
            if (_unityDebugAvailable.HasValue && !_unityDebugAvailable.Value) return;

            try {
                TryLogToUnityInternal(level, tag, msg, ex, isSuccess);
                _unityDebugAvailable = true;
            } catch {
                _unityDebugAvailable = false;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void TryLogToUnityInternal(LogLevel level, string tag, string msg, Exception ex, bool isSuccess) {
            string tagPrefix = (tag == DEFAULT_TAG) ? "" : $"[{tag}] ";
            string levelTag = isSuccess ? "[SUCCESS]" : $"[{level.ToString().ToUpperInvariant()}]";
            string fullMsg = (ex != null) ? $"[{DEFAULT_TAG}] {levelTag} {tagPrefix}{msg}\n{ex}" : $"[{DEFAULT_TAG}] {levelTag} {tagPrefix}{msg}";

            if (level >= LogLevel.Error) {
                UnityEngine.Debug.LogError(fullMsg);
            } else if (level == LogLevel.Warn) {
                UnityEngine.Debug.LogWarning(fullMsg);
            } else {
                UnityEngine.Debug.Log(fullMsg);
            }
        }

        private static void LogToConsole(LogLevel level, string tag, string msg, Exception ex, bool isSuccess) {
            try {
                string tagPrefix = (tag == DEFAULT_TAG) ? "" : $"[{tag}] ";
                string levelTag = isSuccess ? "[SUCCESS]" : $"[{level.ToString().ToUpperInvariant()}]";
                string fullMsg = (ex != null) ? $"[{DEFAULT_TAG}] {levelTag} {tagPrefix}{msg}\n{ex}" : $"[{DEFAULT_TAG}] {levelTag} {tagPrefix}{msg}";

                if (level >= LogLevel.Warn) {
                    Console.Error.WriteLine(fullMsg);
                } else {
                    Console.WriteLine(fullMsg);
                }
            } catch { }
        }

        private static string FormatFileLine(LogLevel level, string tag, string msg, Exception ex, bool isSuccess) {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpperInvariant().PadRight(5);
            string tid = Thread.CurrentThread.ManagedThreadId.ToString("D2");
            string body = isSuccess && !msg.StartsWith("[SUCCESS]", StringComparison.OrdinalIgnoreCase)
                ? $"[SUCCESS] {msg}"
                : msg;

            if (ex != null) {
                body += Environment.NewLine + ex.ToString();
            }

            return $"[{timestamp}] [{levelStr}] [TID:{tid}] [{tag}] {body}";
        }

        private static void WriteToFile(string line, bool immediateFlush) {
            lock (_fileLock) {
                EnsureFileInitialized();
                if (_fileWriter != null) {
                    try {
                        _fileWriter.WriteLine(line);
                        if (immediateFlush) {
                            _fileWriter.Flush();
                        }
                    } catch { }
                }
            }
        }

        #endregion

        #region File Initialization & Rotation

        private static void EnsureFileInitialized() {
            if (_initialized) return;
            _initialized = true;

            try {
                try {
                    _currentLogLevel = KSPGMATSettings.LogLevel;
                    _fileLoggingEnabled = KSPGMATSettings.LogFileEnabled;
                } catch { }

                _resolvedLogFilePath = ResolveLogFilePath();
                RotateLogFile(_resolvedLogFilePath);

                string dir = Path.GetDirectoryName(_resolvedLogFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                    Directory.CreateDirectory(dir);
                }

                var stream = new FileStream(_resolvedLogFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _fileWriter = new StreamWriter(stream, new UTF8Encoding(false)) {
                    AutoFlush = true
                };

                try {
                    AppDomain.CurrentDomain.ProcessExit += (s, e) => {
                        try { Flush(); } catch { }
                    };
                } catch { }

                WriteSessionBanner(_fileWriter, _resolvedLogFilePath);
            } catch (Exception ex) {
                try {
                    Console.Error.WriteLine($"[{DEFAULT_TAG}] [ERROR] Failed to initialize file logger: {ex.Message}");
                } catch { }
                _fileWriter = null;
            }
        }

        private static string ResolveLogFilePath() {
            // 1. Settings specified path
            try {
                string custom = KSPGMATSettings.LogFilePath;
                if (!string.IsNullOrEmpty(custom)) {
                    string dir = Path.GetDirectoryName(custom);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) {
                        Directory.CreateDirectory(dir);
                    }
                    return custom;
                }
            } catch { }

            // 2. KSP ApplicationRootPath
            try {
                string kspRoot = TryGetKspApplicationRoot();
                if (!string.IsNullOrEmpty(kspRoot)) {
                    string path = Path.Combine(kspRoot, "GameData", "KSPGMAT", "Logs", "KSPGMAT.log");
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return path;
                }
            } catch { }

            // 2.5 Current directory if running in repo workspace
            try {
                string cwd = Environment.CurrentDirectory;
                if (!string.IsNullOrEmpty(cwd) && (Directory.Exists(Path.Combine(cwd, "src", "KSPGMAT")) || Directory.Exists(Path.Combine(cwd, "GameData")) || Directory.Exists(Path.Combine(cwd, "Logs")))) {
                    string path = Path.Combine(cwd, "Logs", "KSPGMAT.log");
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return path;
                }
            } catch { }

            // 3. BaseDirectory
            try {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir)) {
                    string path = Path.Combine(baseDir, "Logs", "KSPGMAT.log");
                    string dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    return path;
                }
            } catch { }

            // 4. Temp directory fallback
            try {
                string tempDir = Path.Combine(Path.GetTempPath(), "KSPGMAT", "Logs");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                return Path.Combine(tempDir, "KSPGMAT.log");
            } catch { }

            return "KSPGMAT.log";
        }

        private static string TryGetKspApplicationRoot() {
            try {
                Type kspUtilType = Type.GetType("KSPUtil, Assembly-CSharp");
                if (kspUtilType == null) {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        kspUtilType = asm.GetType("KSPUtil");
                        if (kspUtilType != null) break;
                    }
                }
                if (kspUtilType != null) {
                    var prop = kspUtilType.GetProperty("ApplicationRootPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (prop != null) {
                        return prop.GetValue(null, null) as string;
                    }
                }
            } catch { }
            return null;
        }

        private static void RotateLogFile(string logFilePath) {
            try {
                if (File.Exists(logFilePath)) {
                    string dir = Path.GetDirectoryName(logFilePath);
                    string prevFile = Path.Combine(dir, "KSPGMAT.previous.log");
                    if (File.Exists(prevFile)) {
                        File.Delete(prevFile);
                    }
                    File.Move(logFilePath, prevFile);
                }
            } catch { }
        }

        private static void WriteSessionBanner(StreamWriter writer, string logFilePath) {
            try {
                var nowLocal = DateTime.Now;
                var nowUtc = DateTime.UtcNow;
                string os = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)");
                int pid = 0;
                string procName = "Unknown";
                try {
                    var proc = System.Diagnostics.Process.GetCurrentProcess();
                    pid = proc.Id;
                    procName = proc.ProcessName;
                } catch { }

                string clr = Environment.Version.ToString() + " (.NET Framework / CLR)";

                writer.WriteLine(new string('=', 80));
                writer.WriteLine($"[{DEFAULT_TAG}] Logging Session Initialized");
                writer.WriteLine($"Session Start (Local): {nowLocal:yyyy-MM-dd HH:mm:ss.fff}");
                writer.WriteLine($"Session Start (UTC):   {nowUtc:yyyy-MM-dd HH:mm:ss.fff}Z");
                writer.WriteLine($"Operating System:      {os}");
                writer.WriteLine($"Process ID:            {pid} ({procName})");
                writer.WriteLine($"Runtime Environment:   {clr}");
                writer.WriteLine($"Log File Path:         {logFilePath}");
                writer.WriteLine($"Active Log Level:      {_currentLogLevel}");
                writer.WriteLine(new string('=', 80));
                writer.Flush();
            } catch { }
        }

        #endregion

        #region Screen Notification Internal

        private static void PostScreenMessageInternal(string message, float duration) {
            try {
                if (!_screenMessageResolved) {
                    _screenMessageResolved = true;
                    Type smType = null;
                    Type styleType = null;

                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                        if (smType == null) smType = asm.GetType("ScreenMessages");
                        if (styleType == null) styleType = asm.GetType("ScreenMessageStyle");
                        if (smType != null && styleType != null) break;
                    }

                    if (smType != null && styleType != null) {
                        var method = smType.GetMethod("PostScreenMessage", new[] { typeof(string), typeof(float), styleType });
                        if (method != null) {
                            object upperCenter = Enum.Parse(styleType, "UPPER_CENTER");
                            _screenMessageDelegate = (msg, dur) => {
                                try {
                                    method.Invoke(null, new object[] { msg, dur, upperCenter });
                                } catch { }
                            };
                        }
                    }
                }

                _screenMessageDelegate?.Invoke(message, duration);
            } catch { }
        }

        #endregion
    }
}
