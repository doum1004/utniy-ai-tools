import { describe, test, expect } from "bun:test";
import {
    DevlogComplianceTracker,
    isDevlogReadAction,
    isDevlogWriteAction,
    isMutatingToolCall,
} from "../src/devlog-compliance";

describe("DevlogComplianceTracker", () => {
    test("tracks read/write by session", () => {
        const tracker = new DevlogComplianceTracker();
        expect(tracker.hasRead("s1")).toBe(false);
        expect(tracker.hasWrite("s1")).toBe(false);

        tracker.markRead("s1");
        tracker.markWrite("s1");

        expect(tracker.hasRead("s1")).toBe(true);
        expect(tracker.hasWrite("s1")).toBe(true);
        expect(tracker.hasRead("s2")).toBe(false);
    });
});

describe("devlog action helpers", () => {
    test("recognizes read actions", () => {
        expect(isDevlogReadAction("manage_devlog", { action: "list" })).toBe(true);
        expect(isDevlogReadAction("manage_devlog", { action: "get" })).toBe(true);
        expect(isDevlogReadAction("manage_devlog", { action: "search" })).toBe(true);
        expect(isDevlogReadAction("manage_devlog", { action: "add" })).toBe(false);
    });

    test("recognizes write actions", () => {
        expect(isDevlogWriteAction("manage_devlog", { action: "add" })).toBe(true);
        expect(isDevlogWriteAction("manage_devlog", { action: "update" })).toBe(true);
        expect(isDevlogWriteAction("manage_devlog", { action: "list" })).toBe(false);
    });
});

describe("isMutatingToolCall", () => {
    test("allows read-only calls before devlog read", () => {
        expect(isMutatingToolCall("find_gameobjects", { search_term: "Player" })).toBe(false);
        expect(isMutatingToolCall("manage_scene", { action: "get_hierarchy" })).toBe(false);
        expect(isMutatingToolCall("manage_asset", { action: "search", search_term: "mat" })).toBe(false);
        expect(isMutatingToolCall("manage_script", { action: "read", path: "Assets/Foo.cs" })).toBe(false);
        expect(isMutatingToolCall("manage_feedback", { action: "add", source_type: "tool" })).toBe(false);
    });

    test("blocks mutating calls until devlog read", () => {
        expect(isMutatingToolCall("manage_gameobject", { action: "create" })).toBe(true);
        expect(isMutatingToolCall("manage_scene", { action: "load" })).toBe(true);
        expect(isMutatingToolCall("manage_components", { action: "set_property" })).toBe(true);
        expect(isMutatingToolCall("refresh_unity", { mode: "force" })).toBe(true);
    });

    test("inspects nested batch commands", () => {
        expect(
            isMutatingToolCall("batch_execute", {
                commands: [{ tool: "find_gameobjects", params: { search_term: "Enemy" } }],
            }),
        ).toBe(false);

        expect(
            isMutatingToolCall("batch_execute", {
                commands: [{ tool: "manage_gameobject", params: { action: "delete", target: "Enemy" } }],
            }),
        ).toBe(true);
    });
});
