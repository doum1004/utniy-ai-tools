/**
 * Shared message types for WebSocket communication between
 * the MCP server and Unity Editor plugin.
 */

/** Server → Plugin: Initial welcome on connection */
export interface WelcomeMessage {
    type: "welcome";
    serverTimeout: number;
    keepAliveInterval: number;
}

/** Plugin → Server: Register a Unity project session */
export interface RegisterMessage {
    type: "register";
    project_name: string;
    project_hash: string;
    unity_version: string;
    project_path: string;
}

/** Server → Plugin: Confirm registration with session ID */
export interface RegisteredMessage {
    type: "registered";
    session_id: string;
}

/** Plugin → Server: Register available tool definitions */
export interface RegisterToolsMessage {
    type: "register_tools";
    tools: ToolDefinition[];
}

/** Server → Plugin: Execute a command */
export interface ExecuteCommandMessage {
    type: "execute_command";
    id: string;
    name: string;
    params: Record<string, unknown>;
    timeout: number;
}

/** Plugin → Server: Result of a command execution */
export interface CommandResultMessage {
    type: "command_result";
    id: string;
    result: CommandResult;
}

/** Server → Plugin: Heartbeat ping */
export interface PingMessage {
    type: "ping";
}

/** Plugin → Server: Heartbeat pong */
export interface PongMessage {
    type: "pong";
    session_id: string;
}

/** Tool definition registered by a Unity plugin */
export interface ToolDefinition {
    name: string;
    description: string;
    parameters: Record<string, unknown>;
}

/** Standard result from a Unity command */
export interface CommandResult {
    success: boolean;
    error?: string;
    data?: unknown;
    hint?: string;
}

/** Session info for a connected Unity instance */
export interface SessionInfo {
    sessionId: string;
    projectName: string;
    projectHash: string;
    unityVersion: string;
    projectPath: string;
    connectedAt: Date;
    lastSeen: Date;
    tools: Map<string, ToolDefinition>;
}

/** Inbound message union type */
export type InboundMessage =
    | RegisterMessage
    | RegisterToolsMessage
    | PongMessage
    | CommandResultMessage;

/** Outbound message union type */
export type OutboundMessage =
    | WelcomeMessage
    | RegisteredMessage
    | ExecuteCommandMessage
    | PingMessage;

/** Standard MCP response shape returned to AI clients */
export interface McpResponse {
    success: boolean;
    error?: string;
    data?: unknown;
    hint?: string;
}

export function createMcpResponse(
    success: boolean,
    data?: unknown,
    error?: string,
    hint?: string,
): McpResponse {
    return { success, ...(data !== undefined && { data }), ...(error && { error }), ...(hint && { hint }) };
}
