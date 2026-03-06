/**
 * Snapshot tools — create and revert project snapshots for safe AI editing.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerSnapshotTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_snapshot",
        "Create or revert project snapshots. Use 'create' before making large changes " +
        "so the user can easily undo them. Uses git if available, otherwise file backup.",
        {
            action: z.enum(["create", "revert", "status"])
                .describe("'create' a new snapshot, 'revert' to the last snapshot, or check 'status'"),
            note: z.string().optional()
                .describe("Short description of what the snapshot captures or why it was created (for 'create' action)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_snapshot", params);
            return formatToolResult(response);
        },
    );
}
