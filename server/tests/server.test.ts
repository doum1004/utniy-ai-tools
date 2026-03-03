import { describe, test, expect, mock } from "bun:test";
import { createMcpServer } from "../src/server";

const mockBridge = {
    sendCommand: mock(async () => ({ success: true, data: {} })),
    getSessions: mock(async () => new Map()),
} as any;

describe("createMcpServer", () => {
    test("creates an McpServer instance", () => {
        const server = createMcpServer(mockBridge);
        expect(server).toBeDefined();
    });

    test("does not throw during tool and resource registration", () => {
        expect(() => createMcpServer(mockBridge)).not.toThrow();
    });
});
