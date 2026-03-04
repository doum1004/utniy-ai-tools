import { describe, test, expect, mock } from "bun:test";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { registerSceneTools } from "../../src/tools/scene";
import { registerGameObjectTools } from "../../src/tools/gameobject";
import { registerComponentTools } from "../../src/tools/component";
import { registerScriptTools } from "../../src/tools/script";
import { registerAssetTools } from "../../src/tools/asset";
import { registerMaterialTools } from "../../src/tools/material";
import { registerPrefabTools } from "../../src/tools/prefab";
import { registerEditorTools } from "../../src/tools/editor";
import { registerBatchTools } from "../../src/tools/batch";
import { registerAnalysisTools } from "../../src/tools/analysis";
import { registerPlayTestTools } from "../../src/tools/playtest";
import { registerDevLogTools } from "../../src/tools/devlog";
import { registerFeedbackTools } from "../../src/tools/feedback";

function createTrackingServer() {
    const registeredTools: string[] = [];
    const server = new McpServer({ name: "test", version: "0.0.1" });
    const origTool = server.tool.bind(server);
    server.tool = ((...args: unknown[]) => {
        // First arg is always the tool name
        registeredTools.push(args[0] as string);
        return (origTool as Function)(...args);
    }) as typeof server.tool;
    return { server, registeredTools };
}

const mockBridge = {
    sendCommand: mock(async () => ({ success: true, data: {} })),
} as any;

describe("Tool Registration", () => {
    test("registerSceneTools registers manage_scene", () => {
        const { server, registeredTools } = createTrackingServer();
        registerSceneTools(server, mockBridge);
        expect(registeredTools).toContain("manage_scene");
    });

    test("registerGameObjectTools registers manage_gameobject and find_gameobjects", () => {
        const { server, registeredTools } = createTrackingServer();
        registerGameObjectTools(server, mockBridge);
        expect(registeredTools).toContain("manage_gameobject");
        expect(registeredTools).toContain("find_gameobjects");
    });

    test("registerComponentTools registers manage_components", () => {
        const { server, registeredTools } = createTrackingServer();
        registerComponentTools(server, mockBridge);
        expect(registeredTools).toContain("manage_components");
    });

    test("registerScriptTools registers all script tools", () => {
        const { server, registeredTools } = createTrackingServer();
        registerScriptTools(server, mockBridge);
        expect(registeredTools).toContain("create_script");
        expect(registeredTools).toContain("manage_script");
        expect(registeredTools).toContain("script_apply_edits");
        expect(registeredTools).toContain("validate_script");
        expect(registeredTools).toContain("delete_script");
        expect(registeredTools).toContain("get_sha");
        expect(registeredTools).toContain("apply_text_edits");
        expect(registeredTools).toHaveLength(7);
    });

    test("registerAssetTools registers manage_asset", () => {
        const { server, registeredTools } = createTrackingServer();
        registerAssetTools(server, mockBridge);
        expect(registeredTools).toContain("manage_asset");
    });

    test("registerMaterialTools registers manage_material", () => {
        const { server, registeredTools } = createTrackingServer();
        registerMaterialTools(server, mockBridge);
        expect(registeredTools).toContain("manage_material");
    });

    test("registerPrefabTools registers manage_prefabs", () => {
        const { server, registeredTools } = createTrackingServer();
        registerPrefabTools(server, mockBridge);
        expect(registeredTools).toContain("manage_prefabs");
    });

    test("registerEditorTools registers all editor tools", () => {
        const { server, registeredTools } = createTrackingServer();
        registerEditorTools(server, mockBridge);
        expect(registeredTools).toContain("manage_editor");
        expect(registeredTools).toContain("read_console");
        expect(registeredTools).toContain("refresh_unity");
        expect(registeredTools).toContain("execute_menu_item");
        expect(registeredTools).toContain("set_active_instance");
        expect(registeredTools).toContain("run_tests");
        expect(registeredTools).toContain("get_test_job");
        expect(registeredTools).toHaveLength(7);
    });

    test("registerBatchTools registers batch_execute", () => {
        const { server, registeredTools } = createTrackingServer();
        registerBatchTools(server, mockBridge);
        expect(registeredTools).toContain("batch_execute");
    });

    test("registerAnalysisTools registers all analysis tools", () => {
        const { server, registeredTools } = createTrackingServer();
        registerAnalysisTools(server, mockBridge);
        expect(registeredTools).toContain("analyze_scene");
        expect(registeredTools).toContain("inspect_gameobject");
        expect(registeredTools).toContain("get_project_settings");
        expect(registeredTools).toHaveLength(3);
    });

    test("registerPlayTestTools registers all play-testing tools", () => {
        const { server, registeredTools } = createTrackingServer();
        registerPlayTestTools(server, mockBridge);
        expect(registeredTools).toContain("simulate_input");
        expect(registeredTools).toContain("execute_method");
        expect(registeredTools).toContain("read_runtime_state");
        expect(registeredTools).toContain("capture_gameplay");
        expect(registeredTools).toHaveLength(4);
    });

    test("registerDevLogTools registers manage_devlog", () => {
        const { server, registeredTools } = createTrackingServer();
        registerDevLogTools(server, mockBridge);
        expect(registeredTools).toContain("manage_devlog");
        expect(registeredTools).toHaveLength(1);
    });

    test("registerFeedbackTools registers manage_feedback", () => {
        const { server, registeredTools } = createTrackingServer();
        registerFeedbackTools(server, mockBridge);
        expect(registeredTools).toContain("manage_feedback");
        expect(registeredTools).toHaveLength(1);
    });
});
