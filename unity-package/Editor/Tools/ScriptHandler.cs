using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP commands for creating, reading, editing, and deleting C# scripts.
    /// </summary>
    public class ScriptHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try
                {
                    tcs.SetResult(Execute(commandName, paramsJson));
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new CommandResult { success = false, error = ex.Message });
                }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string commandName, string paramsJson)
        {
            var p = new ToolParams(paramsJson);

            switch (commandName)
            {
                case "create_script": return CreateScript(p);
                case "manage_script": return ManageScript(p);
                case "delete_script": return DeleteScript(p);
                case "get_sha": return GetSha(p);
                case "script_apply_edits": return ApplyScriptEdits(p);
                case "apply_text_edits": return ApplyTextEdits(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        private CommandResult CreateScript(ToolParams p)
        {
            var path = p.RequireString("path");
            var contents = p.RequireString("contents");
            var overwrite = p.GetBool("overwrite") ?? false;

            var fullPath = Path.Combine(Application.dataPath, "..", path);
            fullPath = Path.GetFullPath(fullPath);

            if (File.Exists(fullPath) && !overwrite)
            {
                return new CommandResult { success = false, error = $"File already exists: {path}. Use overwrite=true to replace." };
            }

            // Ensure directory exists
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(fullPath, contents);
            AssetDatabase.ImportAsset(path);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", path },
                    { "created", true }
                }
            };
        }

        private CommandResult ManageScript(ToolParams p)
        {
            var action = p.RequireString("action");
            var path = p.RequireString("path");

            var fullPath = Path.Combine(Application.dataPath, "..", path);
            fullPath = Path.GetFullPath(fullPath);

            switch (action)
            {
                case "read":
                    if (!File.Exists(fullPath))
                        return new CommandResult { success = false, error = $"File not found: {path}" };

                    var contents = File.ReadAllText(fullPath);
                    return new CommandResult
                    {
                        success = true,
                        data = new Dictionary<string, object>
                        {
                            { "path", path },
                            { "contents", contents },
                            { "sha", ComputeSha(contents) }
                        }
                    };

                case "info":
                    if (!File.Exists(fullPath))
                        return new CommandResult { success = false, error = $"File not found: {path}" };

                    var fileInfo = new FileInfo(fullPath);
                    return new CommandResult
                    {
                        success = true,
                        data = new Dictionary<string, object>
                        {
                            { "path", path },
                            { "size", fileInfo.Length },
                            { "lastModified", fileInfo.LastWriteTimeUtc.ToString("o") }
                        }
                    };

                default:
                    return new CommandResult { success = false, error = $"Unknown script action: {action}" };
            }
        }

        private CommandResult DeleteScript(ToolParams p)
        {
            var path = p.RequireString("path");
            var fullPath = Path.Combine(Application.dataPath, "..", path);
            fullPath = Path.GetFullPath(fullPath);

            if (!File.Exists(fullPath))
                return new CommandResult { success = false, error = $"File not found: {path}" };

            AssetDatabase.DeleteAsset(path);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object> { { "deleted", path } }
            };
        }

        private CommandResult GetSha(ToolParams p)
        {
            var path = p.RequireString("path");
            var fullPath = Path.Combine(Application.dataPath, "..", path);
            fullPath = Path.GetFullPath(fullPath);

            if (!File.Exists(fullPath))
                return new CommandResult { success = false, error = $"File not found: {path}" };

            var contents = File.ReadAllText(fullPath);
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", path },
                    { "sha", ComputeSha(contents) }
                }
            };
        }

        private static string ComputeSha(string content)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                var hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private CommandResult ApplyScriptEdits(ToolParams p)
        {
            var path = p.RequireString("path");
            var editsRaw = p.GetRaw("edits") as List<object>;
            var initialSha = p.RequireString("initial_sha");

            var fullPath = Path.Combine(Application.dataPath, "..", path);
            fullPath = Path.GetFullPath(fullPath);

            if (!File.Exists(fullPath))
                return new CommandResult { success = false, error = $"File not found: {path}" };

            var contents = File.ReadAllText(fullPath);
            var currentSha = ComputeSha(contents);

            if (currentSha != initialSha)
                return new CommandResult { success = false, error = $"SHA mismatch. Expected {initialSha}, got {currentSha}. File was modified externally." };

            if (editsRaw == null || editsRaw.Count == 0)
                return new CommandResult { success = false, error = "No edits provided" };

            // Apply edits in reverse order if they use indices/lines (if any), but here we match target text
            foreach (var editObj in editsRaw)
            {
                if (editObj is not Dictionary<string, object> editDict) continue;

                var targetText = editDict.TryGetValue("targetText", out var tt) ? tt?.ToString() : null;
                var replacementText = editDict.TryGetValue("replacementText", out var rt) ? rt?.ToString() : null;
                var exactMatch = editDict.TryGetValue("exactMatch", out var em) && em is bool b ? b : true;

                if (string.IsNullOrEmpty(targetText)) continue;

                if (contents.Contains(targetText))
                {
                    contents = contents.Replace(targetText, replacementText ?? "");
                }
                else if (!exactMatch)
                {
                    // Fallback to ignoring whitespace for matching
                    var normalizedContents = contents.Replace("\r", "").Replace(" ", "");
                    var normalizedTarget = targetText.Replace("\r", "").Replace(" ", "");
                    if (normalizedContents.Contains(normalizedTarget))
                        return new CommandResult { success = false, error = "Could not cleanly apply edit (whitespace mismatch). Use exact text from the file." };
                    else
                        return new CommandResult { success = false, error = "Target text not found in file." };
                }
                else
                {
                    return new CommandResult { success = false, error = "Target text not found in file." };
                }
            }

            File.WriteAllText(fullPath, contents);
            AssetDatabase.ImportAsset(path);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", path },
                    { "sha", ComputeSha(contents) },
                    { "editsApplied", editsRaw.Count }
                }
            };
        }

        private CommandResult ApplyTextEdits(ToolParams p)
        {
            // Same implementation as script_apply_edits for backwards compatibility
            return ApplyScriptEdits(p);
        }
    }
}
