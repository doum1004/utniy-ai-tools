using System.Collections.Generic;
using UnityEngine;

namespace UnityAITools.Editor.Annotations
{
    public enum AnnotationTool { Freehand, Rectangle, Arrow, Text }

    /// <summary>
    /// A single annotation stroke or shape drawn by the user.
    /// </summary>
    [System.Serializable]
    public class AnnotationStroke
    {
        public AnnotationTool Tool;
        public Color Color;
        public float LineWidth;

        /// <summary>
        /// For Freehand: all sampled points.
        /// For Rectangle: [topLeft, bottomRight] in screen space.
        /// For Arrow: [start, end] in screen space.
        /// For Text: [position] in screen space.
        /// </summary>
        public List<Vector2> Points = new List<Vector2>();

        /// <summary>Text content for Text annotations.</summary>
        public string Label;

        public AnnotationStroke(AnnotationTool tool, Color color, float lineWidth)
        {
            Tool = tool;
            Color = color;
            LineWidth = lineWidth;
        }

        /// <summary>Human-readable description for LLM context.</summary>
        public string Describe(int index, int viewWidth, int viewHeight)
        {
            if (Points.Count == 0) return string.Empty;
            switch (Tool)
            {
                case AnnotationTool.Rectangle:
                {
                    var a = Points[0];
                    var b = Points.Count > 1 ? Points[Points.Count - 1] : Points[0];
                    var xMin = Mathf.Min(a.x, b.x);
                    var yMin = Mathf.Min(a.y, b.y);
                    var w = Mathf.Abs(b.x - a.x);
                    var h = Mathf.Abs(b.y - a.y);
                    var cx = (xMin + w * 0.5f) / viewWidth;
                    var cy = (yMin + h * 0.5f) / viewHeight;
                    return $"[{index}] Rectangle at ({cx:F2}, {cy:F2}) normalized center, size {w:F0}x{h:F0}px";
                }
                case AnnotationTool.Arrow:
                {
                    var s = Points[0];
                    var e = Points.Count > 1 ? Points[Points.Count - 1] : Points[0];
                    return $"[{index}] Arrow from ({s.x / viewWidth:F2}, {s.y / viewHeight:F2}) to ({e.x / viewWidth:F2}, {e.y / viewHeight:F2}) (normalized)";
                }
                case AnnotationTool.Text:
                {
                    var p = Points[0];
                    return $"[{index}] Label \"{Label}\" at ({p.x / viewWidth:F2}, {p.y / viewHeight:F2}) (normalized)";
                }
                case AnnotationTool.Freehand:
                {
                    // bounding box center
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    foreach (var pt in Points)
                    {
                        if (pt.x < minX) minX = pt.x;
                        if (pt.y < minY) minY = pt.y;
                        if (pt.x > maxX) maxX = pt.x;
                        if (pt.y > maxY) maxY = pt.y;
                    }
                    var cx = ((minX + maxX) * 0.5f) / viewWidth;
                    var cy = ((minY + maxY) * 0.5f) / viewHeight;
                    return $"[{index}] Freehand mark around ({cx:F2}, {cy:F2}) normalized center";
                }
                default:
                    return string.Empty;
            }
        }
    }
}
