using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Transport
{
    /// <summary>
    /// WebSocket client that connects the Unity Editor to the MCP server.
    /// Handles registration, ping/pong keep-alive, and command dispatch.
    /// </summary>
    public class WebSocketClient
    {
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private string _sessionId;
        private readonly CommandDispatcher _dispatcher;
        private Task _keepAliveTask;
        private int _keepAliveIntervalMs = 15000; // default 15s, updated from welcome
        private int _serverTimeoutMs = 30000;     // default 30s, updated from welcome

        private const int ReconnectDelayMs = 3000;
        private const int ReceiveBufferSize = 1024 * 64; // 64KB

        public bool IsConnected => _ws?.State == WebSocketState.Open;
        public string SessionId => _sessionId;

        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<string> OnError;

        public WebSocketClient(CommandDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Connect to the MCP server WebSocket endpoint.
        /// </summary>
        public async Task ConnectAsync(string url)
        {
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            try
            {
                await _ws.ConnectAsync(new Uri(url), _cts.Token);
                Debug.Log($"[UnityAITools] Connected to {url}");

                // Start receive loop
                _ = ReceiveLoop();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Connection failed: {ex.Message}");
                Debug.LogError($"[UnityAITools] Connection failed: {ex}");
            }
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            _cts?.Cancel();

            // Wait for keep-alive task to finish
            if (_keepAliveTask != null)
            {
                try { await _keepAliveTask; } catch { /* already cancelled */ }
                _keepAliveTask = null;
            }

            if (_ws?.State == WebSocketState.Open)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                }
                catch { /* Ignore close errors */ }
            }
            _ws?.Dispose();
            _ws = null;
            _sessionId = null;
            OnDisconnected?.Invoke();
        }

        /// <summary>
        /// Send a JSON message to the server.
        /// </summary>
        public async Task SendAsync(string json)
        {
            if (_ws?.State != WebSocketState.Open) return;

            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts.Token);
        }

        /// <summary>
        /// Send a registration message to the MCP server.
        /// </summary>
        public async Task RegisterAsync()
        {
            var msg = new RegisterMessage
            {
                project_name = Application.productName,
                project_hash = ComputeProjectHash(),
                unity_version = Application.unityVersion,
                project_path = Application.dataPath.Replace("/Assets", "")
            };

            await SendAsync(JsonUtility.ToJson(msg));
        }

        /// <summary>
        /// Send a command result back to the server.
        /// </summary>
        public async Task SendCommandResultAsync(string commandId, CommandResult result)
        {
            // JsonUtility cannot serialize Dictionary<string,object> or 'object' fields,
            // so we build the JSON manually using MiniJson which handles these types.
            var resultDict = new Dictionary<string, object>
            {
                { "success", result.success }
            };
            if (result.error != null) resultDict["error"] = result.error;
            if (result.data != null) resultDict["data"] = result.data;
            if (result.hint != null) resultDict["hint"] = result.hint;

            var msgDict = new Dictionary<string, object>
            {
                { "type", "command_result" },
                { "id", commandId },
                { "result", resultDict }
            };

            await SendAsync(MiniJson.Serialize(msgDict));
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[ReceiveBufferSize];

            try
            {
                while (_ws?.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var sb = new StringBuilder();
                    WebSocketReceiveResult received;

                    do
                    {
                        received = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                    }
                    while (!received.EndOfMessage);

                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.LogWarning($"[UnityAITools] Server closed connection (status: {received.CloseStatus}, reason: {received.CloseStatusDescription})");
                        break;
                    }

                    var json = sb.ToString();
                    HandleMessage(json);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[UnityAITools] WebSocket receive loop cancelled (normal shutdown)");
            }
            catch (WebSocketException ex)
            {
                Debug.LogError($"[UnityAITools] WebSocket error (code: {ex.WebSocketErrorCode}): {ex.Message}");
                OnError?.Invoke(ex.Message);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityAITools] WebSocket error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                OnError?.Invoke(ex.Message);
            }

            Debug.LogWarning($"[UnityAITools] Receive loop ended. WebSocket state: {_ws?.State}");
            OnDisconnected?.Invoke();
        }

        private void HandleMessage(string json)
        {
            try
            {
                // Parse the base message to determine type
                var baseMsg = JsonUtility.FromJson<BaseMessage>(json);

                switch (baseMsg.type)
                {
                    case "welcome":
                        HandleWelcome(json);
                        break;
                    case "registered":
                        HandleRegistered(json);
                        break;
                    case "execute_command":
                        HandleExecuteCommand(json);
                        break;
                    case "ping":
                        HandlePing();
                        break;
                    default:
                        Debug.LogWarning($"[UnityAITools] Unknown message type: {baseMsg.type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityAITools] Error handling message: {ex.Message}");
            }
        }

        private async void HandleWelcome(string json)
        {
            // Parse keep-alive parameters from server
            try
            {
                var welcomeMsg = JsonUtility.FromJson<WelcomeMessage>(json);
                if (welcomeMsg.keepAliveInterval > 0)
                    _keepAliveIntervalMs = welcomeMsg.keepAliveInterval * 1000;
                if (welcomeMsg.serverTimeout > 0)
                    _serverTimeoutMs = welcomeMsg.serverTimeout * 1000;

                Debug.Log($"[UnityAITools] Received welcome (keepAlive: {welcomeMsg.keepAliveInterval}s, timeout: {welcomeMsg.serverTimeout}s), registering...");
            }
            catch
            {
                Debug.Log("[UnityAITools] Received welcome, registering...");
            }
            await RegisterAsync();
        }

        private void HandleRegistered(string json)
        {
            var msg = JsonUtility.FromJson<RegisteredMessage>(json);
            _sessionId = msg.session_id;
            Debug.Log($"[UnityAITools] Registered with session: {_sessionId}");

            // Start background keep-alive loop (runs on thread pool, not main thread)
            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_cts.Token));

            OnConnected?.Invoke();
        }

        private async void HandleExecuteCommand(string json)
        {
            // We use a simple JSON parser here since JsonUtility can't handle
            // Dictionary<string, object> directly. We parse the raw JSON.
            try
            {
                var result = await _dispatcher.DispatchAsync(json);
                // Extract command ID from the JSON
                var idStart = json.IndexOf("\"id\"", StringComparison.Ordinal);
                if (idStart >= 0)
                {
                    var colonPos = json.IndexOf(':', idStart);
                    var quoteStart = json.IndexOf('"', colonPos + 1);
                    var quoteEnd = json.IndexOf('"', quoteStart + 1);
                    var commandId = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                    await SendCommandResultAsync(commandId, result);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityAITools] Command execution error: {ex}");
            }
        }

        private async void HandlePing()
        {
            if (!string.IsNullOrEmpty(_sessionId))
            {
                var pong = new PongMessage(_sessionId);
                await SendAsync(JsonUtility.ToJson(pong));
            }
        }

        /// <summary>
        /// Background keep-alive loop that proactively sends pong messages on a thread pool thread.
        /// This ensures pongs are sent even when Unity's main thread is blocked (e.g. during compilation/import).
        /// </summary>
        private async Task KeepAliveLoopAsync(CancellationToken token)
        {
            Debug.Log($"[UnityAITools] Background keep-alive started (interval: {_keepAliveIntervalMs}ms)");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_keepAliveIntervalMs, token);
                    if (_ws == null || _ws.State != WebSocketState.Open)
                    {
                        Debug.Log("[UnityAITools] Keep-alive loop exiting: socket no longer open");
                        break;
                    }

                    // Build JSON manually — JsonUtility.ToJson requires main thread
                    var json = $"{{\"type\":\"pong\",\"session_id\":\"{_sessionId}\"}}";
                    var bytes = Encoding.UTF8.GetBytes(json);
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[UnityAITools] Keep-alive failed: {ex.Message}");
                    break;
                }
            }
            Debug.Log("[UnityAITools] Background keep-alive loop ended");
        }

        private static string ComputeProjectHash()
        {
            // Generate a stable hash from the project path
            var projectPath = Application.dataPath;
            var hash = projectPath.GetHashCode();
            return hash.ToString("x8");
        }
    }
}
