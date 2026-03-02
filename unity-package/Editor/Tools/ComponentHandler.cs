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
    /// Handles MCP commands for adding, removing, and modifying Unity components.
    /// </summary>
    public class ComponentHandler : IToolHandler
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
            var target = p.RequireString("target");

            var go = FindTarget(target, p.GetString("search_method") ?? p.GetString("searchMethod"));
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            switch (action)
            {
                case "add": return AddComponent(go, p);
                case "remove": return RemoveComponent(go, p);
                case "get": return GetComponent(go, p);
                case "get_all": return GetAllComponents(go, p);
                case "set_property": return SetProperty(go, p);
                default:
                    return new CommandResult { success = false, error = $"Unknown component action: {action}" };
            }
        }

        private CommandResult AddComponent(GameObject go, ToolParams p)
        {
            var typeName = p.RequireString("component_type") ?? p.RequireString("componentType");
            var type = FindComponentType(typeName);
            if (type == null)
                return new CommandResult { success = false, error = $"Component type not found: {typeName}" };

            var comp = Undo.AddComponent(go, type);
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "component", typeName },
                    { "gameObject", go.name },
                    { "added", true }
                }
            };
        }

        private CommandResult RemoveComponent(GameObject go, ToolParams p)
        {
            var typeName = p.RequireString("component_type") ?? p.RequireString("componentType");
            var type = FindComponentType(typeName);
            if (type == null)
                return new CommandResult { success = false, error = $"Component type not found: {typeName}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new CommandResult { success = false, error = $"Component '{typeName}' not found on '{go.name}'" };

            Undo.DestroyObjectImmediate(comp);
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "component", typeName },
                    { "gameObject", go.name },
                    { "removed", true }
                }
            };
        }

        private CommandResult GetComponent(GameObject go, ToolParams p)
        {
            var typeName = p.RequireString("component_type") ?? p.RequireString("componentType");
            var type = FindComponentType(typeName);
            if (type == null)
                return new CommandResult { success = false, error = $"Component type not found: {typeName}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new CommandResult { success = false, error = $"Component '{typeName}' not found on '{go.name}'" };

            var properties = SerializeComponent(comp);
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "component", typeName },
                    { "gameObject", go.name },
                    { "properties", properties }
                }
            };
        }

        private CommandResult GetAllComponents(GameObject go, ToolParams p)
        {
            var includeProps = p.GetBool("include_properties") ?? p.GetBool("includeProperties") ?? false;
            var components = go.GetComponents<Component>();
            var results = new List<Dictionary<string, object>>();

            foreach (var comp in components)
            {
                if (comp == null) continue; // Missing script
                var entry = new Dictionary<string, object>
                {
                    { "type", comp.GetType().Name },
                    { "fullType", comp.GetType().FullName },
                    { "enabled", IsComponentEnabled(comp) }
                };

                if (includeProps)
                    entry["properties"] = SerializeComponent(comp);

                results.Add(entry);
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "gameObject", go.name },
                    { "components", results },
                    { "count", results.Count }
                }
            };
        }

        private CommandResult SetProperty(GameObject go, ToolParams p)
        {
            var typeName = p.RequireString("component_type") ?? p.RequireString("componentType");
            var propName = p.RequireString("property_name") ?? p.RequireString("propertyName");
            var propValue = p.GetRaw("property_value") ?? p.GetRaw("propertyValue");

            var type = FindComponentType(typeName);
            if (type == null)
                return new CommandResult { success = false, error = $"Component type not found: {typeName}" };

            var comp = go.GetComponent(type);
            if (comp == null)
                return new CommandResult { success = false, error = $"Component '{typeName}' not found on '{go.name}'" };

            // Try using SerializedObject for proper Undo support
            var so = new SerializedObject(comp);
            var prop = so.FindProperty(propName);

            if (prop != null)
            {
                if (!SetSerializedProperty(prop, propValue))
                    return new CommandResult { success = false, error = $"Failed to set property '{propName}' — unsupported type" };

                so.ApplyModifiedProperties();
                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "component", typeName },
                        { "property", propName },
                        { "set", true }
                    }
                };
            }

            // Fallback: try reflection
            var fieldInfo = type.GetField(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var propInfo = type.GetProperty(propName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (fieldInfo != null)
            {
                Undo.RecordObject(comp, $"Set {propName}");
                fieldInfo.SetValue(comp, Convert.ChangeType(propValue, fieldInfo.FieldType));
                return new CommandResult { success = true, data = new Dictionary<string, object> { { "set", true } } };
            }

            if (propInfo != null && propInfo.CanWrite)
            {
                Undo.RecordObject(comp, $"Set {propName}");
                propInfo.SetValue(comp, Convert.ChangeType(propValue, propInfo.PropertyType));
                return new CommandResult { success = true, data = new Dictionary<string, object> { { "set", true } } };
            }

            return new CommandResult { success = false, error = $"Property '{propName}' not found on component '{typeName}'" };
        }

        // --- Helpers ---

        private static bool SetSerializedProperty(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = Convert.ToInt32(value);
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = Convert.ToBoolean(value);
                    return true;
                case SerializedPropertyType.Float:
                    prop.floatValue = Convert.ToSingle(value);
                    return true;
                case SerializedPropertyType.String:
                    prop.stringValue = value?.ToString() ?? "";
                    return true;
                case SerializedPropertyType.Enum:
                    prop.enumValueIndex = Convert.ToInt32(value);
                    return true;
                case SerializedPropertyType.Vector3:
                    if (value is List<object> v3 && v3.Count >= 3)
                    {
                        prop.vector3Value = new Vector3(
                            Convert.ToSingle(v3[0]),
                            Convert.ToSingle(v3[1]),
                            Convert.ToSingle(v3[2])
                        );
                        return true;
                    }
                    return false;
                case SerializedPropertyType.Color:
                    if (value is List<object> c && c.Count >= 3)
                    {
                        var r = Convert.ToSingle(c[0]);
                        var g = Convert.ToSingle(c[1]);
                        var b = Convert.ToSingle(c[2]);
                        var a = c.Count >= 4 ? Convert.ToSingle(c[3]) : 1f;
                        // Auto-detect 0-255 range
                        if (r > 1f || g > 1f || b > 1f || a > 1f)
                        {
                            r /= 255f; g /= 255f; b /= 255f; a /= 255f;
                        }
                        prop.colorValue = new Color(r, g, b, a);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        private static Dictionary<string, object> SerializeComponent(Component comp)
        {
            var props = new Dictionary<string, object>();
            var so = new SerializedObject(comp);
            var iterator = so.GetIterator();

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (iterator.name == "m_Script") continue; // Skip script reference
                    props[iterator.name] = SerializePropertyValue(iterator);
                }
                while (iterator.NextVisible(false));
            }

            return props;
        }

        private static object SerializePropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0
                    ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2: var v2 = prop.vector2Value; return new List<object> { v2.x, v2.y };
                case SerializedPropertyType.Vector3: var v3 = prop.vector3Value; return new List<object> { v3.x, v3.y, v3.z };
                case SerializedPropertyType.Vector4: var v4 = prop.vector4Value; return new List<object> { v4.x, v4.y, v4.z, v4.w };
                case SerializedPropertyType.Color: var c = prop.colorValue; return new List<object> { c.r, c.g, c.b, c.a };
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                default: return prop.propertyType.ToString();
            }
        }

        private static bool IsComponentEnabled(Component comp)
        {
            if (comp is Behaviour b) return b.enabled;
            if (comp is Renderer r) return r.enabled;
            if (comp is Collider c) return c.enabled;
            return true;
        }

        private static GameObject FindTarget(string target, string searchMethod)
        {
            if (searchMethod == "by_id" && int.TryParse(target, out var id))
                return EditorUtility.InstanceIDToObject(id) as GameObject;

            return GameObject.Find(target);
        }

        private static Type FindComponentType(string typeName)
        {
            var type = Type.GetType($"UnityEngine.{typeName}, UnityEngine");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.PhysicsModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.Physics2DModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.AudioModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.{typeName}, UnityEngine.AnimationModule");
            if (type != null) return type;

            type = Type.GetType($"UnityEngine.UI.{typeName}, UnityEngine.UI");
            if (type != null) return type;

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
