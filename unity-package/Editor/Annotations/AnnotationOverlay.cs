using UnityEditor;
using UnityEngine;

namespace UnityAITools.Editor.Annotations
{
    /// <summary>
    /// Hooks into SceneView.duringSceneGui to render annotation strokes and
    /// capture mouse input when annotation mode is active.
    /// </summary>
    [InitializeOnLoad]
    public static class AnnotationOverlay
    {
        private static bool _hooked;

        static AnnotationOverlay()
        {
            AnnotationSession.Instance.OnChanged += OnSessionChanged;
        }

        private static void OnSessionChanged()
        {
            if (AnnotationSession.Instance.IsAnnotating && !_hooked)
            {
                SceneView.duringSceneGui += OnSceneGUI;
                _hooked = true;
                SceneView.RepaintAll();
            }
            else if (!AnnotationSession.Instance.IsAnnotating && _hooked)
            {
                SceneView.duringSceneGui -= OnSceneGUI;
                _hooked = false;
                SceneView.RepaintAll();
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            var session = AnnotationSession.Instance;
            if (!session.IsAnnotating) return;

            int viewW = (int)sceneView.position.width;
            int viewH = (int)sceneView.position.height;
            session.SetViewSize(viewW, viewH);

            var evt = Event.current;

            // Draw all committed strokes + active stroke
            Handles.BeginGUI();
            DrawAllStrokes(session);
            DrawActiveCursor();
            DrawHUD(sceneView);
            Handles.EndGUI();

            // Input handling
            HandleMouseInput(session, evt, viewW, viewH);
        }

        private static void DrawAllStrokes(AnnotationSession session)
        {
            foreach (var stroke in session.Strokes)
                DrawStroke(stroke);
            if (session.ActiveStroke != null)
                DrawStroke(session.ActiveStroke);
        }

        private static void DrawStroke(AnnotationStroke stroke)
        {
            if (stroke.Points.Count == 0) return;

            GUI.color = stroke.Color;

            switch (stroke.Tool)
            {
                case AnnotationTool.Freehand:
                    for (int i = 1; i < stroke.Points.Count; i++)
                        DrawGUILine(stroke.Points[i - 1], stroke.Points[i], stroke.Color, stroke.LineWidth);
                    break;

                case AnnotationTool.Rectangle:
                {
                    if (stroke.Points.Count < 2) break;
                    var a = stroke.Points[0];
                    var b = stroke.Points[stroke.Points.Count - 1];
                    var rect = new Rect(
                        Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                        Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
                    DrawGUIRect(rect, stroke.Color, stroke.LineWidth);
                    break;
                }

                case AnnotationTool.Arrow:
                {
                    if (stroke.Points.Count < 2) break;
                    var start = stroke.Points[0];
                    var end = stroke.Points[stroke.Points.Count - 1];
                    DrawGUILine(start, end, stroke.Color, stroke.LineWidth);
                    DrawArrowHead(start, end, stroke.Color, stroke.LineWidth);
                    break;
                }

                case AnnotationTool.Text:
                {
                    if (stroke.Points.Count == 0) break;
                    var p = stroke.Points[0];
                    var style = new GUIStyle(EditorStyles.boldLabel)
                    {
                        normal = { textColor = stroke.Color },
                        fontSize = 14,
                    };
                    GUI.Label(new Rect(p.x + 4, p.y - 20, 300, 28), stroke.Label ?? "", style);
                    DrawGUILine(p - new Vector2(6, 0), p + new Vector2(6, 0), stroke.Color, 2f);
                    DrawGUILine(p - new Vector2(0, 6), p + new Vector2(0, 6), stroke.Color, 2f);
                    break;
                }
            }

            GUI.color = Color.white;
        }

        private static void DrawActiveCursor()
        {
            EditorGUIUtility.AddCursorRect(new Rect(0, 0, 9999, 9999), MouseCursor.CustomCursor);
        }

        private static void DrawHUD(SceneView sceneView)
        {
            var session = AnnotationSession.Instance;
            var bg = new Color(0, 0, 0, 0.55f);
            var labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 11,
            };

            var hudRect = new Rect(8, 8, 280, 22);
            EditorGUI.DrawRect(hudRect, bg);
            GUI.Label(new Rect(12, 10, 280, 20),
                $"ANNOTATE  |  Tool: {session.CurrentTool}  |  Ctrl+Z Undo  |  ESC Done",
                labelStyle);
        }

        private static void HandleMouseInput(AnnotationSession session, Event evt, int viewW, int viewH)
        {
            if (evt == null) return;

            // Handle keyboard shortcuts
            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    session.DisableAnnotating();
                    evt.Use();
                    return;
                }
                if (evt.control && evt.keyCode == KeyCode.Z)
                {
                    session.Undo();
                    evt.Use();
                    return;
                }
            }

            if (!session.IsAnnotating) return;

            var mousePos = evt.mousePosition;

            // Text tool: place on click, don't drag
            if (session.CurrentTool == AnnotationTool.Text)
            {
                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    session.BeginStroke(mousePos);
                    session.FinishStroke();
                    evt.Use();
                }
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                return;
            }

            switch (evt.type)
            {
                case EventType.MouseDown when evt.button == 0:
                    GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                    session.BeginStroke(mousePos);
                    evt.Use();
                    break;

                case EventType.MouseDrag when evt.button == 0:
                    session.ContinueStroke(mousePos);
                    evt.Use();
                    break;

                case EventType.MouseUp when evt.button == 0:
                    session.FinishStroke();
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    break;
            }

            // Consume all events to prevent scene navigation during annotation
            if (evt.isMouse || evt.isScrollWheel)
            {
                evt.Use();
            }
        }

        // --- GUI Drawing Helpers ---

        private static void DrawGUILine(Vector2 a, Vector2 b, Color color, float width)
        {
            DrawThickLine(a, b, color, width);
        }

        private static void DrawThickLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            var savedMatrix = GUI.matrix;
            var savedColor = GUI.color;
            GUI.color = color;
            float angle = Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg;
            float length = Vector2.Distance(start, end);
            var pivot = start;
            GUIUtility.RotateAroundPivot(angle, pivot);
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
        }

        private static void DrawGUIRect(Rect rect, Color color, float lineWidth)
        {
            DrawThickLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), color, lineWidth);
            DrawThickLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), color, lineWidth);
            DrawThickLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color, lineWidth);
            DrawThickLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), color, lineWidth);
        }

        private static void DrawArrowHead(Vector2 start, Vector2 end, Color color, float lineWidth)
        {
            var dir = (end - start).normalized;
            if (dir == Vector2.zero) return;
            var perp = new Vector2(-dir.y, dir.x);
            float size = 14f;
            var al = end - dir * size + perp * (size * 0.4f);
            var ar = end - dir * size - perp * (size * 0.4f);
            DrawThickLine(end, al, color, lineWidth);
            DrawThickLine(end, ar, color, lineWidth);
        }
    }
}
