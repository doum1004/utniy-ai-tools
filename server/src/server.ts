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
    registerFeedbackTools,
    registerUIDebugTools,
    registerSnapshotTools,
    registerPerformanceTools,
} from "./tools/index";
import { registerResources } from "./resources/index";
import {
    DevlogComplianceTracker,
    isDevlogReadAction,
    isDevlogWriteAction,
    isMutatingToolCall,
} from "./devlog-compliance";

const SERVER_NAME = "unity-ai-tools";
const SERVER_VERSION = "0.1.0";

function truncateForLog(obj: unknown, maxLen = 200): string {
    const str = JSON.stringify(obj, null, 2);
    if (str.length <= maxLen) return str;
    return str.slice(0, maxLen) + `… (${str.length} chars)`;
}

type RequestExtra = { sessionId?: string; requestId?: string };

function extractExtra(cbArgs: unknown[]): RequestExtra | undefined {
    for (const arg of cbArgs) {
        if (!arg || typeof arg !== "object") continue;
        const rec = arg as Record<string, unknown>;
        if ("requestId" in rec || "sessionId" in rec) {
            return rec as RequestExtra;
        }
    }
    return undefined;
}

function getComplianceBlockResult(): { content: Array<{ type: "text"; text: string }> } {
    return {
        content: [{
            type: "text",
            text: JSON.stringify({
                error: "Devlog read required before mutating tools.",
                hint: "Read resource 'unity://project/devlog' first, then retry this tool call.",
            }, null, 2),
        }],
    };
}

function wrapToolWithLogging(server: McpServer, compliance: DevlogComplianceTracker): void {
    const originalTool = server.tool.bind(server);

    server.tool = ((...args: unknown[]) => {
        const name = args[0] as string;
        const lastIdx = args.length - 1;
        const originalCb = args[lastIdx] as (...cbArgs: unknown[]) => Promise<unknown>;

        args[lastIdx] = async (...cbArgs: unknown[]) => {
            const params = cbArgs[0];
            const extra = extractExtra(cbArgs);
            const sessionId = extra?.sessionId;

            if (isMutatingToolCall(name, params as Record<string, unknown> | undefined) && !compliance.hasRead(sessionId)) {
                console.log(`\n┌─ TOOL CALL: ${name}`);
                console.log(`│  BLOCKED: devlog must be read first`);
                console.log(`└─ END ${name}\n`);
                return getComplianceBlockResult();
            }

            console.log(`\n┌─ TOOL CALL: ${name}`);
            console.log(`│  Request: ${truncateForLog(params, 500)}`);

            const start = performance.now();
            try {
                const result = await originalCb(...cbArgs);
                if (isDevlogReadAction(name, params as Record<string, unknown> | undefined)) {
                    compliance.markRead(sessionId);
                }
                if (isDevlogWriteAction(name, params as Record<string, unknown> | undefined)) {
                    compliance.markWrite(sessionId);
                }
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

function wrapResourceWithDevlogTracking(server: McpServer, compliance: DevlogComplianceTracker): void {
    const originalResource = server.resource.bind(server);

    server.resource = ((...args: unknown[]) => {
        const resourceUri = typeof args[1] === "string" ? args[1] : undefined;
        const lastIdx = args.length - 1;
        const originalCb = args[lastIdx] as (...cbArgs: unknown[]) => Promise<unknown>;

        args[lastIdx] = async (...cbArgs: unknown[]) => {
            const extra = extractExtra(cbArgs);
            if (resourceUri === "unity://project/devlog") {
                compliance.markRead(extra?.sessionId);
            }
            return originalCb(...cbArgs);
        };

        return (originalResource as Function)(...args);
    }) as typeof server.resource;
}

export function createMcpServer(bridge: UnityBridge): McpServer {
    const server = new McpServer({
        name: SERVER_NAME,
        version: SERVER_VERSION,
    });

    const compliance = new DevlogComplianceTracker();
    wrapToolWithLogging(server, compliance);
    wrapResourceWithDevlogTracking(server, compliance);

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
    registerFeedbackTools(server, bridge);
    registerUIDebugTools(server, bridge);
    registerSnapshotTools(server, bridge);
    registerPerformanceTools(server, bridge);

    // Register resources
    registerResources(server, bridge);

    return server;
}
