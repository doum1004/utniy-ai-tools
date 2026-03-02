/**
 * Script tools — create, edit, validate, delete, apply edits.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerScriptTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "create_script",
        "Create a new C# script file in the Unity project",
        {
            path: z.string().describe("Script path relative to Assets (e.g., 'Assets/Scripts/MyScript.cs')"),
            contents: z.string().describe("Full C# script contents"),
            overwrite: z.boolean().optional().default(false).describe("Overwrite if file exists"),
        },
        async (params) => {
            const response = await bridge.sendCommand("create_script", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "manage_script",
        "Read or get information about an existing C# script",
        {
            action: z.enum(["read", "info"]).describe("Script action"),
            path: z.string().describe("Script path relative to Assets"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_script", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "script_apply_edits",
        "Apply targeted text edits to an existing C# script using SHA verification for safe concurrent editing",
        {
            path: z.string().describe("Script path relative to Assets"),
            sha: z.string().describe("SHA hash of the current file content (from get_sha)"),
            edits: z.array(z.object({
                old_text: z.string().describe("Exact text to find and replace"),
                new_text: z.string().describe("Replacement text"),
            })).describe("List of text replacements to apply"),
        },
        async (params) => {
            const response = await bridge.sendCommand("script_apply_edits", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "validate_script",
        "Validate a C# script for compilation errors without saving",
        {
            contents: z.string().describe("C# script contents to validate"),
            path: z.string().optional().describe("Optional path context for error messages"),
        },
        async (params) => {
            const response = await bridge.sendCommand("validate_script", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "delete_script",
        "Delete a C# script file from the Unity project",
        {
            path: z.string().describe("Script path relative to Assets"),
        },
        async (params) => {
            const response = await bridge.sendCommand("delete_script", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "get_sha",
        "Get the SHA hash of a file for use with script_apply_edits",
        {
            path: z.string().describe("File path relative to Assets"),
        },
        async (params) => {
            const response = await bridge.sendCommand("get_sha", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "apply_text_edits",
        "Apply text edits to any text file in the Unity project (not just scripts)",
        {
            path: z.string().describe("File path relative to Assets"),
            sha: z.string().describe("SHA hash of the current file content"),
            edits: z.array(z.object({
                old_text: z.string().describe("Exact text to find"),
                new_text: z.string().describe("Replacement text"),
            })).describe("List of text replacements"),
        },
        async (params) => {
            const response = await bridge.sendCommand("apply_text_edits", params);
            return formatToolResult(response);
        },
    );
}
