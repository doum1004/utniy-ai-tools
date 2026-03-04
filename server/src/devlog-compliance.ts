/**
 * Devlog compliance guardrails.
 *
 * Tracks per-session devlog read/write state and determines whether
 * a tool call should be blocked until the session reads unity://project/devlog.
 */

const FALLBACK_SESSION = "__default__";

type ToolParams = Record<string, unknown> | undefined;

export class DevlogComplianceTracker {
    private readonly readSessions = new Set<string>();
    private readonly writeSessions = new Set<string>();

    markRead(sessionId?: string): void {
        this.readSessions.add(sessionId ?? FALLBACK_SESSION);
    }

    markWrite(sessionId?: string): void {
        this.writeSessions.add(sessionId ?? FALLBACK_SESSION);
    }

    hasRead(sessionId?: string): boolean {
        return this.readSessions.has(sessionId ?? FALLBACK_SESSION);
    }

    hasWrite(sessionId?: string): boolean {
        return this.writeSessions.has(sessionId ?? FALLBACK_SESSION);
    }
}

/**
 * Returns true if this call should be blocked unless devlog has been read.
 */
export function isMutatingToolCall(toolName: string, params: ToolParams): boolean {
    const action = typeof params?.action === "string" ? params.action : undefined;

    switch (toolName) {
        case "manage_gameobject":
        case "create_script":
        case "script_apply_edits":
        case "delete_script":
        case "apply_text_edits":
        case "refresh_unity":
        case "execute_menu_item":
        case "simulate_input":
        case "execute_method":
            return true;

        case "manage_scene":
            return action !== "get_hierarchy" &&
                action !== "screenshot" &&
                action !== "annotated_screenshot" &&
                action !== "get_active";

        case "manage_components":
            return action !== "get" && action !== "get_all";

        case "manage_script":
            return action !== "read" && action !== "info";

        case "manage_asset":
            return action !== "search" && action !== "info";

        case "manage_material":
            return action !== "get" && action !== "list";

        case "manage_prefabs":
            return action !== "get_info";

        case "manage_editor":
            return action !== "get_selection";

        case "batch_execute":
            return isMutatingBatch(params);

        case "manage_devlog":
        case "manage_feedback":
        case "find_gameobjects":
        case "validate_script":
        case "get_sha":
        case "read_console":
        case "set_active_instance":
        case "run_tests":
        case "get_test_job":
        case "read_runtime_state":
        case "capture_gameplay":
        case "analyze_scene":
        case "inspect_gameobject":
        case "get_project_settings":
            return false;

        default:
            // Unknown tools default to "mutating" for safety.
            return true;
    }
}

export function isDevlogReadAction(toolName: string, params: ToolParams): boolean {
    if (toolName !== "manage_devlog") return false;
    return params?.action === "list" || params?.action === "get" || params?.action === "search";
}

export function isDevlogWriteAction(toolName: string, params: ToolParams): boolean {
    if (toolName !== "manage_devlog") return false;
    return params?.action === "add" || params?.action === "update";
}

function isMutatingBatch(params: ToolParams): boolean {
    if (!params || !Array.isArray(params.commands)) return false;

    for (const command of params.commands) {
        if (!command || typeof command !== "object") continue;
        const tool = typeof (command as Record<string, unknown>).tool === "string"
            ? (command as Record<string, unknown>).tool as string
            : undefined;
        const nestedParams = ((command as Record<string, unknown>).params ?? {}) as ToolParams;
        if (!tool) continue;
        if (isMutatingToolCall(tool, nestedParams)) {
            return true;
        }
    }

    return false;
}
