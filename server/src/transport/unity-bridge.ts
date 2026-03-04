/**
 * Unity Bridge — WebSocket server managing connections to Unity Editor plugins.
 *
 * This is the core transport layer that:
 * 1. Accepts WebSocket connections from Unity Editor plugins
 * 2. Handles plugin registration and session management
 * 3. Dispatches commands from MCP tools → Unity and returns results
 * 4. Maintains keep-alive via ping/pong
 */

import type { ServerWebSocket } from "bun";
import { randomUUID } from "crypto";
import { PluginRegistry } from "./plugin-registry";
import type {
    CommandResult,
    ExecuteCommandMessage,
    InboundMessage,
    McpResponse,
    PingMessage,
    RegisteredMessage,
    WelcomeMessage,
} from "./types";
import { createMcpResponse } from "./types";

/** Timeouts in seconds (configurable via environment variables) */
const KEEP_ALIVE_INTERVAL = Number(process.env.KEEP_ALIVE_INTERVAL) || 15;
const SERVER_TIMEOUT = Number(process.env.SERVER_TIMEOUT) || 30;
const COMMAND_TIMEOUT = 30;
const PING_INTERVAL = Number(process.env.PING_INTERVAL) || 10;
const PING_TIMEOUT = Number(process.env.PING_TIMEOUT) || 45;
const FAST_FAIL_TIMEOUT = 2;

const FAST_FAIL_COMMANDS = new Set(["get_editor_state", "get_project_settings", "ping"]);

/** WebSocket user data attached to each connection */
interface WsData {
    sessionId?: string;
}

/** Pending command awaiting result from Unity */
interface PendingCommand {
    resolve: (result: CommandResult) => void;
    reject: (error: Error) => void;
    sessionId: string;
    timer: ReturnType<typeof setTimeout>;
}

export class UnityBridge {
    private registry: PluginRegistry;
    private connections = new Map<string, ServerWebSocket<WsData>>();
    private pending = new Map<string, PendingCommand>();
    private pingTimers = new Map<string, ReturnType<typeof setInterval>>();
    private lastPong = new Map<string, number>();

    constructor(registry: PluginRegistry) {
        this.registry = registry;
    }

    /** Get the Bun WebSocket handler config */
    getWebSocketHandler() {
        return {
            open: (ws: ServerWebSocket<WsData>) => this.onConnect(ws),
            message: (ws: ServerWebSocket<WsData>, message: string | Buffer) =>
                this.onMessage(ws, message),
            close: (ws: ServerWebSocket<WsData>, code: number) =>
                this.onDisconnect(ws, code),
        };
    }

    /** Handle new WebSocket connection */
    private onConnect(ws: ServerWebSocket<WsData>): void {
        const welcome: WelcomeMessage = {
            type: "welcome",
            serverTimeout: SERVER_TIMEOUT,
            keepAliveInterval: KEEP_ALIVE_INTERVAL,
        };
        ws.send(JSON.stringify(welcome));
        console.log("[UnityBridge] New connection, welcome sent");
    }

    /** Handle incoming WebSocket message */
    private async onMessage(ws: ServerWebSocket<WsData>, raw: string | Buffer): Promise<void> {
        let data: InboundMessage;
        try {
            data = JSON.parse(typeof raw === "string" ? raw : raw.toString());
        } catch {
            console.warn("[UnityBridge] Invalid JSON from plugin");
            return;
        }

        switch (data.type) {
            case "register":
                await this.handleRegister(ws, data);
                break;
            case "register_tools":
                await this.handleRegisterTools(ws, data);
                break;
            case "pong":
                this.handlePong(data);
                break;
            case "command_result":
                this.handleCommandResult(data);
                break;
            default:
                console.debug("[UnityBridge] Unknown message type:", (data as { type: string }).type);
        }
    }

    /** Handle WebSocket disconnect */
    private async onDisconnect(ws: ServerWebSocket<WsData>, code: number): Promise<void> {
        const sessionId = ws.data.sessionId;
        if (!sessionId) {
            console.log(`[UnityBridge] Connection closed before registration (code: ${code})`);
            return;
        }

        // Grab project name before unregistering
        const session = await this.registry.getSession(sessionId);
        const label = session ? `${session.projectName} (${session.projectHash})` : sessionId;

        // Clean up connection
        this.connections.delete(sessionId);

        // Stop ping timer
        const pingTimer = this.pingTimers.get(sessionId);
        if (pingTimer) {
            clearInterval(pingTimer);
            this.pingTimers.delete(sessionId);
        }
        this.lastPong.delete(sessionId);

        // Fail pending commands for this session
        for (const [commandId, pending] of this.pending) {
            if (pending.sessionId === sessionId) {
                clearTimeout(pending.timer);
                pending.reject(new Error(`Unity plugin session ${sessionId} disconnected`));
                this.pending.delete(commandId);
            }
        }

        await this.registry.unregister(sessionId);
        console.log(`[UnityBridge] Disconnected: ${label} (code: ${code})`);
    }

