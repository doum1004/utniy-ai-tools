/**
 * MCP Resources — expose Unity Editor state and project info to AI clients.
 */

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";

export function registerResources(server: McpServer, bridge: UnityBridge): void {
    // Editor state — compiling, ready, blocking reasons
    server.resource(
        "editor_state",
        "unity://editor/state",
        {
            description: "Current Unity Editor state — compiling, ready_for_tools, blocking reasons",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_editor_state", {});
            return {
                contents: [{
                    uri: "unity://editor/state",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );

    // Project info — name, version, packages, render pipeline
    server.resource(
        "project_info",
        "unity://project/info",
        {
            description: "Unity project information — name, version, installed packages, render pipeline",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_project_info", {});
            return {
                contents: [{
                    uri: "unity://project/info",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );

    // Connected Unity instances
    server.resource(
        "unity_instances",
        "unity://instances",
        {
            description: "List of connected Unity Editor instances (for multi-instance workflows)",
            mimeType: "application/json",
        },
        async () => {
            const sessions = await bridge.getSessions();
            const instances: Record<string, unknown> = {};
            for (const [id, info] of sessions) {
                instances[id] = info;
            }
            return {
                contents: [{
                    uri: "unity://instances",
                    mimeType: "application/json",
                    text: JSON.stringify({ instances, count: sessions.size }, null, 2),
                }],
            };
        },
    );

    // Editor selection
    server.resource(
        "editor_selection",
        "unity://editor/selection",
        {
            description: "Currently selected objects in the Unity Editor",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_editor_selection", {});
            return {
                contents: [{
                    uri: "unity://editor/selection",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );

    // Project tags
    server.resource(
        "project_tags",
        "unity://project/tags",
        {
            description: "Available tags in the Unity project",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_project_tags", {});
            return {
                contents: [{
                    uri: "unity://project/tags",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );

    // Project layers
    server.resource(
        "project_layers",
        "unity://project/layers",
        {
            description: "Available layers in the Unity project",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_project_layers", {});
            return {
                contents: [{
                    uri: "unity://project/layers",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );

    // Menu items
    server.resource(
        "menu_items",
        "unity://editor/menu-items",
        {
            description: "Available Unity Editor menu items",
            mimeType: "application/json",
        },
        async () => {
            const response = await bridge.sendCommand("get_menu_items", {});
            return {
                contents: [{
                    uri: "unity://editor/menu-items",
                    mimeType: "application/json",
                    text: JSON.stringify(response.data ?? response, null, 2),
                }],
            };
        },
    );
}
