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

async function freePort(port: number): Promise<boolean> {
    let killed = false;

    if (process.platform !== "win32") {
        try {
            const proc = Bun.spawn(["sh", "-c", `lsof -ti:${port} | xargs -r kill -9`], {
                stdout: "pipe",
                stderr: "pipe",
            });
            const code = await proc.exited;
            killed = code === 0;
        } catch { /* best-effort */ }
        if (killed) await Bun.sleep(500);
        return killed;
    }

    try {
        const netstat = Bun.spawn(["cmd", "/c", `netstat -aon | findstr :${port} | findstr LISTENING`], {
            stdout: "pipe",
            stderr: "pipe",
        });
        const output = await new Response(netstat.stdout).text();
        await netstat.exited;

        const pids = new Set<string>();
        for (const line of output.split("\n")) {
            const parts = line.trim().split(/\s+/);
            const pid = parts[parts.length - 1];
            if (pid && /^\d+$/.test(pid) && pid !== "0") {
                pids.add(pid);
            }
        }

        for (const pid of pids) {
            const kill = Bun.spawn(["taskkill", "/F", "/PID", pid], {
                stdout: "pipe",
                stderr: "pipe",
            });
            await kill.exited;
            killed = true;
            console.log(`  ⤷ Killed stale process on port ${port} (PID ${pid})`);
        }
    } catch { /* best-effort */ }

    if (killed) await Bun.sleep(500);
    return killed;
}

async function listenWithRetry<T>(
    label: string,
    port: number,
    startFn: () => T,
): Promise<T> {
    try {
        return startFn();
    } catch (err: unknown) {
        const isAddrInUse =
            err instanceof Error &&
            ("code" in err && (err as NodeJS.ErrnoException).code === "EADDRINUSE");

        if (!isAddrInUse) throw err;

        console.log(`⚠  Port ${port} is in use — attempting to free it…`);
        const freed = await freePort(port);

        if (freed) {
            console.log(`✓  Port ${port} freed, retrying…`);
            return startFn();
        }

        console.error(
            `\n✗  Port ${port} is still in use and could not be freed automatically.\n` +
            `   ${label} needs port ${port} to start.\n\n` +
            `   To fix this, run:\n` +
            (process.platform === "win32"
                ? `     netstat -aon | findstr :${port}\n` +
                  `     taskkill /F /PID <pid>\n`
                : `     lsof -ti:${port} | xargs kill -9\n`) +
            `\n   Or set a different port with the ${label === "WebSocket" ? "WS_PORT" : "MCP_PORT"} environment variable.\n`,
        );
        process.exit(1);
    }
}

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
    const wsServer = await listenWithRetry("WebSocket", WS_PORT, () =>
        Bun.serve({
            port: WS_PORT,
            fetch(req, server) {
                if (server.upgrade(req, { data: { sessionId: undefined } })) return undefined;
                return new Response(JSON.stringify({ status: "ok", connections: registry.sessionCount }), {
                    headers: { "Content-Type": "application/json" },
                });
            },
            websocket: wsHandler,
        }),
    );

    console.log(`🔌 Unity WebSocket server listening on ws://localhost:${wsServer.port}`);

    // 3. Start MCP transport
    let httpServer: import("http").Server | undefined;

    if (TRANSPORT === "stdio") {
        console.log("📡 MCP transport: stdio");
        const mcpServer = createMcpServer(bridge);
        const transport = new StdioServerTransport();
        await mcpServer.connect(transport);
    } else {
        const app = express();
        app.use(cors());

        // Streamable HTTP sessions
        const sessions = new Map<string, { transport: StreamableHTTPServerTransport; server: ReturnType<typeof createMcpServer> }>();
        // Legacy SSE sessions (backwards compat for clients like Cursor CLI)
        const sseSessions = new Map<string, { transport: SSEServerTransport; server: ReturnType<typeof createMcpServer> }>();

        app.post("/mcp", async (req, res) => {
            const sessionId = req.headers["mcp-session-id"] as string | undefined;

            if (sessionId && sessions.has(sessionId)) {
                const session = sessions.get(sessionId)!;
                await session.transport.handleRequest(req, res);
                return;
            }

            if (sessionId && !sessions.has(sessionId)) {
                res.status(404).json({
                    jsonrpc: "2.0",
                    error: { code: -32000, message: "Session expired. Please reconnect." },
                    id: null,
                });
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

            // Streamable HTTP: existing session requesting SSE stream
            if (sessionId && sessions.has(sessionId)) {
                const session = sessions.get(sessionId)!;
                await session.transport.handleRequest(req, res);
                return;
            }

            // Legacy SSE: no session ID means client wants to open an SSE stream
            const transport = new SSEServerTransport("/messages", res);
            const server = createMcpServer(bridge);

            sseSessions.set(transport.sessionId, { transport, server });

            transport.onclose = () => {
                sseSessions.delete(transport.sessionId);
            };

            await server.connect(transport);
            await transport.start();
        });

        app.post("/messages", async (req, res) => {
            const sessionId = req.query.sessionId as string;
            const session = sseSessions.get(sessionId);
            if (!session) {
                res.status(404).json({ error: "SSE session not found" });
                return;
            }
            await session.transport.handlePostMessage(req, res);
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
                mcp_sessions: sessions.size,
                sse_sessions: sseSessions.size,
            });
        });

        httpServer = await listenWithRetry("MCP HTTP", MCP_PORT, () =>
            app.listen(MCP_PORT, () => {
                console.log(`📡 MCP endpoint ready at http://localhost:${MCP_PORT}/mcp (Streamable HTTP + SSE)`);
                console.log(`❤️  Health check at http://localhost:${MCP_PORT}/health`);
            }),
        );
    }

    console.log();
    console.log("Waiting for Unity Editor to connect...");
    console.log("Configure your MCP client to use: http://localhost:" + MCP_PORT + "/mcp");
    console.log();

    const gracefulShutdown = () => {
        console.log("\nShutting down...");
        bridge.shutdown();
        wsServer.stop(true);
        httpServer?.close();
        process.exit(0);
    };

    process.on("SIGINT", gracefulShutdown);
    process.on("SIGTERM", gracefulShutdown);
}

main().catch((err) => {
    console.error("\n✗  Fatal error:", err.message ?? err);
    process.exit(1);
});
