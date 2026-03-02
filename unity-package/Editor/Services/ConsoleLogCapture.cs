using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Captures Unity console log messages for retrieval via read_console.
    /// Must be initialized once (typically from McpControlWindow.OnEnable).
    /// </summary>
    public class ConsoleLogCapture
    {
        private static ConsoleLogCapture _instance;
        public static ConsoleLogCapture Instance => _instance ??= new ConsoleLogCapture();

        private readonly List<LogEntry> _logs = new List<LogEntry>();
        private const int MaxEntries = 500;
        private bool _isRegistered;

        public void Register()
        {
            if (_isRegistered) return;
            Application.logMessageReceived += OnLogMessage;
            _isRegistered = true;
        }

        public void Unregister()
        {
            if (!_isRegistered) return;
            Application.logMessageReceived -= OnLogMessage;
            _isRegistered = false;
        }

        private void OnLogMessage(string condition, string stackTrace, LogType logType)
        {
            var entry = new LogEntry
            {
                message = condition,
                stackTrace = stackTrace,
                type = LogTypeToString(logType),
                timestamp = DateTime.UtcNow.ToString("o")
            };

            lock (_logs)
            {
                _logs.Add(entry);
                if (_logs.Count > MaxEntries)
                    _logs.RemoveAt(0);
            }
        }

        /// <summary>
        /// Get recent log entries filtered by type.
        /// </summary>
        public List<Dictionary<string, object>> GetLogs(
            List<string> types = null,
            int count = 10,
            bool includeStackTrace = false,
            string format = "summary")
        {
            var result = new List<Dictionary<string, object>>();

            lock (_logs)
            {
                // Iterate backwards for most recent first
                for (var i = _logs.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    var entry = _logs[i];
                    if (types != null && types.Count > 0 && !types.Contains(entry.type))
                        continue;

                    var dict = new Dictionary<string, object>
                    {
                        { "type", entry.type },
                        { "timestamp", entry.timestamp }
                    };

                    dict["message"] = entry.message;

                    if ((includeStackTrace || format == "detailed") && !string.IsNullOrEmpty(entry.stackTrace))
                    {
                        dict["stackTrace"] = entry.stackTrace;
                    }

                    result.Add(dict);
                }
            }

            return result;
        }

        public void Clear()
        {
            lock (_logs)
            {
                _logs.Clear();
            }
        }

        private static string LogTypeToString(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return "error";
                case LogType.Warning:
                    return "warning";
                default:
                    return "log";
            }
        }

        private class LogEntry
        {
            public string message;
            public string stackTrace;
            public string type;
            public string timestamp;
        }
    }
}
