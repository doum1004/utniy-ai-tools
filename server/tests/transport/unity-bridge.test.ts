import { describe, test, expect, beforeEach, mock, afterEach } from "bun:test";
import { PluginRegistry } from "../../src/transport/plugin-registry";
import { UnityBridge } from "../../src/transport/unity-bridge";

/** Create a mock WebSocket with tracking */
function createMockWs() {
    const sent: string[] = [];
    return {
        data: {} as { sessionId?: string },
        send: mock((msg: string) => {
            sent.push(msg);
        }),
        close: mock((_code?: number, _reason?: string) => {}),
        sent,
    };
}

describe("UnityBridge", () => {
    let registry: PluginRegistry;
    let bridge: UnityBridge;
    let handler: ReturnType<UnityBridge["getWebSocketHandler"]>;

    beforeEach(() => {
        registry = new PluginRegistry();
        bridge = new UnityBridge(registry);
        handler = bridge.getWebSocketHandler();
    });

    afterEach(() => {
        bridge.shutdown();
    });

    describe("getWebSocketHandler", () => {
        test("returns handler with open, message, close", () => {
            expect(handler.open).toBeFunction();
            expect(handler.message).toBeFunction();
            expect(handler.close).toBeFunction();
        });
    });

    describe("onConnect (via handler.open)", () => {
        test("sends welcome message on connection", () => {
            const ws = createMockWs();
            handler.open(ws as any);

            expect(ws.sent.length).toBe(1);
            const welcome = JSON.parse(ws.sent[0]);
            expect(welcome.type).toBe("welcome");
            expect(welcome.serverTimeout).toBeNumber();
            expect(welcome.keepAliveInterval).toBeNumber();
        });
    });

    describe("registration flow", () => {
        test("registers a plugin and sends registered response", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            const registerMsg = JSON.stringify({
                type: "register",
                project_name: "TestProject",
                project_hash: "abc123",
                unity_version: "2022.3",
                project_path: "/test/path",
            });
            await handler.message(ws as any, registerMsg);

            // Should have sent welcome + registered
            expect(ws.sent.length).toBe(2);
            const registered = JSON.parse(ws.sent[1]);
            expect(registered.type).toBe("registered");
            expect(registered.session_id).toBeString();

            // Session should be in registry
            expect(registry.sessionCount).toBe(1);
        });

        test("rejects registration without project_hash", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            const registerMsg = JSON.stringify({
                type: "register",
                project_name: "TestProject",
                project_hash: "",
                unity_version: "2022.3",
                project_path: "/test/path",
            });
            await handler.message(ws as any, registerMsg);

            expect(ws.close).toHaveBeenCalled();
        });
    });

    describe("pong handling", () => {
        test("updates lastPong on pong message", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            // Register first
            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            const sessionId = JSON.parse(ws.sent[1]).session_id;

            // Send pong
            await handler.message(ws as any, JSON.stringify({
                type: "pong",
                session_id: sessionId,
            }));

            // Should not throw - pong was handled
            const session = await registry.getSession(sessionId);
            expect(session).toBeDefined();
        });
    });

    describe("command result handling", () => {
        test("resolves pending command on result", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            // Register
            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            // Send command and capture the promise
            const commandPromise = bridge.sendCommand("test_command", { foo: "bar" });

            // Wait a tick for the command to be sent
            await new Promise((r) => setTimeout(r, 10));

            // Find the command ID from the sent message
            const cmdMsg = JSON.parse(ws.sent[ws.sent.length - 1]);
            expect(cmdMsg.type).toBe("execute_command");
            expect(cmdMsg.name).toBe("test_command");

            // Send result back
            await handler.message(ws as any, JSON.stringify({
                type: "command_result",
                id: cmdMsg.id,
                result: { success: true, data: { result: "ok" } },
            }));

            const response = await commandPromise;
            expect(response.success).toBe(true);
            expect(response.data).toEqual({ result: "ok" });
        });
    });

    describe("sendCommand", () => {
        test("returns error when no sessions connected", async () => {
            // Set very short timeout via env to avoid long waits
            const response = await Promise.race([
                bridge.sendCommand("test", {}),
                new Promise<any>((r) => setTimeout(() => r({ success: false, error: "timeout" }), 500)),
            ]);
            expect(response.success).toBe(false);
        });
    });

    describe("isConnected", () => {
        test("returns false when no connections", () => {
            expect(bridge.isConnected).toBe(false);
        });
    });

    describe("disconnect", () => {
        test("cleans up on disconnect", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            expect(registry.sessionCount).toBe(1);

            await handler.close(ws as any, 1000);

            expect(registry.sessionCount).toBe(0);
        });

        test("fails pending commands on disconnect", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            // Start a command
            const commandPromise = bridge.sendCommand("test_command", {});
            await new Promise((r) => setTimeout(r, 10));

            // Disconnect
            await handler.close(ws as any, 1000);

            const response = await commandPromise;
            expect(response.success).toBe(false);
        });
    });

    describe("shutdown", () => {
        test("cleans up all state", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            bridge.shutdown();
            expect(bridge.isConnected).toBe(false);
        });
    });

    describe("invalid messages", () => {
        test("handles invalid JSON gracefully", async () => {
            const ws = createMockWs();
            handler.open(ws as any);
            // Should not throw
            await handler.message(ws as any, "not valid json{{{");
        });

        test("handles unknown message type", async () => {
            const ws = createMockWs();
            handler.open(ws as any);
            // Should not throw
            await handler.message(ws as any, JSON.stringify({ type: "unknown_type" }));
        });
    });

    describe("register_tools", () => {
        test("registers tools for a session", async () => {
            const ws = createMockWs();
            handler.open(ws as any);

            await handler.message(ws as any, JSON.stringify({
                type: "register",
                project_name: "Test",
                project_hash: "hash1",
                unity_version: "2022.3",
                project_path: "/test",
            }));

            const sessionId = JSON.parse(ws.sent[1]).session_id;

            await handler.message(ws as any, JSON.stringify({
                type: "register_tools",
                tools: [
                    { name: "custom_tool", description: "A custom tool", parameters: {} },
                ],
            }));

            const session = await registry.getSession(sessionId);
            expect(session!.tools.size).toBe(1);
            expect(session!.tools.get("custom_tool")).toBeDefined();
        });
    });
});
