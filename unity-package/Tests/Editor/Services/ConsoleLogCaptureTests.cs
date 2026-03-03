using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tests.Services
{
    [TestFixture]
    public class ConsoleLogCaptureTests
    {
        private ConsoleLogCapture _capture;

        [SetUp]
        public void SetUp()
        {
            _capture = ConsoleLogCapture.Instance;
            _capture.Register();
        }

        [TearDown]
        public void TearDown()
        {
            _capture.Clear();
        }

        [Test]
        public void Instance_IsSingleton()
        {
            Assert.AreSame(ConsoleLogCapture.Instance, ConsoleLogCapture.Instance);
        }

        [Test]
        public void GetLogs_ReturnsListInstance()
        {
            var logs = _capture.GetLogs();
            Assert.IsNotNull(logs);
            Assert.IsInstanceOf<List<Dictionary<string, object>>>(logs);
        }

        [Test]
        public void GetLogs_RespectsCountParameter()
        {
            Debug.Log("test message 1");
            Debug.Log("test message 2");
            Debug.Log("test message 3");

            var logs = _capture.GetLogs(count: 2);
            Assert.LessOrEqual(logs.Count, 2);
        }

        [Test]
        public void GetLogs_EntriesHaveRequiredKeys()
        {
            Debug.Log("required keys test");

            var logs = _capture.GetLogs(count: 50);
            Assert.Greater(logs.Count, 0, "Should have at least one log entry");

            foreach (var entry in logs)
            {
                Assert.IsTrue(entry.ContainsKey("type"), "Entry should have 'type' key");
                Assert.IsTrue(entry.ContainsKey("message"), "Entry should have 'message' key");
                Assert.IsTrue(entry.ContainsKey("timestamp"), "Entry should have 'timestamp' key");
            }
        }

        [Test]
        public void GetLogs_TypeFilterErrors_OnlyReturnsErrors()
        {
            Debug.LogError("error msg");
            Debug.Log("normal msg");
            Debug.LogWarning("warning msg");

            var types = new List<string> { "error" };
            var logs = _capture.GetLogs(types: types, count: 50);

            foreach (var entry in logs)
            {
                Assert.AreEqual("error", entry["type"]);
            }
        }

        [Test]
        public void GetLogs_TypeFilterWarnings_OnlyReturnsWarnings()
        {
            Debug.LogWarning("warning msg");
            Debug.Log("normal msg");

            var types = new List<string> { "warning" };
            var logs = _capture.GetLogs(types: types, count: 50);

            foreach (var entry in logs)
            {
                Assert.AreEqual("warning", entry["type"]);
            }
        }

        [Test]
        public void GetLogs_DetailedFormat_IncludesStackTrace()
        {
            Debug.Log("stack trace test");

            var logs = _capture.GetLogs(count: 50, format: "detailed");
            Assert.Greater(logs.Count, 0);

            var hasStackTrace = false;
            foreach (var entry in logs)
            {
                if (entry.ContainsKey("stackTrace"))
                {
                    hasStackTrace = true;
                    break;
                }
            }
            Assert.IsTrue(hasStackTrace,
                "At least one entry should have stackTrace when format is 'detailed'");
        }

        [Test]
        public void GetLogs_SummaryFormat_NoStackByDefault()
        {
            Debug.Log("no stack test");

            var logs = _capture.GetLogs(count: 1, format: "summary", includeStackTrace: false);

            foreach (var entry in logs)
            {
                if (entry.ContainsKey("source") && (string)entry["source"] != "editor_console")
                {
                    Assert.IsFalse(entry.ContainsKey("stackTrace"),
                        "Fallback entries should not have stackTrace in summary format without includeStackTrace");
                }
            }
        }

        [Test]
        public void GetLogs_IncludeStackTrace_True_AddsStack()
        {
            Debug.Log("include stack test");

            var logs = _capture.GetLogs(count: 50, includeStackTrace: true);
            Assert.Greater(logs.Count, 0);

            var hasStackTrace = false;
            foreach (var entry in logs)
            {
                if (entry.ContainsKey("stackTrace"))
                {
                    hasStackTrace = true;
                    break;
                }
            }
            Assert.IsTrue(hasStackTrace,
                "At least one entry should have stackTrace when includeStackTrace is true");
        }

        [Test]
        public void Clear_RemovesCapturedLogs()
        {
            Debug.Log("before clear");
            _capture.Clear();

            Debug.Log("after clear marker");
            var logs = _capture.GetLogs(count: 100);

            var foundBeforeClear = false;
            foreach (var entry in logs)
            {
                var msg = entry["message"] as string;
                if (msg != null && msg.Contains("before clear") && !msg.Contains("after clear"))
                    foundBeforeClear = true;
            }

            Assert.IsFalse(foundBeforeClear,
                "Cleared logs should not appear (unless re-read from editor console)");
        }

        [Test]
        public void Register_CanBeCalledMultipleTimes()
        {
            _capture.Register();
            _capture.Register();
            Assert.IsNotNull(_capture);
        }

        [Test]
        public void Unregister_StopsCapturing()
        {
            _capture.Unregister();
            _capture.Clear();
            Debug.Log("after unregister");

            _capture.Register();
        }
    }
}
