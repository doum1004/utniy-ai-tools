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
                "get_hierarchy", "load", "save", "create", "screenshot", "annotated_screenshot",
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

    server.tool(
        "get_annotated_screenshot",
        "Returns the latest annotated screenshot captured by the user in Unity's Scene/Game view. " +
        "The image has user-drawn annotations (rectangles, arrows, freehand marks, text labels) baked in. " +
        "Use this to understand what the user wants to change, create, or highlight in the scene.",
        {
            max_resolution: z.number().optional().describe("Max pixel dimension for the output image (default: 1024)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_scene", {
                action: "annotated_screenshot",
                max_resolution: params.max_resolution ?? 1024,
            });
            if (response.success && response.data) {
                const data = response.data as Record<string, unknown>;
                const content: Array<{ type: "image"; data: string; mimeType: string } | { type: "text"; text: string }> = [];
                if (data.image_base64) {
                    content.push({ type: "image", data: data.image_base64 as string, mimeType: "image/png" });
                }
                content.push({
                    type: "text",
                    text: JSON.stringify({
                        width: data.width,
                        height: data.height,
                        source: data.source,
                        annotations_description: data.annotations_description ?? "No annotations.",
                    }, null, 2),
                });
                return { content };
            }
            return formatToolResult(response);
        },
    );
}
