using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles MCP commands for scene management — hierarchy, load, save, screenshots.
    /// </summary>
    public class SceneHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try { tcs.SetResult(Execute(paramsJson)); }
                catch (Exception ex) { tcs.SetResult(new CommandResult { success = false, error = ex.Message }); }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string paramsJson)
        {
            var p = new ToolParams(paramsJson);
            var action = p.RequireString("action");

            switch (action)
            {
                case "get_hierarchy": return GetHierarchy(p);
                case "get_active": return GetActiveScene();
                case "load": return LoadScene(p);
                case "save": return SaveScene();
                case "screenshot": return CaptureScreenshot(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown scene action: {action}" };
            }
        }

        private CommandResult GetHierarchy(ToolParams p)
        {
            var pageSize = p.GetInt("page_size") ?? p.GetInt("pageSize") ?? 50;
            var cursor = p.GetInt("cursor") ?? 0;

            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var items = new List<Dictionary<string, object>>();

            var startIndex = Math.Min(cursor, rootObjects.Length);
            var endIndex = Math.Min(startIndex + pageSize, rootObjects.Length);

            for (var i = startIndex; i < endIndex; i++)
            {
                items.Add(BuildHierarchyNode(rootObjects[i], 0));
            }

            var data = new Dictionary<string, object>
            {
                { "scene", scene.name },
                { "items", items },
                { "total", rootObjects.Length },
                { "cursor", cursor }
            };

            if (endIndex < rootObjects.Length)
                data["next_cursor"] = endIndex;

            return new CommandResult { success = true, data = data };
        }

        private static Dictionary<string, object> BuildHierarchyNode(GameObject go, int depth)
        {
            var node = new Dictionary<string, object>
            {
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                { "active", go.activeSelf },
                { "childCount", go.transform.childCount },
                { "depth", depth }
            };

            if (go.transform.childCount > 0 && depth < 3) // Limit depth
            {
                var children = new List<Dictionary<string, object>>();
                for (var i = 0; i < go.transform.childCount; i++)
                {
                    children.Add(BuildHierarchyNode(go.transform.GetChild(i).gameObject, depth + 1));
                }
                node["children"] = children;
            }

            return node;
        }

        private CommandResult GetActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            return new CommandResult
            {
                success = true,
                data = new Dictionary<string, object>
                {
                    { "name", scene.name },
                    { "path", scene.path },
                    { "isDirty", scene.isDirty },
                    { "rootCount", scene.rootCount }
                }
            };
        }

        private CommandResult LoadScene(ToolParams p)
        {
            var sceneName = p.RequireString("scene_name") ?? p.RequireString("sceneName");

            try
            {
                EditorSceneManager.OpenScene(sceneName);
                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object> { { "loaded", sceneName } }
                };
            }
            catch (Exception ex)
            {
                return new CommandResult { success = false, error = $"Failed to load scene: {ex.Message}" };
            }
        }

        private CommandResult SaveScene()
        {
            var scene = SceneManager.GetActiveScene();
            var saved = EditorSceneManager.SaveScene(scene);
            return new CommandResult
            {
                success = saved,
                data = new Dictionary<string, object>
                {
                    { "scene", scene.name },
                    { "saved", saved }
                }
            };
        }

        private CommandResult CaptureScreenshot(ToolParams p)
        {
            var maxResolution = p.GetInt("max_resolution") ?? p.GetInt("maxResolution") ?? 640;
            var cameraName = p.GetString("camera");

            // Find the camera to render from
            Camera camera = null;
            if (!string.IsNullOrEmpty(cameraName))
            {
                // Find by name or path
                var go = GameObject.Find(cameraName);
                if (go != null) camera = go.GetComponent<Camera>();
                if (camera == null)
                {
                    return new CommandResult { success = false, error = $"Camera '{cameraName}' not found" };
                }
            }
            else
            {
                camera = Camera.main;
                if (camera == null)
                {
                    // Fallback: find any camera
                    var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
                    camera = cameras.FirstOrDefault();
                }
            }

            // If no camera found, capture the Scene view instead
            if (camera == null)
            {
                return CaptureSceneView(maxResolution);
            }

            return CaptureFromCamera(camera, maxResolution);
        }

        private CommandResult CaptureFromCamera(Camera camera, int maxResolution)
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

                // Downscale if needed
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

                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "image_base64", base64 },
                        { "width", imgW },
                        { "height", imgH },
                        { "camera", camera.name },
                        { "source", "camera" }
                    }
                };
            }
            finally
            {
                camera.targetTexture = prevRT;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                DestroyTexture(tex);
                DestroyTexture(downscaled);
            }
        }

        private CommandResult CaptureSceneView(int maxResolution)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return new CommandResult { success = false, error = "No Scene view or Camera available to capture" };
            }

            // Force the scene view to render
            sceneView.Repaint();

            var sceneCamera = sceneView.camera;
            if (sceneCamera == null)
            {
                return new CommandResult { success = false, error = "Scene view camera not available" };
            }

            int width = (int)sceneView.position.width;
            int height = (int)sceneView.position.height;
            if (width <= 0 || height <= 0)
            {
                width = 800;
                height = 600;
            }

            RenderTexture prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D tex = null;
            Texture2D downscaled = null;

            try
            {
                var prevRT = sceneCamera.targetTexture;
                sceneCamera.targetTexture = rt;
                sceneCamera.Render();
                sceneCamera.targetTexture = prevRT;

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

                return new CommandResult
                {
                    success = true,
                    data = new Dictionary<string, object>
                    {
                        { "image_base64", base64 },
                        { "width", imgW },
                        { "height", imgH },
                        { "source", "scene_view" }
                    }
                };
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
                DestroyTexture(tex);
                DestroyTexture(downscaled);
            }
        }

        private static void DestroyTexture(Texture2D tex)
        {
            if (tex == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(tex);
            else
                UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
