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
    /// Handles MCP commands for creating, modifying, deleting, and searching GameObjects.
    /// </summary>
    public class GameObjectHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            // Unity API must be called on the main thread
            CommandResult result = null;
            EditorApplication.CallbackFunction callback = null;

            var tcs = new TaskCompletionSource<CommandResult>();

            callback = () =>
            {
                EditorApplication.update -= callback;
                try
                {
                    result = Execute(commandName, paramsJson);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new CommandResult
                    {
                        success = false,
                        error = ex.Message
                    });
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
                case "manage_gameobject":
                    return HandleManageGameObject(p);
                case "find_gameobjects":
                    return HandleFindGameObjects(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        private CommandResult HandleManageGameObject(ToolParams p)
        {
            var action = p.RequireString("action");

            switch (action)
            {
                case "create": return CreateGameObject(p);
                case "modify": return ModifyGameObject(p);
                case "delete": return DeleteGameObject(p);
                case "duplicate": return DuplicateGameObject(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown action: {action}" };
            }
        }

        private CommandResult CreateGameObject(ToolParams p)
        {
            var name = p.GetString("name", "New GameObject");
            GameObject go;

            var primitiveType = p.GetString("primitive_type") ?? p.GetString("primitiveType");
            if (!string.IsNullOrEmpty(primitiveType))
            {
                if (Enum.TryParse<PrimitiveType>(primitiveType, true, out var pt))
                {
                    go = GameObject.CreatePrimitive(pt);
                    go.name = name;
                }
                else
                {
                    return new CommandResult { success = false, error = $"Invalid primitive type: {primitiveType}" };
                }
            }
            else
            {
                go = new GameObject(name);
            }

            // Apply position
            var pos = p.GetUnityVector3("position");
            if (pos.HasValue) go.transform.position = pos.Value;

            // Apply rotation
            var rot = p.GetUnityVector3("rotation");
            if (rot.HasValue) go.transform.eulerAngles = rot.Value;

            // Apply scale
            var scale = p.GetUnityVector3("scale");
            if (scale.HasValue) go.transform.localScale = scale.Value;

            // Set parent
            var parentName = p.GetString("parent");
            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent != null) go.transform.SetParent(parent.transform);
            }

            // Set tag
            var tag = p.GetString("tag");
            if (!string.IsNullOrEmpty(tag))
            {
                try { go.tag = tag; }
                catch (Exception ex) { Debug.LogWarning($"[UnityAITools] Failed to set tag: {ex.Message}"); }
            }

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            var data = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                { "position", new List<object> { go.transform.position.x, go.transform.position.y, go.transform.position.z } }
            };

            return new CommandResult { success = true, data = data };
        }

        private CommandResult ModifyGameObject(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = FindGameObject(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            Undo.RecordObject(go.transform, $"Modify {go.name}");
            Undo.RecordObject(go, $"Modify {go.name}");

            var newName = p.GetString("name");
            if (!string.IsNullOrEmpty(newName)) go.name = newName;

            var pos = p.GetUnityVector3("position");
            if (pos.HasValue) go.transform.position = pos.Value;

            var rot = p.GetUnityVector3("rotation");
            if (rot.HasValue) go.transform.eulerAngles = rot.Value;

            var scale = p.GetUnityVector3("scale");
            if (scale.HasValue) go.transform.localScale = scale.Value;

            var active = p.GetBool("set_active") ?? p.GetBool("setActive");
            if (active.HasValue) go.SetActive(active.Value);

            // Add components
            var addComps = p.GetStringList("components_to_add") ?? p.GetStringList("componentsToAdd");
            if (addComps != null)
            {
                foreach (var comp in addComps)
                {
                    var type = FindComponentType(comp);
                    if (type != null)
                        Undo.AddComponent(go, type);
                }
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "instanceId", go.GetInstanceID() }
                }
            };
        }

        private CommandResult DeleteGameObject(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = FindGameObject(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            Undo.DestroyObjectImmediate(go);
            return new CommandResult { success = true, data = new Dictionary<string, object> { { "deleted", target } } };
        }

        private CommandResult DuplicateGameObject(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = FindGameObject(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            var duplicate = UnityEngine.Object.Instantiate(go);
            var newName = p.GetString("new_name") ?? $"{go.name}_Copy";
            duplicate.name = newName;

            var offset = p.GetUnityVector3("offset");
            if (offset.HasValue)
                duplicate.transform.position = go.transform.position + offset.Value;

            Undo.RegisterCreatedObjectUndo(duplicate, $"Duplicate {go.name}");

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", duplicate.name },
                    { "instanceId", duplicate.GetInstanceID() }
                }
            };
        }

        private CommandResult HandleFindGameObjects(ToolParams p)
        {
            var searchTerm = p.RequireString("search_term") ?? p.RequireString("searchTerm");
            var method = p.GetString("search_method", "by_name") ?? p.GetString("searchMethod", "by_name");
            var pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;

            var results = new List<Dictionary<string, object>>();

            switch (method)
            {
                case "by_name":
                    foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        if (go.name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(GameObjectToDict(go));
                            if (results.Count >= pageSize) break;
                        }
                    }
                    break;

                case "by_tag":
                    try
                    {
                        var tagged = GameObject.FindGameObjectsWithTag(searchTerm);
                        foreach (var go in tagged)
                        {
                            results.Add(GameObjectToDict(go));
                            if (results.Count >= pageSize) break;
                        }
                    }
                    catch { /* Tag not found */ }
                    break;

                case "by_component":
                    foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        foreach (var comp in go.GetComponents<Component>())
                        {
                            if (comp != null && comp.GetType().Name.Equals(searchTerm, StringComparison.OrdinalIgnoreCase))
                            {
                                results.Add(GameObjectToDict(go));
                                break;
                            }
                        }
                        if (results.Count >= pageSize) break;
                    }
                    break;

                case "by_id":
                    if (int.TryParse(searchTerm, out var instanceId))
                    {
                        var found = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                        if (found != null) results.Add(GameObjectToDict(found));
                    }
                    break;
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "results", results },
                    { "count", results.Count }
                }
            };
        }

        // --- Helpers ---

        private static GameObject FindGameObject(string target)
        {
            // Try by name first
            var go = GameObject.Find(target);
            if (go != null) return go;

            // Try by instance ID
            if (int.TryParse(target, out var instanceId))
            {
                return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            }

            return null;
        }

        private static Dictionary<string, object> GameObjectToDict(GameObject go)
        {
            return new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                { "active", go.activeSelf },
                { "tag", go.tag },
                { "layer", LayerMask.LayerToName(go.layer) },
                { "path", GetGameObjectPath(go) }
            };
        }

        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return "/" + path;
        }

        private static Type FindComponentType(string typeName)
        {
            // Try common Unity types
            var type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.PhysicsModule");
            if (type != null) return type;

            // Try all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in assembly.GetTypes())
                {
                    if (t.Name == typeName && typeof(Component).IsAssignableFrom(t))
                        return t;
                }
            }

            return null;
        }
    }
}
