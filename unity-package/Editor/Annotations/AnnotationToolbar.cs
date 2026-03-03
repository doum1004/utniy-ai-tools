using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Annotations
{
    /// <summary>
    /// Floating toolbar that appears when annotation mode is active.
    /// Lets users pick tools, colors, and finalise/cancel the session.
    /// Open via McpControlWindow or the menu item.
    /// </summary>
    public class AnnotationToolbar : EditorWindow
    {
        private static readonly string[] ToolNames = { "Freehand", "Rect", "Arrow", "Text" };
        private static readonly AnnotationTool[] Tools =
        {
            AnnotationTool.Freehand,
            AnnotationTool.Rectangle,
            AnnotationTool.Arrow,
            AnnotationTool.Text,
        };

        [MenuItem("Window/Unity AI Tools/Annotation Toolbar")]
        public static void ShowWindow()
        {
            var win = GetWindow<AnnotationToolbar>(false, "AI Annotate", true);
            win.minSize = new Vector2(240, 280);
            win.maxSize = new Vector2(240, 400);
        }

        private void OnEnable()
        {
            AnnotationSession.Instance.OnChanged += Repaint;
        }

        private void OnDisable()
        {
            AnnotationSession.Instance.OnChanged -= Repaint;
        }

        private void OnGUI()
        {
            var session = AnnotationSession.Instance;

            GUILayout.Space(6);
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            GUILayout.Label("Annotate Scene/Game View", headerStyle);
            GUILayout.Space(4);
            EditorGUILayout.HelpBox("Draw on the Scene view to mark areas for AI context.", MessageType.Info);
            GUILayout.Space(6);

            // Enable / Disable toggle
            var btnLabel = session.IsAnnotating ? "Stop Annotating" : "Start Annotating";
            var btnColor = session.IsAnnotating ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 1f, 0.6f);
            var oldBg = GUI.backgroundColor;
            GUI.backgroundColor = btnColor;
            if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
            {
                if (session.IsAnnotating) session.DisableAnnotating();
                else session.EnableAnnotating();
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = oldBg;

            GUILayout.Space(8);

            // Tool selector (only when annotating)
            EditorGUI.BeginDisabledGroup(!session.IsAnnotating);

            GUILayout.Label("Tool", EditorStyles.miniBoldLabel);
            var toolIndex = System.Array.IndexOf(Tools, session.CurrentTool);
            var newIndex = GUILayout.SelectionGrid(toolIndex < 0 ? 0 : toolIndex, ToolNames, 2, GUILayout.Height(44));
            if (newIndex != toolIndex)
                session.CurrentTool = Tools[newIndex];

            GUILayout.Space(6);

            // Color
            GUILayout.Label("Color", EditorStyles.miniBoldLabel);
            session.CurrentColor = EditorGUILayout.ColorField(session.CurrentColor);

            GUILayout.Space(4);

            // Line width
            GUILayout.Label("Line Width", EditorStyles.miniBoldLabel);
            session.LineWidth = EditorGUILayout.Slider(session.LineWidth, 1f, 10f);

            // Text label (only when text tool selected)
            if (session.CurrentTool == AnnotationTool.Text)
            {
                GUILayout.Space(4);
                GUILayout.Label("Label Text", EditorStyles.miniBoldLabel);
                session.PendingLabel = EditorGUILayout.TextField(session.PendingLabel);
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            // Undo / Clear
            GUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!session.HasAnnotations && session.ActiveStroke == null);
            if (GUILayout.Button("Undo", GUILayout.Height(26))) session.Undo();
            if (GUILayout.Button("Clear All", GUILayout.Height(26))) session.Clear();
            EditorGUI.EndDisabledGroup();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Capture annotated screenshot
            EditorGUI.BeginDisabledGroup(!session.HasAnnotations);
            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
            if (GUILayout.Button("Capture Annotated Screenshot", GUILayout.Height(30)))
            {
                CaptureAndShowResult(session);
            }
            GUI.backgroundColor = oldBg;
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(4);
            if (session.HasAnnotations)
            {
                var desc = session.BuildDescription();
                EditorGUILayout.HelpBox(desc, MessageType.None);
            }
        }

        private static async void CaptureAndShowResult(AnnotationSession session)
        {
            session.DisableAnnotating();

            var svc = McpBackgroundService.Instance;
            if (svc == null)
            {
                EditorUtility.DisplayDialog("Error", "Background service unavailable.", "OK");
                return;
            }

            var paramsJson = "{\"action\":\"annotated_screenshot\",\"max_resolution\":1024}";
            var result = await svc.ExecuteToolDirectAsync("manage_scene", paramsJson);

            if (!result.success)
            {
                EditorUtility.DisplayDialog("Capture Failed", result.error ?? "Unknown error.", "OK");
                return;
            }

            var desc = session.BuildDescription();
            EditorUtility.DisplayDialog("Annotated Screenshot Ready",
                $"Screenshot captured with annotations.\n\nAnnotation summary:\n{desc}\n\nThe AI can now reference this via the 'annotated_screenshot' MCP tool.", "OK");
        }
    }
}