    // ------------------------------------------------------------------
    // Public API — used by MCP tools
    // ------------------------------------------------------------------

    /**
     * Send a command to a Unity instance and wait for the result.
     * Resolves the correct session automatically.
     */
    async sendCommand(
        commandType: string,
        params: Record<string, unknown>,
        targetInstance?: string,
    ): Promise<McpResponse> {
        const isFastFail = FAST_FAIL_COMMANDS.has(commandType);
        let sessionId: string;
        try {
            sessionId = await this.resolveSessionId(targetInstance, isFastFail);
        } catch (err) {
            return createMcpResponse(false, undefined, (err as Error).message, "retry");
        }

        const ws = this.connections.get(sessionId);
        if (!ws) {
            return createMcpResponse(false, undefined, "Unity session not available", "retry");
        }

        const commandId = randomUUID();
        const timeoutMs = (isFastFail ? FAST_FAIL_TIMEOUT : COMMAND_TIMEOUT) * 1000;

        // Check for caller-specified timeout
        let effectiveTimeoutMs = timeoutMs;
        if (!isFastFail && params.timeout_seconds != null) {
            const requested = Number(params.timeout_seconds);
            if (!isNaN(requested) && requested > 0) {
                effectiveTimeoutMs = Math.max(timeoutMs, Math.min(requested * 1000, 3600_000));
            }
        }

        return new Promise<McpResponse>((resolve) => {
            const timer = setTimeout(() => {
                this.pending.delete(commandId);
                if (isFastFail) {
                    resolve(createMcpResponse(
                        false,
                        undefined,
                        `Unity did not respond to '${commandType}' within ${effectiveTimeoutMs / 1000}s`,
                        "retry",
                    ));
                } else {
                    resolve(createMcpResponse(false, undefined, `Command '${commandType}' timed out`));
                }
            }, effectiveTimeoutMs);

            this.pending.set(commandId, {
                resolve: (result) => {
                    clearTimeout(timer);
                    this.pending.delete(commandId);
                    resolve(createMcpResponse(result.success, result.data, result.error, result.hint));
                },
                reject: (error) => {
                    clearTimeout(timer);
                    this.pending.delete(commandId);
                    resolve(createMcpResponse(false, undefined, error.message, "retry"));
                },
                sessionId,
                timer,
            });

            const msg: ExecuteCommandMessage = {
                type: "execute_command",
                id: commandId,
                name: commandType,
                params,
                timeout: effectiveTimeoutMs / 1000,
            };

            try {
                ws.send(JSON.stringify(msg));
            } catch (err) {
                clearTimeout(timer);
                this.pending.delete(commandId);
                resolve(createMcpResponse(false, undefined, `Failed to send command: ${err}`, "retry"));
            }
        });
    }

    /** Get all connected sessions */
    async getSessions(): Promise<Map<string, { project: string; hash: string; unityVersion: string; connectedAt: string }>> {
        const sessions = await this.registry.listSessions();
        const result = new Map<string, { project: string; hash: string; unityVersion: string; connectedAt: string }>();
        for (const [id, session] of sessions) {
            result.set(id, {
                project: session.projectName,
                hash: session.projectHash,
                unityVersion: session.unityVersion,
                connectedAt: session.connectedAt.toISOString(),
            });
        }
        return result;
    }

    /** Check if any Unity plugins are connected */
    get isConnected(): boolean {
        return this.connections.size > 0;
    }

    // ------------------------------------------------------------------
    // Message handlers
    // ------------------------------------------------------------------

    private async handleRegister(ws: ServerWebSocket<WsData>, data: { project_name: string; project_hash: string; unity_version: string; project_path: string }): Promise<void> {
        if (!data.project_hash) {
            ws.close(4400, "Missing project_hash");
            return;
        }

        const sessionId = randomUUID();
        ws.data.sessionId = sessionId;

        // Send registration confirmation
        const response: RegisteredMessage = {
            type: "registered",
            session_id: sessionId,
        };
        ws.send(JSON.stringify(response));

        // Register in the plugin registry
        await this.registry.register(
            sessionId,
            data.project_name,
            data.project_hash,
            data.unity_version,
            data.project_path,
        );

        this.connections.set(sessionId, ws);

        // Start ping loop
        this.lastPong.set(sessionId, Date.now());
        const pingTimer = setInterval(() => this.pingLoop(sessionId), PING_INTERVAL * 1000);
        this.pingTimers.set(sessionId, pingTimer);

        console.log(`[UnityBridge] Registered: ${data.project_name} (${data.project_hash})`);
    }

