using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles batch execution of multiple commands in a single round-trip.
    /// </summary>
    public class BatchHandler : IToolHandler
    {
        private readonly CommandDispatcher _dispatcher;

        public BatchHandler(CommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var failFast = p.GetBool("fail_fast") ?? p.GetBool("failFast") ?? false;

            // Extract commands array from raw JSON
            var commandsRaw = p.GetRaw("commands") as List<object>;
            if (commandsRaw == null || commandsRaw.Count == 0)
                return new CommandResult { success = false, error = "No commands provided" };

            if (commandsRaw.Count > 25)
                return new CommandResult { success = false, error = "Maximum 25 commands per batch" };

            var results = new List<Dictionary<string, object>>();
            var allSuccess = true;

            foreach (var cmdObj in commandsRaw)
            {
                if (cmdObj is not Dictionary<string, object> cmdDict)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", "Invalid command format" }
                    });
                    allSuccess = false;
                    if (failFast) break;
                    continue;
                }

                var tool = cmdDict.TryGetValue("tool", out var t) ? t?.ToString() : null;
                var cmdParams = cmdDict.TryGetValue("params", out var cp) ? cp as Dictionary<string, object> : null;

                if (string.IsNullOrEmpty(tool))
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "success", false },
                        { "error", "Missing 'tool' field" }
                    });
                    allSuccess = false;
                    if (failFast) break;
                    continue;
                }

                // Serialize params back to JSON for the dispatcher
                var cmdParamsJson = cmdParams != null ? MiniJson.Serialize(cmdParams) : "{}";

                // Construct a fake raw JSON for the dispatcher
                var fakeJson = $"{{\"type\":\"execute_command\",\"id\":\"batch\",\"name\":\"{tool}\",\"params\":{cmdParamsJson},\"timeout\":30}}";

                try
                {
                    var result = await _dispatcher.DispatchAsync(fakeJson);
                    var entry = new Dictionary<string, object>
                    {
                        { "tool", tool },
                        { "success", result.success }
                    };

                    if (result.data != null) entry["data"] = result.data;
                    if (!string.IsNullOrEmpty(result.error)) entry["error"] = result.error;

                    results.Add(entry);

                    if (!result.success)
                    {
                        allSuccess = false;
                        if (failFast) break;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "tool", tool },
                        { "success", false },
                        { "error", ex.Message }
                    });
                    allSuccess = false;
                    if (failFast) break;
                }
            }

            return new CommandResult
            {
                success = allSuccess,
                data = new Dictionary<string, object>
                {
                    { "results", results },
                    { "total", commandsRaw.Count },
                    { "executed", results.Count },
                    { "all_success", allSuccess }
                }
            };
        }
    }
}
