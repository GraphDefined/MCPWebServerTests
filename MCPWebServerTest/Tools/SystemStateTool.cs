
using System.ComponentModel;

using ModelContextProtocol.Server;

namespace MCPWebServerTest.Tools
{

    [McpServerToolType]
    public static class SystemStateTool
    {

        [McpServerTool, Description("Returns the current system state of the charging station.")]
        public static String GetSystemState()
        {

            var state = new {
                            ChargePower    = "22 kW",
                            Temperature    = "45°C",
                            NetworkStatus  = "Connected",
                            ErrorCode      = "None"
                        };

            return System.Text.Json.JsonSerializer.Serialize(state);

        }

    }

}
