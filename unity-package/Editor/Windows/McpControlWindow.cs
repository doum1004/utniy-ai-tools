using System;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Tools;
using UnityAITools.Editor.Services;
using UnityAITools.Editor.Annotations;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UnityAITools.Editor.Windows
{
    /// <summary>
    /// Unity Editor window for controlling the MCP connection.
    /// Access via: Window > Unity AI Tools
    /// </summary>
    public class McpControlWindow : EditorWindow
    {
        private const string UxmlPath = "Packages/com.unity-ai-tools/Editor/Windows/McpControlWindow.uxml";
        private const string UssPath = "Packages/com.unity-ai-tools/Editor/Windows/McpControlWindow.uss";

        private string _serverUrl = "ws://localhost:8091";
        private string _testTypesCsv = "error,warning,log";
        private int _testCount = 50;
        private bool _testIncludeStackTrace = true;
        private int _testFormatIndex = 1; // summary=0, detailed=1
        private bool _isReadConsoleTesting;
        private bool _showReadConsoleDebug;
        private string _readConsoleTestResponse = "No test run yet.";

        private Label _statusLabel;
        private Label _sessionValueLabel;
        private TextField _serverUrlField;
        private Button _connectButton;
        private Button _disconnectButton;
        private Button _annotateButton;
        private Foldout _debugFoldout;
        private TextField _typesField;
        private IntegerField _countField;
        private Toggle _includeStackTraceToggle;
        private DropdownField _formatField;
        private Button _runReadConsoleTestButton;
        private TextField _readConsoleResponseField;

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
                McpBackgroundService.Instance.OnStatusChanged += OnExternalStateChanged;
            }
            else
            {
                EditorApplication.delayCall += () => {
                    if (McpBackgroundService.Instance != null)
                        McpBackgroundService.Instance.OnStatusChanged += OnExternalStateChanged;
                };
            }

            AnnotationSession.Instance.OnChanged += OnExternalStateChanged;
        }

        private void OnDisable()
        {
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged -= OnExternalStateChanged;
            }
            AnnotationSession.Instance.OnChanged -= OnExternalStateChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            BuildUiFromTemplate();
            RefreshUi();
        }

        private void BuildUiFromTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                rootVisualElement.Add(new HelpBox(
                    $"Missing UI template at: {UxmlPath}",
                    HelpBoxMessageType.Error));
                return;
            }

            visualTree.CloneTree(rootVisualElement);
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null && !rootVisualElement.styleSheets.Contains(styleSheet))
                rootVisualElement.styleSheets.Add(styleSheet);

            BuildHelpBoxes();
            BindElements();
            BindCallbacks();
        }

        private void BuildHelpBoxes()
        {
            var introContainer = rootVisualElement.Q<VisualElement>("intro-help-container");
            var configContainer = rootVisualElement.Q<VisualElement>("config-help-container");
            var debugContainer = rootVisualElement.Q<VisualElement>("debug-help-container");

            if (introContainer != null)
            {
                introContainer.Clear();
                introContainer.Add(new HelpBox(
                    "Connect to the MCP server to enable AI-assisted development.\nThe connection stays alive in the background even if you close this window.",
                    HelpBoxMessageType.Info));
            }

            if (configContainer != null)
            {
                configContainer.Clear();
                configContainer.Add(new HelpBox(
                    "Configure your MCP client to connect to:\nhttp://localhost:8090/mcp",
                    HelpBoxMessageType.None));
            }

            if (debugContainer != null)
            {
                debugContainer.Clear();
                debugContainer.Add(new HelpBox(
                    "Runs read_console locally inside Unity and shows raw JSON response for debugging.",
                    HelpBoxMessageType.None));
            }
        }

        private void BindElements()
        {
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _sessionValueLabel = rootVisualElement.Q<Label>("session-value-label");
            _serverUrlField = rootVisualElement.Q<TextField>("server-url-field");
            _connectButton = rootVisualElement.Q<Button>("connect-button");
            _disconnectButton = rootVisualElement.Q<Button>("disconnect-button");
            _annotateButton = rootVisualElement.Q<Button>("annotate-button");
            _debugFoldout = rootVisualElement.Q<Foldout>("debug-foldout");
            _typesField = rootVisualElement.Q<TextField>("types-field");
            _countField = rootVisualElement.Q<IntegerField>("count-field");
            _includeStackTraceToggle = rootVisualElement.Q<Toggle>("include-stacktrace-toggle");
            _formatField = rootVisualElement.Q<DropdownField>("format-field");
            _runReadConsoleTestButton = rootVisualElement.Q<Button>("run-read-console-button");
            _readConsoleResponseField = rootVisualElement.Q<TextField>("read-console-response-field");

            if (_serverUrlField != null)
                _serverUrlField.value = _serverUrl;
            if (_debugFoldout != null)
                _debugFoldout.value = _showReadConsoleDebug;
            if (_typesField != null)
                _typesField.value = _testTypesCsv;
            if (_countField != null)
                _countField.value = _testCount;
            if (_includeStackTraceToggle != null)
                _includeStackTraceToggle.value = _testIncludeStackTrace;
            if (_formatField != null)
            {
                _formatField.choices = new List<string> { "summary", "detailed" };
                _formatField.index = Mathf.Clamp(_testFormatIndex, 0, _formatField.choices.Count - 1);
            }
            if (_readConsoleResponseField != null)
            {
                _readConsoleResponseField.multiline = true;
                _readConsoleResponseField.isReadOnly = true;
                _readConsoleResponseField.value = _readConsoleTestResponse;
            }
        }

        private void BindCallbacks()
        {
            if (_serverUrlField != null)
            {
                _serverUrlField.RegisterValueChangedCallback(evt =>
                {
                    _serverUrl = evt.newValue;
                    EditorPrefs.SetString("UnityAITools_ServerUrl", _serverUrl);
                });
            }

            if (_connectButton != null)
                _connectButton.clicked += OnConnectClicked;
            if (_disconnectButton != null)
                _disconnectButton.clicked += OnDisconnectClicked;
            if (_annotateButton != null)
                _annotateButton.clicked += OnAnnotateClicked;
            if (_debugFoldout != null)
                _debugFoldout.RegisterValueChangedCallback(evt => _showReadConsoleDebug = evt.newValue);
            if (_typesField != null)
                _typesField.RegisterValueChangedCallback(evt => _testTypesCsv = evt.newValue);
            if (_countField != null)
                _countField.RegisterValueChangedCallback(evt => _testCount = Mathf.Max(1, evt.newValue));
            if (_includeStackTraceToggle != null)
                _includeStackTraceToggle.RegisterValueChangedCallback(evt => _testIncludeStackTrace = evt.newValue);
            if (_formatField != null)
                _formatField.RegisterValueChangedCallback(evt => _testFormatIndex = evt.newValue == "detailed" ? 1 : 0);
            if (_runReadConsoleTestButton != null)
                _runReadConsoleTestButton.clicked += () => _ = RunReadConsoleTestAsync();
        }

        private void OnExternalStateChanged()
        {
            RefreshUi();
        }

        private void RefreshUi()
        {
            var svc = McpBackgroundService.Instance;
            if (svc == null)
            {
                if (_statusLabel != null) _statusLabel.text = "Status: Initializing background service...";
                if (_sessionValueLabel != null) _sessionValueLabel.text = "-";
                if (_connectButton != null) _connectButton.SetEnabled(false);
                if (_disconnectButton != null) _disconnectButton.SetEnabled(false);
                if (_annotateButton != null) _annotateButton.SetEnabled(false);
                return;
            }

            var isConnected = svc.Client != null && svc.Client.IsConnected;
            var isConnecting = svc.IsConnecting;
            var statusText = isConnected ? "Connected" : (isConnecting ? "Connecting..." : "Disconnected");

            if (_statusLabel != null)
                _statusLabel.text = $"Status: {statusText}";
            if (_sessionValueLabel != null)
                _sessionValueLabel.text = isConnected && svc.Client?.SessionId != null ? svc.Client.SessionId : "-";
            if (_connectButton != null)
                _connectButton.SetEnabled(!isConnecting && !isConnected);
            if (_disconnectButton != null)
                _disconnectButton.SetEnabled(isConnected || isConnecting);
            if (_annotateButton != null)
            {
                _annotateButton.SetEnabled(true);
                _annotateButton.text = AnnotationSession.Instance.IsAnnotating ? "Stop Annotating" : "Annotate Scene";
            }
        }

        private void OnConnectClicked()
        {
            var svc = McpBackgroundService.Instance;
            if (svc == null) return;
            svc.Connect(_serverUrlField != null ? _serverUrlField.value : _serverUrl);
            RefreshUi();
        }

        private void OnDisconnectClicked()
        {
            var svc = McpBackgroundService.Instance;
            if (svc == null) return;
            svc.Disconnect();
            RefreshUi();
        }

        private void OnAnnotateClicked()
        {
            var annotSession = AnnotationSession.Instance;
            if (annotSession.IsAnnotating)
                annotSession.DisableAnnotating();
            else
                annotSession.EnableAnnotating();

            SceneView.RepaintAll();
            AnnotationToolbar.ShowWindow();
            RefreshUi();
        }

        private async Task RunReadConsoleTestAsync()
        {
            _isReadConsoleTesting = true;
            _readConsoleTestResponse = "Running read_console...";
            UpdateDebugResponseText();
            if (_runReadConsoleTestButton != null)
            {
                _runReadConsoleTestButton.text = "Running...";
                _runReadConsoleTestButton.SetEnabled(false);
            }

            try
            {
                var svc = McpBackgroundService.Instance;
                if (svc == null)
                    throw new InvalidOperationException("Background service is not initialized.");

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
                if (_runReadConsoleTestButton != null)
                {
                    _runReadConsoleTestButton.text = "Run Read Console Test";
                    _runReadConsoleTestButton.SetEnabled(true);
                }
                UpdateDebugResponseText();
            }
        }

        private void UpdateDebugResponseText()
        {
            if (_readConsoleResponseField != null)
                _readConsoleResponseField.value = _readConsoleTestResponse;
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
