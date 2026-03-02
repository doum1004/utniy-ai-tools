using System.Threading.Tasks;

namespace UnityAITools.Editor.Transport
{
    /// <summary>
    /// Interface for Unity-side tool handlers that process MCP commands.
    /// </summary>
    public interface IToolHandler
    {
        /// <summary>
        /// Execute a command and return the result.
        /// </summary>
        /// <param name="commandName">The specific command/action name</param>
        /// <param name="paramsJson">Raw JSON string of command parameters</param>
        /// <returns>Command execution result</returns>
        Task<CommandResult> ExecuteAsync(string commandName, string paramsJson);
    }
}
