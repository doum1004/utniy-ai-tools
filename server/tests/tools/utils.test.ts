import { describe, test, expect } from "bun:test";
import { formatToolResult, parseVector, parseBool } from "../../src/tools/utils";

describe("formatToolResult", () => {
    test("formats successful response with data", () => {
        const result = formatToolResult({ success: true, data: { count: 5 } });
        expect(result.content).toHaveLength(1);
        expect(result.content[0].type).toBe("text");
        const parsed = JSON.parse((result.content[0] as { text: string }).text);
        expect(parsed.count).toBe(5);
    });

    test("formats successful response without data", () => {
        const result = formatToolResult({ success: true });
        const parsed = JSON.parse((result.content[0] as { text: string }).text);
        expect(parsed.success).toBe(true);
    });

    test("formats error response", () => {
        const result = formatToolResult({ success: false, error: "Not found" });
        const parsed = JSON.parse((result.content[0] as { text: string }).text);
        expect(parsed.error).toBe("Not found");
    });

    test("formats error response with hint", () => {
        const result = formatToolResult({ success: false, error: "Disconnected", hint: "retry" });
        const parsed = JSON.parse((result.content[0] as { text: string }).text);
        expect(parsed.error).toBe("Disconnected");
        expect(parsed.hint).toBe("retry");
    });

    test("formats error response with data", () => {
        const result = formatToolResult({ success: false, error: "Partial", data: { partial: true } });
        const parsed = JSON.parse((result.content[0] as { text: string }).text);
        expect(parsed.error).toBe("Partial");
        expect(parsed.data).toEqual({ partial: true });
    });

    test("formats image response", () => {
        const result = formatToolResult({
            success: true,
            data: { image_base64: "abc123", width: 640, height: 480, source: "camera" },
        });
        expect(result.content.length).toBeGreaterThanOrEqual(1);
        expect(result.content[0].type).toBe("image");
        const imgContent = result.content[0] as { type: "image"; data: string; mimeType: string };
        expect(imgContent.data).toBe("abc123");
        expect(imgContent.mimeType).toBe("image/png");
        // Should have metadata text
        expect(result.content[1]?.type).toBe("text");
    });

    test("formats image response without metadata", () => {
        const result = formatToolResult({
            success: true,
            data: { image_base64: "abc123" },
        });
        // Only image, no metadata text (no width/height/source/camera)
        expect(result.content).toHaveLength(1);
        expect(result.content[0].type).toBe("image");
    });
});

describe("parseVector", () => {
    test("parses array of numbers", () => {
        expect(parseVector([1, 2, 3])).toEqual([1, 2, 3]);
    });

    test("parses comma-separated string", () => {
        expect(parseVector("1, 2, 3")).toEqual([1, 2, 3]);
    });

    test("parses bracketed string", () => {
        expect(parseVector("[1, 2, 3]")).toEqual([1, 2, 3]);
    });

    test("returns undefined for null", () => {
        expect(parseVector(null)).toBeUndefined();
    });

    test("returns undefined for undefined", () => {
        expect(parseVector(undefined)).toBeUndefined();
    });

    test("returns undefined for empty string", () => {
        expect(parseVector("")).toBeUndefined();
    });

    test("returns undefined for empty brackets", () => {
        expect(parseVector("[]")).toBeUndefined();
    });
});

describe("parseBool", () => {
    test("returns boolean as-is", () => {
        expect(parseBool(true)).toBe(true);
        expect(parseBool(false)).toBe(false);
    });

    test("parses string 'true'", () => {
        expect(parseBool("true")).toBe(true);
        expect(parseBool("True")).toBe(true);
        expect(parseBool("TRUE")).toBe(true);
    });

    test("parses string 'false'", () => {
        expect(parseBool("false")).toBe(false);
    });

    test("parses string '1' and '0'", () => {
        expect(parseBool("1")).toBe(true);
        expect(parseBool("0")).toBe(false);
    });

    test("parses 'yes' and 'no'", () => {
        expect(parseBool("yes")).toBe(true);
        expect(parseBool("no")).toBe(false);
    });

    test("parses numbers", () => {
        expect(parseBool(1)).toBe(true);
        expect(parseBool(0)).toBe(false);
        expect(parseBool(42)).toBe(true);
    });

    test("returns undefined for null/undefined", () => {
        expect(parseBool(null)).toBeUndefined();
        expect(parseBool(undefined)).toBeUndefined();
    });

    test("returns undefined for unrecognized string", () => {
        expect(parseBool("maybe")).toBeUndefined();
    });
});
