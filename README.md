# Unity AI Tools

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

1. Open `Window > Unity AI Tools`
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

`manage_scene` · `manage_gameobject` · `find_gameobjects` · `manage_components` · `create_script` · `manage_script` · `script_apply_edits` · `validate_script` · `delete_script` · `get_sha` · `apply_text_edits` · `manage_asset` · `manage_material` · `manage_prefabs` · `manage_editor` · `read_console` · `refresh_unity` · `execute_menu_item` · `batch_execute` · `run_tests` · `set_active_instance` · `analyze_scene` · `inspect_gameobject` · `get_project_settings`

## AI Skills

| Skill | Purpose |
|-------|---------|
| `unity-ai` | Core orchestrator — tool usage patterns and workflows |
| `level-design` | Scene composition, lighting, camera setup, feedback loops |
| `game-architecture` | Script patterns, prefab strategy, ScriptableObjects |
| `optimization` | Performance review, batching, pooling, profiling workflows |

## Acknowledgements

Inspired by [unity-mcp](https://github.com/nicholascpark/unity-mcp).

## License

MIT
