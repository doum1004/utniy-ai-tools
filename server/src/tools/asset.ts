/**
 * Asset management tools — import, move, rename, delete, search assets.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerAssetTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_asset",
        "Manage Unity project assets — search, move, rename, delete, get info",
        {
            action: z.enum(["search", "move", "rename", "delete", "info", "import", "create_folder"])
                .describe("Asset action to perform"),
            path: z.string().optional().describe("Asset path relative to Assets folder"),
            search_term: z.string().optional().describe("Search term for asset search"),
            asset_type: z.string().optional().describe("Filter by asset type (e.g., 'Material', 'Texture2D')"),
            new_path: z.string().optional().describe("New path for move operation"),
            new_name: z.string().optional().describe("New name for rename operation"),
            folder_path: z.string().optional().describe("Folder path for create_folder"),
            page_size: z.number().optional().describe("Results per page for search (default: 50)"),
            cursor: z.number().optional().describe("Pagination cursor for search"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_asset", params);
            return formatToolResult(response);
        },
    );
}
