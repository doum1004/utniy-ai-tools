---
name: unity-dev-planning
description: Guide AI through persistent development planning and iteration tracking. Use when starting a new session, building features iteratively, or needing context from previous work.
---

# Dev Planning — Persistent Development Journal Skill

This skill ensures development plans, decisions, and iteration history persist across LLM sessions so follow-up conversations can pick up exactly where things left off.

## Core Principle: Log As You Go

Every significant action should leave a trace in the dev log. Future sessions read this log to understand context without the user having to re-explain.

```
Start Session → Read devlog resource → Understand context → Work → Log progress → End Session
```

## Session Start Workflow

**Always** read the devlog resource at the beginning of a session:

```
1. Read unity://project/devlog → see active plans, recent completions, open issues
2. Summarize current state to the user: "Last session you completed X, Y is still active, Z is blocked"
3. Ask what to work on next (or continue active items)
```

## Entry Types

| Type | When to Use | Example |
|------|-------------|---------|
| `plan` | Starting a new feature or phase | "Phase 2: Core Mechanics — player controller, enemy prefabs, collision" |
| `milestone` | Completing something significant | "Player movement working with new Input System" |
| `decision` | Making an architectural or design choice | "Using Resources.Load pattern instead of serialized refs for prefab spawning" |
| `issue` | Finding a bug or problem | "Enemy spawner creates overlapping enemies at origin" |
| `iteration` | Completing a build-test-fix cycle | "Iteration 3: Added wave progression, fixed score not resetting on restart" |
| `note` | General observations or reminders | "User prefers pixel art style, wants 2D top-down camera" |

## Logging Patterns

### Starting a New Feature

```
manage_devlog(action="add", type="plan", title="Phase 2: Core Mechanics",
  body="Player controller with WASD movement, enemy capsule prefabs in Resources/, basic collision detection. User wants top-down 3D perspective.",
  tags=["phase-2", "core-mechanics"],
  status="active")
```

### Recording a Decision

```
manage_devlog(action="add", type="decision", title="UI Toolkit over UGUI",
  body="Choosing UI Toolkit for all game UI. PanelSettings must be created via Editor script (AssetDatabase.CreateAsset) to avoid missing shader issue.",
  tags=["ui", "architecture"],
  parent_id="<plan-id>")
```

### Completing a Milestone

```
manage_devlog(action="add", type="milestone", title="Player movement working",
  body="WASD movement with new Input System, camera follows player, collides with boundaries. Verified via play test.",
  tags=["player", "phase-2"])
```

Then mark the parent plan as completed if all milestones are done:

```
manage_devlog(action="update", entry_id="<plan-id>", status="completed")
```

### Logging an Iteration Cycle

After a build-test-fix loop:

```
manage_devlog(action="add", type="iteration", title="Iteration 4: Wave system + UI",
  body="Added wave spawning with increasing difficulty. Created HUD with UI Toolkit showing score and wave number. Fixed: enemies not despawning on wave end. Fixed: score label not updating (was querying wrong element name).",
  tags=["waves", "ui", "phase-3"],
  parent_id="<plan-id>")
```

### Tracking an Issue

```
manage_devlog(action="add", type="issue", title="Enemies spawn at origin instead of spawn points",
  body="SpawnEnemy() uses Vector3.zero instead of the configured spawn point positions. Need to fix SpawnManager.GetNextSpawnPoint().",
  tags=["bug", "spawning", "phase-2"],
  status="active")
```

After fixing:

```
manage_devlog(action="update", entry_id="<issue-id>", status="completed",
  body="Fixed: SpawnManager was not iterating through spawnPoints array. Changed to use index cycling.")
```

## Querying History

### See All Active Work

```
manage_devlog(action="list", filter_status="active")
```

### See Plans Only

```
manage_devlog(action="list", filter_type="plan")
```

### Find Entries Related to a Feature

```
manage_devlog(action="search", query="player movement")
```

### Get a Plan and Its Children

```
manage_devlog(action="get", entry_id="<plan-id>")
→ returns the plan entry plus all child entries (iterations, decisions, issues linked to it)
```

### See Completed Work

```
manage_devlog(action="list", filter_status="completed", limit=20)
```

## Tagging Strategy

Use consistent tags to enable filtering:

| Tag Pattern | Purpose |
|-------------|---------|
| `phase-N` | Development phase number |
| Feature name (`player`, `ui`, `waves`) | Feature area |
| `bug` | Bug/issue entries |
| `architecture` | Architectural decisions |
| `polish` | Visual/UX polish items |
| `performance` | Optimization-related |

## Session End Workflow

Before ending a session, log what was accomplished and what's next:

```
1. manage_devlog(action="add", type="iteration", title="Session summary: ...",
     body="Completed: ... | Still active: ... | Next steps: ...",
     tags=["session-summary"])
2. Update any completed plans/issues: manage_devlog(action="update", entry_id="...", status="completed")
3. Log any new issues discovered: manage_devlog(action="add", type="issue", ...)
```

## Integration with Game Dev Workflow

The dev log complements the **unity-game-dev-workflow** skill phases:

| Phase | Dev Log Actions |
|-------|-----------------|
| Phase 1: Scene Foundation | Log plan, log decisions about terrain/camera/lighting |
| Phase 2: Core Mechanics | Log plan, milestones per mechanic, issues found in play testing |
| Phase 3: Game Loop | Log iteration cycles, game manager decisions |
| Phase 4: UI | Log UI Toolkit decisions, layout choices |
| Phase 5: Visual Polish | Log iterations with screenshot observations |
| Phase 6: Gameplay Depth | Log difficulty tuning decisions, balancing notes |
| Phase 7: Polish | Log final issues and completion milestones |

## Resource vs Tool

- **Resource** `unity://project/devlog` — read-only summary of active and recently completed entries. Read this at session start.
- **Tool** `manage_devlog` — full CRUD operations for adding, updating, listing, and searching entries. Use throughout the session.
