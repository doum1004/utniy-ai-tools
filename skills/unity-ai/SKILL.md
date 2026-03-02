---
name: unity-ai-tools
description: Orchestrate Unity Editor via MCP tools and resources. Use when working with Unity projects through Unity AI Tools — creating/modifying GameObjects, editing scripts, managing scenes, or any Unity Editor automation.
---

# Unity AI Tools — Orchestrator Skill

This skill helps you effectively use the Unity Editor through MCP tools and resources.

## Quick Start: Resource-First Workflow

**Always read relevant resources before using tools.** This prevents errors and provides context.

```
1. Check editor state     → unity://editor/state
2. Understand the scene   → find_gameobjects or manage_scene(action="get_hierarchy")
3. Take action            → manage_gameobject, create_script, etc.
4. Verify results         → read_console, manage_scene(action="screenshot")
```

## Critical Best Practices

### 1. After Writing/Editing Scripts: Always Refresh and Check Console

```
refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
read_console(types=["error"], count=10, include_stacktrace=true)
```

Why: Unity must compile scripts before they're usable. Compilation errors block all tool execution.

### 2. Use batch_execute for Multiple Operations

```
batch_execute(commands=[
  { tool: "manage_gameobject", params: { action: "create", name: "Cube1", primitive_type: "Cube" } },
  { tool: "manage_gameobject", params: { action: "create", name: "Cube2", primitive_type: "Cube" } }
], parallel=true)
```

10-100x faster than sequential calls. Max 25 commands per batch.

### 3. Check editor_state Before Complex Operations

Read `unity://editor/state` and check `ready_for_tools` is true before proceeding.

## Core Tool Reference

| Tool | Purpose |
|------|---------|
| `manage_scene` | Scene hierarchy, load/save, screenshots |
| `manage_gameobject` | Create, modify, delete, duplicate GameObjects |
| `find_gameobjects` | Search by name, tag, component, path, or ID |
| `manage_components` | Add, remove, get/set component properties |
| `create_script` | Create new C# scripts |
| `manage_script` | Read script contents and info |
| `script_apply_edits` | Apply targeted edits with SHA verification |
| `validate_script` | Check script for compilation errors |
| `delete_script` | Delete script files |
| `get_sha` | Get file SHA for safe editing |
| `manage_asset` | Search, move, rename, delete assets |
| `manage_material` | Create, modify, assign materials |
| `manage_prefabs` | Create, instantiate, manage prefabs |
| `manage_editor` | Play/pause/stop, focus, select |
| `read_console` | Read Unity console logs |
| `refresh_unity` | Refresh assets and trigger compilation |
| `execute_menu_item` | Execute Unity menu items |
| `batch_execute` | Bulk operations in one round-trip |
| `run_tests` | Run Unity tests |
| `set_active_instance` | Target specific Unity instance |

## Available Resources

| Resource | URI |
|----------|-----|
| Editor state | `unity://editor/state` |
| Project info | `unity://project/info` |
| Tags | `unity://project/tags` |
| Layers | `unity://project/layers` |
| Instances | `unity://instances` |
| Selection | `unity://editor/selection` |
| Menu items | `unity://editor/menu-items` |

## Common Workflows

### Creating a Script and Attaching It

```
1. create_script(path="Assets/Scripts/Player.cs", contents="...")
2. refresh_unity(mode="force", scope="scripts", compile="request", wait_for_ready=true)
3. read_console(types=["error"], count=10)
4. manage_gameobject(action="modify", target="Player", components_to_add=["Player"])
```

### Building a Scene

```
1. manage_scene(action="get_hierarchy") — understand current state
2. batch_execute(commands=[...]) — create multiple objects
3. manage_material(action="create", ...) — create materials
4. manage_material(action="assign", ...) — assign to objects
5. manage_scene(action="save")
```

### Finding and Modifying Objects

```
1. find_gameobjects(search_term="Enemy", search_method="by_tag")
2. manage_gameobject(action="modify", target="Enemy", position=[10, 0, 0])
3. manage_components(action="set_property", target="Enemy", ...)
```

## Error Recovery

| Symptom | Solution |
|---------|----------|
| Tools return "busy" | Wait, re-check `unity://editor/state` |
| "stale_file" error | Re-fetch SHA with `get_sha`, retry |
| Connection lost | Wait ~5s for reconnect, retry |
| Script errors | Check `read_console`, fix and `refresh_unity` |

## Parameter Conventions

- **Vectors**: `[x, y, z]` arrays (e.g., `position=[1, 2, 3]`)
- **Colors**: `[r, g, b, a]` (0-255 or 0.0-1.0)
- **Paths**: Assets-relative (e.g., `"Assets/Scripts/MyScript.cs"`)
- **Targets**: Name, path, or instance ID
