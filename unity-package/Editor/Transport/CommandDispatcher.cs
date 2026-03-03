using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityAITools.Editor.Transport
{
    /// <summary>
    /// Routes incoming commands from the MCP server to the appropriate tool handler.
    /// </summary>
    public class CommandDispatcher
    {
        private readonly Dictionary<string, IToolHandler> _handlers = new Dictionary<string, IToolHandler>();

        /// <summary>
        /// Fired after a command is successfully dispatched, with the command name.
        /// </summary>
        public event Action<string> OnCommandExecuted;

        /// <summary>
        /// Register a tool handler for a specific command name.
        /// </summary>
        public void RegisterHandler(string commandName, IToolHandler handler)
        {
            _handlers[commandName] = handler;
        }

        /// <summary>
        /// Dispatch a raw JSON command and return the result.
        /// </summary>
        public async Task<CommandResult> DispatchAsync(string rawJson)
        {
            try
            {
                // Extract command name from JSON
                var commandName = ExtractField(rawJson, "name");
                if (string.IsNullOrEmpty(commandName))
                {
                    return new CommandResult
                    {
                        success = false,
                        error = "Command missing 'name' field"
                    };
                }

                // Extract params as a raw JSON substring
                var paramsJson = ExtractObjectField(rawJson, "params");

                if (_handlers.TryGetValue(commandName, out var handler))
                {
                    var result = await handler.ExecuteAsync(commandName, paramsJson);
                    OnCommandExecuted?.Invoke(commandName);
                    return result;
                }

                return new CommandResult
                {
                    success = false,
                    error = $"Unknown command: {commandName}"
                };
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CommandDispatcher] Error: {ex}");
                return new CommandResult
                {
                    success = false,
                    error = $"Dispatch error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Extract a simple string field value from JSON.
        /// </summary>
        private static string ExtractField(string json, string fieldName)
        {
            var searchKey = $"\"{fieldName}\"";
            var keyIndex = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;

            // Find the opening quote
            var quoteStart = json.IndexOf('"', colonIndex + 1);
            if (quoteStart < 0) return null;

            var quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        }

        /// <summary>
        /// Extract a JSON object field value as a raw JSON string.
        /// </summary>
        private static string ExtractObjectField(string json, string fieldName)
        {
            var searchKey = $"\"{fieldName}\"";
            var keyIndex = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (keyIndex < 0) return "{}";

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return "{}";

            // Find the opening brace
            var braceStart = json.IndexOf('{', colonIndex + 1);
            if (braceStart < 0) return "{}";

            // Find matching closing brace (track nesting)
            var depth = 0;
            for (var i = braceStart; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;

                if (depth == 0)
                {
                    return json.Substring(braceStart, i - braceStart + 1);
                }
            }

            return "{}";
        }
    }
}
