import { dirname, resolve } from "path";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "fs";
import type { UnityBridge } from "./transport/unity-bridge";

export interface DevLogEntry {
    id: string;
    type: "plan" | "milestone" | "decision" | "issue" | "iteration" | "note";
    title: string;
    body: string;
    status: "active" | "completed" | "dropped" | "blocked";
    tags: string[];
    parent_id?: string;
    created_at: string;
    updated_at: string;
}

export interface DevLogFile {
    project: string;
    entries: DevLogEntry[];
}

interface StorageTarget {
    projectName: string;
    primaryPath?: string;
    dotFolderFallbackPath?: string;
    legacyPath: string;
}

const LEGACY_DATA_DIR = resolve(
    process.env.DEVLOG_DIR ?? resolve(import.meta.dir, "../data/devlogs"),
);

export function sanitizeProjectKey(name: string): string {
    return name.replace(/[^a-zA-Z0-9_-]/g, "_").toLowerCase();
}

export async function resolveStorageTarget(bridge: UnityBridge): Promise<StorageTarget> {
    const sessions = await bridge.getSessions();
    if (sessions.size === 0) {
        const legacyPath = resolve(LEGACY_DATA_DIR, "_unlinked.json");
        return { projectName: "unlinked", legacyPath };
    }

    const first = sessions.values().next().value!;
    const key = sanitizeProjectKey(first.project);
    const legacyPath = resolve(LEGACY_DATA_DIR, `${key}.json`);
    const primaryPath = resolve(first.projectPath, "DevLogs", "devlog.json");
    const dotFolderFallbackPath = resolve(first.projectPath, ".unity-ai", "devlog.json");

    return {
        projectName: first.project,
        primaryPath,
        dotFolderFallbackPath,
        legacyPath,
    };
}

export function loadDevLog(target: StorageTarget): DevLogFile {
    // Preferred location: visible, team-shared folder in project root.
    if (target.primaryPath && existsSync(target.primaryPath)) {
        try {
            return JSON.parse(readFileSync(target.primaryPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    // Backward compatibility: old dot-folder location.
    if (target.dotFolderFallbackPath && existsSync(target.dotFolderFallbackPath)) {
        try {
            return JSON.parse(readFileSync(target.dotFolderFallbackPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    // Legacy fallback for compatibility/migration
    if (existsSync(target.legacyPath)) {
        try {
            return JSON.parse(readFileSync(target.legacyPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    return { project: target.projectName, entries: [] };
}

export function saveDevLog(target: StorageTarget, log: DevLogFile): void {
    if (target.primaryPath) {
        mkdirSync(dirname(target.primaryPath), { recursive: true });
        writeFileSync(target.primaryPath, JSON.stringify(log, null, 2), "utf-8");
        return;
    }

    // Last-resort fallback if Unity is unlinked.
    mkdirSync(dirname(target.legacyPath), { recursive: true });
    writeFileSync(target.legacyPath, JSON.stringify(log, null, 2), "utf-8");
}
