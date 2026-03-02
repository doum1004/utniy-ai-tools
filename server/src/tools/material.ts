/**
 * Material management tools — create, modify, assign materials.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerMaterialTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_material",
        "Create, modify, or assign Unity materials and their properties",
        {
            action: z.enum(["create", "modify", "assign", "get", "list"])
                .describe("Material action to perform"),
            name: z.string().optional().describe("Material name"),
            path: z.string().optional().describe("Material asset path"),
            target: z.string().optional().describe("Target GameObject for assign action"),
            shader: z.string().optional().describe("Shader name (e.g., 'Standard', 'Universal Render Pipeline/Lit')"),
            color: z.array(z.number()).optional().describe("Main color [r, g, b, a] (0-255 or 0.0-1.0)"),
            properties: z.record(z.unknown()).optional().describe("Material property values to set"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_material", params);
            return formatToolResult(response);
        },
    );
}
