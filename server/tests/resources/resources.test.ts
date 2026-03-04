import { describe, test, expect, mock } from "bun:test";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerResources } from "../../src/resources/index";

function createTrackingServer() {
    const registeredResources: string[] = [];
    const server = new McpServer({ name: "test", version: "0.0.1" });
    const origResource = server.resource.bind(server);
    server.resource = ((...args: unknown[]) => {
        registeredResources.push(args[0] as string);
        return (origResource as Function)(...args);
    }) as typeof server.resource;
    return { server, registeredResources };
}

const mockBridge = {
    sendCommand: mock(async () => ({ success: true, data: {} })),
    getSessions: mock(async () => new Map()),
} as any;

describe("Resource Registration", () => {
    test("registers all 8 resources", () => {
        const { server, registeredResources } = createTrackingServer();
        registerResources(server, mockBridge);

        expect(registeredResources).toHaveLength(8);
        expect(registeredResources).toContain("editor_state");
        expect(registeredResources).toContain("project_info");
        expect(registeredResources).toContain("unity_instances");
        expect(registeredResources).toContain("editor_selection");
        expect(registeredResources).toContain("project_tags");
        expect(registeredResources).toContain("project_layers");
        expect(registeredResources).toContain("devlog");
        expect(registeredResources).toContain("menu_items");
    });
});
