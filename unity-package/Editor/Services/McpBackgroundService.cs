using System;
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
        public bool IsConnecting { get; private set; }
        
        // Define an event for status changes so the UI can update
        public event Action OnStatusChanged;

        private CommandDispatcher _dispatcher;
        private bool _isDisconnectingIntentional = false;
        private string _lastServerUrl = "ws://localhost:8091";
        private int _reconnectAttempt = 0;
        private static readonly int[] ReconnectScheduleMs = { 0, 1000, 3000, 5000, 10000, 30000 };

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

            var batchHandler = new BatchHandler(_dispatcher);
            _dispatcher.RegisterHandler("batch_execute", batchHandler);

            Client = new WebSocketClient(_dispatcher);
            
            Client.OnConnected += () => { 
                IsConnecting = false;
                _reconnectAttempt = 0;
                SessionState.SetBool(KeyWasConnected, true);
                SessionState.SetString(KeyServerUrl, _lastServerUrl);
                NotifyStatusChanged(); 
            };
            
            Client.OnDisconnected += () => { 
                IsConnecting = false; 
                NotifyStatusChanged(); 
                
                if (!_isDisconnectingIntentional)
                {
                    Debug.LogWarning("[UnityAITools] Unintentional disconnect detected, will auto-reconnect");
                    EditorApplication.delayCall += TryReconnecting;
                }
                else
                {
                    Debug.Log("[UnityAITools] Intentional disconnect, skipping auto-reconnect");
                }
            };
            
            Client.OnError += (err) => { 
                IsConnecting = false; 
                Debug.LogWarning($"[UnityAITools] WebSocket error: {err}"); 
                NotifyStatusChanged(); 
            };

            // Hook into Editor application quitting to clean up
            EditorApplication.quitting += OnQuitting;

            // Auto-reconnect after domain reload (play mode enter/exit)
            if (SessionState.GetBool(KeyWasConnected, false))
            {
                var url = SessionState.GetString(KeyServerUrl, _lastServerUrl);
                Debug.Log($"[UnityAITools] Domain reload detected, auto-reconnecting to {url}...");
                Connect(url);
            }
        }

        private void OnQuitting()
        {
            Disconnect();
            ConsoleLogCapture.Instance.Unregister();
        }

        public void Connect(string serverUrl)
        {
            if (Client.IsConnected || IsConnecting) return;
            _isDisconnectingIntentional = false;
            _lastServerUrl = serverUrl;
            IsConnecting = true;
            NotifyStatusChanged();
            Client.ConnectAsync(serverUrl);
        }

        public void Disconnect()
        {
            _isDisconnectingIntentional = true;
            SessionState.SetBool(KeyWasConnected, false);
            if (Client != null && Client.IsConnected)
            {
                Client.DisconnectAsync();
            }
        }

        private async void TryReconnecting()
        {
            if (_isDisconnectingIntentional || Client.IsConnected || IsConnecting) return;

            var delayMs = ReconnectScheduleMs[Math.Min(_reconnectAttempt, ReconnectScheduleMs.Length - 1)];
            _reconnectAttempt++;

            Debug.Log($"[UnityAITools] Connection lost. Auto-reconnect attempt #{_reconnectAttempt} in {delayMs}ms...");

            if (delayMs > 0)
                await System.Threading.Tasks.Task.Delay(delayMs);
            
            if (!_isDisconnectingIntentional && !Client.IsConnected && !IsConnecting)
            {
                Connect(_lastServerUrl);
            }
        }

        private void NotifyStatusChanged()
        {
            // Fire event on main thread
            var callback = new EditorApplication.CallbackFunction(() => OnStatusChanged?.Invoke());
            EditorApplication.delayCall += callback;
        }
    }
}
