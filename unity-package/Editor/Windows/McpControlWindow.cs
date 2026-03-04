using System;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Tools;
using UnityAITools.Editor.Services;
using UnityAITools.Editor.Annotations;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnityAITools.Editor.Windows
{
    /// <summary>
    /// Unity Editor window for controlling the MCP connection.
    /// Access via: Window > Unity AI Tools
    /// </summary>
    public class McpControlWindow : EditorWindow
    {
        private string _serverUrl = "ws://localhost:8091";
        private string _testTypesCsv = "error,warning,log";
        private int _testCount = 50;
        private bool _testIncludeStackTrace = true;
        private int _testFormatIndex = 1; // summary=0, detailed=1
        private bool _isReadConsoleTesting;
        private bool _showReadConsoleDebug;
        private string _readConsoleTestResponse = "No test run yet.";
        private Vector2 _mainScroll;
        private Vector2 _readConsoleResponseScroll;

        [MenuItem("Window/Unity AI Tools")]
        public static void ShowWindow()
        {
            var window = GetWindow<McpControlWindow>();
            window.titleContent = new GUIContent("Unity AI Tools");
            window.minSize = new Vector2(350, 280);
        }

        private void OnEnable()
        {
            _serverUrl = EditorPrefs.GetString("UnityAITools_ServerUrl", "ws://localhost:8091");
            
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged += Repaint;
            }
            else
            {
                EditorApplication.delayCall += () => {
                    if (McpBackgroundService.Instance != null)
                        McpBackgroundService.Instance.OnStatusChanged += Repaint;
                };
            }

            AnnotationSession.Instance.OnChanged += Repaint;
        }

        private void OnDisable()
        {
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged -= Repaint;
            }
            AnnotationSession.Instance.OnChanged -= Repaint;
        }

        private void OnGUI()
        {
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
            GUILayout.Space(10);

            // Header
            var headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
            GUILayout.Label("Unity AI Tools", headerStyle);
            GUILayout.Space(5);

            EditorGUILayout.HelpBox(
                "Connect to the MCP server to enable AI-assisted development.\nThe connection stays alive in the background even if you close this window.",
                MessageType.Info);

            GUILayout.Space(10);

            var svc = McpBackgroundService.Instance;
            if (svc == null)
            {
                EditorGUILayout.HelpBox("Initializing background service...", MessageType.Warning);
                return;
            }

            // Connection status
            var isConnected = svc.Client != null && svc.Client.IsConnected;
            var isConnecting = svc.IsConnecting;
            
            var statusText = isConnected ? "🟢 Connected" : (isConnecting ? "🟡 Connecting..." : "🔴 Disconnected");
            var statusStyle = new GUIStyle(EditorStyles.label) { fontSize = 14 };
            GUILayout.Label($"Status: {statusText}", statusStyle);

            if (isConnected && svc.Client?.SessionId != null)
            {
                EditorGUILayout.LabelField("Session", svc.Client.SessionId);
            }

            GUILayout.Space(10);

            // Server URL
            EditorGUI.BeginChangeCheck();
            _serverUrl = EditorGUILayout.TextField("Server URL", _serverUrl);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetString("UnityAITools_ServerUrl", _serverUrl);
            }

            GUILayout.Space(10);

            // Connect button
            EditorGUI.BeginDisabledGroup(isConnecting || isConnected);
            if (GUILayout.Button("Connect", GUILayout.Height(32)))
            {
                svc.Connect(_serverUrl);
            }
            EditorGUI.EndDisabledGroup();

            // Disconnect button (always enabled if connected, allowed even during connection attempts)
            EditorGUI.BeginDisabledGroup(!isConnected && !isConnecting);
            if (GUILayout.Button("Disconnect", GUILayout.Height(32)))
            {
                svc.Disconnect();
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(10);

            // Annotation section
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            var annotSession = AnnotationSession.Instance;
            var annotLabel = annotSession.IsAnnotating ? "Stop Annotating" : "Annotate Scene";
            var annotBgColor = annotSession.IsAnnotating ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.8f, 1f);
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = annotBgColor;
            if (GUILayout.Button(annotLabel, GUILayout.Height(28)))
            {
                if (annotSession.IsAnnotating)
                    annotSession.DisableAnnotating();
                else
                    annotSession.EnableAnnotating();
                SceneView.RepaintAll();
                AnnotationToolbar.ShowWindow();
            }
            GUI.backgroundColor = prevBg;

            GUILayout.Space(10);

            // MCP Client configuration help
            EditorGUILayout.HelpBox(
                "Configure your MCP client to connect to:\nhttp://localhost:8090/mcp",
                MessageType.None);

            GUILayout.Space(8);
            DrawReadConsoleTestArea(svc);
            EditorGUILayout.EndScrollView();
        }

        private void DrawReadConsoleTestArea(McpBackgroundService svc)
        {
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            _showReadConsoleDebug = EditorGUILayout.Foldout(
                _showReadConsoleDebug,
                "Debug: Read Console Test",
                true);
            if (!_showReadConsoleDebug)
                return;

            EditorGUILayout.HelpBox(
                "Runs read_console locally inside Unity and shows raw JSON response for debugging.",
                MessageType.None);

            _testTypesCsv = EditorGUILayout.TextField("Types (csv)", _testTypesCsv);
            _testCount = EditorGUILayout.IntField("Count", Mathf.Max(1, _testCount));
            _testIncludeStackTrace = EditorGUILayout.Toggle("Include Stacktrace", _testIncludeStackTrace);
            _testFormatIndex = EditorGUILayout.Popup("Format", _testFormatIndex, new[] { "summary", "detailed" });

            EditorGUI.BeginDisabledGroup(_isReadConsoleTesting);
            if (GUILayout.Button(_isReadConsoleTesting ? "Running..." : "Run Read Console Test"))
            {
                _ = RunReadConsoleTestAsync(svc);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Raw Response:");
            _readConsoleResponseScroll = EditorGUILayout.BeginScrollView(_readConsoleResponseScroll, GUILayout.Height(180));
            EditorGUILayout.TextArea(_readConsoleTestResponse, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private async Task RunReadConsoleTestAsync(McpBackgroundService svc)
        {
            _isReadConsoleTesting = true;
            _readConsoleTestResponse = "Running read_console...";
            Repaint();

            try
            {
                var types = ParseTypes(_testTypesCsv);
                var parameters = new Dictionary<string, object>
                {
                    { "types", types },
                    { "count", Mathf.Max(1, _testCount) },
                    { "include_stacktrace", _testIncludeStackTrace },
                    { "format", _testFormatIndex == 1 ? "detailed" : "summary" }
                };

                var paramsJson = MiniJson.Serialize(parameters);
                var result = await svc.ExecuteToolDirectAsync("read_console", paramsJson);

                var payload = new Dictionary<string, object>
                {
                    { "success", result.success },
                    { "error", result.error },
                    { "data", result.data }
                };
                _readConsoleTestResponse = MiniJson.Serialize(payload);
            }
            catch (System.Exception ex)
            {
                _readConsoleTestResponse = $"{{\"success\":false,\"error\":\"{EscapeForJson(ex.Message)}\"}}";
            }
            finally
            {
                _isReadConsoleTesting = false;
                Repaint();
            }
        }

        private static List<object> ParseTypes(string csv)
        {
            var result = new List<object>();
            if (string.IsNullOrWhiteSpace(csv))
                return result;

            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var token = parts[i].Trim().ToLowerInvariant();
                if (token == "log" || token == "warning" || token == "error")
                    result.Add(token);
            }

            return result;
        }

        private static string EscapeForJson(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
