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
        private const int MonitorIntervalMs = 5000;

        // SessionState keys — survive domain reloads but not Editor restarts
        private const string KeyWasConnected = "UnityAITools_WasConnected";
        private const string KeyServerUrl = "UnityAITools_ServerUrl";

        static McpBackgroundService()
        {
            // Called on Unity Editor load and domain reloads
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
                SessionState.SetBool(KeyWasConnected, true);
                SessionState.SetString(KeyServerUrl, _lastServerUrl);
                EditorApplication.update += BackgroundTick;
                NotifyStatusChanged(); 
            };
            
            Client.OnDisconnected += () => { 
                _isConnecting = false;
                EditorApplication.update -= BackgroundTick;
                NotifyStatusChanged(); 
                
                if (!_isDisconnectingIntentional)
                {
                    Debug.LogWarning("[UnityAITools] Unintentional disconnect detected, will auto-reconnect");
                    // Use background task instead of EditorApplication.delayCall so reconnection
                    // works even when Unity is unfocused or in the middle of a domain reload.
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
            if (SessionState.GetBool(KeyWasConnected, false))
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

            // Force the editor to repaint so update callbacks keep firing even when unfocused
            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
            {
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            }

            // Auto-refresh after mutating commands
            if (_pendingRefresh)
            {
                _pendingRefresh = false;
                AssetDatabase.Refresh();
            }
        }

        private void OnQuitting()
        {
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
            EditorApplication.update -= BackgroundTick;
            SessionState.SetBool(KeyWasConnected, false);
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
                    if (!_isDisconnectingIntentional && !Client.IsConnected && !_isConnecting
                        && SessionState.GetBool(KeyWasConnected, false))
                    {
                        Debug.Log("[UnityAITools] Monitor: connection lost, triggering reconnect...");
                        await TryReconnectingAsync();
                    }
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
    }
}
