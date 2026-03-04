/**
 * Editor tools — play/pause, console, menu items, refresh, instance management.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerEditorTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_editor",
        "Control Unity Editor — play/pause/stop, focus objects, selection",
        {
            action: z.enum(["play", "pause", "stop", "step", "focus", "select", "get_selection"])
                .describe("Editor action to perform"),
            target: z.string().optional().describe("Target for focus/select actions"),
        },
        async (params) => {
            const response = await bridge.sendCommand("manage_editor", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "read_console",
        "Read Unity console logs, warnings, and errors",
        {
            types: z.array(z.enum(["log", "warning", "error"])).optional()
                .default(["error", "warning"]).describe("Log types to retrieve"),
            count: z.number().optional().default(10).describe("Number of entries to return"),
            include_stacktrace: z.boolean().optional().default(false).describe("Include stack traces"),
            format: z.enum(["summary", "detailed"]).optional().default("summary")
                .describe("Output format"),
        },
        async (params) => {
            const response = await bridge.sendCommand("read_console", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "refresh_unity",
        "Refresh Unity asset database and trigger script compilation. When wait_for_ready is true (default), waits for Unity to finish compiling — including surviving the WebSocket disconnect caused by domain reload.",
        {
            mode: z.enum(["normal", "force"]).optional().default("normal")
                .describe("Refresh mode"),
            scope: z.enum(["all", "scripts"]).optional().default("all")
                .describe("What to refresh"),
            compile: z.enum(["none", "request"]).optional().default("request")
                .describe("Whether to request compilation"),
            wait_for_ready: z.boolean().optional().default(true)
                .describe("Wait until Unity is ready after refresh (survives domain reload disconnect)"),
        },
        async (params) => {
            const refreshResponse = await bridge.sendCommand("refresh_unity", params);

            if (!refreshResponse.success) {
                return formatToolResult(refreshResponse);
            }

            if (!params.wait_for_ready) {
                return formatToolResult(refreshResponse);
            }

            const deadline = Date.now() + 90_000;
            let isCompiling = (refreshResponse.data as Record<string, unknown>)?.is_compiling === true;
            let reconnected = false;
            let pollCount = 0;

            while (Date.now() < deadline) {
                await Bun.sleep(isCompiling || !reconnected ? 2000 : 1000);
                pollCount++;

                const stateResponse = await bridge.sendCommand("get_editor_state", {});

                if (!stateResponse.success) {
                    // Unity is likely disconnected (domain reload). Keep waiting.
                    reconnected = false;
                    continue;
                }

                reconnected = true;
                const state = stateResponse.data as Record<string, unknown>;
                isCompiling = state?.is_compiling === true;

                if (!isCompiling) {
                    return {
                        content: [{
                            type: "text" as const,
                            text: JSON.stringify({
                                refreshed: true,
                                ready: true,
                                waited_polls: pollCount,
                                is_compiling: false,
                            }, null, 2),
                        }],
                    };
                }
            }

            return {
                content: [{
                    type: "text" as const,
                    text: JSON.stringify({
                        refreshed: true,
                        ready: false,
                        warning: "Unity is still compiling after 90s timeout",
                        waited_polls: pollCount,
                    }, null, 2),
                }],
            };
        },
    );

    server.tool(
        "execute_menu_item",
        "Execute a Unity Editor menu item by path",
        {
            menu_path: z.string().describe("Menu item path (e.g., 'Assets/Create/Material')"),
        },
        async (params) => {
            const response = await bridge.sendCommand("execute_menu_item", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "set_active_instance",
        "Set the active Unity instance when multiple editors are connected",
        {
            instance: z.string().describe("Instance identifier (Name@hash from unity://instances)"),
        },
        async (params) => {
            // Instance selection is handled server-side, not forwarded to Unity
            return formatToolResult({
                success: true,
                data: { active_instance: params.instance },
            });
        },
    );

    server.tool(
        "run_tests",
        "Run Unity tests (EditMode or PlayMode)",
        {
            mode: z.enum(["EditMode", "PlayMode"]).describe("Test mode"),
            test_names: z.array(z.string()).optional().describe("Specific test names to run (runs all if empty)"),
            category: z.string().optional().describe("Test category filter"),
        },
        async (params) => {
            const response = await bridge.sendCommand("run_tests", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "get_test_job",
        "Get the status and results of a test run job",
        {
            job_id: z.string().describe("Job ID from run_tests"),
            wait_timeout: z.number().optional().default(60).describe("Max seconds to wait for completion"),
            include_failed_tests: z.boolean().optional().default(true).describe("Include failed test details"),
        },
        async (params) => {
            const response = await bridge.sendCommand("get_test_job", params);
            return formatToolResult(response);
        },
    );
}
