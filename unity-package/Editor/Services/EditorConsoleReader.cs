using System;
using System.Collections.Generic;
using System.Reflection;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Reads the Unity Editor Console (including compiler errors) via reflection.
    /// Application.logMessageReceived only receives runtime logs; compiler errors
    /// are only visible in the Editor Console, so we read LogEntries when available.
    /// </summary>
    public static class EditorConsoleReader
    {
        private static bool _triedInit;
        private static MethodInfo _getCount;
        private static MethodInfo _getEntryInternal;
        private static MethodInfo _getFirstTwoLinesEntryTextAndModeInternal;
        private static Type _logEntryType;
        private static FieldInfo _messageField;
        private static FieldInfo _stackTraceField;
        private static FieldInfo _modeField;

        private static void LazyInit()
        {
            if (_triedInit) return;
            _triedInit = true;

            try
            {
                var editorAssembly = typeof(UnityEditor.AssetDatabase).Assembly;
                var logEntriesType = editorAssembly.GetType("UnityEditorInternal.LogEntries");
                _logEntryType = editorAssembly.GetType("UnityEditorInternal.LogEntry");
                if (logEntriesType == null || _logEntryType == null)
                    return;

                _getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _getEntryInternal = FindGetEntryMethod(logEntriesType);
                _getFirstTwoLinesEntryTextAndModeInternal = logEntriesType.GetMethod(
                    "GetFirstTwoLinesEntryTextAndModeInternal",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (_getCount == null || (_getEntryInternal == null && _getFirstTwoLinesEntryTextAndModeInternal == null))
                    return;

                _messageField = _logEntryType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? _logEntryType.GetField("condition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _stackTraceField = _logEntryType.GetField("stackTrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                  ?? _logEntryType.GetField("stacktrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                _modeField = _logEntryType.GetField("mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (_messageField == null)
                    return;
            }
            catch
            {
                // Reflection failed; GetLogsFromEditorConsole will return empty
            }
        }

        /// <summary>
        /// Returns true if the Editor Console can be read (LogEntries API available).
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                LazyInit();
                var hasEntryApi = _getEntryInternal != null && _messageField != null;
                var hasFallbackApi = _getFirstTwoLinesEntryTextAndModeInternal != null;
                return _getCount != null && (hasEntryApi || hasFallbackApi);
            }
        }

        /// <summary>
        /// Read recent entries from the Editor Console (includes compiler errors).
        /// Returns entries with type "error", "warning", or "log" and message/stackTrace.
        /// Most recent last in list (so reverse for newest-first).
        /// </summary>
        public static List<Dictionary<string, object>> GetLogsFromEditorConsole(
            List<string> types = null,
            int count = 10,
            bool includeStackTrace = true)
        {
            var result = new List<Dictionary<string, object>>();
            LazyInit();
            if (_getCount == null)
                return result;

            try
            {
                var total = (int)_getCount.Invoke(null, null);
                if (total <= 0) return result;

                var need = count;
                if (types != null && types.Count > 0)
                    need = Math.Min(total, count * 4);

                // First attempt: full entry API
                if (_getEntryInternal != null && _logEntryType != null && _messageField != null)
                {
                    var logEntry = Activator.CreateInstance(_logEntryType);
                    var args = new object[] { 0, logEntry };
                    for (var i = total - 1; i >= 0 && result.Count < need; i--)
                    {
                        args[0] = i;
                        var ok = _getEntryInternal.Invoke(null, args);
                        if (ok as bool? != true) continue;
                        logEntry = args[1];

                        var message = _messageField.GetValue(logEntry) as string;
                        if (string.IsNullOrEmpty(message)) continue;

                        var stackTrace = (_stackTraceField?.GetValue(logEntry) as string) ?? string.Empty;
                        var mode = _modeField != null ? (int)(_modeField.GetValue(logEntry) ?? 0) : 0;
                        var type = GetLogTypeFromMode(mode, message);

                        if (types != null && types.Count > 0 && !types.Contains(type))
                            continue;

                        var dict = new Dictionary<string, object>
                        {
                            { "type", type },
                            { "timestamp", DateTime.UtcNow.ToString("o") },
                            { "message", message },
                            { "source", "editor_console" }
                        };
                        if (includeStackTrace && !string.IsNullOrEmpty(stackTrace))
                            dict["stackTrace"] = stackTrace;
                        result.Add(dict);
                    }
                }

                // Fallback: older/newer Unity versions where LogEntry fields differ.
                if (result.Count == 0 && _getFirstTwoLinesEntryTextAndModeInternal != null)
                {
                    for (var i = total - 1; i >= 0 && result.Count < need; i--)
                    {
                        var rowArgs = new object[] { i, 0, string.Empty };
                        _getFirstTwoLinesEntryTextAndModeInternal.Invoke(null, rowArgs);

                        var mask = rowArgs[1] is int m ? m : 0;
                        var message = rowArgs[2] as string;
                        if (string.IsNullOrEmpty(message)) continue;

                        var type = GetLogTypeFromMode(mask, message);
                        if (types != null && types.Count > 0 && !types.Contains(type))
                            continue;

                        var dict = new Dictionary<string, object>
                        {
                            { "type", type },
                            { "timestamp", DateTime.UtcNow.ToString("o") },
                            { "message", message },
                            { "source", "editor_console_fallback" }
                        };
                        result.Add(dict);
                    }
                }
            }
            catch
            {
                // Ignore; return what we have
            }

            return result;
        }

        private static string GetLogTypeFromMode(int mode, string message)
        {
            // Unity ConsoleWindow.Mode: Error=1, Assert=2, ScriptCompileError=32, ScriptingError=4, etc.
            const int errorBits = 1 | 2 | 4 | 32;
            if ((mode & errorBits) != 0)
                return "error";
            if ((mode & 8) != 0)
                return "warning";

            // Fallback for compiler diagnostics when mode flags vary between Unity versions.
            if (!string.IsNullOrEmpty(message) && message.IndexOf(": error CS", StringComparison.OrdinalIgnoreCase) >= 0)
                return "error";

            return "log";
        }

        private static MethodInfo FindGetEntryMethod(Type logEntriesType)
        {
            var methods = logEntriesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name != "GetEntryInternal")
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 2)
                    return method;
            }

            return null;
        }
    }
}
