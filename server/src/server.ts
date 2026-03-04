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
    registerAnalysisTools,
    registerPlayTestTools,
    registerDevLogTools,
} from "./tools/index";
import { registerResources } from "./resources/index";

const SERVER_NAME = "unity-ai-tools";
const SERVER_VERSION = "0.1.0";

function truncateForLog(obj: unknown, maxLen = 200): string {
    const str = JSON.stringify(obj, null, 2);
    if (str.length <= maxLen) return str;
    return str.slice(0, maxLen) + `… (${str.length} chars)`;
}

function wrapToolWithLogging(server: McpServer): void {
    const originalTool = server.tool.bind(server);

    server.tool = ((...args: unknown[]) => {
        const name = args[0] as string;
        const lastIdx = args.length - 1;
        const originalCb = args[lastIdx] as (...cbArgs: unknown[]) => Promise<unknown>;

        args[lastIdx] = async (...cbArgs: unknown[]) => {
            const params = cbArgs[0];
            console.log(`\n┌─ TOOL CALL: ${name}`);
            console.log(`│  Request: ${truncateForLog(params, 500)}`);

            const start = performance.now();
            try {
                const result = await originalCb(...cbArgs);
                const elapsed = (performance.now() - start).toFixed(0);
                console.log(`│  Response (${elapsed}ms): ${truncateForLog(result, 500)}`);
                console.log(`└─ END ${name}\n`);
                return result;
            } catch (err) {
                const elapsed = (performance.now() - start).toFixed(0);
                console.error(`│  ERROR (${elapsed}ms): ${err}`);
                console.log(`└─ END ${name}\n`);
                throw err;
            }
        };

        return (originalTool as Function)(...args);
    }) as typeof server.tool;
}

export function createMcpServer(bridge: UnityBridge): McpServer {
    const server = new McpServer({
        name: SERVER_NAME,
        version: SERVER_VERSION,
    });

    wrapToolWithLogging(server);

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
    registerAnalysisTools(server, bridge);
    registerPlayTestTools(server, bridge);
    registerDevLogTools(server, bridge);

    // Register resources
    registerResources(server, bridge);

    return server;
}
