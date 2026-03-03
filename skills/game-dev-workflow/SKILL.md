---
name: game-dev-workflow
description: Guide AI through iterative Unity game development — phased builds, playtesting loops, prefab workflows, UI Toolkit setup, and known pitfalls. Use when building a game from scratch or adding major features iteratively.
---

# Game Development Workflow — Iterative Build Skill

This skill guides you through building Unity games iteratively with AI assistance, from empty scene to polished gameplay.

## Core Principle: Build in Phases, Test Each Phase

Never try to build everything at once. Work in focused phases, verifying each before moving to the next.

```
Phase → Implement → Compile Check → Play Test → Screenshot → Fix Issues → Next Phase
```

## Development Phases

### Phase 1: Scene Foundation

Set up the world before any gameplay code.

```
1. Ground plane / terrain
2. Camera positioned and configured (FOV, angle, clipping)
3. Skybox (procedural or custom — use Editor script for RenderSettings)
4. Directional light + ambient settings
5. Basic environment: boundaries, landmarks, key structures
6. Materials assigned to all objects
7. Screenshot → verify visual foundation
```

### Phase 2: Core Mechanics

Get one thing playable before adding complexity.

```
1. Player controller script (check Input System — old vs new)
2. Create prefabs in Assets/Resources/ for runtime spawning
3. Basic entity scripts (movement, collision)
4. Wire prefabs via Resources.Load (MCP can't assign serialized fields)
5. Play test → verify core interaction works
6. Check console for errors
```

### Phase 3: Game Loop

Add structure around the core mechanic.

```
1. GameManager (singleton) — spawning, scoring, state management
2. Win/lose conditions
3. Wave/level progression
4. Spawn patterns and difficulty scaling
5. Play test → verify full loop works
```

### Phase 4: UI

Add HUD and overlays after gameplay works.

```
1. Create UXML layout (Assets/Resources/GameHUD.uxml)
2. Create USS stylesheet (Assets/Resources/GameHUD.uss)
3. Create PanelSettings via Editor script (NOT runtime ScriptableObject.CreateInstance)
4. Create UI controller script to bind elements
5. Wire UIDocument in scene via Editor script or MCP
6. Play test → verify UI updates with game state
```

### Phase 5: Visual Polish

Make it look good after it plays good.

```
1. Procedural skybox + fog (Editor script for RenderSettings)
2. Post-processing volume (bloom, tone mapping, color grading, vignette, DoF)
3. Material refinement (smoothness, metallic, emission, colors)
4. Additional lighting (fill light, accent lights)
5. Scene decorations (paths, props, vegetation details)
6. Camera adjustments (FOV, angle fine-tuning)
7. Screenshot → verify visual quality
```

### Phase 6: Gameplay Depth

Layer on complexity.

```
1. Difficulty progression (wave configs, enemy AI variants)
2. Scoring bonuses (streaks, speed, perfect clears)
3. Game over / restart flow
4. Wave transition UI overlays
5. Play test → verify difficulty curve feels right
```

### Phase 7: Polish & Iteration

```
1. Organize scene hierarchy (Editor script — see scene-management skill)
2. analyze_scene → check for missing refs, deep hierarchies
3. Extended play test — multiple waves
4. Performance check (triangle count, material count, draw calls)
5. Final save
```

## Prefab Workflow (Resources.Load Pattern)

MCP cannot assign prefab references to serialized fields on MonoBehaviours. Use this pattern:

```csharp
// In your script — load from Resources folder
private GameObject enemyPrefab;

void Awake()
{
    enemyPrefab = Resources.Load<GameObject>("Enemy"); // Assets/Resources/Enemy.prefab
}

void SpawnEnemy(Vector3 position)
{
    Instantiate(enemyPrefab, position, Quaternion.identity);
}
```

Prefab creation workflow:
```
1. manage_gameobject(action="create", name="Enemy", primitive_type="Capsule")
2. Modify scale, add components, assign material
3. manage_prefabs(action="create", target="Enemy", path="Assets/Resources/Enemy.prefab")
4. manage_gameobject(action="delete", target="Enemy") — remove scene instance
```

## UI Toolkit Setup (Required Steps)

UI Toolkit needs special handling because PanelSettings created at runtime are broken (missing internal shader references).

```
1. Create Editor script that creates PanelSettings via AssetDatabase.CreateAsset()
2. Same script wires UIDocument component with PanelSettings + UXML reference
3. refresh_unity → compile → execute_menu_item
4. UXML should embed USS via: <Style src="MyStyles.uss" />
5. UI controller script uses root.Q<Label>("element-name") to bind elements
```

## Input System Considerations

Check which input system the project uses before writing controller scripts:

- **New Input System** (com.unity.inputsystem): Use `Mouse.current`, `Keyboard.current`, `Pointer.current`
- **Old Input Manager**: Use `Input.GetAxis()`, `Input.GetMouseButton()`
- **Both**: Project Settings → Player → Active Input Handling

If using the New Input System, `UnityEngine.Input` is **disabled** and will cause compile errors.

## Known Pitfalls & Workarounds

| Pitfall | Impact | Workaround |
|---------|--------|------------|
| MCP reparenting fails silently | Hierarchy stays flat | Editor script with `Transform.SetParent()` |
| Custom tags not assignable via MCP | `FindGameObjectsWithTag` breaks | Use `FindObjectsByType<T>()` component lookups |
| Serialized field references can't be set | Prefab spawning fails | `Resources.Load<GameObject>()` pattern |
| PanelSettings via runtime `CreateInstance` | UI Toolkit renders nothing (missing shaders) | `AssetDatabase.CreateAsset()` in Editor script |
| `set_property` fails on Color/enum | Can't configure lights, shadows via MCP | Editor script with direct API calls |
| Domain reload after Editor script creation | MCP disconnects for 30-90 seconds | Wait and retry — Unity is recompiling |
| MCP screenshot doesn't capture UI Toolkit | Can't visually verify UI overlays | Play test manually in Editor, or trust the bindings |
| Prefab children lost after reparenting | Prefab structure breaks | Unpack, add children, re-create prefab |

## Verification Cycle (Run After Every Phase)

```
1. refresh_unity(mode="force", compile="request", wait_for_ready=true)
2. read_console(types=["error"], count=20) → zero compile errors
3. manage_editor(action="play")
4. read_console(types=["error"], count=10) → zero runtime errors
5. manage_scene(action="screenshot") → visual check
6. manage_editor(action="stop")
7. manage_scene(action="save")
```
