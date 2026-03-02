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
    /// Handles MCP commands for creating, modifying, and assigning materials.
    /// </summary>
    public class MaterialHandler : IToolHandler
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
                case "create": return CreateMaterial(p);
                case "modify": return ModifyMaterial(p);
                case "assign": return AssignMaterial(p);
                case "get": return GetMaterial(p);
                case "list": return ListMaterials(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown material action: {action}" };
            }
        }

        private CommandResult CreateMaterial(ToolParams p)
        {
            var name = p.GetString("name", "New Material");
            var path = p.GetString("path") ?? $"Assets/Materials/{name}.mat";
            var shaderName = p.GetString("shader") ?? "Standard";

            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Replace("\\", "/").Split('/');
                var currentPath = parts[0]; // "Assets"
                for (var i = 1; i < parts.Length; i++)
                {
                    var nextPath = currentPath + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(nextPath))
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    currentPath = nextPath;
                }
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                // Try common alternatives
                shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            }

            if (shader == null)
                return new CommandResult { success = false, error = $"Shader not found: {shaderName}" };

            var material = new Material(shader) { name = name };

            // Apply color if specified
            var color = p.GetVector3("color");
            if (color != null)
            {
                var c = ParseColor(color);
                material.color = c;
            }

            // Apply additional properties
            var properties = p.GetRaw("properties") as Dictionary<string, object>;
            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    ApplyMaterialProperty(material, kvp.Key, kvp.Value);
                }
            }

            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", name },
                    { "path", path },
                    { "shader", shader.name },
                    { "created", true }
                }
            };
        }

        private CommandResult ModifyMaterial(ToolParams p)
        {
            var path = p.GetString("path");
            var name = p.GetString("name");

            Material material = null;

            if (!string.IsNullOrEmpty(path))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                var guids = AssetDatabase.FindAssets($"t:Material {name}");
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (mat != null && mat.name == name)
                    {
                        material = mat;
                        break;
                    }
                }
            }

            if (material == null)
                return new CommandResult { success = false, error = $"Material not found: {path ?? name}" };

            Undo.RecordObject(material, "Modify Material");

            var shader = p.GetString("shader");
            if (!string.IsNullOrEmpty(shader))
            {
                var s = Shader.Find(shader);
                if (s != null) material.shader = s;
            }

            var color = p.GetVector3("color");
            if (color != null)
                material.color = ParseColor(color);

            var properties = p.GetRaw("properties") as Dictionary<string, object>;
            if (properties != null)
            {
                foreach (var kvp in properties)
                    ApplyMaterialProperty(material, kvp.Key, kvp.Value);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", material.name },
                    { "modified", true }
                }
            };
        }

        private CommandResult AssignMaterial(ToolParams p)
        {
            var target = p.RequireString("target");
            var materialPath = p.GetString("path");
            var materialName = p.GetString("name");

            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return new CommandResult { success = false, error = $"No Renderer on '{target}'" };

            Material material = null;
            if (!string.IsNullOrEmpty(materialPath))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            }
            else if (!string.IsNullOrEmpty(materialName))
            {
                var guids = AssetDatabase.FindAssets($"t:Material {materialName}");
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (mat != null && mat.name == materialName)
                    {
                        material = mat;
                        break;
                    }
                }
            }

            if (material == null)
                return new CommandResult { success = false, error = $"Material not found: {materialPath ?? materialName}" };

            Undo.RecordObject(renderer, "Assign Material");
            renderer.sharedMaterial = material;

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "gameObject", target },
                    { "material", material.name },
                    { "assigned", true }
                }
            };
        }

        private CommandResult GetMaterial(ToolParams p)
        {
            var path = p.GetString("path");
            var name = p.GetString("name");
            var target = p.GetString("target");

            Material material = null;

            if (!string.IsNullOrEmpty(target))
            {
                var go = GameObject.Find(target);
                if (go == null)
                    return new CommandResult { success = false, error = $"GameObject not found: {target}" };
                var renderer = go.GetComponent<Renderer>();
                if (renderer == null)
                    return new CommandResult { success = false, error = $"No Renderer on '{target}'" };
                material = renderer.sharedMaterial;
            }
            else if (!string.IsNullOrEmpty(path))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            if (material == null)
                return new CommandResult { success = false, error = "Material not found" };

            var data = new Dictionary<string, object>
            {
                { "name", material.name },
                { "shader", material.shader.name },
                { "color", new List<object> { material.color.r, material.color.g, material.color.b, material.color.a } },
                { "renderQueue", material.renderQueue }
            };

            // List shader properties
            var shaderProps = new List<Dictionary<string, object>>();
            var shader = material.shader;
            var propCount = shader.GetPropertyCount();
            for (var i = 0; i < propCount; i++)
            {
                shaderProps.Add(new Dictionary<string, object>
                {
                    { "name", shader.GetPropertyName(i) },
                    { "type", shader.GetPropertyType(i).ToString() },
                    { "description", shader.GetPropertyDescription(i) }
                });
            }
            data["shaderProperties"] = shaderProps;

            return new CommandResult { success = true, data = data };
        }

        private CommandResult ListMaterials(ToolParams p)
        {
            var guids = AssetDatabase.FindAssets("t:Material");
            var pageSize = p.GetInt("page_size") ?? 50;
            var cursor = p.GetInt("cursor") ?? 0;

            var results = new List<Dictionary<string, object>>();
            var startIndex = Math.Min(cursor, guids.Length);
            var endIndex = Math.Min(startIndex + pageSize, guids.Length);

            for (var i = startIndex; i < endIndex; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (mat != null)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        { "name", mat.name },
                        { "path", assetPath },
                        { "shader", mat.shader.name }
                    });
                }
            }

            var data = new Dictionary<string, object>
            {
                { "materials", results },
                { "total", guids.Length },
                { "count", results.Count }
            };

            if (endIndex < guids.Length)
                data["next_cursor"] = endIndex;

            return new CommandResult { success = true, data = data };
        }

        // --- Helpers ---

        private static Color ParseColor(float[] components)
        {
            if (components == null || components.Length < 3)
                return Color.white;

            var r = components[0];
            var g = components[1];
            var b = components[2];
            var a = components.Length >= 4 ? components[3] : 1f;

            // Auto-detect 0-255 range
            if (r > 1f || g > 1f || b > 1f || (a > 1f && components.Length >= 4))
            {
                r /= 255f; g /= 255f; b /= 255f;
                if (components.Length >= 4) a /= 255f;
            }

            return new Color(r, g, b, a);
        }

        private static void ApplyMaterialProperty(Material material, string propertyName, object value)
        {
            if (!material.HasProperty(propertyName)) return;

            if (value is double d)
                material.SetFloat(propertyName, (float)d);
            else if (value is long l)
                material.SetInt(propertyName, (int)l);
            else if (value is List<object> list)
            {
                if (list.Count == 4)
                {
                    var color = new Color(
                        Convert.ToSingle(list[0]),
                        Convert.ToSingle(list[1]),
                        Convert.ToSingle(list[2]),
                        Convert.ToSingle(list[3])
                    );
                    material.SetColor(propertyName, color);
                }
                else if (list.Count == 3)
                {
                    material.SetVector(propertyName, new Vector4(
                        Convert.ToSingle(list[0]),
                        Convert.ToSingle(list[1]),
                        Convert.ToSingle(list[2]),
                        0
                    ));
                }
            }
        }
    }
}
