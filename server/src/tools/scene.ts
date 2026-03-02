/**
 * Scene management tools — hierarchy, screenshots, scene operations.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerSceneTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_scene",
        "Manage Unity scenes — get hierarchy, load, save, create, screenshot, play/pause controls",
        {
            action: z.enum([
                "get_hierarchy", "load", "save", "create", "screenshot",
                "get_active", "set_active",
            ]).describe("Scene action to perform"),
            scene_name: z.string().optional().describe("Scene name or path (for load/create/set_active)"),
            page_size: z.number().optional().describe("Number of items per page for hierarchy (default: 50)"),
            cursor: z.number().optional().describe("Pagination cursor for hierarchy"),
            include_image: z.boolean().optional().describe("Return screenshot as base64 PNG inline"),
            max_resolution: z.number().optional().describe("Max pixel dimension for screenshots (default: 640)"),
            camera: z.string().optional().describe("Camera name/path for screenshots"),
            batch: z.enum(["surround", "orbit"]).optional().describe("Batch screenshot mode"),
            look_at: z.string().optional().describe("Target for screenshot camera"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_scene", params);
            return formatToolResult(response);
        },
    );
}
