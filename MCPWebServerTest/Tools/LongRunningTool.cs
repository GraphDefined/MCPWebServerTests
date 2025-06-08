
using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace MCPWebServerTest.Tools
{

    [McpServerToolType]
    public class LongRunningTool
    {

        [McpServerTool(Name = "longRunningOperation"), Description("Demonstrates a long running operation with progress updates")]
        public static async Task<String> LongRunningOperation(IMcpServer                             Server,
                                                              RequestContext<CallToolRequestParams>  Context,
                                                              Int32                                  Duration   = 10,
                                                              Int32                                  Steps      = 5)
        {

            var progressToken  = Context.Params?.Meta?.ProgressToken;
            var stepDuration   = Duration / Steps;

            for (var i = 1; i <= Steps + 1; i++)
            {

                await Task.Delay(stepDuration * 1000);

                if (progressToken is not null)
                {

                    await Server.SendNotificationAsync(
                              "notifications/progress",
                              new {
                                  Progress  = i,
                                  Total     = Steps,
                                  progressToken
                              }
                          );

                }

            }

            return $"Long running operation completed. Duration: {Duration} seconds. Steps: {Steps}.";

        }

    }

}
