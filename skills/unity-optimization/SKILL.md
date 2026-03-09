---
name: unity-optimization
description: Guide AI through Unity performance optimization — draw calls, batching, pooling, LOD, memory, and profiling workflows using analysis tools. Use when reviewing or improving scene/project performance.
---

# Optimization — Performance Review Skill

This skill guides you through identifying and fixing performance issues in Unity projects.

## Core Principle: Measure First, Optimize Second

Never optimize blindly. Always gather data before making changes.

```
1. analyze_performance() → full audit with prioritized issues and fix suggestions
2. analyze_scene(include_details=true) → scene-level stats
3. get_project_settings(categories=["quality", "rendering"]) → understand config
4. Fix issues starting from highest severity
5. Re-run analyze_performance() to verify improvement
```

## Performance Budget Guidelines

### Scene Complexity Targets

| Platform | Triangles | Draw Calls | Lights (Realtime) | Materials |
|----------|-----------|------------|--------------------|-----------|
| Mobile | < 100K | < 100 | < 4 | < 50 |
| Desktop | < 1M | < 500 | < 8 | < 200 |
| VR | < 200K | < 100 | < 4 | < 50 |

### How to Check

```
analyze_performance() returns per-category stats + issues:
- rendering → total_triangles, unique_materials, expensive_objects, multi_material_renderers
- lighting → realtime_shadow_casters, light counts by type, expensive_shadow_lights
- textures → uncompressed_count, oversized_count, total_memory_mb
- meshes → should_be_static_count, read_write_mesh_count
- physics → non_convex_on_rigidbody, active_rigidbodies, mesh_colliders_non_convex
- audio → large_clips, total_audio_memory_mb
- memory → total_allocated_mb, gfx_allocated_mb, mono_used_mb

Each issue includes severity (high/medium/low) and a concrete fix suggestion.

analyze_scene() returns scene-level counts:
- total_triangles, total_vertices, unique_materials
- light_count + light_breakdown
- renderer_count, missing_component_count
```

## Optimization Checklist

### Rendering

- [ ] **Static batching**: Mark non-moving objects as Static
  - `analyze_performance(categories=["meshes"])` → `should_be_static` lists objects to fix
  - Fix: `manage_gameobject(action="modify", target="...", static=true)`
- [ ] **Material consolidation**: Reduce unique material count
  - `analyze_performance(categories=["rendering"])` → `unique_materials`, `multi_material_details`
  - Objects sharing the same visual appearance should share materials
- [ ] **High-poly objects**: Reduce mesh detail or add LODs
  - `analyze_performance(categories=["rendering"])` → `expensive_objects` lists top offenders
- [ ] **Occlusion culling**: Enable for indoor/complex scenes
- [ ] **LOD groups**: Add for detailed meshes visible at varying distances
- [ ] **Light baking**: Bake static lights instead of realtime for static scenes
  - `analyze_performance(categories=["lighting"])` → `expensive_shadow_lights`, `realtime_shadow_casters`

### Physics

- [ ] **Collider complexity**: Use primitive colliders (Box, Sphere, Capsule) over MeshColliders
  - `analyze_performance(categories=["physics"])` → `non_convex_on_rigidbody` (high severity), `mesh_colliders_non_convex`
- [ ] **Physics settings**: Verify timestep and solver iterations
  - `get_project_settings(categories=["physics"])` → check fixed_timestep and solver iterations
- [ ] **Layer-based collision matrix**: Disable unnecessary collision pairs

### Scripts

- [ ] **No allocations in Update**: Avoid `new`, LINQ, string concatenation in hot paths
- [ ] **Cache component references**: `GetComponent` in Awake, not in Update
- [ ] **Object pooling**: Reuse frequently spawned/destroyed objects
- [ ] **Coroutine yields**: Cache `WaitForSeconds` instances

### Memory & Assets

- [ ] **Texture compression**: Fix uncompressed textures (RGBA32/RGB24)
  - `analyze_performance(categories=["textures"])` → `uncompressed_textures` (high severity)
