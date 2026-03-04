/**
 * MCP Resources — expose Unity Editor state and project info to AI clients.
 */

import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { loadDevLog, resolveStorageTarget } from "../devlog-storage";
import { loadFeedback, resolveFeedbackStorageTarget } from "../feedback-storage";

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

    // Dev log — persistent development journal for cross-session context
    server.resource(
        "devlog",
        "unity://project/devlog",
        {
            description: "Development journal — plans, decisions, milestones, issues, and iteration history. Read this at session start to understand what has been done and what's next.",
            mimeType: "application/json",
        },
        async () => {
            const target = await resolveStorageTarget(bridge);
            const log = loadDevLog(target) as { project: string; entries: unknown[] };

            const entries = log.entries as Array<{
                id: string; type: string; title: string; body: string;
                status: string; tags: string[]; parent_id?: string;
                created_at: string; updated_at: string;
            }>;

            const active = entries.filter((e) => e.status === "active");
            const recentCompleted = entries
                .filter((e) => e.status === "completed")
                .slice(-10);

            const summary = {
                project: log.project,
                total_entries: entries.length,
                active_entries: active.length,
                active: active,
                recent_completed: recentCompleted,
            };

            return {
                contents: [{
                    uri: "unity://project/devlog",
                    mimeType: "application/json",
                    text: JSON.stringify(summary, null, 2),
                }],
            };
        },
    );

    // Agent feedback — user-owned drafts for tool/skill improvements
    server.resource(
        "project_feedback",
        "unity://project/feedback",
        {
            description: "User-owned feedback drafts/exports for tool and skill improvements. Kept in project DevLogs and exported only on demand (no remote submission endpoint).",
            mimeType: "application/json",
        },
        async () => {
            const target = await resolveFeedbackStorageTarget(bridge);
            const feedback = loadFeedback(target);
            const entries = feedback.entries;
            const drafts = entries.filter((e) => e.status === "draft");
            const exported = entries.filter((e) => e.status === "exported" || e.status === "submitted");

            const summary = {
                project: feedback.project,
                total_entries: entries.length,
                draft_entries: drafts.length,
                exported_entries: exported.length,
                recent_drafts: drafts.slice(-10),
                recent_exported: exported.slice(-10),
            };

            return {
                contents: [{
                    uri: "unity://project/feedback",
                    mimeType: "application/json",
                    text: JSON.stringify(summary, null, 2),
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
