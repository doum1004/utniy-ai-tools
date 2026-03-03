/**
 * Analysis tools — scene analysis, object inspection, project settings.
 * Provides AI with feedback data for game design planning and quality review.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerAnalysisTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "analyze_scene",
        "Analyze the current Unity scene — returns object counts, triangle/vertex stats, lights, cameras, materials, missing references, hierarchy depth, and 2D/3D detection. Use this after building or modifying a scene to review quality and identify issues.",
        {
            include_details: z.boolean().optional().default(false)
                .describe("Include detailed breakdowns for cameras, lights, and root objects"),
        },
        async (params) => {
            const response = await bridge.sendCommand("analyze_scene", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "inspect_gameobject",
        "Deep inspection of a single GameObject — components, missing references, null serialized fields, scale anomalies, prefab status, static flags, and identified issues. Use to diagnose problems with specific objects.",
        {
            target: z.string().describe("GameObject name or path to inspect"),
        },
        async (params) => {
            const response = await bridge.sendCommand("inspect_gameobject", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "get_project_settings",
        "Read Unity project settings — physics, quality, rendering, player, time, and audio configuration. Use to understand the project context before making design decisions.",
        {
            categories: z.array(z.enum([
                "physics", "physics2d", "quality", "rendering", "player", "time", "audio",
            ])).optional().describe("Setting categories to retrieve (returns all if empty)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("get_project_settings", params);
            return formatToolResult(response);
        },
    );
}
