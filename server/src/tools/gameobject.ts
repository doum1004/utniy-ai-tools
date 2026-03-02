/**
 * GameObject tools — create, find, modify, delete, duplicate, move.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerGameObjectTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_gameobject",
        "Create, modify, delete, duplicate, or move GameObjects in the Unity scene",
        {
            action: z.enum(["create", "modify", "delete", "duplicate", "move_relative", "look_at"])
                .describe("Action to perform"),
            name: z.string().optional().describe("Name of the GameObject (for create/rename)"),
            target: z.string().optional().describe("Target GameObject (name, path, or instance ID)"),
            primitive_type: z.enum(["Cube", "Sphere", "Cylinder", "Plane", "Capsule", "Quad"])
                .optional().describe("Primitive type for create action"),
            position: z.array(z.number()).length(3).optional().describe("Position [x, y, z]"),
            rotation: z.array(z.number()).length(3).optional().describe("Rotation euler angles [x, y, z]"),
            scale: z.array(z.number()).length(3).optional().describe("Scale [x, y, z]"),
            parent: z.string().optional().describe("Parent GameObject name or path"),
            tag: z.string().optional().describe("Tag to assign"),
            layer: z.string().optional().describe("Layer to assign"),
            set_active: z.boolean().optional().describe("Set active/inactive state"),
            components_to_add: z.array(z.string()).optional().describe("Component types to add"),
            components_to_remove: z.array(z.string()).optional().describe("Component types to remove"),
            // Move relative
            reference_object: z.string().optional().describe("Reference object for relative movement"),
            direction: z.enum(["left", "right", "up", "down", "forward", "back"])
                .optional().describe("Direction for relative movement"),
            distance: z.number().optional().describe("Distance for relative movement"),
            world_space: z.boolean().optional().describe("Use world space for movement (default: true)"),
            // Duplicate
            new_name: z.string().optional().describe("Name for duplicated object"),
            offset: z.array(z.number()).length(3).optional().describe("Position offset for duplicate"),
            // Look at
            look_at_target: z.string().optional().describe("Target to look at"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_gameobject", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "find_gameobjects",
        "Search for GameObjects by name, tag, component, path, or instance ID",
        {
            search_term: z.string().describe("Search term"),
            search_method: z.enum(["by_name", "by_tag", "by_component", "by_path", "by_id", "by_layer"])
                .optional().default("by_name").describe("Search method (default: by_name)"),
            include_inactive: z.boolean().optional().default(false)
                .describe("Include inactive GameObjects"),
            page_size: z.number().optional().default(50).describe("Max results per page"),
            cursor: z.number().optional().default(0).describe("Pagination cursor"),
        },
        async (params) => {
            const response = await bridge.sendCommand("find_gameobjects", params);
            return formatToolResult(response);
        },
    );
}
