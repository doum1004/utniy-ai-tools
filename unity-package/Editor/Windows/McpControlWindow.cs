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

        // Error display
        private Label _lastErrorLabel;
        private string _lastError = "";

        // Server process UI
        private Foldout _serverFoldout;
        private Label _serverStatusLabel;
        private TextField _serverExecutableField;
        private Button _startServerButton;
        private Button _stopServerButton;

        // Connection UI
        private Foldout _connectionFoldout;
        private Label _statusLabel;
        private Label _sessionValueLabel;
        private TextField _serverUrlField;
        private Button _connectButton;
        private Button _disconnectButton;
        private Foldout _toolsFoldout;
        private Button _annotateButton;
        private TextField _snapshotNoteField;
        private Button _snapshotCreateButton;
        private Button _snapshotRevertButton;
        private Label _snapshotStatusLabel;
        // Preferences UI
        private Foldout _preferencesFoldout;
        private Toggle _autoConnectToggle;

        private Foldout _debugFoldout;
        private TextField _typesField;
        private IntegerField _countField;
        private Toggle _includeStackTraceToggle;
        private DropdownField _formatField;
        private Button _runReadConsoleTestButton;
        private TextField _readConsoleResponseField;

        [MenuItem("Window/Unity AI Tools/Control Panel")]
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
                if (McpBackgroundService.Instance.Client != null)
                    McpBackgroundService.Instance.Client.OnError += OnConnectionError;
            }
            else
            {
                EditorApplication.delayCall += () => {
                    if (McpBackgroundService.Instance != null)
                    {
                        McpBackgroundService.Instance.OnStatusChanged += OnExternalStateChanged;
                        if (McpBackgroundService.Instance.Client != null)
                            McpBackgroundService.Instance.Client.OnError += OnConnectionError;
                    }
                };
            }

            if (ServerProcessManager.Instance != null)
            {
                ServerProcessManager.Instance.OnStatusChanged += OnExternalStateChanged;
            }
            else
            {
                EditorApplication.delayCall += () => {
                    if (ServerProcessManager.Instance != null)
                        ServerProcessManager.Instance.OnStatusChanged += OnExternalStateChanged;
                };
            }

            AnnotationSession.Instance.OnChanged += OnExternalStateChanged;
        }

        private void OnDisable()
        {
            if (McpBackgroundService.Instance != null)
            {
                McpBackgroundService.Instance.OnStatusChanged -= OnExternalStateChanged;
                if (McpBackgroundService.Instance.Client != null)
                    McpBackgroundService.Instance.Client.OnError -= OnConnectionError;
            }
            if (ServerProcessManager.Instance != null)
            {
                ServerProcessManager.Instance.OnStatusChanged -= OnExternalStateChanged;
            }
            AnnotationSession.Instance.OnChanged -= OnExternalStateChanged;
        }

        private void OnConnectionError(string error)
        {
            _lastError = error;
            RefreshUi();
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
            var debugContainer = rootVisualElement.Q<VisualElement>("debug-help-container");

            if (introContainer != null)
            {
                introContainer.Clear();
                introContainer.Add(new HelpBox(
                    "Connect to the MCP server to enable AI-assisted development.\nThe connection stays alive in the background even if you close this window.",
                    HelpBoxMessageType.Info));
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
            // Error label
            _lastErrorLabel = rootVisualElement.Q<Label>("last-error-label");

            // Server process elements
            _serverFoldout = rootVisualElement.Q<Foldout>("server-foldout");
            if (_serverFoldout != null)
                _serverFoldout.value = EditorPrefs.GetBool("UnityAITools_Foldout_Server", false);
            _serverStatusLabel = rootVisualElement.Q<Label>("server-status-label");
            _serverExecutableField = rootVisualElement.Q<TextField>("server-executable-field");
            _startServerButton = rootVisualElement.Q<Button>("start-server-button");
            _stopServerButton = rootVisualElement.Q<Button>("stop-server-button");

            // Connection elements
            _connectionFoldout = rootVisualElement.Q<Foldout>("connection-foldout");
            if (_connectionFoldout != null)
                _connectionFoldout.value = EditorPrefs.GetBool("UnityAITools_Foldout_Connection", false);
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _sessionValueLabel = rootVisualElement.Q<Label>("session-value-label");
            _serverUrlField = rootVisualElement.Q<TextField>("server-url-field");
            _connectButton = rootVisualElement.Q<Button>("connect-button");
            _disconnectButton = rootVisualElement.Q<Button>("disconnect-button");
            _toolsFoldout = rootVisualElement.Q<Foldout>("tools-foldout");
            if (_toolsFoldout != null)
                _toolsFoldout.value = EditorPrefs.GetBool("UnityAITools_Foldout_Tools", false);
            _annotateButton = rootVisualElement.Q<Button>("annotate-button");
            _snapshotNoteField = rootVisualElement.Q<TextField>("snapshot-note-field");
            _snapshotCreateButton = rootVisualElement.Q<Button>("snapshot-create-button");
            _snapshotRevertButton = rootVisualElement.Q<Button>("snapshot-revert-button");
            _snapshotStatusLabel = rootVisualElement.Q<Label>("snapshot-status-label");
            // Preferences elements
            _preferencesFoldout = rootVisualElement.Q<Foldout>("preferences-foldout");
            if (_preferencesFoldout != null)
                _preferencesFoldout.value = EditorPrefs.GetBool("UnityAITools_Foldout_Preferences", false);
            _autoConnectToggle = rootVisualElement.Q<Toggle>("auto-connect-toggle");
            if (_autoConnectToggle != null)
                _autoConnectToggle.value = McpBackgroundService.AutoConnect;

            _debugFoldout = rootVisualElement.Q<Foldout>("debug-foldout");
            _typesField = rootVisualElement.Q<TextField>("types-field");
            _countField = rootVisualElement.Q<IntegerField>("count-field");
            _includeStackTraceToggle = rootVisualElement.Q<Toggle>("include-stacktrace-toggle");
            _formatField = rootVisualElement.Q<DropdownField>("format-field");
            _runReadConsoleTestButton = rootVisualElement.Q<Button>("run-read-console-button");
            _readConsoleResponseField = rootVisualElement.Q<TextField>("read-console-response-field");

            // Server executable field
            if (_serverExecutableField != null)
            {
                var spm = ServerProcessManager.Instance;
                _serverExecutableField.value = spm != null ? spm.GetExecutablePath() : "";
            }

            // Connection fields
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
            // Server process callbacks
            if (_serverFoldout != null)
                _serverFoldout.RegisterValueChangedCallback(evt => EditorPrefs.SetBool("UnityAITools_Foldout_Server", evt.newValue));
            if (_serverExecutableField != null)
            {
                _serverExecutableField.RegisterValueChangedCallback(evt =>
                {
                    ServerProcessManager.Instance?.SetExecutablePath(evt.newValue);
                });
            }
            if (_startServerButton != null)
                _startServerButton.clicked += OnStartServerClicked;
            if (_stopServerButton != null)
                _stopServerButton.clicked += OnStopServerClicked;

            // Connection callbacks
            if (_connectionFoldout != null)
                _connectionFoldout.RegisterValueChangedCallback(evt => EditorPrefs.SetBool("UnityAITools_Foldout_Connection", evt.newValue));
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
            if (_toolsFoldout != null)
                _toolsFoldout.RegisterValueChangedCallback(evt => EditorPrefs.SetBool("UnityAITools_Foldout_Tools", evt.newValue));
            if (_annotateButton != null)
                _annotateButton.clicked += OnAnnotateClicked;
            if (_snapshotCreateButton != null)
                _snapshotCreateButton.clicked += () => _ = OnSnapshotCreateClicked();
            if (_snapshotRevertButton != null)
                _snapshotRevertButton.clicked += () => _ = OnSnapshotRevertClicked();
            // Preferences callbacks
            if (_preferencesFoldout != null)
                _preferencesFoldout.RegisterValueChangedCallback(evt => EditorPrefs.SetBool("UnityAITools_Foldout_Preferences", evt.newValue));
            if (_autoConnectToggle != null)
                _autoConnectToggle.RegisterValueChangedCallback(evt => McpBackgroundService.AutoConnect = evt.newValue);

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
            RefreshErrorUi();
            RefreshServerUi();
            RefreshConnectionUi();
            RefreshSnapshotUi();
        }

        private void RefreshErrorUi()
        {
            if (_lastErrorLabel == null) return;

            if (string.IsNullOrEmpty(_lastError))
            {
                _lastErrorLabel.text = "";
                _lastErrorLabel.RemoveFromClassList("mcp-last-error--visible");
            }
            else
            {
                _lastErrorLabel.text = _lastError;
                _lastErrorLabel.AddToClassList("mcp-last-error--visible");
            }

            // Clear error when connection succeeds
            var svc = McpBackgroundService.Instance;
            if (svc?.Client != null && svc.Client.IsConnected)
                _lastError = "";
        }

        private void RefreshServerUi()
        {
            var spm = ServerProcessManager.Instance;
            if (spm == null)
            {
                if (_serverStatusLabel != null) _serverStatusLabel.text = "Initializing...";
                if (_startServerButton != null) _startServerButton.SetEnabled(false);
                if (_stopServerButton != null) _stopServerButton.SetEnabled(false);
                return;
            }

            var status = spm.Status;
            if (_serverStatusLabel != null)
            {
                _serverStatusLabel.RemoveFromClassList("mcp-server-status-running");
                _serverStatusLabel.RemoveFromClassList("mcp-server-status-stopped");
                _serverStatusLabel.RemoveFromClassList("mcp-server-status-unknown");

                switch (status)
                {
                    case ServerProcessManager.ServerStatus.Running:
                        _serverStatusLabel.text = spm.IsManagedProcess ? "Running (managed)" : "Running (external)";
                        _serverStatusLabel.AddToClassList("mcp-server-status-running");
                        break;
                    case ServerProcessManager.ServerStatus.Stopped:
                        _serverStatusLabel.text = "Not Running";
                        _serverStatusLabel.AddToClassList("mcp-server-status-stopped");
                        break;
                    default:
                        _serverStatusLabel.text = "Checking...";
                        _serverStatusLabel.AddToClassList("mcp-server-status-unknown");
                        break;
                }
            }

            if (_serverExecutableField != null)
            {
                var currentPath = spm.GetExecutablePath();
                if (_serverExecutableField.value != currentPath)
                    _serverExecutableField.SetValueWithoutNotify(currentPath);

                var isValid = !string.IsNullOrEmpty(currentPath) && System.IO.File.Exists(currentPath);
                _serverExecutableField.style.color = isValid || string.IsNullOrEmpty(currentPath)
                    ? StyleKeyword.Null
                    : new StyleColor(new Color(0.86f, 0.31f, 0.31f));
            }

            var executableExists = !string.IsNullOrEmpty(spm.GetExecutablePath()) &&
                                   System.IO.File.Exists(spm.GetExecutablePath());
            if (_startServerButton != null)
                _startServerButton.SetEnabled(status != ServerProcessManager.ServerStatus.Running && executableExists);
            if (_stopServerButton != null)
                _stopServerButton.SetEnabled(spm.IsManagedProcess);
        }

        private void RefreshConnectionUi()
        {
            var svc = McpBackgroundService.Instance;
            if (svc == null)
            {
                if (_statusLabel != null) _statusLabel.text = "Initializing...";
                if (_sessionValueLabel != null) _sessionValueLabel.text = "-";
                if (_connectButton != null) _connectButton.SetEnabled(false);
                if (_disconnectButton != null) _disconnectButton.SetEnabled(false);
                if (_annotateButton != null) _annotateButton.SetEnabled(false);
                return;
            }

            var isConnected = svc.Client != null && svc.Client.IsConnected;
            var isConnecting = svc.IsConnecting;

            if (_statusLabel != null)
            {
                _statusLabel.RemoveFromClassList("mcp-server-status-running");
                _statusLabel.RemoveFromClassList("mcp-server-status-stopped");
                _statusLabel.RemoveFromClassList("mcp-server-status-unknown");

                if (isConnected)
                {
                    _statusLabel.text = "Connected";
                    _statusLabel.AddToClassList("mcp-server-status-running");
                }
                else if (isConnecting)
                {
                    _statusLabel.text = "Connecting...";
                    _statusLabel.AddToClassList("mcp-server-status-unknown");
                }
                else
                {
                    _statusLabel.text = "Disconnected";
                    _statusLabel.AddToClassList("mcp-server-status-stopped");
                }
            }

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

        private void OnStartServerClicked()
        {
            var spm = ServerProcessManager.Instance;
            if (spm == null) return;

            if (spm.StartServer())
            {
                // Auto-connect to the WebSocket once the server is ready
                EditorApplication.delayCall += () => _ = AutoConnectAfterServerStartAsync();
            }
            RefreshUi();
        }

        private async Task AutoConnectAfterServerStartAsync()
        {
            var svc = McpBackgroundService.Instance;
            var spm = ServerProcessManager.Instance;
            if (svc == null || spm == null) return;
            if (svc.Client != null && svc.Client.IsConnected) return;

            // Wait for the server to become responsive (up to 10 seconds)
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(500);
                if (spm.Status == ServerProcessManager.ServerStatus.Running)
                {
                    svc.Connect(_serverUrlField != null ? _serverUrlField.value : _serverUrl);
                    RefreshUi();
                    return;
                }
            }
            Debug.LogWarning("[UnityAITools] Server did not become responsive in time. Try connecting manually.");
        }

        private void OnStopServerClicked()
        {
            var spm = ServerProcessManager.Instance;
            if (spm == null) return;

            var svc = McpBackgroundService.Instance;
            svc?.Disconnect();

            spm.StopServer();
            RefreshUi();
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

        private async Task OnSnapshotCreateClicked()
        {
            var mgr = SnapshotManager.Instance;
            if (mgr == null) return;

            if (_snapshotCreateButton != null) _snapshotCreateButton.SetEnabled(false);
            if (_snapshotStatusLabel != null) _snapshotStatusLabel.text = "Creating snapshot...";

            var note = _snapshotNoteField?.value;
            var (success, message) = await mgr.CreateSnapshotAsync(string.IsNullOrWhiteSpace(note) ? null : note.Trim());

            if (_snapshotStatusLabel != null)
            {
                if (success)
                {
                    var status = $"Snapshot: {mgr.LastSnapshotTime} ({(mgr.IsGitRepo ? "git" : "file")})";
                    if (!string.IsNullOrEmpty(mgr.LastSnapshotNote))
                        status += $" — {mgr.LastSnapshotNote}";
                    _snapshotStatusLabel.text = status;
                }
                else
                {
                    _snapshotStatusLabel.text = message;
                }
            }
            if (_snapshotCreateButton != null) _snapshotCreateButton.SetEnabled(true);
            RefreshSnapshotUi();
        }

        private async Task OnSnapshotRevertClicked()
        {
            var mgr = SnapshotManager.Instance;
            if (mgr == null || !mgr.HasSnapshot) return;

            if (!EditorUtility.DisplayDialog("Revert Snapshot",
                "This will revert all changes since the last snapshot. Continue?",
                "Revert", "Cancel"))
                return;

            if (_snapshotRevertButton != null) _snapshotRevertButton.SetEnabled(false);
            if (_snapshotStatusLabel != null) _snapshotStatusLabel.text = "Reverting...";

            var (success, message) = await mgr.RevertToSnapshotAsync();

            if (success && !mgr.IsGitRepo)
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (_snapshotStatusLabel != null)
                _snapshotStatusLabel.text = success ? "Reverted successfully" : message;
            if (_snapshotRevertButton != null) _snapshotRevertButton.SetEnabled(true);
            RefreshSnapshotUi();
        }

        private void RefreshSnapshotUi()
        {
            var mgr = SnapshotManager.Instance;
            if (_snapshotRevertButton != null)
                _snapshotRevertButton.SetEnabled(mgr != null && mgr.HasSnapshot);

            if (_snapshotStatusLabel != null && mgr != null)
            {
                if (mgr.HasSnapshot)
                {
                    var status = $"Snapshot: {mgr.LastSnapshotTime} ({(mgr.IsGitRepo ? "git" : "file")})";
                    if (!string.IsNullOrEmpty(mgr.LastSnapshotNote))
                        status += $" — {mgr.LastSnapshotNote}";
                    _snapshotStatusLabel.text = status;
                }
                else if (string.IsNullOrEmpty(_snapshotStatusLabel.text) ||
                         _snapshotStatusLabel.text.StartsWith("Snapshot:"))
                {
                    _snapshotStatusLabel.text = "";
                }
            }
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
