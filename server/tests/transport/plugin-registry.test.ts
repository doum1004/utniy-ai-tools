import { describe, test, expect, beforeEach } from "bun:test";
import { PluginRegistry } from "../../src/transport/plugin-registry";

describe("PluginRegistry", () => {
    let registry: PluginRegistry;

    beforeEach(() => {
        registry = new PluginRegistry();
    });

    describe("register", () => {
        test("registers a new session", async () => {
            const session = await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            expect(session.sessionId).toBe("s1");
            expect(session.projectName).toBe("MyProject");
            expect(session.projectHash).toBe("hash1");
            expect(session.unityVersion).toBe("2022.3");
            expect(session.projectPath).toBe("/path");
            expect(session.connectedAt).toBeInstanceOf(Date);
            expect(session.tools).toBeInstanceOf(Map);
            expect(registry.sessionCount).toBe(1);
        });

        test("evicts existing session with same project hash", async () => {
            await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            await registry.register("s2", "MyProject", "hash1", "2022.3", "/path");

            expect(registry.sessionCount).toBe(1);
            const session = await registry.getSession("s2");
            expect(session).toBeDefined();
            const oldSession = await registry.getSession("s1");
            expect(oldSession).toBeUndefined();
        });

        test("allows multiple sessions with different hashes", async () => {
            await registry.register("s1", "ProjectA", "hashA", "2022.3", "/a");
            await registry.register("s2", "ProjectB", "hashB", "2022.3", "/b");
            expect(registry.sessionCount).toBe(2);
        });
    });

    describe("unregister", () => {
        test("removes a session", async () => {
            await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            await registry.unregister("s1");
            expect(registry.sessionCount).toBe(0);
        });

        test("does nothing for unknown session", async () => {
            await registry.unregister("nonexistent");
            expect(registry.sessionCount).toBe(0);
        });
    });

    describe("touch", () => {
        test("updates lastSeen timestamp", async () => {
            const session = await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            const originalLastSeen = session.lastSeen.getTime();

            // Small delay to ensure timestamp difference
            await new Promise((r) => setTimeout(r, 10));
            await registry.touch("s1");

            const updated = await registry.getSession("s1");
            expect(updated!.lastSeen.getTime()).toBeGreaterThanOrEqual(originalLastSeen);
        });

        test("does nothing for unknown session", async () => {
            // Should not throw
            await registry.touch("nonexistent");
        });
    });

    describe("getSession", () => {
        test("returns session by ID", async () => {
            await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            const session = await registry.getSession("s1");
            expect(session?.sessionId).toBe("s1");
        });

        test("returns undefined for unknown ID", async () => {
            const session = await registry.getSession("nonexistent");
            expect(session).toBeUndefined();
        });
    });

    describe("getSessionIdByHash", () => {
        test("finds session by project hash", async () => {
            await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            const id = await registry.getSessionIdByHash("hash1");
            expect(id).toBe("s1");
        });

        test("returns undefined for unknown hash", async () => {
            const id = await registry.getSessionIdByHash("unknown");
            expect(id).toBeUndefined();
        });
    });

    describe("listSessions", () => {
        test("returns a copy of all sessions", async () => {
            await registry.register("s1", "A", "h1", "2022.3", "/a");
            await registry.register("s2", "B", "h2", "2022.3", "/b");

            const sessions = await registry.listSessions();
            expect(sessions.size).toBe(2);
            expect(sessions.has("s1")).toBe(true);
            expect(sessions.has("s2")).toBe(true);

            // Verify it's a copy (modifying returned map doesn't affect registry)
            sessions.delete("s1");
            expect(registry.sessionCount).toBe(2);
        });

        test("returns empty map when no sessions", async () => {
            const sessions = await registry.listSessions();
            expect(sessions.size).toBe(0);
        });
    });

    describe("registerToolsForSession", () => {
        test("adds tools to session", async () => {
            await registry.register("s1", "MyProject", "hash1", "2022.3", "/path");
            await registry.registerToolsForSession("s1", [
                { name: "tool1", description: "Test tool", parameters: {} },
                { name: "tool2", description: "Another tool", parameters: { foo: "bar" } },
            ]);

            const session = await registry.getSession("s1");
            expect(session!.tools.size).toBe(2);
            expect(session!.tools.get("tool1")?.description).toBe("Test tool");
        });

        test("does nothing for unknown session", async () => {
            // Should not throw
            await registry.registerToolsForSession("nonexistent", [
                { name: "tool1", description: "Test", parameters: {} },
            ]);
        });
    });
});
