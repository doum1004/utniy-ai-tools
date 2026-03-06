using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityAITools.Editor.Transport;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tools
{
    /// <summary>
    /// Handles performance analysis — memory stats, rendering costs, texture/mesh audits,
    /// lighting overhead, physics complexity, and audio memory. Returns categorized issues
    /// with severity and actionable suggestions.
    /// </summary>
    public class PerformanceHandler : IToolHandler
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
            var categories = p.GetStringList("categories");
            bool all = categories == null || categories.Count == 0;

            var data = new Dictionary<string, object>();
            var issues = new List<Dictionary<string, object>>();

            if (all || categories.Contains("memory"))
                data["memory"] = GatherMemoryStats();

            if (all || categories.Contains("rendering"))
            {
                var (renderData, renderIssues) = GatherRenderingAnalysis();
                data["rendering"] = renderData;
                issues.AddRange(renderIssues);
            }

            if (all || categories.Contains("textures"))
            {
                var (texData, texIssues) = GatherTextureAnalysis();
                data["textures"] = texData;
                issues.AddRange(texIssues);
            }

            if (all || categories.Contains("meshes"))
            {
                var (meshData, meshIssues) = GatherMeshAnalysis();
                data["meshes"] = meshData;
                issues.AddRange(meshIssues);
            }

            if (all || categories.Contains("lighting"))
            {
                var (lightData, lightIssues) = GatherLightingAnalysis();
                data["lighting"] = lightData;
                issues.AddRange(lightIssues);
            }

            if (all || categories.Contains("physics"))
            {
                var (physData, physIssues) = GatherPhysicsAnalysis();
                data["physics"] = physData;
                issues.AddRange(physIssues);
            }

            if (all || categories.Contains("audio"))
            {
                var (audioData, audioIssues) = GatherAudioAnalysis();
                data["audio"] = audioData;
                issues.AddRange(audioIssues);
            }

            var severityOrder = new Dictionary<string, int> { { "high", 0 }, { "medium", 1 }, { "low", 2 } };
            issues.Sort((a, b) =>
            {
                var sa = severityOrder.GetValueOrDefault((string)a["severity"], 9);
                var sb = severityOrder.GetValueOrDefault((string)b["severity"], 9);
                return sa.CompareTo(sb);
            });

            data["issues"] = issues;
            data["issue_count"] = issues.Count;
            data["high_severity_count"] = issues.Count(i => (string)i["severity"] == "high");
            data["medium_severity_count"] = issues.Count(i => (string)i["severity"] == "medium");
            data["is_playing"] = Application.isPlaying;

            return new CommandResult { success = true, data = data };
        }

        // ───────────────────────── Memory ─────────────────────────

        private static Dictionary<string, object> GatherMemoryStats()
        {
            return new Dictionary<string, object>
            {
                { "total_allocated_mb", Math.Round(Profiler.GetTotalAllocatedMemoryLong() / (1024.0 * 1024.0), 2) },
                { "total_reserved_mb", Math.Round(Profiler.GetTotalReservedMemoryLong() / (1024.0 * 1024.0), 2) },
                { "total_unused_reserved_mb", Math.Round(Profiler.GetTotalUnusedReservedMemoryLong() / (1024.0 * 1024.0), 2) },
                { "mono_heap_mb", Math.Round(Profiler.GetMonoHeapSizeLong() / (1024.0 * 1024.0), 2) },
                { "mono_used_mb", Math.Round(Profiler.GetMonoUsedSizeLong() / (1024.0 * 1024.0), 2) },
                { "gfx_allocated_mb", Math.Round(Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024.0 * 1024.0), 2) },
            };
        }

        // ───────────────────────── Rendering ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherRenderingAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var renderers = GameObject.FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            int totalTris = 0;
            int totalVerts = 0;
            int multiMatCount = 0;
            var materialSet = new HashSet<Material>();
            var shaderSet = new HashSet<string>();
            var expensiveObjects = new List<Dictionary<string, object>>();

            foreach (var r in renderers)
            {
                if (!r.gameObject.activeInHierarchy) continue;

                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    materialSet.Add(mat);
                    if (mat.shader != null) shaderSet.Add(mat.shader.name);
                }

                var mf = r.GetComponent<MeshFilter>();
                int tris = 0, verts = 0;
                if (mf != null && mf.sharedMesh != null)
                {
                    tris = mf.sharedMesh.triangles.Length / 3;
                    verts = mf.sharedMesh.vertexCount;
                    totalTris += tris;
                    totalVerts += verts;
                }

                if (r.sharedMaterials.Length > 1)
                    multiMatCount++;

                if (tris > 10000)
                {
                    expensiveObjects.Add(new Dictionary<string, object>
                    {
                        { "name", r.gameObject.name },
                        { "path", GetPath(r.transform) },
                        { "triangles", tris },
                        { "vertices", verts },
                        { "materials", r.sharedMaterials.Length },
                    });
                }
            }

            expensiveObjects.Sort((a, b) => ((int)b["triangles"]).CompareTo((int)a["triangles"]));
            if (expensiveObjects.Count > 20)
                expensiveObjects = expensiveObjects.GetRange(0, 20);

            var multiMatDetails = renderers
                .Where(r => r.gameObject.activeInHierarchy && r.sharedMaterials.Length > 3)
                .Select(r => new Dictionary<string, object>
                {
                    { "name", r.gameObject.name },
                    { "path", GetPath(r.transform) },
                    { "material_count", r.sharedMaterials.Length },
                })
                .Take(10)
                .ToList();

            if (totalTris > 500000)
                issues.Add(Issue("high", "rendering", $"High triangle count: {totalTris:N0}", "Reduce mesh detail, use LODs, or enable occlusion culling"));
            else if (totalTris > 200000)
                issues.Add(Issue("medium", "rendering", $"Moderate triangle count: {totalTris:N0}", "Consider LOD groups for distant objects"));

            if (materialSet.Count > 100)
                issues.Add(Issue("medium", "rendering", $"{materialSet.Count} unique materials — high draw call potential", "Use material atlasing or shared materials where possible"));

            if (multiMatDetails.Count > 0)
                issues.Add(Issue("medium", "rendering", $"{multiMatDetails.Count} objects have 4+ materials (extra draw calls each)", "Combine materials or split meshes"));

            var data = new Dictionary<string, object>
            {
                { "total_triangles", totalTris },
                { "total_vertices", totalVerts },
                { "active_renderer_count", renderers.Count(r => r.gameObject.activeInHierarchy) },
                { "unique_materials", materialSet.Count },
                { "unique_shaders", shaderSet.Count },
                { "multi_material_renderers", multiMatCount },
                { "expensive_objects", expensiveObjects },
            };

            if (multiMatDetails.Count > 0)
                data["multi_material_details"] = multiMatDetails;

            return (data, issues);
        }

        // ───────────────────────── Textures ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherTextureAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var texGuids = AssetDatabase.FindAssets("t:Texture2D");
            int totalCount = texGuids.Length;
            long totalSizeBytes = 0;
            var oversized = new List<Dictionary<string, object>>();
            var uncompressed = new List<Dictionary<string, object>>();

            foreach (var guid in texGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) continue;

                long sizeBytes = Profiler.GetRuntimeMemorySizeLong(tex);
                totalSizeBytes += sizeBytes;

                if (tex.width > 2048 || tex.height > 2048)
                {
                    oversized.Add(new Dictionary<string, object>
                    {
                        { "path", path },
                        { "size", $"{tex.width}x{tex.height}" },
                        { "memory_mb", Math.Round(sizeBytes / (1024.0 * 1024.0), 2) },
                        { "max_size", importer.maxTextureSize },
                    });
                }

                var platformSettings = importer.GetDefaultPlatformTextureSettings();
                if (platformSettings.format == TextureImporterFormat.RGBA32 ||
                    platformSettings.format == TextureImporterFormat.RGB24)
                {
                    uncompressed.Add(new Dictionary<string, object>
                    {
                        { "path", path },
                        { "size", $"{tex.width}x{tex.height}" },
                        { "format", platformSettings.format.ToString() },
                        { "memory_mb", Math.Round(sizeBytes / (1024.0 * 1024.0), 2) },
                    });
                }
            }

            oversized.Sort((a, b) => ((double)b["memory_mb"]).CompareTo((double)a["memory_mb"]));
            uncompressed.Sort((a, b) => ((double)b["memory_mb"]).CompareTo((double)a["memory_mb"]));

            if (oversized.Count > 0)
                issues.Add(Issue("medium", "textures", $"{oversized.Count} textures exceed 2048px — high memory usage", "Reduce max size in import settings or use mipmaps"));
            if (uncompressed.Count > 0)
                issues.Add(Issue("high", "textures", $"{uncompressed.Count} uncompressed textures (RGBA32/RGB24)", "Switch to compressed format (DXT, ASTC, ETC2) in import settings"));

            return (new Dictionary<string, object>
            {
                { "total_textures", totalCount },
                { "total_memory_mb", Math.Round(totalSizeBytes / (1024.0 * 1024.0), 2) },
                { "oversized_textures", oversized.Take(10).ToList() },
                { "oversized_count", oversized.Count },
                { "uncompressed_textures", uncompressed.Take(10).ToList() },
                { "uncompressed_count", uncompressed.Count },
            }, issues);
        }

        // ───────────────────────── Meshes ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherMeshAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var renderers = GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);

            int staticCount = 0;
            int nonStaticCount = 0;
            var shouldBeStatic = new List<Dictionary<string, object>>();
            var readWriteMeshes = new List<Dictionary<string, object>>();
            var checkedMeshes = new HashSet<int>();

            foreach (var r in renderers)
            {
                if (!r.gameObject.activeInHierarchy) continue;
                var go = r.gameObject;

                if (GameObjectUtility.GetStaticEditorFlags(go).HasFlag(StaticEditorFlags.BatchingStatic))
                    staticCount++;
                else
                    nonStaticCount++;

                bool hasMotion = go.GetComponent<Rigidbody>() != null ||
                                 go.GetComponent<Rigidbody2D>() != null ||
                                 go.GetComponent<Animator>() != null ||
                                 go.GetComponentInParent<Animator>() != null;
                if (!hasMotion && !go.isStatic)
                {
                    shouldBeStatic.Add(new Dictionary<string, object>
                    {
                        { "name", go.name },
                        { "path", GetPath(go.transform) },
                    });
                }

                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    int meshId = mf.sharedMesh.GetInstanceID();
                    if (!checkedMeshes.Contains(meshId))
                    {
                        checkedMeshes.Add(meshId);
                        if (mf.sharedMesh.isReadable)
                        {
                            readWriteMeshes.Add(new Dictionary<string, object>
                            {
                                { "mesh_name", mf.sharedMesh.name },
                                { "vertex_count", mf.sharedMesh.vertexCount },
                                { "used_by", go.name },
                            });
                        }
                    }
                }
            }

            if (shouldBeStatic.Count > 5)
                issues.Add(Issue("medium", "meshes", $"{shouldBeStatic.Count} rendered objects are non-static with no movement components", "Mark as Static for batching/lightmap/navigation benefits"));

            if (readWriteMeshes.Count > 10)
                issues.Add(Issue("low", "meshes", $"{readWriteMeshes.Count} meshes have Read/Write enabled — doubles memory", "Disable Read/Write in mesh import settings unless needed at runtime"));

            return (new Dictionary<string, object>
            {
                { "static_batching_count", staticCount },
                { "non_static_count", nonStaticCount },
                { "should_be_static", shouldBeStatic.Take(15).ToList() },
                { "should_be_static_count", shouldBeStatic.Count },
                { "read_write_meshes", readWriteMeshes.Take(10).ToList() },
                { "read_write_mesh_count", readWriteMeshes.Count },
            }, issues);
        }

        // ───────────────────────── Lighting ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherLightingAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var lights = GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None);
            var activeLights = lights.Where(l => l.gameObject.activeInHierarchy).ToArray();

            int realtimeShadowCount = 0;
            int pointCount = 0, spotCount = 0, directionalCount = 0;
            var expensiveLights = new List<Dictionary<string, object>>();

            foreach (var light in activeLights)
            {
                switch (light.type)
                {
                    case LightType.Point: pointCount++; break;
                    case LightType.Spot: spotCount++; break;
                    case LightType.Directional: directionalCount++; break;
                }

                if (light.shadows != LightShadows.None)
                {
                    realtimeShadowCount++;
                    if (light.type != LightType.Directional)
                    {
                        expensiveLights.Add(new Dictionary<string, object>
                        {
                            { "name", light.gameObject.name },
                            { "path", GetPath(light.transform) },
                            { "type", light.type.ToString() },
                            { "shadow_type", light.shadows.ToString() },
                            { "range", light.range },
                        });
                    }
                }
            }

            if (realtimeShadowCount > 3)
                issues.Add(Issue("high", "lighting", $"{realtimeShadowCount} lights cast real-time shadows", "Reduce to 1-2 shadow casters; bake shadows for static geometry"));

            if (pointCount > 8)
                issues.Add(Issue("medium", "lighting", $"{pointCount} active point lights", "Reduce count or use light probes"));

            if (directionalCount > 1)
                issues.Add(Issue("low", "lighting", $"{directionalCount} directional lights (usually only 1 needed)", "Remove extra directional lights"));

            return (new Dictionary<string, object>
            {
                { "total_active_lights", activeLights.Length },
                { "directional", directionalCount },
                { "point", pointCount },
                { "spot", spotCount },
                { "realtime_shadow_casters", realtimeShadowCount },
                { "expensive_shadow_lights", expensiveLights },
            }, issues);
        }

        // ───────────────────────── Physics ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherPhysicsAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var rigidbodies = GameObject.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            var colliders = GameObject.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var meshColliders = GameObject.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);

            int activeRbs = rigidbodies.Count(rb => rb.gameObject.activeInHierarchy);
            int convexMC = meshColliders.Count(mc => mc.convex && mc.gameObject.activeInHierarchy);
            int nonConvexMC = meshColliders.Count(mc => !mc.convex && mc.gameObject.activeInHierarchy);

            var nonConvexOnRb = meshColliders
                .Where(mc => mc.gameObject.activeInHierarchy && !mc.convex && mc.GetComponent<Rigidbody>() != null)
                .Select(mc => new Dictionary<string, object>
                {
                    { "name", mc.gameObject.name },
                    { "path", GetPath(mc.transform) },
                    { "vertex_count", mc.sharedMesh != null ? mc.sharedMesh.vertexCount : 0 },
                })
                .ToList();

            if (nonConvexOnRb.Count > 0)
                issues.Add(Issue("high", "physics", $"{nonConvexOnRb.Count} non-convex MeshColliders on Rigidbodies", "Use convex MeshColliders or primitive colliders on moving objects"));

            if (nonConvexMC > 10)
                issues.Add(Issue("medium", "physics", $"{nonConvexMC} non-convex MeshColliders in scene", "Replace with primitive colliders or convex meshes where possible"));

            if (activeRbs > 100)
                issues.Add(Issue("medium", "physics", $"{activeRbs} active Rigidbodies", "Use object pooling or deactivate distant physics objects"));

            return (new Dictionary<string, object>
            {
                { "active_rigidbodies", activeRbs },
                { "total_colliders", colliders.Count(c => c.gameObject.activeInHierarchy) },
                { "mesh_colliders_convex", convexMC },
                { "mesh_colliders_non_convex", nonConvexMC },
                { "non_convex_on_rigidbody", nonConvexOnRb },
                { "solver_iterations", Physics.defaultSolverIterations },
                { "solver_velocity_iterations", Physics.defaultSolverVelocityIterations },
            }, issues);
        }

        // ───────────────────────── Audio ─────────────────────────

        private static (Dictionary<string, object>, List<Dictionary<string, object>>) GatherAudioAnalysis()
        {
            var issues = new List<Dictionary<string, object>>();
            var audioSources = GameObject.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var activeAudio = audioSources.Where(a => a.gameObject.activeInHierarchy).ToArray();

            var clipGuids = AssetDatabase.FindAssets("t:AudioClip");
            long totalAudioBytes = 0;
            var largeClips = new List<Dictionary<string, object>>();

            foreach (var guid in clipGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;

                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                long sizeBytes = Profiler.GetRuntimeMemorySizeLong(clip);
                totalAudioBytes += sizeBytes;

                if (sizeBytes > 5 * 1024 * 1024)
                {
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    largeClips.Add(new Dictionary<string, object>
                    {
                        { "path", path },
                        { "memory_mb", Math.Round(sizeBytes / (1024.0 * 1024.0), 2) },
                        { "length_seconds", Math.Round(clip.length, 1) },
                        { "load_type", importer?.defaultSampleSettings.loadType.ToString() ?? "unknown" },
                    });
                }
            }

            largeClips.Sort((a, b) => ((double)b["memory_mb"]).CompareTo((double)a["memory_mb"]));

            if (largeClips.Count > 0)
                issues.Add(Issue("medium", "audio", $"{largeClips.Count} audio clips use >5MB each", "Use Streaming or Compressed In Memory load type for large clips"));

            return (new Dictionary<string, object>
            {
                { "active_audio_sources", activeAudio.Length },
                { "total_audio_clips", clipGuids.Length },
                { "total_audio_memory_mb", Math.Round(totalAudioBytes / (1024.0 * 1024.0), 2) },
                { "large_clips", largeClips.Take(10).ToList() },
            }, issues);
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static Dictionary<string, object> Issue(string severity, string category, string description, string suggestion)
        {
            return new Dictionary<string, object>
            {
                { "severity", severity },
                { "category", category },
                { "description", description },
                { "suggestion", suggestion },
            };
        }

        private static string GetPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
