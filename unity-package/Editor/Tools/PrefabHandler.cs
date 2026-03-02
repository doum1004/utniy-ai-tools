using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

#if UNITY_2021_3_OR_NEWER
using UnityEditor.SceneManagement;
#endif

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP commands for creating, instantiating, and managing prefabs.
    /// </summary>
    public class PrefabHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try { tcs.SetResult(Execute(paramsJson)); }
                catch (Exception ex) { tcs.SetResult(new CommandResult { success = false, error = ex.Message }); }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var action = p.RequireString("action");

            switch (action)
            {
                case "create": return CreatePrefab(p);
                case "instantiate": return InstantiatePrefab(p);
                case "apply": return ApplyPrefab(p);
                case "revert": return RevertPrefab(p);
                case "unpack": return UnpackPrefab(p);
                case "get_info": return GetPrefabInfo(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown prefab action: {action}" };
            }
        }

        private CommandResult CreatePrefab(ToolParams p)
        {
            var target = p.GetString("target");
            var path = p.RequireString("path");

            if (string.IsNullOrEmpty(target))
                return new CommandResult { success = false, error = "Missing 'target' — specify a GameObject name to save as prefab" };

            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            // Ensure directory exists
            var dir = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var currentPath = parts[0];
                for (var i = 1; i < parts.Length; i++)
                {
                    var nextPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(nextPath))
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    currentPath = nextPath;
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out var success);
            if (!success)
                return new CommandResult { success = false, error = $"Failed to create prefab at: {path}" };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "path", path },
                    { "name", prefab.name },
                    { "created", true }
                }
            };
        }

        private CommandResult InstantiatePrefab(ToolParams p)
        {
            var path = p.RequireString("path");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                return new CommandResult { success = false, error = $"Prefab not found: {path}" };

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
                return new CommandResult { success = false, error = "Failed to instantiate prefab" };

            var name = p.GetString("name");
            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            var pos = p.GetUnityVector3("position");
            if (pos.HasValue) instance.transform.position = pos.Value;

            var rot = p.GetUnityVector3("rotation");
            if (rot.HasValue) instance.transform.eulerAngles = rot.Value;

            var parentName = p.GetString("parent");
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null) instance.transform.SetParent(parent.transform);
            }

            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", instance.name },
                    { "instanceId", instance.GetInstanceID() },
                    { "prefabPath", path },
                    { "position", new List<object> { instance.transform.position.x, instance.transform.position.y, instance.transform.position.z } }
                }
            };
        }

        private CommandResult ApplyPrefab(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new CommandResult { success = false, error = $"'{target}' is not a prefab instance" };

            var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            PrefabUtility.ApplyPrefabInstance(prefabRoot, InteractionMode.UserAction);

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "target", target },
                    { "prefabPath", prefabPath },
                    { "applied", true }
                }
            };
        }

        private CommandResult RevertPrefab(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new CommandResult { success = false, error = $"'{target}' is not a prefab instance" };

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.UserAction);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "target", target },
                    { "reverted", true }
                }
            };
        }

        private CommandResult UnpackPrefab(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new CommandResult { success = false, error = $"'{target}' is not a prefab instance" };

            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "target", target },
                    { "unpacked", true }
                }
            };
        }

        private CommandResult GetPrefabInfo(ToolParams p)
        {
            var path = p.GetString("path");
            var target = p.GetString("target");

            if (!string.IsNullOrEmpty(path))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    return new CommandResult { success = false, error = $"Prefab not found: {path}" };

                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "name", prefab.name },
                        { "path", path },
                        { "childCount", prefab.transform.childCount },
                        { "componentCount", prefab.GetComponents<Component>().Length }
                    }
                };
            }

            if (!string.IsNullOrEmpty(target))
            {
                var go = GameObject.Find(target);
                if (go == null)
                    return new CommandResult { success = false, error = $"GameObject not found: {target}" };

                var isPrefab = PrefabUtility.IsPartOfPrefabInstance(go);
                var data = new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "isPrefabInstance", isPrefab }
                };

                if (isPrefab)
                {
                    data["prefabPath"] = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    data["hasOverrides"] = PrefabUtility.HasPrefabInstanceAnyOverrides(go, false);
                    var status = PrefabUtility.GetPrefabInstanceStatus(go);
                    data["status"] = status.ToString();
                }

                return new CommandResult { success = true, data = data };
            }

            return new CommandResult { success = false, error = "Specify either 'path' (asset) or 'target' (instance)" };
        }
    }
}
