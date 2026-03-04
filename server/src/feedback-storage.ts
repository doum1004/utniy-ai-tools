import { dirname, resolve } from "path";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "fs";
import type { UnityBridge } from "./transport/unity-bridge";

export interface FeedbackEntry {
    id: string;
    source_type: "tool" | "skill" | "workflow";
    source_name: string;
    summary: string;
    details: string;
    suggestion: string;
    rating?: number;
    tags: string[];
    status: "draft" | "exported" | "archived" | "submitted";
    created_at: string;
    updated_at: string;
    exported_at?: string;
    submitted_at?: string;
}

export interface FeedbackFile {
    project: string;
    entries: FeedbackEntry[];
}

interface FeedbackStorageTarget {
    projectName: string;
    primaryPath?: string;
    dotFolderFallbackPath?: string;
    legacyPath: string;
    submissionDir?: string;
}

const LEGACY_FEEDBACK_DIR = resolve(
    process.env.FEEDBACK_DIR ?? resolve(import.meta.dir, "../data/feedback"),
);

function sanitizeProjectKey(name: string): string {
    return name.replace(/[^a-zA-Z0-9_-]/g, "_").toLowerCase();
}

export async function resolveFeedbackStorageTarget(bridge: UnityBridge): Promise<FeedbackStorageTarget> {
    const sessions = await bridge.getSessions();
    if (sessions.size === 0) {
        const legacyPath = resolve(LEGACY_FEEDBACK_DIR, "_unlinked.json");
        const submissionDir = resolve(LEGACY_FEEDBACK_DIR, "submissions");
        return { projectName: "unlinked", legacyPath, submissionDir };
    }

    const first = sessions.values().next().value!;
    const key = sanitizeProjectKey(first.project);
    const legacyPath = resolve(LEGACY_FEEDBACK_DIR, `${key}.json`);
    const primaryDir = resolve(first.projectPath, "DevLogs");
    const primaryPath = resolve(primaryDir, "agent-feedback.json");
    const dotFolderFallbackPath = resolve(first.projectPath, ".unity-ai", "agent-feedback.json");
    const submissionDir = resolve(primaryDir, "feedback-submissions");

    return {
        projectName: first.project,
        primaryPath,
        dotFolderFallbackPath,
        legacyPath,
        submissionDir,
    };
}

export function loadFeedback(target: FeedbackStorageTarget): FeedbackFile {
    if (target.primaryPath && existsSync(target.primaryPath)) {
        try {
            return JSON.parse(readFileSync(target.primaryPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    if (target.dotFolderFallbackPath && existsSync(target.dotFolderFallbackPath)) {
        try {
            return JSON.parse(readFileSync(target.dotFolderFallbackPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    if (existsSync(target.legacyPath)) {
        try {
            return JSON.parse(readFileSync(target.legacyPath, "utf-8"));
        } catch {
            // fall through
        }
    }

    return { project: target.projectName, entries: [] };
}

export function saveFeedback(target: FeedbackStorageTarget, file: FeedbackFile): void {
    if (target.primaryPath) {
        mkdirSync(dirname(target.primaryPath), { recursive: true });
        writeFileSync(target.primaryPath, JSON.stringify(file, null, 2), "utf-8");
        return;
    }

    mkdirSync(dirname(target.legacyPath), { recursive: true });
    writeFileSync(target.legacyPath, JSON.stringify(file, null, 2), "utf-8");
}

export function writeFeedbackSubmission(
    target: FeedbackStorageTarget,
    fileName: string,
    content: string,
): string {
    const baseDir = target.submissionDir ?? resolve(LEGACY_FEEDBACK_DIR, "submissions");
    const outputPath = resolve(baseDir, fileName);
    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, content, "utf-8");
    return outputPath;
}
