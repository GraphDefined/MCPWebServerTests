
using System.ComponentModel;
using System.Net.NetworkInformation;

using ModelContextProtocol.Server;

namespace MCPWebServerTest.Tools
{

    [McpServerToolType]
    public static class TimeServerTool
    {

        [McpServerTool, Description("Checks if a time server is reachable.")]
        public static async Task<String> CheckTimeServer(String serverAddress)
        {
            try
            {

                var ping   = new Ping();
                var reply  = await ping.SendPingAsync(
                                       serverAddress,
                                       TimeSpan.FromSeconds(2)
                                   );

                return reply.Status == IPStatus.Success
                           ? $"Timeserver {serverAddress} is reachable (RTT: {reply.RoundtripTime} ms)."
                           : $"Timeserver {serverAddress} is not reachable (Status: {reply.Status}).";

            }
            catch (Exception ex)
            {
                return $"Error checking timeserver {serverAddress}: {ex.Message}";
            }
        }

    }

}
