using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// MCP handler for creating and reverting project snapshots.
    /// </summary>
    public class SnapshotHandler : IToolHandler
    {
        public async Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var action = p.GetString("action") ?? commandName;

            switch (action)
            {
                case "create":
                case "create_snapshot":
                {
                    var mgr = SnapshotManager.Instance;
                    if (mgr == null)
                        return new CommandResult { success = false, error = "SnapshotManager not initialized" };

                    var note = p.GetString("note");
                    var (success, message) = await mgr.CreateSnapshotAsync(note);
                    var data = new Dictionary<string, object>
                    {
                        { "message", message },
                        { "snapshot_id", mgr.LastSnapshotId },
                        { "time", mgr.LastSnapshotTime },
                        { "is_git", mgr.IsGitRepo },
                    };
                    if (!string.IsNullOrEmpty(mgr.LastSnapshotNote))
                        data["note"] = mgr.LastSnapshotNote;

                    return new CommandResult
                    {
                        success = success,
                        data = success ? data : null,
                        error = success ? null : message,
                    };
                }

                case "revert":
                case "revert_snapshot":
                {
                    var mgr = SnapshotManager.Instance;
                    if (mgr == null)
                        return new CommandResult { success = false, error = "SnapshotManager not initialized" };

                    var (success, message) = await mgr.RevertToSnapshotAsync();
                    return new CommandResult
                    {
                        success = success,
                        data = success
                            ? new Dictionary<string, object> { { "message", message } }
                            : null,
                        error = success ? null : message,
                    };
                }

                case "status":
                case "snapshot_status":
                {
                    var mgr = SnapshotManager.Instance;
                    if (mgr == null)
                        return new CommandResult { success = false, error = "SnapshotManager not initialized" };

                    var statusData = new Dictionary<string, object>
                    {
                        { "has_snapshot", mgr.HasSnapshot },
                        { "snapshot_id", mgr.LastSnapshotId },
                        { "time", mgr.LastSnapshotTime },
                        { "is_git", mgr.IsGitRepo },
                    };
                    if (!string.IsNullOrEmpty(mgr.LastSnapshotNote))
                        statusData["note"] = mgr.LastSnapshotNote;

                    return new CommandResult
                    {
                        success = true,
                        data = statusData
                    };
                }

                default:
                    return new CommandResult { success = false, error = $"Unknown snapshot action: {action}" };
            }
        }
    }
}
