#if UNITY_AI_TOOLS_TEST_RUNNER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP commands for running Unity tests (EditMode / PlayMode)
    /// and retrieving test results.
    /// Only available when com.unity.test-framework is installed.
    /// </summary>
    public class TestRunnerHandler : IToolHandler, ICallbacks
    {
        private static readonly Dictionary<string, TestJobState> Jobs = new Dictionary<string, TestJobState>();

        private class TestJobState
        {
            public string JobId;
            public string Status; // "running", "completed", "failed"
            public int TotalTests;
            public int PassedTests;
            public int FailedTests;
            public int SkippedTests;
            public List<Dictionary<string, object>> FailedDetails = new List<Dictionary<string, object>>();
            public DateTime StartedAt;
            public DateTime? CompletedAt;
        }

        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try { tcs.SetResult(Execute(commandName, paramsJson)); }
                catch (Exception ex) { tcs.SetResult(new CommandResult { success = false, error = ex.Message }); }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string commandName, string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            switch (commandName)
            {
                case "run_tests": return RunTests(p);
                case "get_test_job": return GetTestJob(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        private CommandResult RunTests(ToolParams p)
        {
            var mode = p.RequireString("mode");
            var testNames = p.GetStringList("test_names");
            var category = p.GetString("category");

            var testMode = mode == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode;
            var jobId = Guid.NewGuid().ToString("N").Substring(0, 12);

            var job = new TestJobState
            {
                JobId = jobId,
                Status = "running",
                StartedAt = DateTime.UtcNow,
            };
            Jobs[jobId] = job;

            try
            {
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(this);

                var filter = new Filter
                {
                    testMode = testMode,
                };

                if (testNames != null && testNames.Count > 0)
                    filter.testNames = testNames.ToArray();
                if (!string.IsNullOrEmpty(category))
                    filter.categoryNames = new[] { category };

                api.Execute(new ExecutionSettings(filter));

                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "job_id", jobId },
                        { "status", "running" },
                        { "mode", mode },
                    }
                };
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.CompletedAt = DateTime.UtcNow;
                return new CommandResult { success = false, error = $"Failed to start tests: {ex.Message}" };
            }
        }

        private CommandResult GetTestJob(ToolParams p)
        {
            var jobId = p.RequireString("job_id");

            if (!Jobs.TryGetValue(jobId, out var job))
                return new CommandResult { success = false, error = $"Test job '{jobId}' not found" };

            var data = new Dictionary<string, object>
            {
                { "job_id", job.JobId },
                { "status", job.Status },
                { "total_tests", job.TotalTests },
                { "passed", job.PassedTests },
                { "failed", job.FailedTests },
                { "skipped", job.SkippedTests },
                { "started_at", job.StartedAt.ToString("o") },
            };

            if (job.CompletedAt.HasValue)
                data["completed_at"] = job.CompletedAt.Value.ToString("o");
            if (job.FailedDetails.Count > 0)
                data["failed_details"] = job.FailedDetails;

            return new CommandResult { success = true, data = data };
        }

        public void RunStarted(ITestAdaptor testsToRun)
        {
            var runningJob = Jobs.Values.LastOrDefault(j => j.Status == "running");
            if (runningJob != null)
                runningJob.TotalTests = CountLeafTests(testsToRun);
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            var runningJob = Jobs.Values.LastOrDefault(j => j.Status == "running");
            if (runningJob != null)
            {
                runningJob.Status = "completed";
                runningJob.CompletedAt = DateTime.UtcNow;
            }
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result)
        {
            var runningJob = Jobs.Values.LastOrDefault(j => j.Status == "running");
            if (runningJob == null || result.HasChildren) return;

            switch (result.TestStatus)
            {
                case TestStatus.Passed:
                    runningJob.PassedTests++;
                    break;
                case TestStatus.Failed:
                    runningJob.FailedTests++;
                    runningJob.FailedDetails.Add(new Dictionary<string, object>
                    {
                        { "name", result.Test.Name },
                        { "full_name", result.FullName },
                        { "message", result.Message ?? "" },
                        { "stack_trace", result.StackTrace ?? "" },
                    });
                    break;
                case TestStatus.Skipped:
                case TestStatus.Inconclusive:
                    runningJob.SkippedTests++;
                    break;
            }
        }

        private static int CountLeafTests(ITestAdaptor test)
        {
            if (!test.HasChildren) return 1;
            return test.Children.Sum(c => CountLeafTests(c));
        }
    }
}
#endif
