import { describe, test, expect } from "bun:test";
import { createMcpResponse } from "../../src/transport/types";

describe("createMcpResponse", () => {
    test("creates a success response with data", () => {
        const response = createMcpResponse(true, { foo: "bar" });
        expect(response.success).toBe(true);
        expect(response.data).toEqual({ foo: "bar" });
        expect(response.error).toBeUndefined();
        expect(response.hint).toBeUndefined();
    });

    test("creates a failure response with error", () => {
        const response = createMcpResponse(false, undefined, "Something went wrong");
        expect(response.success).toBe(false);
        expect(response.error).toBe("Something went wrong");
        expect(response.data).toBeUndefined();
    });

    test("creates a failure response with error and hint", () => {
        const response = createMcpResponse(false, undefined, "Not connected", "retry");
        expect(response.success).toBe(false);
        expect(response.error).toBe("Not connected");
        expect(response.hint).toBe("retry");
    });

    test("includes data even on failure if provided", () => {
        const response = createMcpResponse(false, { partial: true }, "Partial failure");
        expect(response.success).toBe(false);
        expect(response.data).toEqual({ partial: true });
        expect(response.error).toBe("Partial failure");
    });

    test("success response without data omits data field", () => {
        const response = createMcpResponse(true);
        expect(response.success).toBe(true);
        expect("data" in response).toBe(false);
    });

    test("omits error field when error is empty string", () => {
        const response = createMcpResponse(false, undefined, "");
        expect("error" in response).toBe(false);
    });

    test("omits hint field when hint is undefined", () => {
        const response = createMcpResponse(false, undefined, "error");
        expect("hint" in response).toBe(false);
    });
});
