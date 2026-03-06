/**
 * UI Debug tools — capture editor window screenshots and inspect UI Toolkit trees.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerUIDebugTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "capture_editor_window",
        "Capture a screenshot of a Unity Editor window as a base64 PNG image. " +
        "Use this to see what the UI looks like after making changes.",
        {
            window_title: z.string().optional().describe(
                "Title of the editor window to capture (e.g., 'Unity AI Tools', 'Scene', 'Inspector'). " +
                "Captures the focused window if not specified."
            ),
            max_resolution: z.number().optional().describe("Max pixel dimension (default: 800)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("capture_editor_window", params);
            if (response.success && response.data) {
                const data = response.data as Record<string, unknown>;
                const content: Array<{ type: "image"; data: string; mimeType: string } | { type: "text"; text: string }> = [];
                if (data.image_base64) {
                    content.push({ type: "image", data: data.image_base64 as string, mimeType: "image/png" });
                }
                content.push({
                    type: "text",
                    text: JSON.stringify({
                        window_title: data.window_title,
                        width: data.width,
                        height: data.height,
                    }, null, 2),
                });
                return { content };
            }
            return formatToolResult(response);
        },
    );

    server.tool(
        "inspect_ui_tree",
        "Inspect the live VisualElement tree of a Unity Editor window. " +
        "Returns the full element hierarchy with types, names, classes, layout sizes, and text content. " +
        "Use this to debug UI Toolkit layout issues without seeing pixels.",
        {
            window_title: z.string().optional().describe(
                "Title of the editor window to inspect. Inspects the focused window if not specified."
            ),
            max_depth: z.number().optional().describe("Max tree depth (default: 10)"),
            include_styles: z.boolean().optional().describe("Include computed styles (default: false)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("inspect_ui_tree", params);
            return formatToolResult(response);
        },
    );
}
