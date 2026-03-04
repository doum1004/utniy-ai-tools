using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Tools;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Services
{
    /// <summary>
    /// Background manager for the MCP connection.
    /// Ensures the connection stays alive even if the EditorWindow is closed.
    /// Uses InitializeOnLoad to start automatically and survive assembly reloads.
    /// </summary>
    [InitializeOnLoad]
    public class McpBackgroundService
    {
        public static McpBackgroundService Instance { get; private set; }

        public WebSocketClient Client { get; private set; }

        private volatile bool _isConnecting;
        public bool IsConnecting => _isConnecting;
        
        // Define an event for status changes so the UI can update
        public event Action OnStatusChanged;

        private CommandDispatcher _dispatcher;
        private volatile bool _isDisconnectingIntentional = false;
        private volatile bool _wasConnected = false;
        private string _lastServerUrl = "ws://localhost:8091";
        private int _reconnectAttempt = 0;
        private static readonly int[] ReconnectScheduleMs = { 0, 1000, 3000, 5000, 10000, 30000 };

        // Background refresh: keeps editor responsive and auto-refreshes after MCP commands
        private double _lastBackgroundTick;
        private const double BackgroundTickIntervalSec = 1.0;
        private volatile bool _pendingRefresh;

        // Background reconnect monitor — runs independently of Unity's main thread update loop.
        // This ensures reconnection works even when Unity is unfocused or between domain reloads.
        private CancellationTokenSource _monitorCts;
        private const int MonitorIntervalMs = 2000;

        // SessionState keys — survive domain reloads but not Editor restarts
        private const string KeyWasConnected = "UnityAITools_WasConnected";
        private const string KeyServerUrl = "UnityAITools_ServerUrl";
        private const string KeyRunInBackgroundOverridden = "UnityAITools_RunInBackgroundOverridden";
        private const string KeyPrevRunInBackground = "UnityAITools_PrevRunInBackground";

        static McpBackgroundService()
        {
            // Initialize immediately on domain reload so reconnect does not depend on focus/update ticks.
            // Guard static constructor from hard-failing if Unity is in a transient reload state.
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAITools] Immediate MCP service init deferred: {ex.Message}");
            }
            // Keep a delayed fallback in case Unity isn't fully ready during static init.
            EditorApplication.delayCall += Initialize;
        }

        private static void Initialize()
        {
            if (Instance != null) return;
            Instance = new McpBackgroundService();
            Instance.Setup();
        }

        private void Setup()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;

            ConsoleLogCapture.Instance.Register();
            _dispatcher = new CommandDispatcher();

            // Register tool handlers
            var gameObjectHandler = new GameObjectHandler();
            _dispatcher.RegisterHandler("manage_gameobject", gameObjectHandler);
            _dispatcher.RegisterHandler("find_gameobjects", gameObjectHandler);

            var scriptHandler = new ScriptHandler();
            _dispatcher.RegisterHandler("create_script", scriptHandler);
            _dispatcher.RegisterHandler("manage_script", scriptHandler);
            _dispatcher.RegisterHandler("delete_script", scriptHandler);
            _dispatcher.RegisterHandler("get_sha", scriptHandler);
            _dispatcher.RegisterHandler("script_apply_edits", scriptHandler);
            _dispatcher.RegisterHandler("apply_text_edits", scriptHandler);

            var sceneHandler = new SceneHandler();
            _dispatcher.RegisterHandler("manage_scene", sceneHandler);

            var editorHandler = new EditorHandler();
            _dispatcher.RegisterHandler("manage_editor", editorHandler);
            _dispatcher.RegisterHandler("read_console", editorHandler);
            _dispatcher.RegisterHandler("refresh_unity", editorHandler);
            _dispatcher.RegisterHandler("execute_menu_item", editorHandler);
            _dispatcher.RegisterHandler("get_editor_state", editorHandler);
            _dispatcher.RegisterHandler("get_project_info", editorHandler);
            _dispatcher.RegisterHandler("get_editor_selection", editorHandler);
            _dispatcher.RegisterHandler("get_project_tags", editorHandler);
            _dispatcher.RegisterHandler("get_project_layers", editorHandler);

            var componentHandler = new ComponentHandler();
            _dispatcher.RegisterHandler("manage_components", componentHandler);

            var assetHandler = new AssetHandler();
            _dispatcher.RegisterHandler("manage_asset", assetHandler);

            var materialHandler = new MaterialHandler();
            _dispatcher.RegisterHandler("manage_material", materialHandler);

            var prefabHandler = new PrefabHandler();
            _dispatcher.RegisterHandler("manage_prefabs", prefabHandler);

            var analysisHandler = new AnalysisHandler();
            _dispatcher.RegisterHandler("analyze_scene", analysisHandler);
            _dispatcher.RegisterHandler("inspect_gameobject", analysisHandler);
            _dispatcher.RegisterHandler("get_project_settings", analysisHandler);

            var batchHandler = new BatchHandler(_dispatcher);
            _dispatcher.RegisterHandler("batch_execute", batchHandler);

            var playTestHandler = new PlayTestHandler();
            _dispatcher.RegisterHandler("simulate_input", playTestHandler);
            _dispatcher.RegisterHandler("read_runtime_state", playTestHandler);
            _dispatcher.RegisterHandler("capture_gameplay", playTestHandler);
            _dispatcher.RegisterHandler("execute_method", playTestHandler);

            // Commands that modify assets/scripts and need a refresh
            var refreshCommands = new HashSet<string> {
                "create_script", "manage_script", "delete_script", "script_apply_edits",
                "apply_text_edits", "manage_asset", "manage_material", "manage_prefabs",
                "manage_gameobject", "manage_components", "manage_scene", "batch_execute"
            };
            _dispatcher.OnCommandExecuted += (cmd) =>
            {
                if (refreshCommands.Contains(cmd))
                    _pendingRefresh = true;
            };

            Client = new WebSocketClient(_dispatcher);
            
            Client.OnConnected += () => {
                _isConnecting = false;
                _reconnectAttempt = 0;
                _wasConnected = true;
                // SessionState and EditorApplication require the main thread;
                // the receive loop may fire this callback from a background thread.
                EditorApplication.delayCall += () =>
                {
                    SessionState.SetBool(KeyWasConnected, true);
                    SessionState.SetString(KeyServerUrl, _lastServerUrl);
                    EditorApplication.update += BackgroundTick;
                    EnsureAutoRefreshEnabled();
                    EnsureRunInBackgroundEnabled();
                };
                NotifyStatusChanged();
            };
            
            Client.OnDisconnected += () => { 
                _isConnecting = false;
                EditorApplication.delayCall += () => EditorApplication.update -= BackgroundTick;
                NotifyStatusChanged(); 
                
                if (!_isDisconnectingIntentional)
                {
                    Debug.LogWarning("[UnityAITools] Unintentional disconnect detected, will auto-reconnect");
                    _ = Task.Run(() => TryReconnectingAsync());
                }
                else
                {
                    Debug.Log("[UnityAITools] Intentional disconnect, skipping auto-reconnect");
                }
            };
            
            Client.OnError += (err) => { 
                _isConnecting = false; 
                Debug.LogWarning($"[UnityAITools] WebSocket error: {err}"); 
                NotifyStatusChanged(); 
            };

            // Hook into Editor application quitting to clean up
            EditorApplication.quitting += OnQuitting;

            // Auto-reconnect after domain reload (play mode enter/exit).
            // Use Task.Run so this fires even if Unity is currently unfocused.
            _wasConnected = SessionState.GetBool(KeyWasConnected, false);
            if (_wasConnected)
            {
                var url = SessionState.GetString(KeyServerUrl, _lastServerUrl);
                Debug.Log($"[UnityAITools] Domain reload detected, auto-reconnecting to {url}...");
                _ = Task.Run(() => TryReconnectingAsync());
            }

            // Start the background connection monitor.
            StartMonitor();
        }

        /// <summary>
        /// Runs while connected to keep the editor responsive in background
        /// and auto-refresh assets after MCP commands modify them.
        /// </summary>
        private void BackgroundTick()
        {
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastBackgroundTick < BackgroundTickIntervalSec)
                return;
            _lastBackgroundTick = now;

            // Force the editor to stay responsive when unfocused
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }

            // Auto-refresh after mutating commands
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>
        /// Called by Unity just before a domain reload (script recompilation).
        /// Must tear down the WebSocket synchronously so background threads don't
        /// block the reload — the root cause of Unity hanging when unfocused.
        /// </summary>
        private void OnBeforeAssemblyReload()
        {
            Debug.Log("[UnityAITools] Domain reload imminent — tearing down WebSocket...");
            _monitorCts?.Cancel();
            EditorApplication.update -= BackgroundTick;

            if (Client != null)
            {
                Client.AbortImmediate();
            }
        }

        private void OnQuitting()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            _monitorCts?.Cancel();
            EditorApplication.update -= BackgroundTick;
            Disconnect();
            ConsoleLogCapture.Instance.Unregister();
        }

        /// <summary>
        /// Initiate a connection from the UI (e.g. Connect button). Safe to call from main thread.
        /// </summary>
        public void Connect(string serverUrl)
        {
            if (Client.IsConnected || _isConnecting) return;
            _isDisconnectingIntentional = false;
            _lastServerUrl = serverUrl;
            _isConnecting = true;
            _reconnectAttempt = 0;
            NotifyStatusChanged();
            _ = Client.ConnectAsync(serverUrl);
        }

        public void Disconnect()
        {
            _isDisconnectingIntentional = true;
            _pendingRefresh = false;
            _reconnectAttempt = 0;
            _wasConnected = false;
            EditorApplication.update -= BackgroundTick;
            SessionState.SetBool(KeyWasConnected, false);
            RestoreAutoRefreshSetting();
            RestoreRunInBackgroundSetting();
            if (Client != null && Client.IsConnected)
            {
                _ = Client.DisconnectAsync();
            }
        }

        /// <summary>
        /// Attempts to reconnect using exponential backoff. Runs entirely on a background
        /// thread so it works even when Unity is unfocused or between domain reloads.
        /// </summary>
        private async Task TryReconnectingAsync()
        {
            if (_isDisconnectingIntentional || Client.IsConnected || _isConnecting) return;

            var delayMs = ReconnectScheduleMs[Math.Min(_reconnectAttempt, ReconnectScheduleMs.Length - 1)];
            _reconnectAttempt++;

            Debug.Log($"[UnityAITools] Connection lost. Auto-reconnect attempt #{_reconnectAttempt} in {delayMs}ms...");

            if (delayMs > 0)
                await Task.Delay(delayMs);

            if (_isDisconnectingIntentional || Client.IsConnected || _isConnecting) return;

            _isConnecting = true;
            NotifyStatusChanged();
            // ConnectAsync is thread-safe — ClientWebSocket.ConnectAsync works off the main thread.
            await Client.ConnectAsync(_lastServerUrl);
        }

        /// <summary>
        /// Starts a long-running background monitor that periodically checks the connection
        /// and triggers reconnection if needed — independent of Unity's main thread update loop.
        /// This is the key fix: reconnection now works when Unity is unfocused.
        /// </summary>
        private void StartMonitor()
        {
            _monitorCts?.Cancel();
            _monitorCts = new CancellationTokenSource();
            _ = Task.Run(() => ConnectionMonitorAsync(_monitorCts.Token));
        }

        private async Task ConnectionMonitorAsync(CancellationToken token)
        {
            Debug.Log("[UnityAITools] Background connection monitor started");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(MonitorIntervalMs, token);

                    // If we should be connected but aren't, trigger a reconnect.
                    if (!_isDisconnectingIntentional && !Client.IsConnected && !_isConnecting && _wasConnected)
                    {
                        Debug.Log("[UnityAITools] Monitor: connection lost, triggering reconnect...");
                        await TryReconnectingAsync();
                    }

                    // Wake the editor so EditorApplication.update / delayCall
                    // keep firing even when Unity is unfocused.
                    // Heavy-duty reload forcing is handled by BackgroundReloadForcer.
                    try { EditorApplication.QueuePlayerLoopUpdate(); }
                    catch { /* May fail during domain reload */ }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityAITools] Monitor error: {ex.Message}");
                }
            }
            Debug.Log("[UnityAITools] Background connection monitor stopped");
        }

        private void NotifyStatusChanged()
        {
            // Fire event on main thread
            var callback = new EditorApplication.CallbackFunction(() => OnStatusChanged?.Invoke());
            EditorApplication.delayCall += callback;
        }

        private static void RestoreAutoRefreshSetting()
        {
            if (!SessionState.GetBool("UnityAITools_AutoRefreshOverridden", false)) return;

            var saved = SessionState.GetInt("UnityAITools_PrevAutoRefreshMode", 1);
            EditorPrefs.SetInt("kAutoRefreshMode", saved);
            SessionState.SetBool("UnityAITools_AutoRefreshOverridden", false);
            Debug.Log($"[UnityAITools] Auto Refresh restored to mode {saved}");
        }

        private static void RestoreRunInBackgroundSetting()
        {
            if (!SessionState.GetBool(KeyRunInBackgroundOverridden, false)) return;

            var saved = SessionState.GetBool(KeyPrevRunInBackground, false);
            PlayerSettings.runInBackground = saved;
            SessionState.SetBool(KeyRunInBackgroundOverridden, false);
            Debug.Log($"[UnityAITools] Run In Background restored to {saved}");
        }

        /// <summary>
        /// Ensures Auto Refresh is set to "Enabled" (mode 1) so Unity recompiles
        /// scripts immediately — even when the editor window doesn't have OS focus.
        /// Without this, domain reload stalls until the user alt-tabs back to Unity.
        /// The previous value is saved so it can be restored on disconnect.
        /// </summary>
        private static void EnsureAutoRefreshEnabled()
        {
            const string key = "kAutoRefreshMode";
            const string savedKey = "UnityAITools_PrevAutoRefreshMode";
            var current = EditorPrefs.GetInt(key, 1);

            // 0 = Disabled, 1 = Enabled (always), 2 = Enabled Outside Playmode
            if (current == 1) return;

            if (!SessionState.GetBool("UnityAITools_AutoRefreshOverridden", false))
            {
                SessionState.SetInt(savedKey, current);
                SessionState.SetBool("UnityAITools_AutoRefreshOverridden", true);
            }

            EditorPrefs.SetInt(key, 1);
            Debug.Log($"[UnityAITools] Auto Refresh changed from mode {current} to 1 (always enabled) for MCP compatibility");
        }

        /// <summary>
        /// Ensures play mode continues running even when Unity loses focus.
        /// This prevents background automation sessions from effectively stalling.
        /// The previous value is saved and restored on disconnect.
        /// </summary>
        private static void EnsureRunInBackgroundEnabled()
        {
            if (PlayerSettings.runInBackground) return;

            if (!SessionState.GetBool(KeyRunInBackgroundOverridden, false))
            {
                SessionState.SetBool(KeyPrevRunInBackground, PlayerSettings.runInBackground);
                SessionState.SetBool(KeyRunInBackgroundOverridden, true);
            }

            PlayerSettings.runInBackground = true;
            Debug.Log("[UnityAITools] Enabled PlayerSettings.runInBackground for MCP compatibility");
        }

        /// <summary>
        /// Executes a tool command locally (bypasses WebSocket) — used for UI-initiated captures
        /// like annotated screenshots where no round-trip to the MCP server is needed.
        /// </summary>
        public Task<CommandResult> ExecuteToolDirectAsync(string commandName, string paramsJson)
        {
            return _dispatcher.DispatchAsync(commandName, paramsJson);
        }
    }
}
