using System;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Tools;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Windows
{
    /// <summary>
    /// Unity Editor window for controlling the MCP connection.
    /// Access via: Window > Unity AI Tools
    /// </summary>
    public class McpControlWindow : EditorWindow
    {
        private string _serverUrl = "ws://localhost:8091";

        [MenuItem("Window/Unity AI Tools")]
        public static void ShowWindow()
        {
            var window = GetWindow<McpControlWindow>();
            window.titleContent = new GUIContent("Unity AI Tools");
            window.minSize = new Vector2(350, 220);
        }

        private void OnEnable()
        {
            // Load saved URL if any
            _serverUrl = EditorPrefs.GetString("UnityAITools_ServerUrl", "ws://localhost:8091");
            
            // Subscribe to status changes from the background service
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged += Repaint;
            }
            else
            {
                // Edge case: if window opens before service init, delay subscribe
                EditorApplication.delayCall += () => {
                    if (McpBackgroundService.Instance != null)
                        McpBackgroundService.Instance.OnStatusChanged += Repaint;
                };
            }
        }

        private void OnDisable()
        {
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged -= Repaint;
            }
        }

        private void OnGUI()
        {
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

            // MCP Client configuration help
            EditorGUILayout.HelpBox(
                "Configure your MCP client to connect to:\nhttp://localhost:8090/mcp",
                MessageType.None);
        }
    }
}
