/**
 * Prefab management tools — create, instantiate, unpack prefabs.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerPrefabTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_prefabs",
        "Create, instantiate, modify, or unpack Unity prefabs",
        {
            action: z.enum(["create", "instantiate", "apply", "revert", "unpack", "get_info"])
                .describe("Prefab action to perform"),
            path: z.string().optional().describe("Prefab asset path"),
            target: z.string().optional().describe("Target GameObject for apply/revert/unpack"),
            name: z.string().optional().describe("Name for instantiated prefab"),
            position: z.array(z.number()).length(3).optional().describe("Position for instantiation [x, y, z]"),
            rotation: z.array(z.number()).length(3).optional().describe("Rotation for instantiation [x, y, z]"),
            parent: z.string().optional().describe("Parent GameObject for instantiation"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_prefabs", params);
            return formatToolResult(response);
        },
    );
}
