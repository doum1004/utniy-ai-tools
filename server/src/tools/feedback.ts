import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import type { UnityBridge } from "../transport/unity-bridge";
import {
    FeedbackEntry,
    loadFeedback,
    resolveFeedbackStorageTarget,
    saveFeedback,
    writeFeedbackSubmission,
} from "../feedback-storage";

function generateId(): string {
    const ts = Date.now().toString(36);
    const rand = Math.random().toString(36).slice(2, 6);
    return `fb-${ts}-${rand}`;
}

function toSubmissionMarkdown(project: string, entries: FeedbackEntry[]): string {
    const lines: string[] = [];
    lines.push(`# Agent Feedback Submission`);
    lines.push("");
    lines.push(`- Project: ${project}`);
    lines.push(`- Generated: ${new Date().toISOString()}`);
    lines.push(`- Entry Count: ${entries.length}`);
    lines.push("");

    for (const entry of entries) {
        lines.push(`## ${entry.summary}`);
        lines.push("");
        lines.push(`- ID: ${entry.id}`);
        lines.push(`- Source: ${entry.source_type}:${entry.source_name}`);
        lines.push(`- Status: ${entry.status}`);
        lines.push(`- Rating: ${entry.rating ?? "n/a"}`);
        lines.push(`- Tags: ${entry.tags.join(", ") || "none"}`);
        lines.push(`- Created: ${entry.created_at}`);
        if (entry.exported_at) lines.push(`- Exported: ${entry.exported_at}`);
        if (entry.submitted_at) lines.push(`- Submitted (manual): ${entry.submitted_at}`);
        lines.push("");
        lines.push(`### Details`);
        lines.push(entry.details || "_No details provided._");
        lines.push("");
        lines.push(`### Suggested Improvement`);
        lines.push(entry.suggestion || "_No suggestion provided._");
        lines.push("");
    }

    return lines.join("\n");
}