- [ ] **Texture sizes**: Reduce oversized textures (>2048px)
  - `analyze_performance(categories=["textures"])` → `oversized_textures`, `total_memory_mb`
- [ ] **Mesh Read/Write**: Disable unless needed at runtime (doubles memory)
  - `analyze_performance(categories=["meshes"])` → `read_write_meshes`
- [ ] **Audio load types**: Use Streaming for large clips (>5MB)
  - `analyze_performance(categories=["audio"])` → `large_clips`
- [ ] **Resource unloading**: Call `Resources.UnloadUnusedAssets()` on scene transitions

## Object Pooling Pattern

When you see frequent Instantiate/Destroy patterns, suggest pooling:

```csharp
public class ObjectPool : MonoBehaviour
{
    [SerializeField] private GameObject prefab;
    [SerializeField] private int initialSize = 10;

    private readonly Queue<GameObject> _pool = new();

    void Awake()
    {
        for (int i = 0; i < initialSize; i++)
        {
            var obj = Instantiate(prefab, transform);
            obj.SetActive(false);
            _pool.Enqueue(obj);
        }
    }

    public GameObject Get(Vector3 position, Quaternion rotation)
    {
        var obj = _pool.Count > 0 ? _pool.Dequeue() : Instantiate(prefab, transform);
        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public void Return(GameObject obj)
    {
        obj.SetActive(false);
        _pool.Enqueue(obj);
    }
}
```

## Performance Review Workflow

Run this full review after building a scene or before a build:

```
Step 1: Full Performance Audit
  analyze_performance()
  → Review issues list sorted by severity (high → medium → low)
  → Each issue has a concrete suggestion — apply them in order
  → Note: covers rendering, textures, meshes, lighting, physics, audio, memory

Step 2: Project Settings Check
  get_project_settings(categories=["quality", "rendering", "physics"])
  → Verify shadow distance isn't excessive
  → Verify anti-aliasing level matches target platform
  → Verify physics timestep (0.02 = 50Hz is standard)

Step 3: Problematic Object Scan
  For expensive objects flagged by analyze_performance:
  inspect_gameobject(target="ObjectName")
  → Check: is_static flags (should be set for environment)
  → Check: component count (too many components on one object?)
  → Check: total_descendants (deeply nested objects cause overhead)

Step 4: Apply Fixes
  Use suggestions from analyze_performance issues:
  → Mark objects as static: manage_gameobject(action="modify", target="...", static=true)
  → Replace colliders: manage_components(action="remove/add", ...)
  → Fix scripts: script_apply_edits(...)

Step 5: Verify
  analyze_performance() → confirm issue_count decreased
  manage_scene(action="screenshot") → visual quality preserved
```

## Quick Wins

These optimizations are almost always beneficial:

| Fix | Impact | How |
|-----|--------|-----|
| Mark static objects | Enables batching | Set Static flag on non-moving environment |
| Bake lightmaps | Eliminates realtime light cost | Mark lights and objects as static, bake |
| Reduce shadow distance | Fewer shadow draw calls | Quality Settings → Shadow Distance |
| Compress textures | Less memory, faster loads | Texture import settings |
| Disable unused components | Skip processing | Uncheck enabled on unused Colliders, etc. |

## Platform-Specific Notes

### Mobile
- Prefer baked lighting over realtime
- Use texture atlases to reduce draw calls
- Avoid transparent/alpha materials where possible
- Use Simplified shaders (Mobile/ shader category)

### Desktop
- More headroom but still budget-conscious
- Use GPU instancing for repeated objects
- Enable dynamic batching for small meshes
- Post-processing is affordable but monitor frame time

### 2D Games
- Sprite atlases are critical for reducing draw calls
- Use sorting layers and order-in-layer instead of z-position tricks
- Minimize overdraw from overlapping transparent sprites
- Physics2D is cheaper than 3D physics but still budget it
