# Unity AI Tools

[![CI](https://github.com/doum1004/utniy-ai-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/doum1004/utniy-ai-tools/actions/workflows/ci.yml)

A toolkit for AI-assisted Unity development via the [Model Context Protocol (MCP)](https://modelcontextprotocol.io).

## Components

| Component | Path | Tech |
|-----------|------|------|
| **MCP Server** | `server/` | Bun + TypeScript |
| **Unity Plugin** | `unity-package/` | C# Editor Package |
| **AI Skills** | `skills/` | Markdown |

## Quick Start

### 1. Start the MCP Server

```bash
cd server
bun install
bun run dev
```

The server starts:
- **MCP endpoint**: `http://localhost:8090/mcp` (for AI clients)
- **WebSocket**: `ws://localhost:8091` (for Unity plugin)

### 2. Install the Unity Package

In Unity: `Window > Package Manager > + > Add package from disk...`

Select `unity-package/package.json`.

### 3. Connect

1. Open `Window > Unity AI Tools > Control Panel`
2. Click **Connect**
3. Configure your MCP client to use `http://localhost:8090/mcp`

### 4. Try It

Ask your AI assistant: *"Create a red, blue and yellow cube"*

## MCP Client Config

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:8090/mcp"
    }
  }
}
```

## Available Tools

`manage_scene` · `manage_gameobject` · `find_gameobjects` · `manage_components` · `create_script` · `manage_script` · `script_apply_edits` · `validate_script` · `delete_script` · `get_sha` · `apply_text_edits` · `manage_asset` · `manage_material` · `manage_prefabs` · `manage_editor` · `read_console` · `refresh_unity` · `execute_menu_item` · `batch_execute` · `run_tests` · `get_test_job` · `set_active_instance` · `analyze_scene` · `inspect_gameobject` · `get_project_settings` · `analyze_performance` · `simulate_input` · `execute_method` · `read_runtime_state` · `capture_gameplay` · `manage_snapshot` · `manage_devlog` · `manage_feedback` · `capture_editor_window` · `inspect_ui_tree` · `get_annotated_screenshot`

`refresh_unity` automatically exits Play Mode before refreshing/compiling to reduce domain reload teardown errors from runtime object destruction.

### Performance Analysis

`analyze_performance` audits the scene and project assets across 7 categories: **memory**, **rendering**, **textures**, **meshes**, **lighting**, **physics**, and **audio**. Each category returns detailed stats plus a list of issues with severity (`high`/`medium`/`low`) and actionable fix suggestions.

### Snapshots

`manage_snapshot` creates lightweight restore points before large changes. Supports an optional `note` describing what the snapshot captures. Uses `git` when available (records HEAD, zero-cost), otherwise falls back to file backup.

### Preferences

The Control Panel includes a **Preferences** section with:
- **Auto Connect** (default: on) — automatically reconnects after domain reloads and unexpected disconnects

## Session Guardrails

To improve continuity and reduce blind edits, mutating tool calls are blocked until the session reads `unity://project/devlog` at least once.

- Read access: `manage_devlog` actions `list`, `get`, or `search`
- Write tracking: `manage_devlog` actions `add` and `update`
- Safety default: unknown tools are treated as mutating

## Resources

| URI | Description |
|-----|-------------|
| `unity://editor/state` | Editor state — compiling, ready, blocking reasons |
| `unity://project/info` | Project name, version, packages, render pipeline |
| `unity://instances` | Connected Unity Editor instances |
| `unity://editor/selection` | Currently selected objects |
| `unity://project/tags` | Available project tags |
| `unity://project/layers` | Available project layers |
| `unity://editor/menu-items` | Available Editor menu items |
| `unity://project/devlog` | Development journal — plans, decisions, iteration history |
| `unity://project/feedback` | User-owned feedback drafts/exports for tool and skill improvements (manual sharing only) |

## AI Skills

| Skill | Purpose |
|-------|---------|
| `unity-mcp-tools` | Core orchestrator — tool usage patterns and workflows |
| `unity-level-design` | Scene composition, lighting, camera setup, feedback loops |
| `unity-game-architecture` | Script patterns, prefab strategy, ScriptableObjects |
| `unity-optimization` | Performance review, batching, pooling, profiling workflows |
| `unity-ui-design` | UI Toolkit-first UI design, responsive layout, UGUI exceptions |
| `unity-testing-debugging` | Writing tests, console analysis, debugging workflows |
| `unity-physics-gameplay` | Rigidbody, collisions, raycasting, movement patterns |
| `unity-2d-game-development` | Sprites, tilemaps, sorting, 2D camera, lighting, animation |
| `unity-game-dev-workflow` | Iterative build phases, prefab workflows, input system patterns |
| `unity-scene-management` | Scene hierarchy, loading, and organization |
| `unity-dev-planning` | Persistent development journal — plans, iterations, cross-session context |

## Testing

### MCP Server

```bash
cd server
bun test
```

Runs server unit tests covering transport, tools, resources, and utilities.

### Unity Package

Open the Unity Test Runner (`Window > General > Test Runner`) and run **EditMode** tests. Tests cover `CommandDispatcher`, `MessageTypes`, `ToolParams`, and `MiniJson`.

### CI

GitHub Actions runs three jobs on every pull request and push to `main`:

| Job | What it runs |
|-----|-------------|
| MCP Server Tests | `bun test` — server unit tests |
| MCP Server Type Check | `tsc --noEmit` |
| Unity Package EditMode Tests | GameCI `unity-test-runner` — EditMode tests via Unity 6 |

The Unity job requires the following GitHub repository secrets:

| Secret | Description |
|--------|-------------|
| `UNITY_LICENSE` | Contents of a Unity `.ulf` activation file |
| `UNITY_EMAIL` | Unity account email |
| `UNITY_PASSWORD` | Unity account password |
| `UNITY_SERIAL` | Unity Pro serial number |

The `unity-project/Library/` folder is cached between runs keyed on `manifest.json` and `packages-lock.json` to avoid re-importing packages on every run.

## Acknowledgements

Inspired by [unity-mcp](https://github.com/nicholascpark/unity-mcp).

## License

MIT