export function registerFeedbackTools(server: McpServer, bridge: UnityBridge): void {
    server.tool(
        "manage_feedback",
        "Local, user-owned feedback journal for tools/skills/workflows. " +
        "Use 'add' to draft feedback, 'list' to review entries, 'update' to refine, and 'export' to create a sharable file. " +
        "This tool does not submit to any remote endpoint.",
        {
            action: z.enum(["add", "list", "update", "export", "export_submission"]).describe(
                "add: create feedback draft | list: retrieve local entries | update: edit an existing item | export: write a local markdown/json package for optional manual sharing (git, issue, chat, etc). " +
                "export_submission is a backward-compatible alias of export.",
            ),
            source_type: z.enum(["tool", "skill", "workflow"]).optional(),
            source_name: z.string().optional().describe("Tool/skill/workflow name, e.g. manage_scene or unity-level-design"),
            summary: z.string().optional().describe("Short one-line feedback summary"),
            details: z.string().optional().describe("Long-form details of what worked or failed"),
            suggestion: z.string().optional().describe("Concrete recommendation for improvement"),
            rating: z.number().int().min(1).max(5).optional().describe("Optional satisfaction score, 1-5"),
            tags: z.array(z.string()).optional().describe("Optional tags"),
            status: z.enum(["draft", "exported", "archived", "submitted"]).optional(),
            entry_id: z.string().optional().describe("Entry id for update"),
            filter_status: z.enum(["draft", "exported", "archived", "submitted"]).optional(),
            filter_source_type: z.enum(["tool", "skill", "workflow"]).optional(),
            limit: z.number().int().positive().optional().describe("Max entries returned (default 50)"),
            entry_ids: z.array(z.string()).optional().describe("Specific entries to export; defaults to all draft entries"),
            format: z.enum(["markdown", "json"]).optional().describe("Export file format (default markdown)"),
            mark_exported: z.boolean().optional().describe("Mark exported entries as exported (default true)"),
            mark_submitted: z.boolean().optional().describe("Legacy alias for mark_exported"),
        },
        async (params) => {
            const target = await resolveFeedbackStorageTarget(bridge);
            const feedback = loadFeedback(target);

            switch (params.action) {
                case "add": {
                    if (!params.source_type || !params.source_name || !params.summary) {
                        return {
                            content: [{
                                type: "text",
                                text: JSON.stringify({
                                    error: "'source_type', 'source_name', and 'summary' are required for 'add'",
                                }),
                            }],
                        };
                    }

                    const now = new Date().toISOString();
                    const entry: FeedbackEntry = {
                        id: generateId(),
                        source_type: params.source_type,
                        source_name: params.source_name,
                        summary: params.summary,
                        details: params.details ?? "",
                        suggestion: params.suggestion ?? "",
                        rating: params.rating,
                        tags: params.tags ?? [],
                        status: params.status ?? "draft",
                        created_at: now,
                        updated_at: now,
                    };

                    feedback.entries.push(entry);
                    saveFeedback(target, feedback);

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

                    const entry = feedback.entries.find((e) => e.id === params.entry_id);
                    if (!entry) {
                        return {
                            content: [{ type: "text", text: JSON.stringify({ error: `Entry '${params.entry_id}' not found` }) }],
                        };
                    }

                    if (params.summary !== undefined) entry.summary = params.summary;
                    if (params.details !== undefined) entry.details = params.details;
                    if (params.suggestion !== undefined) entry.suggestion = params.suggestion;
                    if (params.rating !== undefined) entry.rating = params.rating;
                    if (params.tags !== undefined) entry.tags = params.tags;
                    if (params.status !== undefined) {
                        entry.status = params.status;
                        if (params.status === "submitted") entry.submitted_at = new Date().toISOString();
                    }
                    entry.updated_at = new Date().toISOString();

                    saveFeedback(target, feedback);
                    return {
                        content: [{ type: "text", text: JSON.stringify({ success: true, entry }, null, 2) }],
                    };
                }

                case "list": {
                    let entries = [...feedback.entries];
                    if (params.filter_status) entries = entries.filter((e) => e.status === params.filter_status);
                    if (params.filter_source_type) entries = entries.filter((e) => e.source_type === params.filter_source_type);
                    entries.reverse();
                    entries = entries.slice(0, params.limit ?? 50);

                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify({
                                project: feedback.project,
                                total: feedback.entries.length,
                                returned: entries.length,
                                entries,
                            }, null, 2),
                        }],
                    };
                }

                case "export":
                case "export_submission": {
                    const targetIds = params.entry_ids?.length
                        ? new Set(params.entry_ids)
                        : undefined;

                    const selected = feedback.entries.filter((entry) => {
                        if (targetIds && !targetIds.has(entry.id)) return false;
                        return entry.status === "draft" || (targetIds ? true : false);
                    });

                    if (selected.length === 0) {
                        return {
                            content: [{
                                type: "text",
                                text: JSON.stringify({
                                    error: "No matching feedback entries to export. Add drafts first or provide explicit entry_ids.",
                                }),
                            }],
                        };
                    }

                    const ts = new Date().toISOString().replace(/[:.]/g, "-");
                    const format = params.format ?? "markdown";
                    const fileName = format === "json"
                        ? `feedback-submission-${ts}.json`
                        : `feedback-submission-${ts}.md`;
                    const output = format === "json"
                        ? JSON.stringify({ project: feedback.project, entries: selected }, null, 2)
                        : toSubmissionMarkdown(feedback.project, selected);

                    const outputPath = writeFeedbackSubmission(target, fileName, output);

                    const shouldMarkExported = params.mark_exported ?? params.mark_submitted ?? true;
                    if (shouldMarkExported) {
                        const now = new Date().toISOString();
                        const selectedIds = new Set(selected.map((e) => e.id));
                        feedback.entries = feedback.entries.map((entry) => {
                            if (!selectedIds.has(entry.id)) return entry;
                            return {
                                ...entry,
                                status: "exported",
                                exported_at: now,
                                updated_at: now,
                            };
                        });
                        saveFeedback(target, feedback);
                    }

                    return {
                        content: [{
                            type: "text",
                            text: JSON.stringify({
                                success: true,
                                output_path: outputPath,
                                exported: selected.length,
                                note: "Local export only. Share manually via git, issue tracker, or preferred channel.",
                            }, null, 2),
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
