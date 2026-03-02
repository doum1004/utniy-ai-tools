/**
 * Component management tools — add, remove, get, set properties.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerComponentTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_components",
        "Add, remove, get, or set properties on Unity GameObject components",
        {
            action: z.enum(["add", "remove", "get", "get_all", "set_property"])
                .describe("Component action to perform"),
            target: z.string().describe("Target GameObject (name, path, or instance ID)"),
            component_type: z.string().optional().describe("Component type name (e.g., 'Rigidbody', 'BoxCollider')"),
            property_name: z.string().optional().describe("Property name for get/set"),
            property_value: z.unknown().optional().describe("Property value for set"),
            include_properties: z.boolean().optional().describe("Include property values when getting components"),
            search_method: z.enum(["by_name", "by_tag", "by_path", "by_id"])
                .optional().describe("How to find the target GameObject"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_components", params);
            return formatToolResult(response);
        },
    );
}
