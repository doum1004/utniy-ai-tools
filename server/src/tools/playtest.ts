/**
 * Play-testing tools — input simulation, runtime state, gameplay capture.
 * Enables LLM agents to QA-test games by interacting and observing.
 */

import { z } from "zod";
import type { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { formatToolResult } from "./utils";

export function registerPlayTestTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "simulate_input",
        "Simulate keyboard and mouse input in Unity Play mode for QA testing. " +
        "Supports key presses (with optional hold duration), mouse clicks, moves, and drags. " +
        "Unity must be in Play mode.",
        {
            action: z.enum(["key_press", "key_down", "key_up", "mouse_click", "mouse_move", "mouse_drag"])
                .describe("Input action type"),
            key: z.string().optional()
                .describe("Key name for keyboard actions (e.g., 'W', 'Space', 'LeftShift', 'F1', 'Return')"),
            duration: z.number().optional()
                .describe("Hold duration in seconds for key_press (0 = tap)"),
            position: z.array(z.number()).optional()
                .describe("Screen position [x, y] for mouse actions"),
            from: z.array(z.number()).optional()
                .describe("Start position [x, y] for mouse_drag"),
            to: z.array(z.number()).optional()
                .describe("End position [x, y] for mouse_drag"),
            button: z.number().optional()
                .describe("Mouse button (0=left, 1=right, 2=middle)"),
        },
        async (params) => {
            const response = await bridge.sendCommand("simulate_input", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "read_runtime_state",
        "Read live runtime state during Unity Play mode — FPS, time, GameObject positions, " +
        "component values, physics state. Optionally target a specific GameObject and read " +
        "specific fields by path (e.g., 'Rigidbody.velocity', 'health').",
        {
            target: z.string().optional()
                .describe("GameObject name/path to inspect (e.g., 'Player', '/Environment/Enemy')"),
            fields: z.array(z.string()).optional()
                .describe("Specific field paths to read (e.g., ['Rigidbody.velocity', 'health', 'Transform.position'])"),
        },
        async (params) => {
            const response = await bridge.sendCommand("read_runtime_state", params);
            return formatToolResult(response);
        },
    );

    server.tool(
        "capture_gameplay",
        "Capture a sequence of screenshots during Play mode over a time period. " +
        "Returns multiple frames with timestamps, screenshots, FPS, and any console errors. " +
        "Use this to visually observe gameplay, detect visual bugs, and verify behavior. " +
        "Unity must be in Play mode.",
        {
            duration: z.number().optional().default(5)
                .describe("Total capture duration in seconds"),
            interval: z.number().optional().default(1)
                .describe("Seconds between each capture"),
            max_resolution: z.number().optional().default(640)
                .describe("Max pixel dimension for screenshots"),
            include_state: z.boolean().optional().default(true)
                .describe("Include FPS, frame count, and recent errors with each capture"),
        },
        async (params) => {
            const response = await bridge.sendCommand("capture_gameplay", {
                ...params,
                timeout_seconds: (params.duration ?? 5) + 10,
            });
            return formatToolResult(response);
        },
    );
}
