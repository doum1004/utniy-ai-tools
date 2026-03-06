/**
 * Performance analysis tools — memory, rendering, textures, meshes, lighting, physics, audio.
 * Returns categorized issues with severity and actionable suggestions.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerPerformanceTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "analyze_performance",
        "Analyze Unity project performance — memory usage, rendering costs (triangles, materials, draw calls), " +
        "texture/mesh import issues, lighting overhead, physics complexity, and audio memory. " +
        "Returns stats per category plus a prioritized list of issues with severity and fix suggestions. " +
        "Use this to identify optimization opportunities before or after building a scene.",
        {
            categories: z.array(z.enum([
                "memory", "rendering", "textures", "meshes", "lighting", "physics", "audio",
            ])).optional().describe("Categories to analyze (analyzes all if empty)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("analyze_performance", params);
            return formatToolResult(response);
        },
    );
}
