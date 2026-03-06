using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// MCP tools for debugging UI Toolkit — capture editor window screenshots
    /// and inspect the live VisualElement tree with computed styles.
    /// </summary>
    public class UIDebugHandler : IToolHandler
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
                case "capture_editor_window": return CaptureEditorWindow(p);
                case "inspect_ui_tree": return InspectUiTree(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        private CommandResult CaptureEditorWindow(ToolParams p)
        {
            var windowTitle = p.GetString("window_title");
            var maxResolution = p.GetInt("max_resolution") ?? 800;

            EditorWindow targetWindow = null;

            if (!string.IsNullOrEmpty(windowTitle))
            {
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                targetWindow = allWindows.FirstOrDefault(w =>
                    w.titleContent.text.Equals(windowTitle, StringComparison.OrdinalIgnoreCase));

                if (targetWindow == null)
                {
                    var available = string.Join(", ",
                        allWindows.Select(w => w.titleContent.text).Distinct());
                    return new CommandResult
                    {
                        success = false,
                        error = $"Window '{windowTitle}' not found. Available: {available}"
                    };
                }
            }
            else
            {
                targetWindow = EditorWindow.focusedWindow;
                if (targetWindow == null)
                {
                    targetWindow = SceneView.lastActiveSceneView;
                }
            }

            if (targetWindow == null)
                return new CommandResult { success = false, error = "No editor window available to capture" };

            targetWindow.Repaint();

            var pos = targetWindow.position;
            int width = Mathf.Max(1, (int)pos.width);
            int height = Mathf.Max(1, (int)pos.height);

            // Read pixels from the window's screen position
            var colors = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(
                new Vector2(pos.x, pos.y), width, height);

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels(colors);
            tex.Apply();

            try
            {
                int imgW = width, imgH = height;
                string base64;

                if (width > maxResolution || height > maxResolution)
                {
                    float scale = Mathf.Min((float)maxResolution / width, (float)maxResolution / height);
                    int dstW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                    int dstH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

                    var rt = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
                    rt.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(tex, rt);
                    var prevActive = RenderTexture.active;
                    RenderTexture.active = rt;
                    var downscaled = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                    downscaled.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                    downscaled.Apply();
                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(rt);

                    base64 = Convert.ToBase64String(downscaled.EncodeToPNG());
                    imgW = dstW;
                    imgH = dstH;
                    UnityEngine.Object.DestroyImmediate(downscaled);
                }
                else
                {
                    base64 = Convert.ToBase64String(tex.EncodeToPNG());
                }

                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "image_base64", base64 },
                        { "width", imgW },
                        { "height", imgH },
                        { "window_title", targetWindow.titleContent.text },
                        { "source", "editor_window" }
                    }
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private CommandResult InspectUiTree(ToolParams p)
        {
            var windowTitle = p.GetString("window_title");
            var maxDepth = p.GetInt("max_depth") ?? 10;
            var includeStyles = p.GetBool("include_styles") ?? false;

            EditorWindow targetWindow = null;

            if (!string.IsNullOrEmpty(windowTitle))
            {
                var allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                targetWindow = allWindows.FirstOrDefault(w =>
                    w.titleContent.text.Equals(windowTitle, StringComparison.OrdinalIgnoreCase));

                if (targetWindow == null)
                {
                    var available = string.Join(", ",
                        allWindows.Select(w => w.titleContent.text).Distinct());
                    return new CommandResult
                    {
                        success = false,
                        error = $"Window '{windowTitle}' not found. Available: {available}"
                    };
                }
            }
            else
            {
                targetWindow = EditorWindow.focusedWindow ?? SceneView.lastActiveSceneView;
            }

            if (targetWindow == null)
                return new CommandResult { success = false, error = "No editor window available to inspect" };

            var root = targetWindow.rootVisualElement;
            var pos = targetWindow.position;

            var tree = BuildElementTree(root, 0, maxDepth, includeStyles);
            var textTree = new StringBuilder();
            BuildTextTree(root, 0, maxDepth, includeStyles, textTree, "");

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "window_title", targetWindow.titleContent.text },
                    { "window_size", $"{(int)pos.width}x{(int)pos.height}" },
                    { "tree", tree },
                    { "text_tree", textTree.ToString() }
                }
            };
        }

        private static Dictionary<string, object> BuildElementTree(
            VisualElement element, int depth, int maxDepth, bool includeStyles)
        {
            var node = new Dictionary<string, object>
            {
                { "type", element.GetType().Name },
                { "name", element.name ?? "" },
            };

            var classes = element.GetClasses().ToList();
            if (classes.Count > 0)
                node["classes"] = string.Join(" ", classes);

            var layout = element.layout;
            if (!float.IsNaN(layout.width) && !float.IsNaN(layout.height))
                node["layout"] = $"{(int)layout.x},{(int)layout.y} {(int)layout.width}x{(int)layout.height}";

            if (element is Label label)
                node["text"] = TruncateText(label.text, 80);
            else if (element is TextField textField)
            {
                node["text"] = TruncateText(textField.value, 80);
                node["label"] = textField.label ?? "";
            }
            else if (element is Button button)
                node["text"] = button.text ?? "";
            else if (element is Foldout foldout)
            {
                node["text"] = foldout.text ?? "";
                node["expanded"] = foldout.value;
            }
            else if (element is Toggle toggle)
            {
                node["label"] = toggle.label ?? "";
                node["checked"] = toggle.value;
            }

            if (!element.visible)
                node["visible"] = false;
            if (!element.enabledSelf)
                node["enabled"] = false;

            if (includeStyles)
            {
                var styles = new Dictionary<string, object>();
                var rs = element.resolvedStyle;
                if (rs.display != DisplayStyle.Flex)
                    styles["display"] = rs.display.ToString();
                if (rs.visibility != Visibility.Visible)
                    styles["visibility"] = rs.visibility.ToString();
                if (rs.fontSize != 0)
                    styles["font-size"] = $"{rs.fontSize}px";
                if (rs.color != Color.clear)
                    styles["color"] = ColorUtility.ToHtmlStringRGBA(rs.color);
                if (rs.backgroundColor != Color.clear)
                    styles["background-color"] = ColorUtility.ToHtmlStringRGBA(rs.backgroundColor);
                if (styles.Count > 0)
                    node["styles"] = styles;
            }

            if (depth < maxDepth && element.childCount > 0)
            {
                var children = new List<object>();
                foreach (var child in element.Children())
                    children.Add(BuildElementTree(child, depth + 1, maxDepth, includeStyles));
                node["children"] = children;
            }
            else if (element.childCount > 0)
            {
                node["child_count"] = element.childCount;
            }

            return node;
        }

        private static void BuildTextTree(
            VisualElement element, int depth, int maxDepth, bool includeStyles,
            StringBuilder sb, string indent)
        {
            var typeName = element.GetType().Name;
            var name = !string.IsNullOrEmpty(element.name) ? $"#{element.name}" : "";
            var classes = element.GetClasses().ToList();
            var classStr = classes.Count > 0 ? "." + string.Join(".", classes) : "";

            var info = $"{typeName}{name}{classStr}";

            var textContent = "";
            if (element is Label label && !string.IsNullOrEmpty(label.text))
                textContent = $" \"{TruncateText(label.text, 50)}\"";
            else if (element is Button button && !string.IsNullOrEmpty(button.text))
                textContent = $" \"{button.text}\"";
            else if (element is Foldout foldout)
                textContent = $" \"{foldout.text}\" [{(foldout.value ? "open" : "closed")}]";

            var layout = element.layout;
            var size = !float.IsNaN(layout.width)
                ? $" ({(int)layout.width}x{(int)layout.height})"
                : "";

            sb.AppendLine($"{indent}{info}{textContent}{size}");

            if (depth >= maxDepth && element.childCount > 0)
            {
                sb.AppendLine($"{indent}  ... ({element.childCount} children)");
                return;
            }

            var childIndent = indent + "  ";
            for (var i = 0; i < element.childCount; i++)
            {
                BuildTextTree(element[i], depth + 1, maxDepth, includeStyles, sb, childIndent);
            }
        }

        private static string TruncateText(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", "\\n").Replace("\r", "");
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }
    }
}
