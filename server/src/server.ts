/**
 * MCP Server factory — creates and configures the McpServer with all tools and resources.
 */

import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "./transport/unity-bridge";
import {
    registerSceneTools,
    registerGameObjectTools,
    registerComponentTools,
    registerScriptTools,
    registerAssetTools,
    registerMaterialTools,
    registerPrefabTools,
    registerEditorTools,
    registerBatchTools,
} from "./tools/index";
import { registerResources } from "./resources/index";

const SERVER_NAME = "unity-ai-tools";
const SERVER_VERSION = "0.1.0";

export function createMcpServer(bridge: UnityBridge): McpServer {
    const server = new McpServer({
        name: SERVER_NAME,
        version: SERVER_VERSION,
    });

    // Register all tools
    registerSceneTools(server, bridge);
    registerGameObjectTools(server, bridge);
    registerComponentTools(server, bridge);
    registerScriptTools(server, bridge);
    registerAssetTools(server, bridge);
    registerMaterialTools(server, bridge);
    registerPrefabTools(server, bridge);
    registerEditorTools(server, bridge);
    registerBatchTools(server, bridge);

    // Register resources
    registerResources(server, bridge);

    return server;
}