    private async handleRegisterTools(ws: ServerWebSocket<WsData>, data: { tools: Array<{ name: string; description: string; parameters: Record<string, unknown> }> }): Promise<void> {
        const sessionId = ws.data.sessionId;
        if (!sessionId) {
            console.warn("[UnityBridge] register_tools from unknown connection");
            return;
        }

        await this.registry.registerToolsForSession(sessionId, data.tools);
        console.log(`[UnityBridge] Registered ${data.tools.length} tools for session ${sessionId}`);
    }

    private handlePong(data: { session_id: string }): void {
        if (data.session_id) {
            this.lastPong.set(data.session_id, Date.now());
            this.registry.touch(data.session_id);
        }
    }

    private handleCommandResult(data: { id: string; result: CommandResult }): void {
        const pending = this.pending.get(data.id);
        if (pending) {
            pending.resolve(data.result);
        }
    }

    // ------------------------------------------------------------------
    // Session resolution
    // ------------------------------------------------------------------

    /**
     * Resolve to a valid session ID.
     * Supports targetInstance as "Name@hash" or just "hash".
     * If no target specified and only one session exists, auto-selects it.
     */
    private async resolveSessionId(targetInstance?: string, fastFail = false): Promise<string> {
        if (targetInstance) {
            let targetHash: string;
            if (targetInstance.includes("@")) {
                targetHash = targetInstance.split("@").pop()!;
            } else {
                targetHash = targetInstance;
            }

            const sessionId = await this.registry.getSessionIdByHash(targetHash);
            if (sessionId) return sessionId;

            if (fastFail) {
                throw new Error(`Unity instance '${targetInstance}' is not connected`);
            }

            // Wait briefly for reconnection (domain reload)
            const deadline = Date.now() + 10_000;
            while (Date.now() < deadline) {
                await Bun.sleep(250);
                const retryId = await this.registry.getSessionIdByHash(targetHash);
                if (retryId) return retryId;
            }

            throw new Error(`Unity instance '${targetInstance}' is not connected`);
        }

        // No target — auto-resolve
        const sessions = await this.registry.listSessions();
        if (sessions.size === 0) {
            if (fastFail) {
                throw new Error("No Unity plugins are currently connected");
            }

            // Wait for any session to appear
            const deadline = Date.now() + 15_000;
            while (Date.now() < deadline) {
                await Bun.sleep(250);
                const retry = await this.registry.listSessions();
                if (retry.size > 0) {
                    return retry.keys().next().value!;
                }
            }
            throw new Error("No Unity plugins are currently connected");
        }

        if (sessions.size === 1) {
            return sessions.keys().next().value!;
        }

        throw new Error(
            "Multiple Unity instances are connected. Use set_active_instance with Name@hash from unity://instances.",
        );
    }

    // ------------------------------------------------------------------
    // Keep-alive
    // ------------------------------------------------------------------

    private pingLoop(sessionId: string): void {
        const lastPongTime = this.lastPong.get(sessionId);
        if (lastPongTime && Date.now() - lastPongTime > PING_TIMEOUT * 1000) {
            console.warn(`[UnityBridge] Session ${sessionId} stale — closing`);
            const ws = this.connections.get(sessionId);
            if (ws) ws.close(1001, "Ping timeout");
            return;
        }

        const ws = this.connections.get(sessionId);
        if (!ws) return;

        try {
            const ping: PingMessage = { type: "ping" };
            ws.send(JSON.stringify(ping));
        } catch {
            console.warn(`[UnityBridge] Failed to ping session ${sessionId}`);
        }
    }

    /** Clean up all connections and timers */
    shutdown(): void {
        for (const timer of this.pingTimers.values()) {
            clearInterval(timer);
        }
        for (const pending of this.pending.values()) {
            clearTimeout(pending.timer);
            pending.reject(new Error("Server shutting down"));
        }
        for (const ws of this.connections.values()) {
            try {
                ws.close(1001, "Server shutdown");
            } catch { /* ignore */ }
        }
        this.connections.clear();
        this.pending.clear();
        this.pingTimers.clear();
        this.lastPong.clear();
    }
}
