
using System.Text.Json;
using System.ComponentModel;

using ModelContextProtocol.Server;

namespace MCPWebServerTest.Tools
{

    [McpServerToolType]
    public class PrintEnvTool
    {

        private static readonly JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        [McpServerTool(Name = "printEnv"), Description("Prints all environment variables, helpful for debugging MCP server configuration")]
        public static string PrintEnv() =>
            JsonSerializer.Serialize(Environment.GetEnvironmentVariables(), options);

    }

}
