using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP editor control commands — play/pause/stop, console, refresh, state.
    /// </summary>
    public class EditorHandler : IToolHandler
    {
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
                case "manage_editor": return HandleManageEditor(p);
                case "read_console": return HandleReadConsole(p);
                case "refresh_unity": return HandleRefreshUnity(p);
                case "execute_menu_item": return HandleExecuteMenuItem(p);
                case "get_editor_state": return GetEditorState();
                case "get_project_info": return GetProjectInfo();
                case "get_editor_selection": return GetEditorSelection();
                case "get_project_tags": return GetProjectTags();
                case "get_project_layers": return GetProjectLayers();
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        private CommandResult HandleManageEditor(ToolParams p)
        {
            var action = p.RequireString("action");

            switch (action)
            {
                case "play":
                    EditorApplication.isPlaying = true;
                    return new CommandResult { success = true, data = new Dictionary<string, object> { { "action", "play" } } };

                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return new CommandResult { success = true, data = new Dictionary<string, object> { { "paused", EditorApplication.isPaused } } };

                case "stop":
                    EditorApplication.isPlaying = false;
                    return new CommandResult { success = true, data = new Dictionary<string, object> { { "action", "stop" } } };

                case "get_selection":
                    return GetEditorSelection();

                case "select":
                    var target = p.GetString("target");
                    if (!string.IsNullOrEmpty(target))
                    {
                        var go = GameObject.Find(target);
                        if (go != null)
                        {
                            Selection.activeGameObject = go;
                            return new CommandResult { success = true, data = new Dictionary<string, object> { { "selected", target } } };
                        }
                        return new CommandResult { success = false, error = $"GameObject not found: {target}" };
                    }
                    return new CommandResult { success = false, error = "Missing target parameter" };

                default:
                    return new CommandResult { success = false, error = $"Unknown editor action: {action}" };
            }
        }

        private CommandResult HandleReadConsole(ToolParams p)
        {
            var types = p.GetStringList("types");
            var count = p.GetInt("count") ?? 10;
            var includeStackTrace = p.GetBool("include_stacktrace") ?? p.GetBool("includeStacktrace") ?? false;
            var format = p.GetString("format", "summary");

            var logs = ConsoleLogCapture.Instance.GetLogs(types, count, includeStackTrace, format);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "logs", logs },
                    { "count", logs.Count },
                    { "is_compiling", EditorApplication.isCompiling },
                    { "is_playing", EditorApplication.isPlaying }
                }
            };
        }

        private CommandResult HandleRefreshUnity(ToolParams p)
        {
            AssetDatabase.Refresh();
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object> { { "refreshed", true } }
            };
        }

        private CommandResult HandleExecuteMenuItem(ToolParams p)
        {
            var menuPath = p.RequireString("menu_path") ?? p.RequireString("menuPath");
            var result = EditorApplication.ExecuteMenuItem(menuPath);
            return new CommandResult
            {
                success = result,
                data = new Dictionary<string, object>
                {
                    { "menu_path", menuPath },
                    { "executed", result }
                }
            };
        }

        private CommandResult GetEditorState()
        {
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "is_compiling", EditorApplication.isCompiling },
                    { "is_playing", EditorApplication.isPlaying },
                    { "is_paused", EditorApplication.isPaused },
                    { "ready_for_tools", !EditorApplication.isCompiling },
                    { "unity_version", Application.unityVersion },
                    { "platform", Application.platform.ToString() }
                }
            };
        }

        private CommandResult GetProjectInfo()
        {
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "project_name", Application.productName },
                    { "company_name", Application.companyName },
                    { "unity_version", Application.unityVersion },
                    { "data_path", Application.dataPath },
                    { "platform", Application.platform.ToString() },
                    { "scripting_backend", PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() },
                    { "api_compatibility", PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() }
                }
            };
        }

        private CommandResult GetEditorSelection()
        {
            var selection = Selection.gameObjects;
            var items = new List<Dictionary<string, object>>();
            foreach (var go in selection)
            {
                items.Add(new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "instanceId", go.GetInstanceID() }
                });
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "selection", items },
                    { "count", items.Count }
                }
            };
        }

        private CommandResult GetProjectTags()
        {
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "tags", new List<object>(tags) },
                    { "count", tags.Length }
                }
            };
        }

        private CommandResult GetProjectLayers()
        {
            var layers = new List<Dictionary<string, object>>();
            for (var i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layers.Add(new Dictionary<string, object>
                    {
                        { "index", i },
                        { "name", name }
                    });
                }
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "layers", layers },
                    { "count", layers.Count }
                }
            };
        }
    }
}
