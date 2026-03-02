/**
 * Plugin Registry — In-memory registry of connected Unity Editor sessions.
 */

import type { SessionInfo, ToolDefinition } from "./types";

export class PluginRegistry {
    private sessions = new Map<string, SessionInfo>();

    /** Register a new Unity plugin session */
    async register(
        sessionId: string,
        projectName: string,
        projectHash: string,
        unityVersion: string,
        projectPath: string,
    ): Promise<SessionInfo> {
        // Evict any existing session for the same project hash
        for (const [existingId, session] of this.sessions) {
            if (session.projectHash === projectHash) {
                this.sessions.delete(existingId);
                break;
            }
        }

        const session: SessionInfo = {
            sessionId,
            projectName,
            projectHash,
            unityVersion,
            projectPath,
            connectedAt: new Date(),
            lastSeen: new Date(),
            tools: new Map(),
        };

        this.sessions.set(sessionId, session);
        return session;
    }

    /** Unregister a session */
    async unregister(sessionId: string): Promise<void> {
        this.sessions.delete(sessionId);
    }

    /** Update last-seen timestamp */
    async touch(sessionId: string): Promise<void> {
        const session = this.sessions.get(sessionId);
        if (session) {
            session.lastSeen = new Date();
        }
    }

    /** Get session by ID */
    async getSession(sessionId: string): Promise<SessionInfo | undefined> {
        return this.sessions.get(sessionId);
    }

    /** Find session ID by project hash */
    async getSessionIdByHash(projectHash: string): Promise<string | undefined> {
        for (const [sessionId, session] of this.sessions) {
            if (session.projectHash === projectHash) {
                return sessionId;
            }
        }
        return undefined;
    }

    /** List all active sessions */
    async listSessions(): Promise<Map<string, SessionInfo>> {
        return new Map(this.sessions);
    }

    /** Get the count of active sessions */
    get sessionCount(): number {
        return this.sessions.size;
    }

    /** Register tools for a session */
    async registerToolsForSession(sessionId: string, tools: ToolDefinition[]): Promise<void> {
        const session = this.sessions.get(sessionId);
        if (!session) return;
        for (const tool of tools) {
            session.tools.set(tool.name, tool);
        }
    }
}
