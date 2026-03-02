/**
 * Tool utilities — shared helpers for MCP tool implementations.
 */

import type { McpResponse } from "../transport/types";

/**
 * Wraps a Unity bridge command call with consistent error handling.
 * Returns MCP-compatible text content for tool results.
 */
type TextContent = { type: "text"; text: string };
type ImageContent = { type: "image"; data: string; mimeType: string };
type ToolContent = TextContent | ImageContent;

export function formatToolResult(response: McpResponse): { content: ToolContent[] } {
    if (response.success) {
        const data = response.data as Record<string, unknown> | undefined;

        // If response contains an inline image, return it as MCP image content
        if (data?.image_base64) {
            const base64 = data.image_base64 as string;
            const metadata: Record<string, unknown> = {};
            if (data.width) metadata.width = data.width;
            if (data.height) metadata.height = data.height;
            if (data.source) metadata.source = data.source;
            if (data.camera) metadata.camera = data.camera;

            const content: ToolContent[] = [
                { type: "image", data: base64, mimeType: "image/png" },
            ];
            if (Object.keys(metadata).length > 0) {
                content.push({ type: "text", text: JSON.stringify(metadata, null, 2) });
            }
            return { content };
        }

        return {
            content: [{
                type: "text",
                text: JSON.stringify(data ?? { success: true }, null, 2),
            }],
        };
    }

    const errorObj: Record<string, unknown> = {
        error: response.error ?? "Unknown error",
    };
    if (response.hint) errorObj.hint = response.hint;
    if (response.data != null) errorObj.data = response.data;

    return {
        content: [{
            type: "text",
            text: JSON.stringify(errorObj, null, 2),
        }],
    };
}

/**
 * Parse a vector string or array into a number array.
 * Accepts: [1, 2, 3], "1,2,3", "[1, 2, 3]"
 */
export function parseVector(value: unknown): number[] | undefined {
    if (value == null) return undefined;
    if (Array.isArray(value)) return value.map(Number);
    if (typeof value === "string") {
        const cleaned = value.replace(/[\[\]]/g, "").trim();
        if (!cleaned) return undefined;
        return cleaned.split(",").map((s) => Number(s.trim()));
    }
    return undefined;
}

/**
 * Normalize a boolean value from various input formats.
 */
export function parseBool(value: unknown): boolean | undefined {
    if (value == null) return undefined;
    if (typeof value === "boolean") return value;
    if (typeof value === "string") {
        const lower = value.toLowerCase();
        if (lower === "true" || lower === "1" || lower === "yes") return true;
        if (lower === "false" || lower === "0" || lower === "no") return false;
    }
    if (typeof value === "number") return value !== 0;
    return undefined;
}
