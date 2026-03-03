---
name: scene-management
description: Guide AI through Unity scene hierarchy organization, object reparenting, material workflows, and MCP tool limitations with workarounds. Use when organizing scene trees, bulk-modifying objects, or hitting MCP tool issues.
---

# Scene Management — Hierarchy & Tooling Skill

This skill guides you through organizing Unity scene hierarchies and working around MCP tool limitations.

## Core Principle: Editor Scripts for Bulk Operations

MCP tools are great for creating and modifying individual objects, but **unreliable for bulk hierarchy reorganization**. When you need to reparent many objects, always write an Editor script.

## Known MCP Tool Limitations

| Operation | Issue | Workaround |
|-----------|-------|------------|
| `manage_gameobject` modify + parent | Silently fails to reparent existing objects | Use Editor script with `Transform.SetParent()` |
| `manage_components` set_property (Color) | "unsupported type" error for Color fields | Use Editor script or `manage_material` for material colors |
| `manage_components` set_property (enum) | Fails on enum types like shadow mode | Use Editor script with direct component API |
| Tags (`FindGameObjectsWithTag`) | Custom tags can't be created/assigned reliably via MCP | Use component-based lookups: `FindObjectsByType<T>()` |
| Serialized field assignment | MCP can't assign prefab references to script fields | Use `Resources.Load<GameObject>()` from `Assets/Resources/` |
| PanelSettings (UI Toolkit) | `ScriptableObject.CreateInstance<PanelSettings>()` at runtime is missing shader refs | Create via `AssetDatabase.CreateAsset()` in an Editor script |
| Prefab child objects | Children added after initial prefab save may get lost | Unpack, modify, then re-create the prefab |

## Hierarchy Organization Convention

Use `---` prefix naming for organizational empty GameObjects:

```
Scene Root
├── --- Environment ---
│   ├── Ground
│   ├── Fences
│   │   ├── FenceLeft, FenceRight, ...
│   │   └── FencePostFL, FencePostFR, ...
│   ├── Vegetation
│   │   ├── Bush1, Bush2, ...
│   │   └── Flower1, Flower2, ...
│   └── Props
├── --- Buildings ---
│   ├── Barn, BarnRoof, BarnDoor
│   └── BarnTrigger
├── --- Lighting ---
│   ├── Directional Light (Sun)
│   └── FillLight
├── --- Cameras ---
│   └── Main Camera
└── --- Systems ---
    ├── GameManager
    ├── UIDocument
    └── PostProcessVolume
```

Guidelines:
- Max 5-7 root groups
- Sub-group when a category has 4+ objects (e.g., Fences, Vegetation)
- Keep max hierarchy depth < 4 for scene objects
- Name every object descriptively — no "Cube (4)" or "GameObject (12)"

## Editor Script Template: Hierarchy Organizer

When you need to reparent objects, create an Editor script:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class OrganizeHierarchy
{
    [MenuItem("Tools/Organize Scene Hierarchy")]
    public static void Organize()
    {
        var parent = FindOrCreate("--- GroupName ---");
        Reparent("ChildObject", parent);
        // ... more reparenting ...

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    static Transform FindOrCreate(string name, Transform parent = null)
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var t in all)
            if (t.name == name) { if (parent != null) t.SetParent(parent, true); return t; }
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, true);
        return go.transform;
    }

    static void Reparent(string name, Transform parent)
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var t in all)
            if (t.name == name) { t.SetParent(parent, true); return; }
    }
}
```

Workflow:
```
1. create_script(path="Assets/Editor/OrganizeHierarchy.cs", contents="...")
2. refresh_unity(mode="force", compile="request", wait_for_ready=true)
3. execute_menu_item(menu_path="Tools/Organize Scene Hierarchy")
4. manage_scene(action="get_hierarchy") → verify structure
```

## Material Workflow

```
1. manage_material(action="create", name="...", shader="Universal Render Pipeline/Lit", color=[...])
2. manage_material(action="assign", name="MaterialName", target="GameObjectName")
3. manage_scene(action="screenshot") → verify visually
```

For material properties that `set_property` can't handle, use an Editor script:
```csharp
[MenuItem("Tools/Setup Materials")]
public static void SetupMaterials()
{
    var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MyMat.mat");
    mat.SetFloat("_Smoothness", 0.3f);
    mat.SetFloat("_Metallic", 0.1f);
    mat.EnableKeyword("_EMISSION");
    mat.SetColor("_EmissionColor", Color.white * 0.2f);
    EditorUtility.SetDirty(mat);
    AssetDatabase.SaveAssets();
}
```

## Editor Script Pattern: When to Use

Use an Editor script (instead of MCP tools directly) when:
- Reparenting more than 2-3 objects
- Setting component properties that fail via `set_property` (Color, enum, struct)
- Creating assets that need internal Unity references (PanelSettings, RenderPipelineAsset)
- Configuring RenderSettings (skybox, ambient, fog) — no MCP tool for this
- Bulk operations that need atomicity (all-or-nothing)

Always follow this cycle:
```
1. create_script(path="Assets/Editor/MySetup.cs", contents="...")
2. refresh_unity(mode="force", compile="request", wait_for_ready=true)
3. read_console(types=["error"]) → verify no compile errors
4. execute_menu_item(menu_path="Tools/My Setup")
5. read_console(types=["error"]) → verify no runtime errors
```

## Post-Organize Verification

After organizing a scene, always verify:
```
1. manage_scene(action="get_hierarchy") → confirm structure
2. analyze_scene(include_details=true) → check missing_component_count == 0
3. manage_scene(action="screenshot") → visual sanity check
4. manage_editor(action="play") → brief play test
5. read_console(types=["error"]) → no runtime errors
6. manage_editor(action="stop")
```
