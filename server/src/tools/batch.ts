/**
 * Batch execution tool — run multiple tool calls in a single round-trip.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerBatchTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "batch_execute",
        "Execute multiple Unity commands in a single round-trip for 10-100x better performance. Max 25 commands per batch.",
        {
            commands: z.array(z.object({
                tool: z.string().describe("Tool name to execute"),
                params: z.record(z.unknown()).describe("Tool parameters"),
            })).min(1).max(25).describe("Array of commands to execute"),
            parallel: z.boolean().optional().default(false)
                .describe("Hint to execute in parallel where safe (Unity may still execute sequentially)"),
            fail_fast: z.boolean().optional().default(false)
                .describe("Stop on first failure (use for dependent operations)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("batch_execute", params);
            return formatToolResult(response);
        },
    );
}
