using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityAITools.Editor.Annotations
{
    /// <summary>
    /// Manages the active annotation session: current strokes, undo stack, and serialisation.
    /// Single instance shared between the overlay and toolbar.
    /// </summary>
    public class AnnotationSession
    {
        private static AnnotationSession _instance;
        public static AnnotationSession Instance => _instance ?? (_instance = new AnnotationSession());

        // Current in-progress stroke (while mouse is held)
        public AnnotationStroke ActiveStroke { get; private set; }

        // Completed strokes
        private readonly List<AnnotationStroke> _strokes = new List<AnnotationStroke>();
        public IReadOnlyList<AnnotationStroke> Strokes => _strokes;

        public bool IsAnnotating { get; private set; }
        public AnnotationTool CurrentTool { get; set; } = AnnotationTool.Freehand;
        public Color CurrentColor { get; set; } = new Color(1f, 0.2f, 0.2f, 1f);
        public float LineWidth { get; set; } = 3f;

        // Pending text label for the next Text annotation
        public string PendingLabel { get; set; } = "";

        // Dimensions of the view when annotations were made (for normalising coordinates)
        public int ViewWidth { get; private set; } = 800;
        public int ViewHeight { get; private set; } = 600;

        public delegate void SessionChangedDelegate();
        public event SessionChangedDelegate OnChanged;

        public void SetViewSize(int w, int h)
        {
            if (w > 0) ViewWidth = w;
            if (h > 0) ViewHeight = h;
        }

        public void EnableAnnotating()
        {
            IsAnnotating = true;
            OnChanged?.Invoke();
        }

        public void DisableAnnotating()
        {
            IsAnnotating = false;
            FinishStroke();
            OnChanged?.Invoke();
        }

        public void BeginStroke(Vector2 startPoint)
        {
            ActiveStroke = new AnnotationStroke(CurrentTool, CurrentColor, LineWidth);
            ActiveStroke.Points.Add(startPoint);
            if (CurrentTool == AnnotationTool.Text)
                ActiveStroke.Label = PendingLabel;
        }

        public void ContinueStroke(Vector2 point)
        {
            if (ActiveStroke == null) return;
            // For freehand sample every point; for rect/arrow only keep start+end
            if (ActiveStroke.Tool == AnnotationTool.Freehand)
            {
                var last = ActiveStroke.Points[ActiveStroke.Points.Count - 1];
                if (Vector2.Distance(last, point) > 2f)
                    ActiveStroke.Points.Add(point);
            }
            else
            {
                if (ActiveStroke.Points.Count > 1)
                    ActiveStroke.Points[ActiveStroke.Points.Count - 1] = point;
                else
                    ActiveStroke.Points.Add(point);
            }
            OnChanged?.Invoke();
        }

        public void FinishStroke()
        {
            if (ActiveStroke == null) return;
            if (ActiveStroke.Points.Count > 0)
                _strokes.Add(ActiveStroke);
            ActiveStroke = null;
            OnChanged?.Invoke();
        }

        public void Undo()
        {
            if (ActiveStroke != null)
            {
                ActiveStroke = null;
            }
            else if (_strokes.Count > 0)
            {
                _strokes.RemoveAt(_strokes.Count - 1);
            }
            OnChanged?.Invoke();
        }

        public void Clear()
        {
            _strokes.Clear();
            ActiveStroke = null;
            OnChanged?.Invoke();
        }

        public bool HasAnnotations => _strokes.Count > 0;

        /// <summary>
        /// Returns a human-readable description of all annotations for LLM context.
        /// </summary>
        public string BuildDescription()
        {
            if (_strokes.Count == 0) return "No annotations.";
            var sb = new StringBuilder();
            sb.AppendLine($"User annotations on a {ViewWidth}x{ViewHeight} view:");
            for (int i = 0; i < _strokes.Count; i++)
            {
                var desc = _strokes[i].Describe(i + 1, ViewWidth, ViewHeight);
                if (!string.IsNullOrEmpty(desc))
                    sb.AppendLine(desc);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Renders all strokes onto a transparent RGBA32 Texture2D the same size as the view.
        /// Caller is responsible for destroying the returned texture.
        /// </summary>
        public Texture2D BakeToTexture()
        {
            var tex = new Texture2D(ViewWidth, ViewHeight, TextureFormat.RGBA32, false);
            // Fill transparent
            var pixels = new Color32[ViewWidth * ViewHeight];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(0, 0, 0, 0);
            tex.SetPixels32(pixels);

            foreach (var stroke in _strokes)
                DrawStrokeOnTexture(tex, stroke);

            tex.Apply();
            return tex;
        }

        private void DrawStrokeOnTexture(Texture2D tex, AnnotationStroke stroke)
        {
            if (stroke.Points.Count == 0) return;
            int w = tex.width, h = tex.height;
            Color32 c = stroke.Color;
            int lw = Mathf.Max(1, Mathf.RoundToInt(stroke.LineWidth));

            switch (stroke.Tool)
            {
                case AnnotationTool.Freehand:
                    for (int i = 1; i < stroke.Points.Count; i++)
                        DrawLineOnTexture(tex, stroke.Points[i - 1], stroke.Points[i], c, lw, w, h);
                    break;

                case AnnotationTool.Rectangle:
                {
                    if (stroke.Points.Count < 2) break;
                    var a = stroke.Points[0];
                    var b = stroke.Points[stroke.Points.Count - 1];
                    var tl = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
                    var br = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                    var tr = new Vector2(br.x, tl.y);
                    var bl = new Vector2(tl.x, br.y);
                    DrawLineOnTexture(tex, tl, tr, c, lw, w, h);
                    DrawLineOnTexture(tex, tr, br, c, lw, w, h);
                    DrawLineOnTexture(tex, br, bl, c, lw, w, h);
                    DrawLineOnTexture(tex, bl, tl, c, lw, w, h);
                    break;
                }

                case AnnotationTool.Arrow:
                {
                    if (stroke.Points.Count < 2) break;
                    var start = stroke.Points[0];
                    var end = stroke.Points[stroke.Points.Count - 1];
                    DrawLineOnTexture(tex, start, end, c, lw, w, h);
                    // Arrowhead
                    var dir = (end - start).normalized;
                    var perp = new Vector2(-dir.y, dir.x);
                    float arrowSize = 14f;
                    var al = end - dir * arrowSize + perp * (arrowSize * 0.4f);
                    var ar = end - dir * arrowSize - perp * (arrowSize * 0.4f);
                    DrawLineOnTexture(tex, end, al, c, lw, w, h);
                    DrawLineOnTexture(tex, end, ar, c, lw, w, h);
                    break;
                }

                case AnnotationTool.Text:
                    // Text labels are rendered in the GUI overlay, not baked as pixels.
                    // We draw a small cross marker at the anchor point.
                    if (stroke.Points.Count > 0)
                    {
                        var p = stroke.Points[0];
                        float cs = 8f;
                        DrawLineOnTexture(tex, p - new Vector2(cs, 0), p + new Vector2(cs, 0), c, lw, w, h);
                        DrawLineOnTexture(tex, p - new Vector2(0, cs), p + new Vector2(0, cs), c, lw, w, h);
                    }
                    break;
            }
        }

        // Bresenham-style thick line using pixel painting
        private static void DrawLineOnTexture(Texture2D tex, Vector2 a, Vector2 b, Color32 color, int thickness, int w, int h)
        {
            int x0 = Mathf.RoundToInt(a.x), y0 = h - 1 - Mathf.RoundToInt(a.y);
            int x1 = Mathf.RoundToInt(b.x), y1 = h - 1 - Mathf.RoundToInt(b.y);
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy, half = thickness / 2;

            while (true)
            {
                for (int ty = -half; ty <= half; ty++)
                    for (int tx = -half; tx <= half; tx++)
                    {
                        int px = x0 + tx, py = y0 + ty;
                        if (px >= 0 && px < w && py >= 0 && py < h)
                            tex.SetPixel(px, py, color);
                    }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }
    }
}
