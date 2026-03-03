/**
 * Unity AI Tools — MCP Server Entry Point
 *
 * Starts both:
 * 1. An HTTP server with WebSocket for Unity plugin connections
 * 2. An MCP server (stdio or HTTP Streamable) for AI client connections
 *
 * Usage:
 *   bun run src/index.ts                    # HTTP Streamable (default)
 *   bun run src/index.ts --transport stdio  # stdio transport
 */

import express from "express";
import cors from "cors";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { PluginRegistry } from "./transport/plugin-registry";
import { UnityBridge } from "./transport/unity-bridge";
import { createMcpServer } from "./server";

// ------------------------------------------------------------------
// Configuration
// ------------------------------------------------------------------

const MCP_PORT = parseInt(process.env.MCP_PORT ?? "8090", 10);
const WS_PORT = parseInt(process.env.WS_PORT ?? "8091", 10);
const TRANSPORT = process.argv.includes("--transport")
    ? process.argv[process.argv.indexOf("--transport") + 1]
    : "http";

// ------------------------------------------------------------------
// Bootstrap
// ------------------------------------------------------------------

async function main(): Promise<void> {
    console.log("╔══════════════════════════════════════════╗");
    console.log("║        Unity AI Tools MCP Server         ║");
    console.log("╚══════════════════════════════════════════╝");
    console.log();

    // 1. Create plugin registry and Unity bridge
    const registry = new PluginRegistry();
    const bridge = new UnityBridge(registry);

    // 2. Start WebSocket server for Unity plugin connections
    const wsHandler = bridge.getWebSocketHandler();
    const wsServer = Bun.serve({
        port: WS_PORT,
        fetch(req, server) {
            // Upgrade WebSocket connections
            if (server.upgrade(req, { data: { sessionId: undefined } })) return undefined;
            // Health check endpoint
            return new Response(JSON.stringify({ status: "ok", connections: registry.sessionCount }), {
                headers: { "Content-Type": "application/json" },
            });
        },
        websocket: wsHandler,
    });

    console.log(`🔌 Unity WebSocket server listening on ws://localhost:${wsServer.port}`);

    // 4. Start MCP transport
    if (TRANSPORT === "stdio") {
        console.log("📡 MCP transport: stdio");
        const mcpServer = createMcpServer(bridge);
        const transport = new StdioServerTransport();
        await mcpServer.connect(transport);
    } else {
        // SSE transport with Express
        const app = express();
        app.use(cors());

        const transports = new Map<string, SSEServerTransport>();

        app.get("/mcp", async (_req, res) => {
            const transport = new SSEServerTransport("/mcp/messages", res);
            transports.set(transport.sessionId, transport);

            res.on("close", () => {
                transports.delete(transport.sessionId);
            });

            const server = createMcpServer(bridge);
            await server.connect(transport);
        });

        app.post("/mcp/messages", async (req, res) => {
            const sessionId = req.query.sessionId as string;
            const transport = transports.get(sessionId);
            if (transport) {
                await transport.handlePostMessage(req, res);
            } else {
                res.status(400).send("Unknown session ID");
            }
        });

        app.get("/health", (_req, res) => {
            res.json({
                status: "ok",
                server: "unity-ai-tools",
                version: "0.1.0",
                unity_connected: bridge.isConnected,
                connections: registry.sessionCount
            });
        });

        app.listen(MCP_PORT, () => {
            console.log(`📡 MCP SSE endpoint ready at http://localhost:${MCP_PORT}/mcp`);
            console.log(`❤️  Health check at http://localhost:${MCP_PORT}/health`);
        });
    }

    console.log();
    console.log("Waiting for Unity Editor to connect...");
    console.log("Configure your MCP client to use: http://localhost:" + MCP_PORT + "/mcp");
    console.log();

    // Graceful shutdown
    process.on("SIGINT", () => {
        console.log("\nShutting down...");
        bridge.shutdown();
        process.exit(0);
    });

    process.on("SIGTERM", () => {
        bridge.shutdown();
        process.exit(0);
    });
}

main().catch((err) => {
    console.error("Fatal error:", err);
    process.exit(1);
});
