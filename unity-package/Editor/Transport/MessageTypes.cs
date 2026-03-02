using System;
using System.Collections.Generic;

namespace UnityAITools.Editor.Transport
{
    /// <summary>
    /// Message types for WebSocket communication between MCP server and Unity plugin.
    /// Must mirror the TypeScript types in server/src/transport/types.ts.
    /// </summary>

    [Serializable]
    public class BaseMessage
    {
        public string type;
    }

    [Serializable]
    public class WelcomeMessage : BaseMessage
    {
        public int serverTimeout;
        public int keepAliveInterval;
    }

    [Serializable]
    public class RegisterMessage : BaseMessage
    {
        public string project_name;
        public string project_hash;
        public string unity_version;
        public string project_path;

        public RegisterMessage()
        {
            type = "register";
        }
    }

    [Serializable]
    public class RegisteredMessage : BaseMessage
    {
        public string session_id;
    }

    [Serializable]
    public class ExecuteCommandMessage : BaseMessage
    {
        public string id;
        public string name;
        public Dictionary<string, object> @params;
        public float timeout;
    }

    [Serializable]
    public class CommandResult
    {
        public bool success;
        public string error;
        public object data;
        public string hint;
    }

    [Serializable]
    public class CommandResultMessage : BaseMessage
    {
        public string id;
        public CommandResult result;

        public CommandResultMessage()
        {
            type = "command_result";
        }

        public CommandResultMessage(string commandId, CommandResult commandResult)
        {
            type = "command_result";
            id = commandId;
            result = commandResult;
        }
    }

    [Serializable]
    public class PingMessage : BaseMessage
    {
        // Empty — server sends ping, we respond with pong
    }

    [Serializable]
    public class PongMessage : BaseMessage
    {
        public string session_id;

        public PongMessage()
        {
            type = "pong";
        }

        public PongMessage(string sessionId)
        {
            type = "pong";
            session_id = sessionId;
        }
    }
}
