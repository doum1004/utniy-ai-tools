/**
 * Dev Log tools — persistent development journal for tracking plans,
 * decisions, iterations, and progress across LLM sessions.
 *
 * Stored server-side as JSON files (one per Unity project).
 * Does NOT require Unity to be connected.
 */

import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import { resolve } from "path";
import { mkdirSync, readFileSync, writeFileSync, existsSync } from "fs";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface DevLogEntry {
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

interface DevLogFile {
    project: string;
    entries: DevLogEntry[];
}

// ---------------------------------------------------------------------------
// Storage helpers
// ---------------------------------------------------------------------------

const DATA_DIR = resolve(
    process.env.DEVLOG_DIR ?? resolve(import.meta.dir, "../../data/devlogs"),
);

function sanitizeProjectKey(name: string): string {
    return name.replace(/[^a-zA-Z0-9_-]/g, "_").toLowerCase();
}

function logPath(projectKey: string): string {
    return resolve(DATA_DIR, `${projectKey}.json`);
}

function loadLog(projectKey: string, projectName: string): DevLogFile {
    const path = logPath(projectKey);
    if (existsSync(path)) {
        try {
            return JSON.parse(readFileSync(path, "utf-8"));
        } catch {
            return { project: projectName, entries: [] };
        }
    }
    return { project: projectName, entries: [] };
}

function saveLog(projectKey: string, log: DevLogFile): void {
    mkdirSync(DATA_DIR, { recursive: true });
    writeFileSync(logPath(projectKey), JSON.stringify(log, null, 2), "utf-8");
}

function generateId(): string {
    const ts = Date.now().toString(36);
    const rand = Math.random().toString(36).slice(2, 6);
    return `${ts}-${rand}`;
}

async function resolveProjectKey(bridge: UnityBridge): Promise<{ key: string; name: string }> {
    const sessions = await bridge.getSessions();
    if (sessions.size === 0) {
        return { key: "_unlinked", name: "unlinked" };
    }
    const first = sessions.values().next().value!;
    return { key: sanitizeProjectKey(first.project), name: first.project };
}

// ---------------------------------------------------------------------------
// Tool registration
// ---------------------------------------------------------------------------

export function registerDevLogTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_devlog",
        "Persistent development journal — log plans, milestones, decisions, issues, and iterations so follow-up sessions can pick up context. " +
        "Use 'add' to record new entries, 'list' to retrieve history (with optional filters), 'update' to change status/body of existing entries, 'get' to read a single entry, 'search' to find entries by keyword.",
        {
            action: z.enum(["add", "list", "update", "get", "search"]).describe(
                "add: create entry | list: retrieve entries (with filters) | update: modify existing entry | get: single entry by id | search: keyword search across titles and bodies",
            ),

            // --- add fields ---
            type: z.enum(["plan", "milestone", "decision", "issue", "iteration", "note"]).optional().describe(
                "Entry type (required for 'add'). plan = high-level goal/phase, milestone = completed achievement, decision = architectural/design choice, issue = bug/problem found, iteration = what changed and why in a cycle, note = general observation",
            ),
            title: z.string().optional().describe("Short summary (required for 'add')"),
            body: z.string().optional().describe("Detailed description, context, or reasoning"),
            status: z.enum(["active", "completed", "dropped", "blocked"]).optional().describe(
                "Entry status (default: 'active' for add, required for 'update' if changing status)",
            ),
            tags: z.array(z.string()).optional().describe("Tags for categorization, e.g. ['player', 'movement', 'phase-2']"),
            parent_id: z.string().optional().describe("Link to a parent entry id (e.g. iteration under a plan)"),

            // --- update / get fields ---
            entry_id: z.string().optional().describe("Entry ID (required for 'update' and 'get')"),

            // --- list / search fields ---
            filter_type: z.enum(["plan", "milestone", "decision", "issue", "iteration", "note"]).optional().describe("Filter by entry type"),
            filter_status: z.enum(["active", "completed", "dropped", "blocked"]).optional().describe("Filter by status"),
            filter_tags: z.array(z.string()).optional().describe("Filter entries that have ALL of these tags"),
            query: z.string().optional().describe("Search keyword (required for 'search')"),
            limit: z.number().optional().describe("Max entries to return (default: 50, most recent first)"),
        },
        async (params) => {
            const { key, name } = await resolveProjectKey(bridge);
            const log = loadLog(key, name);

            switch (params.action) {
                case "add": {
                    if (!params.type || !params.title) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: "'type' and 'title' are required for 'add'" }) }],
                        };
                    }
                    const now = new Date().toISOString();
                    const entry: DevLogEntry = {
                        id: generateId(),
                        type: params.type,
                        title: params.title,
                        body: params.body ?? "",
                        status: params.status ?? "active",
                        tags: params.tags ?? [],
                        parent_id: params.parent_id,
                        created_at: now,
                        updated_at: now,
                    };
                    log.entries.push(entry);
                    saveLog(key, log);
                    return {
                        content: [{ type: "text", text: JSON.stringify({ success: true, entry }, null, 2) }],
                    };
                }

                case "update": {
                    if (!params.entry_id) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: "'entry_id' is required for 'update'" }) }],
                        };
                    }
                    const entry = log.entries.find((e) => e.id === params.entry_id);
                    if (!entry) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: `Entry '${params.entry_id}' not found` }) }],
                        };
                    }
                    if (params.title) entry.title = params.title;
                    if (params.body !== undefined) entry.body = params.body;
                    if (params.status) entry.status = params.status;
                    if (params.tags) entry.tags = params.tags;
                    if (params.parent_id) entry.parent_id = params.parent_id;
                    entry.updated_at = new Date().toISOString();
                    saveLog(key, log);
                    return {
                        content: [{ type: "text", text: JSON.stringify({ success: true, entry }, null, 2) }],
                    };
                }

                case "get": {
                    if (!params.entry_id) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: "'entry_id' is required for 'get'" }) }],
                        };
                    }
                    const entry = log.entries.find((e) => e.id === params.entry_id);
                    if (!entry) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: `Entry '${params.entry_id}' not found` }) }],
                        };
                    }
                    const children = log.entries.filter((e) => e.parent_id === params.entry_id);
                    return {
                        content: [{ type: "text", text: JSON.stringify({ entry, children }, null, 2) }],
                    };
                }

                case "list": {
                    let results = [...log.entries];
                    if (params.filter_type) results = results.filter((e) => e.type === params.filter_type);
                    if (params.filter_status) results = results.filter((e) => e.status === params.filter_status);
                    if (params.filter_tags?.length) {
                        results = results.filter((e) =>
                            params.filter_tags!.every((t) => e.tags.includes(t)),
                        );
                    }
                    results.reverse();
                    const limit = params.limit ?? 50;
                    results = results.slice(0, limit);
                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify({ total: log.entries.length, returned: results.length, entries: results }, null, 2),
                        }],
                    };
                }

                case "search": {
                    if (!params.query) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: "'query' is required for 'search'" }) }],
                        };
                    }
                    const q = params.query.toLowerCase();
                    let results = log.entries.filter(
                        (e) => e.title.toLowerCase().includes(q) || e.body.toLowerCase().includes(q),
                    );
                    if (params.filter_type) results = results.filter((e) => e.type === params.filter_type);
                    if (params.filter_status) results = results.filter((e) => e.status === params.filter_status);
                    results.reverse();
                    const limit = params.limit ?? 50;
                    results = results.slice(0, limit);
                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify({ query: params.query, returned: results.length, entries: results }, null, 2),
                        }],
                    };
                }

                default:
                    return {
                        content: [{ type: "text", text: JSON.stringify({ error: `Unknown action: ${params.action}` }) }],
                    };
            }
        },
    );
}
