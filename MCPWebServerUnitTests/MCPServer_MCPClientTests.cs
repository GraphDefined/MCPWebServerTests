/*
 * Copyright (c) 2010-2025 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Vanaheimr MCP <https://www.github.com/Vanaheimr/MCP>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Reflection;

using Microsoft.Extensions.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using MCPWebServerTest.Tools;

#endregion

namespace MCPServerTests
{

    [TestFixture]
    public class MCPServer_MCPClientTests
    {

        #region Data

        private IMcpClient?  client;
        private IWebHost?    webServer;

        #endregion


        private List<String> GetConfiguredEndpoints(IWebHost webHost)
        {

            var endpointsInfo = new List<string>();

            // Get Kestrel listening URLs
            var serverAddressesFeature = webHost.ServerFeatures.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
            if (serverAddressesFeature != null)
            {
                foreach (var address in serverAddressesFeature.Addresses)
                {
                    endpointsInfo.Add($"Listening URL: {address}");
                }
            }

            // Build the application to access endpoint routes
            using var scope = webHost.Services.CreateScope();
            var endpointDataSources = scope.ServiceProvider.GetRequiredService<IEnumerable<EndpointDataSource>>();
            foreach (var dataSource in endpointDataSources)
            {
                foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
                {
                    var routePattern = endpoint.RoutePattern.RawText;
                    var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [ "Unknown" ];
                    endpointsInfo.Add($"Endpoint: {routePattern} [{String.Join(", ", methods)}]");
                }
            }

            return endpointsInfo;

        }


        public static String HelloWorld(String YourName)
        {
            return $"Hello: '{YourName}'!";
        }


        #region Setup()

        [SetUp]
        public async Task Setup()
        {

            webServer = new WebHostBuilder().

                            UseKestrel(options => {
                                options.ListenLocalhost(5248);
                            }).

                            ConfigureServices(services => {

                                services.AddRouting();

                                services.AddMcpServer(options => {
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
                                        GetType().GetMethod(nameof(HelloWorld), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!,
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
                                ]).
                                WithTools<PrintEnvTool>();


                        //        a .AddTool("GetBatteryLevel", "Returns the current battery level of the charging station.",
                        //new[] { new ParameterInfo("unit", "string", isRequired: false, defaultValue: "percent") },
                        //(args) =>
                        //{
                        //    string unit = args.GetValue<string>("unit") ?? "percent";
                        //    int level = 75; // Simulated battery level
                        //    return Task.FromResult($"{level} {unit}");
                        //});

                            }).

                            Configure(app => {

                                          app.UseRouting();

                                          app.UseEndpoints(endpoints => {
                                              endpoints.MapMcp();
                                          });

                                      }).

                            Build();

            await webServer.StartAsync();

            var endpointsInfo = GetConfiguredEndpoints(webServer);

            //// Initialize HttpClient to communicate with the MCP server
            //httpClient = new HttpClient {
            //    BaseAddress = new Uri("http://localhost:5248/")
            //};
            //httpClient.DefaultRequestHeaders.Accept.Clear();
            //httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            //httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            await Connect();

        }

        #endregion

        #region TearDown()

        [TearDown]
        public async Task TearDown()
        {

            await client.DisposeAsync();

            if (webServer is not null)
            {
                await webServer.StopAsync(TimeSpan.FromSeconds(1));
                webServer.Dispose();
            }

        }

        #endregion


        #region Connect()

        public async Task Connect()
        {

            var clientTransport = new SseClientTransport(
                                      new SseClientTransportOptions() {
                                          Endpoint           = new Uri("http://localhost:5248/"),
                                          TransportMode      = HttpTransportMode.StreamableHttp,
                                          Name               = "Everything",
                                          ConnectionTimeout  = TimeSpan.FromSeconds(10),
                                      }
                                  );

            client = await McpClientFactory.CreateAsync(clientTransport);

        }

        #endregion

        #region Test01_CheckServer()

        [Test]
        public void Test01_CheckServer()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var serverCapabilities  = client!.ServerCapabilities;
            Assert.That(serverCapabilities.Tools?.ListChanged,  Is.True);


            var serverInfo          = client!.ServerInfo;
            Assert.That(serverInfo.Name,                        Is.EqualTo("MCP WebServer Test"));
            Assert.That(serverInfo.Version,                     Is.EqualTo("1.0.0"));

            var serverInstructions  = client!.ServerInstructions;
            Assert.That(serverInstructions,                     Is.Null);

        }

        #endregion

        #region Test02_GetAllMCPTools()

        [Test]
        public async Task Test02_GetAllMCPTools()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var tools = await client!.ListToolsAsync();

            Assert.That(tools.Count,  Is.EqualTo(5));

        }

        #endregion

        #region Test03_HelloWorld()

        [Test]
        public async Task Test03_HelloWorld()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var tools       = await client!.ListToolsAsync();

            var helloWorld  = tools.FirstOrDefault(tool => tool.Name == "HelloWorld");
            Assert.That(helloWorld?.Description,                                Is.EqualTo("Returns a greeting message."));

            Assert.That(helloWorld?.ProtocolTool.Annotations?.Title,            Is.EqualTo("Hello World Tool"));
            Assert.That(helloWorld?.ProtocolTool.Annotations?.DestructiveHint,  Is.False);
            Assert.That(helloWorld?.ProtocolTool.Annotations?.IdempotentHint,   Is.True);
            Assert.That(helloWorld?.ProtocolTool.Annotations?.OpenWorldHint,    Is.False);
            Assert.That(helloWorld?.ProtocolTool.Annotations?.ReadOnlyHint,     Is.False);

            Assert.That(helloWorld?.ProtocolTool.Description,                   Is.EqualTo("Returns a greeting message."));

            var inputSchema = helloWorld?.ProtocolTool.InputSchema.ToString();
            // {
            //     "title":        "HelloWorld",
            //     "description":  "Returns a greeting message.",
            //     "type":         "object",
            //     "properties": {
            //         "YourName": {
            //             "type":  "string"
            //         }
            //     },
            //     "required": [
            //         "YourName"
            //     ]
            // }


            var response = await client!.CallToolAsync(
                                     "HelloWorld",
                                     new Dictionary<String, Object?>() { ["YourName"] = "ahzf" },
                                     cancellationToken: CancellationToken.None
                                 );

            Assert.That(response,                   Is.Not.Null);
            Assert.That(response.IsError,           Is.False);
            Assert.That(response.Content.Count,     Is.EqualTo(1));

            var firstContent = response.Content.First();
            Assert.That(firstContent.Type,          Is.EqualTo("text"));
            Assert.That(firstContent.Text,          Is.EqualTo("Hello: 'ahzf'!"));
            Assert.That(firstContent.Annotations,   Is.Null);
            Assert.That(firstContent.Data,          Is.Null);
            Assert.That(firstContent.MimeType,      Is.Null);
            Assert.That(firstContent.Resource,      Is.Null);

        }

        #endregion

        #region Test04_SystemStateTool_ReturnsValidSystemState()

        [Test]
        public async Task Test04_SystemStateTool_ReturnsValidSystemState()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var tools           = await client!.ListToolsAsync();

            var GetSystemState  = tools.FirstOrDefault(tool => tool.Name == "GetSystemState");
            Assert.That(GetSystemState?.Description,                                Is.EqualTo("Returns the current system state of the charging station."));

            Assert.That(GetSystemState?.ProtocolTool.Annotations?.Title,            Is.Null);
            Assert.That(GetSystemState?.ProtocolTool.Annotations?.DestructiveHint,  Is.Null);
            Assert.That(GetSystemState?.ProtocolTool.Annotations?.IdempotentHint,   Is.Null);
            Assert.That(GetSystemState?.ProtocolTool.Annotations?.OpenWorldHint,    Is.Null);
            Assert.That(GetSystemState?.ProtocolTool.Annotations?.ReadOnlyHint,     Is.Null);

            Assert.That(GetSystemState?.ProtocolTool.Description,                   Is.EqualTo("Returns the current system state of the charging station."));

            var inputSchema = GetSystemState?.ProtocolTool.InputSchema.ToString();
            // {
            //     "title":       "GetSystemState",
            //     "description": "Returns the current system state of the charging station.",
            //     "type":        "object",
            //     "properties":  {}
            // }


            var response = await client!.CallToolAsync(
                                     "GetSystemState",
                                     cancellationToken: CancellationToken.None
                                 );

            Assert.That(response,                   Is.Not.Null);
            Assert.That(response.IsError,           Is.False);
            Assert.That(response.Content.Count,     Is.EqualTo(1));

            var firstContent = response.Content.First();
            Assert.That(firstContent.Type,          Is.EqualTo("text"));
            Assert.That(firstContent.Text,          Is.EqualTo("{\"ChargePower\":\"22 kW\",\"Temperature\":\"45\\u00B0C\",\"NetworkStatus\":\"Connected\",\"ErrorCode\":\"None\"}"));
            Assert.That(firstContent.Annotations,   Is.Null);
            Assert.That(firstContent.Data,          Is.Null);
            Assert.That(firstContent.MimeType,      Is.Null);
            Assert.That(firstContent.Resource,      Is.Null);

            var json = JObject.Parse(firstContent.Text ?? "{}");
            Assert.That(json["ChargePower"]?.  Value<String>(),  Is.EqualTo("22 kW"),       "ChargePower should be 22 kW.");
            Assert.That(json["Temperature"]?.  Value<String>(),  Is.EqualTo("45°C"),        "Temperature should be 45°C.");
            Assert.That(json["NetworkStatus"]?.Value<String>(),  Is.EqualTo("Connected"),   "NetworkStatus should be Connected.");
            Assert.That(json["ErrorCode"]?.    Value<String>(),  Is.EqualTo("None"),        "ErrorCode should be None.");

        }

        #endregion

        #region Test05_TimeServerTool_ReturnsValidPingResult()

        [Test]
        public async Task Test05_TimeServerTool_ReturnsValidPingResult()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var tools            = await client!.ListToolsAsync();

            var CheckTimeServer  = tools.FirstOrDefault(tool => tool.Name == "CheckTimeServer");
            Assert.That(CheckTimeServer?.Description,                                Is.EqualTo("Checks if a time server is reachable."));

            Assert.That(CheckTimeServer?.ProtocolTool.Annotations?.Title,            Is.Null);
            Assert.That(CheckTimeServer?.ProtocolTool.Annotations?.DestructiveHint,  Is.Null);
            Assert.That(CheckTimeServer?.ProtocolTool.Annotations?.IdempotentHint,   Is.Null);
            Assert.That(CheckTimeServer?.ProtocolTool.Annotations?.OpenWorldHint,    Is.Null);
            Assert.That(CheckTimeServer?.ProtocolTool.Annotations?.ReadOnlyHint,     Is.Null);

            Assert.That(CheckTimeServer?.ProtocolTool.Description,                   Is.EqualTo("Checks if a time server is reachable."));

            var inputSchema = CheckTimeServer?.ProtocolTool.InputSchema.ToString();
            // {
            //     "title":       "CheckTimeServer",
            //     "description": "Checks if a time server is reachable.",
            //     "type":        "object",
            //     "properties": {
            //         "serverAddress": {
            //             "type":  "string"
            //         }
            //     },
            //     "required": [
            //         "serverAddress"
            //     ]
            // }


            var response = await client!.CallToolAsync(
                                     "CheckTimeServer",
                                     new Dictionary<String, Object?>() { ["serverAddress"] = "141.24.12.2" },
                                     cancellationToken: CancellationToken.None
                                 );

            Assert.That(response,                                                 Is.Not.Null);
            Assert.That(response.IsError,                                         Is.False);
            Assert.That(response.Content.Count,                                   Is.EqualTo(1));

            var firstContent = response.Content.First();
            Assert.That(firstContent.Type,                                        Is.EqualTo("text"));
            Assert.That(firstContent.Text?.StartsWith("Timeserver 141.24.12.2"),  Is.True);
            Assert.That(firstContent.Annotations,                                 Is.Null);
            Assert.That(firstContent.Data,                                        Is.Null);
            Assert.That(firstContent.MimeType,                                    Is.Null);
            Assert.That(firstContent.Resource,                                    Is.Null);

        }

        #endregion

        #region Test06_LongRunningOperation()

        private sealed class SynchronousProgress(Action<ModelContextProtocol.ProgressNotificationValue> callback)
            : IProgress<ModelContextProtocol.ProgressNotificationValue>
        {
            public void Report(ModelContextProtocol.ProgressNotificationValue value) => callback(value);

        }

        [Test]
        public async Task Test06_LongRunningOperation()
        {

            if (client is null)
                Assert.Fail("MCP client is not initialized.");

            var tools                 = await client!.ListToolsAsync();

            var longRunningOperation  = tools.FirstOrDefault(tool => tool.Name == "longRunningOperation");
            Assert.That(longRunningOperation?.Description,                                Is.EqualTo("Demonstrates a long running operation with progress updates"));

            Assert.That(longRunningOperation?.ProtocolTool.Annotations?.Title,            Is.Null);
            Assert.That(longRunningOperation?.ProtocolTool.Annotations?.DestructiveHint,  Is.Null);
            Assert.That(longRunningOperation?.ProtocolTool.Annotations?.IdempotentHint,   Is.Null);
            Assert.That(longRunningOperation?.ProtocolTool.Annotations?.OpenWorldHint,    Is.Null);
            Assert.That(longRunningOperation?.ProtocolTool.Annotations?.ReadOnlyHint,     Is.Null);

            Assert.That(longRunningOperation?.ProtocolTool.Description,                   Is.EqualTo("Demonstrates a long running operation with progress updates"));

            var inputSchema = JObject.Parse(longRunningOperation?.ProtocolTool.InputSchema.ToString() ?? "");
            // {
            //     "title":        "longRunningOperation",
            //     "description":  "Demonstrates a long running operation with progress updates",
            //     "type":         "object",
            //     "properties": {
            //         "Duration": {
            //             "type":    "integer",
            //             "default":  10
            //         },
            //         "Steps": {
            //             "type":    "integer",
            //             "default":  5
            //         }
            //     }
            // }

            var totalNotifications                             = inputSchema["properties"]?["Steps"]?["default"]?.Value<Int32>() ?? 0;
            var remainingProgress                              = totalNotifications;
            TaskCompletionSource<Boolean> allProgressReceived  = new (TaskCreationOptions.RunContinuationsAsynchronously);

            IProgress<ModelContextProtocol.ProgressNotificationValue> progress = new SynchronousProgress(value => {

                //Assert.True(value.Progress >= 0 && value.Progress <= 100);
                //Assert.Equal("making progress", value.Message);

                if (Interlocked.Decrement(ref remainingProgress) == 0) {
                    allProgressReceived.SetResult(true);
                }

            });


            var response = await longRunningOperation!.
                                     WithProgress(progress).
                                     InvokeAsync(cancellationToken: CancellationToken.None);

            var ss = response?.ToString();

            Assert.That(response,                                                 Is.Not.Null);
            //Assert.That(response.IsError,                                         Is.False);
            //Assert.That(response.Content.Count,                                   Is.EqualTo(1));

            //var firstContent = response.Content.First();
            //Assert.That(firstContent.Type,                                        Is.EqualTo("text"));
            //Assert.That(firstContent.Text?.StartsWith("Timeserver 141.24.12.2"),  Is.True);
            //Assert.That(firstContent.Annotations,                                 Is.Null);
            //Assert.That(firstContent.Data,                                        Is.Null);
            //Assert.That(firstContent.MimeType,                                    Is.Null);
            //Assert.That(firstContent.Resource,                                    Is.Null);

        }

        #endregion



        //var result = await tool.WithProgress(progress).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        //Assert.Contains("42", result?.ToString());


        // POST / HTTP/1.1
        // host: localhost:5248
        // connection: keep-alive
        // mcp-session-id: 3BeH-hKjqD2mQXQANkwbpA
        // accept: application/json, text/event-stream
        // content-type: application/json
        // accept-language: *
        // sec-fetch-mode: cors
        // user-agent: node
        // accept-encoding: gzip, deflate
        // content-length: 153
        // 
        // {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"_meta":{"progressToken":2},"name":"longRunningOperation","arguments":{"Duration":10,"Steps":5}}}

        // HTTP/1.1 200 OK
        // Content-Type: text/event-stream
        // Date: Sun, 08 Jun 2025 23:39:30 GMT
        // Server: Kestrel
        // Cache-Control: no-cache,no-store
        // Content-Encoding: identity
        // Transfer-Encoding: chunked
        // mcp-session-id: 3BeH-hKjqD2mQXQANkwbpA
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":1,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":2,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":3,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":4,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":5,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // 7e
        // event: message
        // data: {"method":"notifications/progress","params":{"progress":6,"total":5,"progressToken":2},"jsonrpc":"2.0"}
        // 
        // 
        // b1
        // event: message
        // data: {"result":{"content":[{"type":"text","text":"Long running operation completed. Duration: 10 seconds. Steps: 5."}],"isError":false},"id":2,"jsonrpc":"2.0"}
        // 
        // 
        // 0




        // GET / HTTP/1.1




        // DELETE / HTTP/1.1
        // host: localhost:5248
        // connection: keep-alive
        // mcp-session-id: BoFPzisDd1b9TmZUNp6eMw
        // Accept: text/event-stream, application/json
        // accept-language: *
        // sec-fetch-mode: cors
        // user-agent: node
        // accept-encoding: gzip, deflate
        // 
        // 
        // HTTP/1.1 200 OK
        // Content-Length: 0
        // Date: Sun, 08 Jun 2025 18:54:11 GMT
        // Server: Kestrel







    }

}
