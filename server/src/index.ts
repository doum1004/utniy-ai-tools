/**
 * Unity AI Tools — MCP Server Entry Point
 *
 * Starts both:
 * 1. An HTTP server with WebSocket for Unity plugin connections
 * 2. An MCP server (stdio or Streamable HTTP) for AI client connections
 *
 * Usage:
 *   bun run src/index.ts                    # Streamable HTTP (default)
 *   bun run src/index.ts --transport stdio  # stdio transport
 */

import express from "express";
import cors from "cors";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { PluginRegistry } from "./transport/plugin-registry";
import { UnityBridge } from "./transport/unity-bridge";
import { createMcpServer } from "./server";
import { isInitializeRequest } from "@modelcontextprotocol/sdk/types.js";

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
            if (server.upgrade(req, { data: { sessionId: undefined } })) return undefined;
            return new Response(JSON.stringify({ status: "ok", connections: registry.sessionCount }), {
                headers: { "Content-Type": "application/json" },
            });
        },
        websocket: wsHandler,
    });

    console.log(`🔌 Unity WebSocket server listening on ws://localhost:${wsServer.port}`);

    // 3. Start MCP transport
    if (TRANSPORT === "stdio") {
        console.log("📡 MCP transport: stdio");
        const mcpServer = createMcpServer(bridge);
        const transport = new StdioServerTransport();
        await mcpServer.connect(transport);
    } else {
        const app = express();
        app.use(cors());

        const sessions = new Map<string, { transport: StreamableHTTPServerTransport; server: ReturnType<typeof createMcpServer> }>();

        app.post("/mcp", async (req, res) => {
            const sessionId = req.headers["mcp-session-id"] as string | undefined;

            if (sessionId && sessions.has(sessionId)) {
                const session = sessions.get(sessionId)!;
                await session.transport.handleRequest(req, res);
                return;
            }

            const transport = new StreamableHTTPServerTransport({
                sessionIdGenerator: () => crypto.randomUUID(),
                onsessioninitialized: (id) => {
                    sessions.set(id, { transport, server });
                },
            });

            transport.onclose = () => {
                const id = [...sessions.entries()].find(([, s]) => s.transport === transport)?.[0];
                if (id) sessions.delete(id);
            };

            const server = createMcpServer(bridge);
            await server.connect(transport);
            await transport.handleRequest(req, res);
        });

        app.get("/mcp", async (req, res) => {
            const sessionId = req.headers["mcp-session-id"] as string | undefined;
            if (sessionId && sessions.has(sessionId)) {
                const session = sessions.get(sessionId)!;
                await session.transport.handleRequest(req, res);
                return;
            }
            res.status(400).json({ error: "No valid session. Send an initialize request first via POST." });
        });

        app.delete("/mcp", async (req, res) => {
            const sessionId = req.headers["mcp-session-id"] as string | undefined;
            if (sessionId && sessions.has(sessionId)) {
                const session = sessions.get(sessionId)!;
                await session.transport.handleRequest(req, res);
                sessions.delete(sessionId);
                return;
            }
            res.status(404).json({ error: "Session not found" });
        });

        app.get("/health", (_req, res) => {
            res.json({
                status: "ok",
                server: "unity-ai-tools",
                version: "0.1.0",
                unity_connected: bridge.isConnected,
                connections: registry.sessionCount,
                mcp_sessions: sessions.size
            });
        });

        app.listen(MCP_PORT, () => {
            console.log(`📡 MCP Streamable HTTP endpoint ready at http://localhost:${MCP_PORT}/mcp`);
            console.log(`❤️  Health check at http://localhost:${MCP_PORT}/health`);
        });
    }

    console.log();
    console.log("Waiting for Unity Editor to connect...");
    console.log("Configure your MCP client to use: http://localhost:" + MCP_PORT + "/mcp");
    console.log();

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
