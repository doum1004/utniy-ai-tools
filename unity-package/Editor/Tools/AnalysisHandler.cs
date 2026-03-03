using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles scene analysis, object inspection, and project settings retrieval
    /// for AI-driven feedback and game design planning.
    /// </summary>
    public class AnalysisHandler : IToolHandler
    {
        public Task<CommandResult> ExecuteAsync(string commandName, string paramsJson)
        {
            var tcs = new TaskCompletionSource<CommandResult>();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                try { tcs.SetResult(Execute(commandName, paramsJson)); }
                catch (Exception ex) { tcs.SetResult(new CommandResult { success = false, error = ex.Message }); }
            };
            EditorApplication.update += callback;
            return tcs.Task;
        }

        private CommandResult Execute(string commandName, string paramsJson)
        {
            var p = new ToolParams(paramsJson);

            switch (commandName)
            {
                case "analyze_scene": return HandleAnalyzeScene(p);
                case "inspect_gameobject": return HandleInspectGameObject(p);
                case "get_project_settings": return HandleGetProjectSettings(p);
                default:
                    return new CommandResult { success = false, error = $"Unknown command: {commandName}" };
            }
        }

        // ───────────────────────── analyze_scene ─────────────────────────

        private CommandResult HandleAnalyzeScene(ToolParams p)
        {
            var includeDetails = p.GetBool("include_details") ?? false;
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            var allTransforms = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            var allRenderers = GameObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var allColliders = GameObject.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var allColliders2D = GameObject.FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            var allLights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var allCameras = GameObject.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            var allCanvases = GameObject.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var allAudioSources = GameObject.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var allRigidbodies = GameObject.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            var allRigidbodies2D = GameObject.FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None);

            // Count totals
            int totalObjects = allTransforms.Length;
            int activeObjects = allTransforms.Count(t => t.gameObject.activeInHierarchy);
            int inactiveObjects = totalObjects - activeObjects;

            // Hierarchy depth
            int maxDepth = 0;
            string deepestPath = "";
            foreach (var t in allTransforms)
            {
                int depth = GetDepth(t);
                if (depth > maxDepth)
                {
                    maxDepth = depth;
                    deepestPath = GetPath(t);
                }
            }

            // Materials
            var materialSet = new HashSet<Material>();
            int totalTriangles = 0;
            int totalVertices = 0;
            foreach (var r in allRenderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat != null) materialSet.Add(mat);
                }
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    totalTriangles += mf.sharedMesh.triangles.Length / 3;
                    totalVertices += mf.sharedMesh.vertexCount;
                }
            }

            // Missing references check
            var missingRefObjects = new List<string>();
            foreach (var t in allTransforms)
            {
                var components = t.GetComponents<Component>();
                foreach (var c in components)
                {
                    if (c == null)
                    {
                        missingRefObjects.Add(GetPath(t));
                        break;
                    }
                }
            }

            // Lights breakdown
            var lightBreakdown = new Dictionary<string, int>();
            foreach (var light in allLights)
            {
                var typeName = light.type.ToString();
                lightBreakdown[typeName] = lightBreakdown.GetValueOrDefault(typeName, 0) + 1;
            }

            var data = new Dictionary<string, object>
            {
                { "scene_name", scene.name },
                { "scene_path", scene.path },
                { "total_gameobjects", totalObjects },
                { "active_gameobjects", activeObjects },
                { "inactive_gameobjects", inactiveObjects },
                { "root_objects", rootObjects.Length },
                { "max_hierarchy_depth", maxDepth },
                { "deepest_path", deepestPath },
                { "total_triangles", totalTriangles },
                { "total_vertices", totalVertices },
                { "renderer_count", allRenderers.Length },
                { "unique_materials", materialSet.Count },
                { "light_count", allLights.Length },
                { "light_breakdown", lightBreakdown },
                { "camera_count", allCameras.Length },
                { "canvas_count", allCanvases.Length },
                { "collider_count", allColliders.Length + allColliders2D.Length },
                { "rigidbody_count", allRigidbodies.Length + allRigidbodies2D.Length },
                { "audio_source_count", allAudioSources.Length },
                { "missing_component_objects", missingRefObjects },
                { "missing_component_count", missingRefObjects.Count },
                { "is_2d_scene", allColliders2D.Length > allColliders.Length || allRigidbodies2D.Length > allRigidbodies.Length }
            };

            if (includeDetails)
            {
                // Root object names
                data["root_object_names"] = rootObjects.Select(go => go.name).ToList();

                // Camera details
                var cameraDetails = new List<Dictionary<string, object>>();
                foreach (var cam in allCameras)
                {
                    cameraDetails.Add(new Dictionary<string, object>
                    {
                        { "name", cam.gameObject.name },
                        { "path", GetPath(cam.transform) },
                        { "projection", cam.orthographic ? "orthographic" : "perspective" },
                        { "depth", cam.depth },
                        { "fov", cam.fieldOfView },
                        { "is_main", cam == Camera.main }
                    });
                }
                data["camera_details"] = cameraDetails;

                // Light details
                var lightDetails = new List<Dictionary<string, object>>();
                foreach (var light in allLights)
                {
                    lightDetails.Add(new Dictionary<string, object>
                    {
                        { "name", light.gameObject.name },
                        { "type", light.type.ToString() },
                        { "intensity", light.intensity },
                        { "color", new float[] { light.color.r, light.color.g, light.color.b, light.color.a } },
                        { "shadows", light.shadows.ToString() },
                        { "is_active", light.gameObject.activeInHierarchy }
                    });
                }
                data["light_details"] = lightDetails;
            }

            return new CommandResult { success = true, data = data };
        }

        // ───────────────────────── inspect_gameobject ─────────────────────────

        private CommandResult HandleInspectGameObject(ToolParams p)
        {
            var target = p.RequireString("target");
            var go = GameObject.Find(target);
            if (go == null)
                return new CommandResult { success = false, error = $"GameObject not found: {target}" };

            var components = go.GetComponents<Component>();
            var componentList = new List<Dictionary<string, object>>();
            var issues = new List<string>();

            // Check for missing/null components
            int nullComponents = 0;
            foreach (var c in components)
            {
                if (c == null)
                {
                    nullComponents++;
                    issues.Add("Missing (null) component detected — likely a deleted script reference");
                    continue;
                }

                var info = new Dictionary<string, object>
                {
                    { "type", c.GetType().Name },
                    { "full_type", c.GetType().FullName },
                    { "enabled", IsComponentEnabled(c) }
                };

                // Gather serialized properties with null/missing refs
                var so = new SerializedObject(c);
                var prop = so.GetIterator();
                var nullFields = new List<string>();
                var fieldSummary = new Dictionary<string, object>();

                if (prop.NextVisible(true))
                {
                    do
                    {
                        if (prop.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            if (prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0)
                            {
                                nullFields.Add(prop.displayName);
                            }
                            else
                            {
                                fieldSummary[prop.displayName] = prop.objectReferenceValue != null
                                    ? prop.objectReferenceValue.name
                                    : "None";
                            }
                        }
                    } while (prop.NextVisible(false));
                }

                if (nullFields.Count > 0)
                {
                    info["missing_references"] = nullFields;
                    issues.Add($"{c.GetType().Name} has missing references: {string.Join(", ", nullFields)}");
                }

                info["reference_fields"] = fieldSummary;
                componentList.Add(info);
            }

            // Transform analysis
            var transform = go.transform;
            var scale = transform.localScale;
            if (scale.x < 0 || scale.y < 0 || scale.z < 0)
                issues.Add($"Negative scale detected: ({scale.x}, {scale.y}, {scale.z})");
            if (Mathf.Approximately(scale.x, 0) || Mathf.Approximately(scale.y, 0) || Mathf.Approximately(scale.z, 0))
                issues.Add($"Zero scale on one or more axes: ({scale.x}, {scale.y}, {scale.z})");
            float scaleRatio = Mathf.Max(scale.x, scale.y, scale.z) / Mathf.Max(0.0001f, Mathf.Min(scale.x, scale.y, scale.z));
            if (scaleRatio > 100f)
                issues.Add($"Extreme non-uniform scale ratio: {scaleRatio:F1}x");

            // Static flags
            var staticFlags = GameObjectUtility.GetStaticEditorFlags(go);

            // Children summary
            int childCount = transform.childCount;
            int totalDescendants = transform.GetComponentsInChildren<Transform>(true).Length - 1;

            var data = new Dictionary<string, object>
            {
                { "name", go.name },
                { "path", GetPath(transform) },
                { "tag", go.tag },
                { "layer", LayerMask.LayerToName(go.layer) },
                { "is_active", go.activeSelf },
                { "is_active_in_hierarchy", go.activeInHierarchy },
                { "is_static", go.isStatic },
                { "static_flags", staticFlags.ToString() },
                { "position", new float[] { transform.position.x, transform.position.y, transform.position.z } },
                { "rotation", new float[] { transform.eulerAngles.x, transform.eulerAngles.y, transform.eulerAngles.z } },
                { "local_scale", new float[] { scale.x, scale.y, scale.z } },
                { "components", componentList },
                { "component_count", componentList.Count },
                { "null_component_count", nullComponents },
                { "direct_children", childCount },
                { "total_descendants", totalDescendants },
                { "issues", issues },
                { "issue_count", issues.Count }
            };

            // Check if it's a prefab instance
            var prefabStatus = PrefabUtility.GetPrefabInstanceStatus(go);
            if (prefabStatus != PrefabInstanceStatus.NotAPrefab)
            {
                data["is_prefab_instance"] = true;
                data["prefab_status"] = prefabStatus.ToString();
                var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabAsset != null)
                    data["prefab_asset"] = AssetDatabase.GetAssetPath(prefabAsset);
            }
            else
            {
                data["is_prefab_instance"] = false;
            }

            return new CommandResult { success = true, data = data };
        }

        // ───────────────────────── get_project_settings ─────────────────────────

        private CommandResult HandleGetProjectSettings(ToolParams p)
        {
            var categories = p.GetStringList("categories");
            bool all = categories == null || categories.Count == 0;

            var data = new Dictionary<string, object>();

            if (all || categories.Contains("physics"))
            {
                data["physics"] = new Dictionary<string, object>
                {
                    { "gravity", new float[] { Physics.gravity.x, Physics.gravity.y, Physics.gravity.z } },
                    { "default_solver_iterations", Physics.defaultSolverIterations },
                    { "default_solver_velocity_iterations", Physics.defaultSolverVelocityIterations },
                    { "bounce_threshold", Physics.bounceThreshold },
                    { "default_contact_offset", Physics.defaultContactOffset },
                    { "auto_simulation", Physics.simulationMode.ToString() }
                };
            }

            if (all || categories.Contains("physics2d"))
            {
                data["physics2d"] = new Dictionary<string, object>
                {
                    { "gravity", new float[] { Physics2D.gravity.x, Physics2D.gravity.y } },
                    { "default_contact_offset", Physics2D.defaultContactOffset },
                    { "velocity_iterations", Physics2D.velocityIterations },
                    { "position_iterations", Physics2D.positionIterations }
                };
            }

            if (all || categories.Contains("quality"))
            {
                data["quality"] = new Dictionary<string, object>
                {
                    { "current_level", QualitySettings.names[QualitySettings.GetQualityLevel()] },
                    { "all_levels", new List<string>(QualitySettings.names) },
                    { "shadow_resolution", QualitySettings.shadowResolution.ToString() },
                    { "shadow_distance", QualitySettings.shadowDistance },
                    { "anti_aliasing", QualitySettings.antiAliasing },
                    { "vsync_count", QualitySettings.vSyncCount },
                    { "texture_quality", QualitySettings.globalTextureMipmapLimit },
                    { "anisotropic_filtering", QualitySettings.anisotropicFiltering.ToString() }
                };
            }

            if (all || categories.Contains("rendering"))
            {
                data["rendering"] = new Dictionary<string, object>
                {
                    { "color_space", PlayerSettings.colorSpace.ToString() },
                    { "render_pipeline", GraphicsSettings.currentRenderPipeline != null
                        ? GraphicsSettings.currentRenderPipeline.name
                        : "Built-in" },
                    { "target_frame_rate", Application.targetFrameRate }
                };
            }

            if (all || categories.Contains("player"))
            {
                data["player"] = new Dictionary<string, object>
                {
                    { "product_name", PlayerSettings.productName },
                    { "company_name", PlayerSettings.companyName },
                    { "version", PlayerSettings.bundleVersion },
                    { "target_platform", EditorUserBuildSettings.activeBuildTarget.ToString() },
                    { "scripting_backend", PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() },
                    { "api_compatibility", PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString() }
                };
            }

            if (all || categories.Contains("time"))
            {
                data["time"] = new Dictionary<string, object>
                {
                    { "fixed_timestep", Time.fixedDeltaTime },
                    { "maximum_delta_time", Time.maximumDeltaTime },
                    { "time_scale", Time.timeScale }
                };
            }

            if (all || categories.Contains("audio"))
            {
                var audioConfig = AudioSettings.GetConfiguration();
                data["audio"] = new Dictionary<string, object>
                {
                    { "speaker_mode", audioConfig.speakerMode.ToString() },
                    { "sample_rate", audioConfig.sampleRate },
                    { "dsp_buffer_size", audioConfig.dspBufferSize }
                };
            }

            return new CommandResult { success = true, data = data };
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static int GetDepth(Transform t)
        {
            int depth = 0;
            while (t.parent != null) { depth++; t = t.parent; }
            return depth;
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static bool IsComponentEnabled(Component c)
        {
            if (c is Behaviour b) return b.enabled;
            if (c is Renderer r) return r.enabled;
            if (c is Collider col) return col.enabled;
            return true;
        }
    }
}
