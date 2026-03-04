---
name: unity-optimization
description: Guide AI through Unity performance optimization — draw calls, batching, pooling, LOD, memory, and profiling workflows using analysis tools. Use when reviewing or improving scene/project performance.
---

# Optimization — Performance Review Skill

This skill guides you through identifying and fixing performance issues in Unity projects.

## Core Principle: Measure First, Optimize Second

Never optimize blindly. Always gather data before making changes.

```
1. analyze_scene(include_details=true) → get baseline stats
2. get_project_settings(categories=["quality", "rendering"]) → understand config
3. Identify bottlenecks from data
4. Apply targeted fixes
5. Re-analyze to verify improvement
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
analyze_scene() returns:
- total_triangles → compare against budget
- total_vertices → vertex count
- unique_materials → material count
- light_count + light_breakdown → realtime light count
- renderer_count → approximate draw call indicator
```

## Optimization Checklist

### Rendering

- [ ] **Static batching**: Mark non-moving objects as Static
  - Use `inspect_gameobject` → check `is_static` and `static_flags`
  - Fix: `manage_gameobject(action="modify", target="...", static=true)`
- [ ] **Material consolidation**: Reduce unique material count
  - Check `analyze_scene` → `unique_materials`
  - Objects sharing the same visual appearance should share materials
- [ ] **Occlusion culling**: Enable for indoor/complex scenes
- [ ] **LOD groups**: Add for detailed meshes visible at varying distances
- [ ] **Light baking**: Bake static lights instead of realtime for static scenes
  - Check `analyze_scene(include_details=true)` → light details for shadow settings

### Physics

- [ ] **Collider complexity**: Use primitive colliders (Box, Sphere, Capsule) over MeshColliders
  - `analyze_scene` → `collider_count` gives total
  - Use `find_gameobjects(search_term="MeshCollider", search_method="by_component")` to find mesh colliders
- [ ] **Physics settings**: Verify timestep and solver iterations
  - `get_project_settings(categories=["physics"])` → check fixed_timestep and solver iterations
- [ ] **Layer-based collision matrix**: Disable unnecessary collision pairs

### Scripts

- [ ] **No allocations in Update**: Avoid `new`, LINQ, string concatenation in hot paths
- [ ] **Cache component references**: `GetComponent` in Awake, not in Update
- [ ] **Object pooling**: Reuse frequently spawned/destroyed objects
- [ ] **Coroutine yields**: Cache `WaitForSeconds` instances

### Memory

- [ ] **Texture sizes**: Use appropriate resolution (not 4K for small objects)
- [ ] **Audio compression**: Compressed in memory for music, decompress on load for short SFX
- [ ] **Mesh compression**: Enable in import settings for meshes
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
Step 1: Scene Analysis
  analyze_scene(include_details=true)
  → Record: total_triangles, unique_materials, light_count, renderer_count
  → Flag if any exceed platform budget

Step 2: Project Settings Check
  get_project_settings(categories=["quality", "rendering", "physics"])
  → Verify shadow distance isn't excessive
  → Verify anti-aliasing level matches target platform
  → Verify physics timestep (0.02 = 50Hz is standard)

Step 3: Problematic Object Scan
  For objects flagged by analyze_scene (missing components, deep hierarchy):
  inspect_gameobject(target="ObjectName")
  → Check: is_static flags (should be set for environment)
  → Check: component count (too many components on one object?)
  → Check: total_descendants (deeply nested objects cause overhead)

Step 4: Heavy Component Search
  find_gameobjects(search_term="MeshCollider", search_method="by_component")
  → Consider replacing with primitive colliders

Step 5: Visual Verification
  manage_scene(action="screenshot")
  → Confirm visual quality is maintained after optimizations
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
