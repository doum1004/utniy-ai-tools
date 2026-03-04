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
        private static bool _getEntryReturnsBool;
        private static MethodInfo _startGettingEntries;
        private static MethodInfo _endGettingEntries;
        private static MethodInfo _getFirstTwoLinesEntryTextAndModeInternal;
        private static Type _logEntryType;
        private static Type _entryRuntimeType;
        private static FieldInfo _messageField;
        private static FieldInfo _stackTraceField;
        private static FieldInfo _modeField;
        private static PropertyInfo _messageProperty;
        private static PropertyInfo _stackTraceProperty;
        private static PropertyInfo _modeProperty;

        private static void LazyInit()
        {
            if (_triedInit) return;
            _triedInit = true;

            try
            {
                var logEntriesType = FindTypeAcrossLoadedAssemblies("UnityEditorInternal.LogEntries", "UnityEditor.LogEntries");
                _logEntryType = FindTypeAcrossLoadedAssemblies("UnityEditorInternal.LogEntry", "UnityEditor.LogEntry");
                if (logEntriesType == null)
                    return;

                _getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _getEntryInternal = FindGetEntryMethod(logEntriesType);
                if (_getEntryInternal != null)
                    _getEntryReturnsBool = _getEntryInternal.ReturnType == typeof(bool);

                _startGettingEntries = logEntriesType.GetMethod(
                    "StartGettingEntries",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _endGettingEntries = logEntriesType.GetMethod(
                    "EndGettingEntries",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                _getFirstTwoLinesEntryTextAndModeInternal = logEntriesType.GetMethod(
                    "GetFirstTwoLinesEntryTextAndModeInternal",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (_getCount == null || (_getEntryInternal == null && _getFirstTwoLinesEntryTextAndModeInternal == null))
                    return;

                _entryRuntimeType = ResolveEntryRuntimeType(_getEntryInternal, _logEntryType);
                ResolveEntryMembers(_entryRuntimeType);
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
                var hasEntryApi = _getEntryInternal != null && _entryRuntimeType != null;
                var hasFallbackApi = _getFirstTwoLinesEntryTextAndModeInternal != null;
                return _getCount != null && (hasEntryApi || hasFallbackApi);
            }
        }

        public static Dictionary<string, object> GetDiagnostics()
        {
            LazyInit();
            return new Dictionary<string, object>
            {
                { "is_available", IsAvailable },
                { "has_get_count", _getCount != null },
                { "has_get_entry_internal", _getEntryInternal != null },
                { "entry_returns_bool", _getEntryReturnsBool },
                { "has_first_two_lines_api", _getFirstTwoLinesEntryTextAndModeInternal != null },
                { "entry_runtime_type", _entryRuntimeType != null ? _entryRuntimeType.FullName : string.Empty },
                { "has_message_member", _messageField != null || _messageProperty != null }
            };
        }

        /// <summary>
        /// Read recent entries from the Editor Console (includes compiler errors).
        /// Returns entries newest-first, filtered by type.
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

                if (_getEntryInternal != null && _entryRuntimeType != null)
                {
                    result = ReadViaGetEntryInternal(total, need, types, includeStackTrace);
                }

                if (result.Count == 0 && _getFirstTwoLinesEntryTextAndModeInternal != null)
                {
                    result = ReadViaFirstTwoLines(total, need, types);
                }
            }
            catch
            {
                // Return what we have
            }

            if (result.Count > count)
                result = result.GetRange(0, count);

            return result;
        }

        /// <summary>
        /// Primary path: uses GetEntryInternal to read full LogEntry objects.
        /// Handles both bool-returning (older Unity) and void-returning (newer Unity) signatures.
        /// Wraps reads in StartGettingEntries/EndGettingEntries when available.
        /// </summary>
        private static List<Dictionary<string, object>> ReadViaGetEntryInternal(
            int total, int need, List<string> types, bool includeStackTrace)
        {
            var result = new List<Dictionary<string, object>>();
            var needsEndGettingEntries = false;
            var hasTypeFilter = types != null && types.Count > 0;

            try
            {
                if (_startGettingEntries != null)
                {
                    _startGettingEntries.Invoke(null, null);
                    needsEndGettingEntries = true;
                }

                var logEntry = Activator.CreateInstance(_entryRuntimeType);
                var args = new object[] { 0, logEntry };

                for (var i = total - 1; i >= 0; i--)
                {
                    args[0] = i;
                    var ret = _getEntryInternal.Invoke(null, args);

                    if (_getEntryReturnsBool && ret as bool? != true)
                        continue;

                    logEntry = args[1];

                    var message = GetEntryMessage(logEntry);
                    if (string.IsNullOrEmpty(message)) continue;

                    var stackTrace = GetEntryStackTrace(logEntry);
                    var mode = GetEntryMode(logEntry);
                    var type = ClassifyType(mode, message, stackTrace);

                    if (hasTypeFilter && !types.Contains(type))
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

                    if (!hasTypeFilter && result.Count >= need)
                        break;
                    if (hasTypeFilter && result.Count >= need)
                        break;
                }
            }
            finally
            {
                if (needsEndGettingEntries && _endGettingEntries != null)
                    _endGettingEntries.Invoke(null, null);
            }

            return result;
        }

        /// <summary>
        /// Fallback path: uses GetFirstTwoLinesEntryTextAndModeInternal for older/newer
        /// Unity versions where GetEntryInternal is unavailable or broken.
        /// </summary>
        private static List<Dictionary<string, object>> ReadViaFirstTwoLines(
            int total, int need, List<string> types)
        {
            var result = new List<Dictionary<string, object>>();
            var hasTypeFilter = types != null && types.Count > 0;

            for (var i = total - 1; i >= 0; i--)
            {
                var rowArgs = new object[] { i, 0, string.Empty };
                _getFirstTwoLinesEntryTextAndModeInternal.Invoke(null, rowArgs);

                var mask = rowArgs[1] is int m ? m : 0;
                var message = rowArgs[2] as string;
                if (string.IsNullOrEmpty(message)) continue;

                var type = ClassifyType(mask, message, string.Empty);
                if (hasTypeFilter && !types.Contains(type))
                    continue;

                var dict = new Dictionary<string, object>
                {
                    { "type", type },
                    { "timestamp", DateTime.UtcNow.ToString("o") },
                    { "message", message },
                    { "source", "editor_console_fallback" }
                };
                result.Add(dict);

                if (!hasTypeFilter && result.Count >= need)
                    break;
                if (hasTypeFilter && result.Count >= need)
                    break;
            }

            return result;
        }

        private static string ClassifyType(int mode, string message, string stackTrace)
        {
            var type = GetLogTypeFromMode(mode, message);
            if (type != "log")
                return type;

            // Some Unity versions can report zero mode bits. Heuristics keep filtered
            // read_console calls from dropping obvious diagnostics.
            if (LooksLikeError(message) || LooksLikeError(stackTrace))
                return "error";
            if (LooksLikeWarning(message) || LooksLikeWarning(stackTrace))
                return "warning";

            return "log";
        }

        private static string GetLogTypeFromMode(int mode, string message)
        {
            const int errorBits = 1 | 2 | 4 | 32;
            if ((mode & errorBits) != 0)
                return "error";
            if ((mode & 8) != 0)
                return "warning";

            if (!string.IsNullOrEmpty(message) && message.IndexOf(": error CS", StringComparison.OrdinalIgnoreCase) >= 0)
                return "error";

            return "log";
        }

        private static bool LooksLikeError(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var lower = text.ToLowerInvariant();
            return lower.Contains(": error cs")
                || lower.Contains(" exception:")
                || lower.StartsWith("error:")
                || lower.Contains("compilation failed")
                || lower.Contains("failed with errors");
        }

        private static bool LooksLikeWarning(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            var lower = text.ToLowerInvariant();
            return lower.StartsWith("warning:")
                || lower.Contains(": warning cs");
        }

        private static Type FindTypeAcrossLoadedAssemblies(params string[] fullTypeNames)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                foreach (var fullTypeName in fullTypeNames)
                {
                    var t = asm.GetType(fullTypeName, false);
                    if (t != null)
                        return t;
                }
            }

            return null;
        }

        private static Type ResolveEntryRuntimeType(MethodInfo getEntryMethod, Type declaredLogEntryType)
        {
            if (declaredLogEntryType != null)
                return declaredLogEntryType;

            if (getEntryMethod == null)
                return null;

            var parameters = getEntryMethod.GetParameters();
            if (parameters.Length < 2)
                return null;

            var type = parameters[1].ParameterType;
            if (type.IsByRef)
                type = type.GetElementType();

            return type;
        }

        private static void ResolveEntryMembers(Type runtimeType)
        {
            if (runtimeType == null)
                return;

            _messageField = runtimeType.GetField("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? runtimeType.GetField("condition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _stackTraceField = runtimeType.GetField("stackTrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? runtimeType.GetField("stacktrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _modeField = runtimeType.GetField("mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            _messageProperty = runtimeType.GetProperty("message", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? runtimeType.GetProperty("condition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _stackTraceProperty = runtimeType.GetProperty("stackTrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                  ?? runtimeType.GetProperty("stacktrace", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _modeProperty = runtimeType.GetProperty("mode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private static string GetEntryMessage(object logEntry)
        {
            if (logEntry == null)
                return string.Empty;

            if (_messageField != null)
                return _messageField.GetValue(logEntry) as string ?? string.Empty;
            if (_messageProperty != null)
                return _messageProperty.GetValue(logEntry, null) as string ?? string.Empty;

            return string.Empty;
        }

        private static string GetEntryStackTrace(object logEntry)
        {
            if (logEntry == null)
                return string.Empty;

            if (_stackTraceField != null)
                return _stackTraceField.GetValue(logEntry) as string ?? string.Empty;
            if (_stackTraceProperty != null)
                return _stackTraceProperty.GetValue(logEntry, null) as string ?? string.Empty;

            return string.Empty;
        }

        private static int GetEntryMode(object logEntry)
        {
            if (logEntry == null)
                return 0;

            object raw = null;
            if (_modeField != null)
                raw = _modeField.GetValue(logEntry);
            else if (_modeProperty != null)
                raw = _modeProperty.GetValue(logEntry, null);

            if (raw == null)
                return 0;

            try
            {
                return Convert.ToInt32(raw);
            }
            catch
            {
                return 0;
            }
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
