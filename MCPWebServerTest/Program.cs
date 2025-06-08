
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json;

namespace MCPWebServerTest
{
    public class Program
    {

        public static String HelloWorld(String YourName)
        {

            return $"Hello: '{YourName}'!";

        }


        public static void Main(String[] args)
        {

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            //    builder.Services.AddAuthorization();

            builder.Services.AddMcpServer(options => {
                options.ServerInfo = new Implementation {
                    Name     = "MCP WebServer Test",
                    Version  = "1.0.0"
                };
            }).
            // With new streaming HTTP and older HTTP SSE transport
            WithHttpTransport().

            WithToolsFromAssembly().
            WithTools([
                McpServerTool.Create(
                    typeof(Program).GetMethod(nameof(HelloWorld), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!,
                    options:  new McpServerToolCreateOptions() {

                                  //Services           = services,
                                  //SerializerOptions  = new JsonSerializerOptions() {
                                  //                         // Custom transformation options can be added here
                                  //                     },
                                  Name               = "HelloWorld",
                                  Description        = "Returns a greeting message.",
                                  Title              = "Hello World Tool",
                                  Destructive        = false,
                                  Idempotent         = true,
                                  OpenWorld          = false,
                                  ReadOnly           = false,

                                  SchemaCreateOptions  = new AIJsonSchemaCreateOptions() {
                                      TransformSchemaNode   = (context, jsonNode) => {
                                                                  // Custom transformation logic can be added here
                                                                  return jsonNode;
                                                              },
                                      IncludeParameter      = parameterInfo => {
                                                                  return true;
                                                              },
                                      TransformOptions      = new AIJsonSchemaTransformOptions() {
                                                                   TransformSchemaNode              = (context, jsonNode) => {
                                                                       // Custom transformation logic can be added here
                                                                       return jsonNode;
                                                                   },
                                                                   ConvertBooleanSchemas            = false,
                                                                   DisallowAdditionalProperties     = false,
                                                                   RequireAllProperties             = false,
                                                                   UseNullableKeyword               = false,
                                                                   MoveDefaultKeywordToDescription  = false
                                                              },
                                      IncludeSchemaKeyword  = false
                                  }

                              }
                )
            ]);

            builder.WebHost.ConfigureKestrel(options => {
                options.ListenLocalhost(
                    5248  // HTTP port
                  //  listenOptions => { listenOptions.UseHttps(); } // TLS aktivieren
                );
            });

            var app = builder.Build();
            app.MapMcp();


            // Simple test endpoint to check whether the server is running
            app.MapGet("/test", () => $"Server is running: {DateTime.UtcNow}");

            app.Run();


            // Start within a console...
            // npx @modelcontextprotocol/inspector node build/index.js

            // Starting MCP inspector...
            // ⚙️ Proxy server listening on port 6277
            // 🔍 MCP Inspector is up and running at http://127.0.0.1:6274 🚀

            // Set "Transport Type" to "Streamable HTTP"
            // Set "URL" to "http://localhost:5248"
            // Press [Connect]

        }

    }

}
