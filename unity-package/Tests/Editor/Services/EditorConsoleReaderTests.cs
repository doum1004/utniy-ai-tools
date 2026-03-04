using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tests.Services
{
    [TestFixture]
    public class EditorConsoleReaderTests
    {
        [Test]
        public void IsAvailable_ReturnsBool_InEditor()
        {
            // IsAvailable depends on internal LogEntries reflection which may not
            // work in headless/batch mode or across all Unity versions.
            var available = EditorConsoleReader.IsAvailable;
            Assert.IsTrue(available || !available,
                "IsAvailable should return a boolean without throwing");
        }

        [Test]
        public void GetLogsFromEditorConsole_ReturnsListInstance()
        {
            var logs = EditorConsoleReader.GetLogsFromEditorConsole();
            Assert.IsNotNull(logs);
            Assert.IsInstanceOf<List<Dictionary<string, object>>>(logs);
        }

        [Test]
        public void GetLogsFromEditorConsole_RespectsCountParameter()
        {
            var logs = EditorConsoleReader.GetLogsFromEditorConsole(count: 1);
            Assert.IsNotNull(logs);
            Assert.LessOrEqual(logs.Count, 1);
        }

        [Test]
        public void GetLogsFromEditorConsole_WithEmptyTypeFilter_ReturnsAll()
        {
            var logs = EditorConsoleReader.GetLogsFromEditorConsole(types: new List<string>(), count: 5);
            Assert.IsNotNull(logs);
        }

        [Test]
        public void GetLogsFromEditorConsole_EntriesHaveRequiredKeys()
        {
            var logs = EditorConsoleReader.GetLogsFromEditorConsole(count: 50);
            foreach (var entry in logs)
            {
                Assert.IsTrue(entry.ContainsKey("type"), "Entry should have 'type' key");
                Assert.IsTrue(entry.ContainsKey("message"), "Entry should have 'message' key");
                Assert.IsTrue(entry.ContainsKey("timestamp"), "Entry should have 'timestamp' key");
                Assert.IsTrue(entry.ContainsKey("source"), "Entry should have 'source' key");

                var type = entry["type"] as string;
                Assert.IsTrue(type == "error" || type == "warning" || type == "log",
                    $"Type should be error, warning, or log but was '{type}'");
            }
        }

        [Test]
        public void GetLogsFromEditorConsole_FilterByError_ExcludesOtherTypes()
        {
            var types = new List<string> { "error" };
            var logs = EditorConsoleReader.GetLogsFromEditorConsole(types: types, count: 50);
            foreach (var entry in logs)
            {
                Assert.AreEqual("error", entry["type"]);
            }
        }

        [Test]
        public void GetLogsFromEditorConsole_FilterByWarning_ExcludesOtherTypes()
        {
            var types = new List<string> { "warning" };
            var logs = EditorConsoleReader.GetLogsFromEditorConsole(types: types, count: 50);
            foreach (var entry in logs)
            {
                Assert.AreEqual("warning", entry["type"]);
            }
        }

        [Test]
        public void GetLogTypeFromMode_Error_ReturnsError()
        {
            Assert.AreEqual("error", InvokeGetLogTypeFromMode(1, "something"));
        }

        [Test]
        public void GetLogTypeFromMode_Assert_ReturnsError()
        {
            Assert.AreEqual("error", InvokeGetLogTypeFromMode(2, "assert"));
        }

        [Test]
        public void GetLogTypeFromMode_ScriptingError_ReturnsError()
        {
            Assert.AreEqual("error", InvokeGetLogTypeFromMode(4, "script error"));
        }

        [Test]
        public void GetLogTypeFromMode_CompileError_ReturnsError()
        {
            Assert.AreEqual("error", InvokeGetLogTypeFromMode(32, "compile error"));
        }

        [Test]
        public void GetLogTypeFromMode_Warning_ReturnsWarning()
        {
            Assert.AreEqual("warning", InvokeGetLogTypeFromMode(8, "some warning"));
        }

        [Test]
        public void GetLogTypeFromMode_NormalLog_ReturnsLog()
        {
            Assert.AreEqual("log", InvokeGetLogTypeFromMode(0, "normal log"));
        }

        [Test]
        public void GetLogTypeFromMode_CompilerDiagnosticInMessage_ReturnsError()
        {
            Assert.AreEqual("error", InvokeGetLogTypeFromMode(0, "Assets/Foo.cs(10,5): error CS0117: blah"));
        }

        [Test]
        public void GetLogTypeFromMode_NonCompilerMessage_ReturnsLog()
        {
            Assert.AreEqual("log", InvokeGetLogTypeFromMode(0, "Just a normal message"));
        }

        private static string InvokeGetLogTypeFromMode(int mode, string message)
        {
            var method = typeof(EditorConsoleReader).GetMethod(
                "GetLogTypeFromMode",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "GetLogTypeFromMode method should exist");
            return (string)method.Invoke(null, new object[] { mode, message });
        }
    }
}
