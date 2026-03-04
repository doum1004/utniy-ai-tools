using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles gameplay testing commands — input simulation, runtime state reading,
    /// and gameplay capture for LLM-driven QA.
    /// </summary>
    public class PlayTestHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            switch (commandName)
            {
                case "simulate_input":
                    return ExecuteOnMainThread(commandName, paramsJson);
                case "read_runtime_state":
                    return ExecuteOnMainThread(commandName, paramsJson);
                case "capture_gameplay":
                    return ExecuteCaptureGameplay(paramsJson);
                default:
                    return Task.FromResult(new CommandResult
                    {
                        success = false,
                        error = $"Unknown command: {commandName}"
                    });
            }
        }

        private Task<CommandResult> ExecuteOnMainThread(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try
                {
                    var p = new ToolParams(paramsJson);
                    CommandResult result;
                    switch (commandName)
                    {
                        case "simulate_input":
                            result = HandleSimulateInput(p);
                            break;
                        case "read_runtime_state":
                            result = HandleReadRuntimeState(p);
                            break;
                        default:
                            result = new CommandResult { success = false, error = $"Unknown command: {commandName}" };
                            break;
                    }
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new CommandResult { success = false, error = ex.Message });
                }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        // ──────────────────────────────────────────────────────────────
        // simulate_input
        // ──────────────────────────────────────────────────────────────

        private CommandResult HandleSimulateInput(ToolParams p)
        {
            if (!EditorApplication.isPlaying)
                return new CommandResult { success = false, error = "Unity is not in Play mode. Enter Play mode first." };

            var action = p.RequireString("action");

            switch (action)
            {
                case "key_down":
                case "key_up":
                case "key_press":
                    return SimulateKey(p, action);
                case "mouse_click":
                    return SimulateMouseClick(p);
                case "mouse_move":
                    return SimulateMouseMove(p);
                case "mouse_drag":
                    return SimulateMouseDrag(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown input action: {action}" };
            }
        }

        private CommandResult SimulateKey(ToolParams p, string action)
        {
            var keyName = p.RequireString("key");
            if (!TryParseKeyCode(keyName, out var keyCode))
                return new CommandResult { success = false, error = $"Unknown key: {keyName}" };

            var duration = p.GetFloat("duration") ?? 0f;

            switch (action)
            {
                case "key_down":
                    QueueKeyEvent(keyCode, EventType.KeyDown);
                    break;
                case "key_up":
                    QueueKeyEvent(keyCode, EventType.KeyUp);
                    break;
                case "key_press":
                    if (duration > 0)
                    {
                        QueueKeyEvent(keyCode, EventType.KeyDown);
                        ScheduleKeyUp(keyCode, duration);
                    }
                    else
                    {
                        QueueKeyEvent(keyCode, EventType.KeyDown);
                        QueueKeyEvent(keyCode, EventType.KeyUp);
                    }
                    break;
            }

            var data = new Dictionary<string, object>
            {
                { "action", action },
                { "key", keyName },
                { "key_code", keyCode.ToString() }
            };
            if (duration > 0) data["duration"] = duration;

            return new CommandResult { success = true, data = data };
        }

        private CommandResult SimulateMouseClick(ToolParams p)
        {
            var pos = p.GetVector3("position");
            var button = p.GetInt("button") ?? 0;

            if (pos == null || pos.Length < 2)
                return new CommandResult { success = false, error = "position [x, y] is required for mouse_click" };

            var screenPos = new Vector2(pos[0], pos[1]);
            QueueMouseEvent(screenPos, (EventType)(-1), button); // down+up

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "action", "mouse_click" },
                    { "position", new List<object> { screenPos.x, screenPos.y } },
                    { "button", button }
                }
            };
        }

        private CommandResult SimulateMouseMove(ToolParams p)
        {
            var pos = p.GetVector3("position");
            if (pos == null || pos.Length < 2)
                return new CommandResult { success = false, error = "position [x, y] is required for mouse_move" };

            var screenPos = new Vector2(pos[0], pos[1]);

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "action", "mouse_move" },
                    { "position", new List<object> { screenPos.x, screenPos.y } }
                }
            };
        }

        private CommandResult SimulateMouseDrag(ToolParams p)
        {
            var from = p.GetVector3("from");
            var to = p.GetVector3("to");
            var button = p.GetInt("button") ?? 0;

            if (from == null || from.Length < 2)
                return new CommandResult { success = false, error = "from [x, y] is required for mouse_drag" };
            if (to == null || to.Length < 2)
                return new CommandResult { success = false, error = "to [x, y] is required for mouse_drag" };

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "action", "mouse_drag" },
                    { "from", new List<object> { from[0], from[1] } },
                    { "to", new List<object> { to[0], to[1] } },
                    { "button", button }
                }
            };
        }

        private static void QueueKeyEvent(KeyCode keyCode, EventType eventType)
        {
            var gameView = GetGameView();
            if (gameView == null) return;

            var evt = new Event
            {
                type = eventType,
                keyCode = keyCode
            };
            gameView.SendEvent(evt);
        }

        private static void QueueMouseEvent(Vector2 position, EventType eventType, int button)
        {
            var gameView = GetGameView();
            if (gameView == null) return;

            // Send mouse down then mouse up for a click
            var downEvt = new Event
            {
                type = EventType.MouseDown,
                mousePosition = position,
                button = button
            };
            gameView.SendEvent(downEvt);

            var upEvt = new Event
            {
                type = EventType.MouseUp,
                mousePosition = position,
                button = button
            };
            gameView.SendEvent(upEvt);
        }

        private static void ScheduleKeyUp(KeyCode keyCode, float delaySeconds)
        {
            var startTime = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction keyUpCallback = null;
            keyUpCallback = () =>
            {
                if (EditorApplication.timeSinceStartup - startTime >= delaySeconds)
                {
                    EditorApplication.update -= keyUpCallback;
                    QueueKeyEvent(keyCode, EventType.KeyUp);
                }
            };
            EditorApplication.update += keyUpCallback;
        }

        private static EditorWindow GetGameView()
        {
            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return null;
            return EditorWindow.GetWindow(gameViewType, false, null, false);
        }

        private static bool TryParseKeyCode(string name, out KeyCode keyCode)
        {
            // Try direct enum parse first
            if (Enum.TryParse<KeyCode>(name, true, out keyCode))
                return true;

            // Common aliases
            var normalized = name.ToLowerInvariant();
            switch (normalized)
            {
                case "space": keyCode = KeyCode.Space; return true;
                case "enter": keyCode = KeyCode.Return; return true;
                case "return": keyCode = KeyCode.Return; return true;
                case "esc": case "escape": keyCode = KeyCode.Escape; return true;
                case "tab": keyCode = KeyCode.Tab; return true;
                case "backspace": keyCode = KeyCode.Backspace; return true;
                case "delete": keyCode = KeyCode.Delete; return true;
                case "up": keyCode = KeyCode.UpArrow; return true;
                case "down": keyCode = KeyCode.DownArrow; return true;
                case "left": keyCode = KeyCode.LeftArrow; return true;
                case "right": keyCode = KeyCode.RightArrow; return true;
                case "shift": case "lshift": keyCode = KeyCode.LeftShift; return true;
                case "rshift": keyCode = KeyCode.RightShift; return true;
                case "ctrl": case "control": case "lctrl": keyCode = KeyCode.LeftControl; return true;
                case "rctrl": keyCode = KeyCode.RightControl; return true;
                case "alt": case "lalt": keyCode = KeyCode.LeftAlt; return true;
                case "ralt": keyCode = KeyCode.RightAlt; return true;
            }

            // Single character (a-z, 0-9)
            if (normalized.Length == 1)
            {
                var ch = normalized[0];
                if (ch >= 'a' && ch <= 'z')
                {
                    keyCode = (KeyCode)((int)KeyCode.A + (ch - 'a'));
                    return true;
                }
                if (ch >= '0' && ch <= '9')
                {
                    keyCode = (KeyCode)((int)KeyCode.Alpha0 + (ch - '0'));
                    return true;
                }
            }

            // F-keys
            if (normalized.StartsWith("f") && int.TryParse(normalized.Substring(1), out var fNum) && fNum >= 1 && fNum <= 15)
            {
                keyCode = (KeyCode)((int)KeyCode.F1 + (fNum - 1));
                return true;
            }

            keyCode = KeyCode.None;
            return false;
        }

        // ──────────────────────────────────────────────────────────────
        // read_runtime_state
        // ──────────────────────────────────────────────────────────────

        private CommandResult HandleReadRuntimeState(ToolParams p)
        {
            var data = new Dictionary<string, object>
            {
                { "is_playing", EditorApplication.isPlaying },
                { "is_paused", EditorApplication.isPaused },
                { "time", EditorApplication.isPlaying ? Time.time : 0f },
                { "frame_count", Time.frameCount },
                { "delta_time", Time.deltaTime },
                { "fps", Time.deltaTime > 0 ? 1f / Time.deltaTime : 0f },
                { "scene", SceneManager.GetActiveScene().name }
            };

            var target = p.GetString("target");
            var fields = p.GetStringList("fields");

            if (!string.IsNullOrEmpty(target))
            {
                var go = GameObject.Find(target);
                if (go == null)
                    return new CommandResult { success = false, error = $"GameObject not found: {target}" };

                var goData = new Dictionary<string, object>
                {
                    { "name", go.name },
                    { "active", go.activeSelf },
                    { "layer", LayerMask.LayerToName(go.layer) },
                    { "tag", go.tag },
                    { "position", Vec3ToList(go.transform.position) },
                    { "rotation", Vec3ToList(go.transform.eulerAngles) },
                    { "local_scale", Vec3ToList(go.transform.localScale) }
                };

                if (fields != null && fields.Count > 0)
                {
                    var fieldValues = ReadFields(go, fields);
                    goData["fields"] = fieldValues;
                }
                else
                {
                    var components = go.GetComponents<Component>();
                    var compList = new List<Dictionary<string, object>>();
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        var compInfo = new Dictionary<string, object>
                        {
                            { "type", comp.GetType().Name }
                        };

                        if (comp is Behaviour behaviour)
                            compInfo["enabled"] = behaviour.enabled;
                        if (comp is Collider collider)
                            compInfo["is_trigger"] = collider.isTrigger;
                        if (comp is Rigidbody rb)
                        {
                            compInfo["velocity"] = Vec3ToList(rb.linearVelocity);
                            compInfo["angular_velocity"] = Vec3ToList(rb.angularVelocity);
                            compInfo["is_kinematic"] = rb.isKinematic;
                        }
                        if (comp is Animator animator)
                        {
                            var clipInfo = animator.GetCurrentAnimatorClipInfo(0);
                            if (clipInfo.Length > 0)
                                compInfo["current_clip"] = clipInfo[0].clip.name;
                            compInfo["speed"] = animator.speed;
                        }

                        compList.Add(compInfo);
                    }
                    goData["components"] = compList;
                }

                data["target"] = goData;
            }

            // Object counts
            var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            data["total_gameobjects"] = allObjects.Length;
            data["active_gameobjects"] = allObjects.Count(o => o.activeInHierarchy);

            return new CommandResult { success = true, data = data };
        }

        private static Dictionary<string, object> ReadFields(GameObject go, List<string> fieldPaths)
        {
            var result = new Dictionary<string, object>();

            foreach (var fieldPath in fieldPaths)
            {
                try
                {
                    // Format: "ComponentType.fieldName" or just "fieldName" (searches all)
                    var parts = fieldPath.Split(new[] { '.' }, 2);
                    object value = null;
                    var found = false;

                    if (parts.Length == 2)
                    {
                        var comp = go.GetComponent(parts[0]);
                        if (comp != null)
                        {
                            value = GetFieldOrProperty(comp, parts[1]);
                            found = true;
                        }
                    }
                    else
                    {
                        foreach (var comp in go.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            try
                            {
                                value = GetFieldOrProperty(comp, parts[0]);
                                found = true;
                                break;
                            }
                            catch { /* try next component */ }
                        }
                    }

                    result[fieldPath] = found ? SerializeValue(value) : "not_found";
                }
                catch (Exception ex)
                {
                    result[fieldPath] = $"error: {ex.Message}";
                }
            }

            return result;
        }

        private static object GetFieldOrProperty(object obj, string name)
        {
            var type = obj.GetType();
            var field = type.GetField(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (field != null)
                return field.GetValue(obj);

            var prop = type.GetProperty(name,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (prop != null)
                return prop.GetValue(obj);

            throw new Exception($"Field/property '{name}' not found on {type.Name}");
        }

        private static object SerializeValue(object value)
        {
            if (value == null) return null;
            if (value is Vector3 v3) return Vec3ToList(v3);
            if (value is Vector2 v2) return new List<object> { v2.x, v2.y };
            if (value is Quaternion q) return new List<object> { q.x, q.y, q.z, q.w };
            if (value is Color c) return new List<object> { c.r, c.g, c.b, c.a };
            if (value is bool || value is int || value is float || value is double || value is long || value is string)
                return value;
            if (value is Enum) return value.ToString();
            if (value is UnityEngine.Object uObj) return uObj != null ? uObj.name : null;
            return value.ToString();
        }

        private static List<object> Vec3ToList(Vector3 v)
        {
            return new List<object>
            {
                (float)Math.Round(v.x, 4),
                (float)Math.Round(v.y, 4),
                (float)Math.Round(v.z, 4)
            };
        }

        // ──────────────────────────────────────────────────────────────
        // capture_gameplay
        // ──────────────────────────────────────────────────────────────

        private async Task<CommandResult> ExecuteCaptureGameplay(string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var duration = p.GetFloat("duration") ?? 5f;
            var intervalSec = p.GetFloat("interval") ?? 1f;
            var maxResolution = p.GetInt("max_resolution") ?? 640;
            var includeState = p.GetBool("include_state") ?? true;

            if (!EditorApplication.isPlaying)
                return new CommandResult { success = false, error = "Unity is not in Play mode. Enter Play mode first." };

            var frameCount = Mathf.Max(1, Mathf.FloorToInt(duration / intervalSec));
            var captures = new List<Dictionary<string, object>>();
            var startTime = EditorApplication.timeSinceStartup;

            for (int i = 0; i < frameCount; i++)
            {
                if (!EditorApplication.isPlaying) break;

                // Wait for interval
                if (i > 0)
                {
                    var targetTime = startTime + (i * intervalSec);
                    while (EditorApplication.timeSinceStartup < targetTime && EditorApplication.isPlaying)
                    {
                        await Task.Delay(50);
                    }
                }

                // Capture on main thread
                var capture = await CaptureFrameOnMainThread(maxResolution, includeState, i);
                captures.Add(capture);
            }

            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "captures", captures },
                    { "frame_count", captures.Count },
                    { "duration", duration },
                    { "interval", intervalSec }
                }
            };
        }

        private Task<Dictionary<string, object>> CaptureFrameOnMainThread(int maxResolution, bool includeState, int index)
        {
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try
                {
                    var frame = new Dictionary<string, object>
                    {
                        { "index", index },
                        { "time", EditorApplication.isPlaying ? Time.time : 0f }
                    };

                    // Capture screenshot
                    Camera camera = Camera.main;
                    if (camera == null)
                    {
                        var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                        camera = cameras.FirstOrDefault();
                    }

                    if (camera != null)
                    {
                        var screenshotData = CaptureFromCamera(camera, maxResolution);
                        if (screenshotData != null)
                        {
                            frame["image_base64"] = screenshotData["image_base64"];
                            frame["width"] = screenshotData["width"];
                            frame["height"] = screenshotData["height"];
                        }
                    }

                    if (includeState)
                    {
                        frame["fps"] = Time.deltaTime > 0 ? Mathf.Round(1f / Time.deltaTime) : 0f;
                        frame["frame_count"] = Time.frameCount;

                        var logs = ConsoleLogCapture.Instance.GetLogs(
                            new List<string> { "error", "warning" }, 5, false, "summary");
                        if (logs.Count > 0)
                            frame["recent_errors"] = logs;
                    }

                    tcs.SetResult(frame);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new Dictionary<string, object>
                    {
                        { "index", index },
                        { "error", ex.Message }
                    });
                }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private static Dictionary<string, object> CaptureFromCamera(Camera camera, int maxResolution)
        {
            int width = Mathf.Max(1, camera.pixelWidth > 0 ? camera.pixelWidth : Screen.width);
            int height = Mathf.Max(1, camera.pixelHeight > 0 ? camera.pixelHeight : Screen.height);

            RenderTexture prevRT = camera.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            Texture2D downscaled = null;

            try
            {
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                int imgW = width, imgH = height;
                string base64;
                if (width > maxResolution || height > maxResolution)
                {
                    float scale = Mathf.Min((float)maxResolution / width, (float)maxResolution / height);
                    int dstW = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                    int dstH = Mathf.Max(1, Mathf.RoundToInt(height * scale));

                    var scaleRT = RenderTexture.GetTemporary(dstW, dstH, 0, RenderTextureFormat.ARGB32);
                    scaleRT.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(tex, scaleRT);
                    RenderTexture.active = scaleRT;
                    downscaled = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                    downscaled.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                    downscaled.Apply();
                    RenderTexture.ReleaseTemporary(scaleRT);

                    base64 = Convert.ToBase64String(downscaled.EncodeToPNG());
                    imgW = dstW;
                    imgH = dstH;
                }
                else
                {
                    base64 = Convert.ToBase64String(tex.EncodeToPNG());
                }

                return new Dictionary<string, object>
                {
                    { "image_base64", base64 },
                    { "width", imgW },
                    { "height", imgH }
                };
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                if (tex != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
                    else UnityEngine.Object.DestroyImmediate(tex);
                }
                if (downscaled != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(downscaled);
                    else UnityEngine.Object.DestroyImmediate(downscaled);
                }
            }
        }
    }
}
