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
using System.Text.Json;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using MCPWebServerTest.Tools;

using MCP = org.GraphDefined.Vanaheimr.Hermod.MCP;

#endregion

namespace MCPServerTests
{

    [TestFixture]
    public class MCPServer_HTTPTests
    {

        #region Data

        private HttpClient? httpClient;
        private IWebHost?   webServer;
        private String?     sessionId;
        private UInt32      requestId = 0;

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

            // Initialize HttpClient to communicate with the MCP server
            httpClient = new HttpClient {
                BaseAddress = new Uri("http://localhost:5248/")
            };
            httpClient.DefaultRequestHeaders.Accept.Clear();
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

            await Connect();

        }

        #endregion

        #region TearDown()

        [TearDown]
        public async Task TearDown()
        {

            // Clean up HttpClient and stop the server
            httpClient?.Dispose();

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

            #region Step 1: Initialize connection

            // POST / HTTP/1.1
            // host:               localhost:5248
            // connection:         keep-alive
            // accept:             application/json, text/event-stream
            // content-type:       application/json
            // accept-language:    *
            // sec-fetch-mode:     cors
            // user-agent:         node
            // accept-encoding:    gzip, deflate
            // content-length:     205
            // 
            // {
            //     "jsonrpc":   "2.0",
            //     "id":         0,
            //     "method":    "initialize",
            //     "params": {
            //         "protocolVersion": "2025-03-26",
            //         "capabilities": {
            //             "sampling": {},
            //             "roots": {
            //                 "listChanged": true
            //             }
            //         },
            //         "clientInfo": {
            //             "name":     "mcp-inspector",
            //             "version":  "0.14.0"
            //         }
            //     }
            // }

            // HTTP/1.1 200 OK
            // Content-Type:       text/event-stream
            // Date:               Sun, 08 Jun 2025 16:37:30 GMT
            // Server:             Kestrel
            // Cache-Control:      no-cache,no-store
            // Content-Encoding:   identity
            // Transfer-Encoding:  chunked
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // 
            // d2
            // event: message
            // data: {
            //     "result": {
            //         "protocolVersion": "2025-03-26",
            //         "capabilities": {
            //             "logging": {},
            //             "tools": {
            //                 "listChanged": true
            //             }
            //         },
            //         "serverInfo": {
            //             "name":    "MCP WebServer Test",
            //             "version": "1.0.0"
            //         }
            //     },
            //     "id":       0,
            //     "jsonrpc": "2.0"
            // }
            // 0

            var request1   = new MCP.InitializeRequest(
                                 MCP.Request_Id.Parse(requestId++),
                                 new MCP.ClientInfo(
                                     "mcp-inspector",
                                     "0.14.0"
                                 ),
                                 new MCP.Client.ClientCapabilities(
                                     null,//new MCP.ElicitationCapability(),
                                     new MCP.RootsCapability(
                                         ListChanged: true
                                     ),
                                     new MCP.SamplingCapability()
                                 )
                             );

            var response1  = await httpClient!.PostAsync(
                                       "/",
                                       new StringContent(
                                           request1.ToJSON().ToString(),
                                           System.Text.Encoding.UTF8,
                                           "application/json"
                                       )
                                   );

            Assert.That(response1.StatusCode,  Is.EqualTo(System.Net.HttpStatusCode.Accepted));
            if (!response1.IsSuccessStatusCode)
            {
                var errorContent    = await response1.Content.ReadAsStringAsync();
                Assert.Fail($"Initialize request failed with status {response1.StatusCode}: {errorContent}");
            }

            var responseContent1    = await response1.Content.ReadAsStringAsync();
            var initializeLines     = responseContent1.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            var initializeJsonData  = String.Empty;
            foreach (var line in initializeLines)
            {
                if (line.StartsWith("data: "))
                {
                    initializeJsonData = line["data: ".Length..];
                    break;
                }
            }

            if (initializeJsonData is null)
                Assert.Fail("No JSON data found in initialize response.");

            var success = MCP.InitializeResponse.TryParse(
                              request1,
                              JObject.Parse(initializeJsonData!),
                              out var initializeResponse,
                              out var errorResponse1
                          );

            Assert.That(success,                                               Is.    True);
            Assert.That(initializeResponse,                                    Is.Not.Null);
            Assert.That(initializeResponse?.Id,                                Is.EqualTo(MCP.Request_Id.Parse(0)));
            Assert.That(initializeResponse?.JSONRPCVersion,                    Is.EqualTo("2.0"));

            Assert.That(initializeResponse?.Capabilities,                      Is.Not.Null);
            Assert.That(initializeResponse?.Capabilities?.Logging,             Is.Not.Null);
            Assert.That(initializeResponse?.Capabilities?.Prompts,             Is.    Null);
            Assert.That(initializeResponse?.Capabilities?.Resources,           Is.    Null);
            Assert.That(initializeResponse?.Capabilities?.Tools,               Is.Not.Null);
            Assert.That(initializeResponse?.Capabilities?.Tools?.ListChanged,  Is.    True);
            Assert.That(initializeResponse?.Capabilities?.Completions,         Is.    Null);
            Assert.That(initializeResponse?.Capabilities?.Experimental,        Is.    Null);

            Assert.That(initializeResponse?.ServerInfo,                        Is.Not.Null);
            Assert.That(initializeResponse?.ServerInfo?.Name,                  Is.EqualTo("MCP WebServer Test"));
            Assert.That(initializeResponse?.ServerInfo?.Version,               Is.EqualTo("1.0.0"));

            Assert.That(initializeResponse?.Instructions,                      Is.    Null);


            sessionId = response1.Headers.GetValues("mcp-session-id").FirstOrDefault();
            Assert.That(sessionId, Is.Not.Null, "Session ID should be present in response headers.");

            #endregion

            #region Step 2: Send initialized notification

            // POST / HTTP/1.1
            // host:               localhost:5248
            // connection:         keep-alive
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // accept:             application/json, text/event-stream
            // content-type:       application/json
            // accept-language:    *
            // sec-fetch-mode:     cors
            // user-agent:         node
            // accept-encoding:    gzip, deflate
            // content-length:     54
            // 
            // {"jsonrpc":"2.0","method":"notifications/initialized"}

            // HTTP/1.1 202 Accepted
            // Content-Length:     0
            // Date:               Sun, 08 Jun 2025 16:37:30 GMT
            // Server:             Kestrel
            // Cache-Control:      no-cache,no-store
            // Content-Encoding:   identity
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg


            var request2        = new MCP.InitializedNotification();

            var requestContent  = new StringContent(
                                      request2.ToJSON().ToString(),
                                      System.Text.Encoding.UTF8,
                                      "application/json"
                                  );

            if (sessionId is not null)
                requestContent.Headers.Add("mcp-session-id",  sessionId);

            var response2       = await httpClient.PostAsync(
                                            "/",
                                            requestContent
                                        );

            Assert.That(response2.StatusCode,  Is.EqualTo(System.Net.HttpStatusCode.Accepted), "Should return 202 Accepted.");

            #endregion

        }

        #endregion

        #region Test01_GetAllMCPTools

        [Test]
        public async Task Test01_GetAllMCPTools()
        {

            // POST / HTTP/1.1
            // host:               localhost:5248
            // connection:         keep-alive
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // accept:             application/json, text/event-stream
            // content-type:       application/json
            // accept-language:    *
            // sec-fetch-mode:     cors
            // user-agent:         node
            // accept-encoding:    gzip, deflate
            // content-length:     85
            // 
            // {
            //     "jsonrpc":  "2.0",
            //     "id":        1,
            //     "method":   "tools/list",
            //     "params": {
            //         "_meta": {
            //             "progressToken": 1
            //         }
            //     }
            // }

            // HTTP/1.1 200 OK
            // Content-Type:       text/event-stream
            // Date:               Sun, 08 Jun 2025 16:37:32 GMT
            // Server:             Kestrel
            // Cache-Control:      no-cache,no-store
            // Content-Encoding:   identity
            // Transfer-Encoding:  chunked
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // 
            // 24a
            // event: message
            // data: {
            //     "result": {
            //         "tools": [
            //             {
            //                 "name": "CheckTimeServer",
            //                 "description": "Checks if a time server is reachable.",
            //                 "inputSchema": {
            //                     "title": "CheckTimeServer",
            //                     "description": "Checks if a time server is reachable.",
            //                     "type": "object",
            //                     "properties": {
            //                         "serverAddress": {
            //                             "type": "string"
            //                         }
            //                     },
            //                     "required": [
            //                         "serverAddress"
            //                     ]
            //                 }
            //             },
            //             {
            //                 "name": "GetSystemState",
            //                 "description": "Returns the current system state of the charging station.",
            //                 "inputSchema": {
            //                     "title": "GetSystemState",
            //                     "description": "Returns the current system state of the charging station.",
            //                     "type": "object",
            //                     "properties": {}
            //                 }
            //             }
            //         ]
            //     },
            //     "id": 1,
            //     "jsonrpc": "2.0"
            // }
            // 0

            //await Task.Delay(TimeSpan.FromHours(1));

            var request          = new JObject(
                                       new JProperty("jsonrpc",  "2.0"),
                                       new JProperty("id",        requestId++),
                                       new JProperty("method",   "tools/list"),
                                       new JProperty("params",    new JObject(
                                           new JProperty("_meta",  new JObject(
                                               new JProperty("progressToken",  1)
                                           ))
                                       ))
                                   );

            var requestContent   = new StringContent(
                                       request.ToString(),
                                       System.Text.Encoding.UTF8,
                                       "application/json"
                                   );

            if (sessionId is not null)
                requestContent.Headers.Add("mcp-session-id", sessionId);

            var response         = await httpClient.PostAsync("/", requestContent);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"Tools list request failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent  = await response.Content.ReadAsStringAsync();
            var toolsLines       = responseContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var toolsJsonData    = String.Empty;
            foreach (var line in toolsLines)
            {
                if (line.StartsWith("data: "))
                {
                    toolsJsonData = line[6..];
                    break;
                }
            }

            if (toolsJsonData is null)
                Assert.Fail("No JSON data found in tools list response.");

            var toolsResult = JsonSerializer.Deserialize<JsonRpcResponse>(toolsJsonData);
            Assert.That(toolsResult,         Is.Not.Null,   "Tools list response should not be null.");
            Assert.That(toolsResult.Result,  Is.Not.Null,   "Tools list result should not be null.");
            if (toolsResult.Result.AsObject().TryGetPropertyValue("tools", out var tools))
            {
                var _tools = tools.AsArray();
                Assert.That(_tools,                                                                                                                             Is.Not.Null,                  "Tools list should not be null.");
                Assert.That(_tools.Count,                                                                                                                       Is.GreaterThanOrEqualTo(2),   "At least two tools should be registered.");
                Assert.That(_tools.Any(t => t?.AsObject().TryGetPropertyValue("name", out var tool) == true && tool?.GetValue<String>() == "GetSystemState"),   Is.True,                      "GetSystemState should be registered.");
                Assert.That(_tools.Any(t => t?.AsObject().TryGetPropertyValue("name", out var tool) == true && tool?.GetValue<String>() == "CheckTimeServer"),  Is.True,                      "CheckTimeServer should be registered.");
            }

        }

        #endregion

        #region Test02_SystemStateTool_ReturnsValidSystemState

        [Test]
        public async Task Test02_SystemStateTool_ReturnsValidSystemState()
        {

            // POST / HTTP/1.1
            // host:               localhost:5248
            // connection:         keep-alive
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // accept:             application/json, text/event-stream
            // content-type:       application/json
            // accept-language:    *
            // sec-fetch-mode:     cors
            // user-agent:         node
            // accept-encoding:    gzip, deflate
            // content-length:     124
            // 
            // {
            //     "jsonrpc":  "2.0",
            //     "id":        2,
            //     "method":   "tools/call",
            //     "params": {
            //         "_meta": {
            //             "progressToken": 2
            //         },
            //         "name":       "GetSystemState",
            //         "arguments":   {}
            //     }
            // }

            // HTTP/1.1 200 OK
            // Content-Type:       text/event-stream
            // Date:               Sun, 08 Jun 2025 16:52:12 GMT
            // Server:             Kestrel
            // Cache-Control:      no-cache,no-store
            // Content-Encoding:   identity
            // Transfer-Encoding:  chunked
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // 
            // 121
            // event: message
            // data: {
            //     "result": {
            //         "content": [
            //             {
            //                 "type": "text",
            //                 "text": "{\u0022ChargePower\u0022:\u002222 kW\u0022,\u0022Temperature\u0022:\u002245\u00B0C\u0022,\u0022NetworkStatus\u0022:\u0022Connected\u0022,\u0022ErrorCode\u0022:\u0022None\u0022}"
            //             }
            //         ],
            //         "isError": false
            //     },
            //     "id":        2,
            //     "jsonrpc":  "2.0"
            // }
            // 0

            var request          = new JObject(
                                       new JProperty("jsonrpc",  "2.0"),
                                       new JProperty("id",        requestId++),
                                       new JProperty("method",   "tools/call"),
                                       new JProperty("params",    new JObject(
                                           new JProperty("_meta",      new JObject(
                                               new JProperty("progressToken",  2)
                                           )),
                                           new JProperty("name",      "GetSystemState"),
                                           new JProperty("arguments",  new JObject())
                                       ))
                                   );

            var requestContent   = new StringContent(
                                       request.ToString(),
                                       System.Text.Encoding.UTF8,
                                       "application/json"
                                   );

            if (sessionId is not null)
                requestContent.Headers.Add("mcp-session-id", sessionId);

            var response         = await httpClient.PostAsync("/", requestContent);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"Request to GetSystemState failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent  = await response.Content.ReadAsStringAsync();
            var lines            = responseContent.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            var jsonData         = String.Empty;
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    jsonData = line[6..];
                    break;
                }
            }

            if (jsonData is null)
                Assert.Fail("No JSON data found in event-stream response.");

            var result = JsonSerializer.Deserialize<JsonRpcResponse>(jsonData);
            Assert.That(result,                        Is.Not.Null,               "JSON-RPC response should not be null.");
            Assert.That(result.Result,                 Is.Not.Null,               "Result should not be null.");

            var state = JsonSerializer.Deserialize<dynamic>(result.Result.ToString());
            Assert.That(state,                         Is.Not.Null,               "System state should not be null.");
            //Assert.That((String) state.ChargePower,    Is.EqualTo("22 kW"),       "ChargePower should be 22 kW.");
            //Assert.That((String) state.Temperature,    Is.EqualTo("45°C"),        "Temperature should be 45°C.");
            //Assert.That((String) state.NetworkStatus,  Is.EqualTo("Connected"),   "NetworkStatus should be Connected.");
            //Assert.That((String) state.ErrorCode,      Is.EqualTo("None"),        "ErrorCode should be None.");

        }

        #endregion

        #region Test03_TimeServerTool_ReturnsValidPingResult

        [Test]
        public async Task Test03_TimeServerTool_ReturnsValidPingResult()
        {

            // POST / HTTP/1.1
            // host: localhost:5248
            // connection:         keep-alive
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // accept:             application/json, text/event-stream
            // content-type:       application/json
            // accept-language:    *
            // sec-fetch-mode:     cors
            // user-agent:         node
            // accept-encoding:    gzip, deflate
            // content-length:     154
            // 
            // {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"_meta":{"progressToken":3},"name":"CheckTimeServer","arguments":{"serverAddress":"141.24.12.2"}}}

            // HTTP/1.1 200 OK
            // Content-Type:       text/event-stream
            // Date:               Sun, 08 Jun 2025 18:54:08 GMT
            // Server:             Kestrel
            // Cache-Control:      no-cache,no-store
            // Content-Encoding:   identity
            // Transfer-Encoding:  chunked
            // mcp-session-id:     QTCfbXMQrzSNg1ndSSAVmg
            // 
            // event: message
            // data: {"result":{"content":[{"type":"text","text":"Timeserver 141.24.12.2 is reachable (RTT: 39 ms)."}],"isError":false},"id":3,"jsonrpc":"2.0"}

            var request          = new JObject(
                                       new JProperty("jsonrpc",  "2.0"),
                                       new JProperty("id",        requestId++),
                                       new JProperty("method",   "tools/call"),
                                       new JProperty("params",    new JObject(
                                           new JProperty("_meta",      new JObject(
                                               new JProperty("progressToken",  3)
                                           )),
                                           new JProperty("name",      "CheckTimeServer"),
                                           new JProperty("arguments",  new JObject(
                                               new JProperty("serverAddress", "141.24.12.2")
                                           ))
                                    )));

            var requestContent   = new StringContent(
                                       request.ToString(),
                                       System.Text.Encoding.UTF8,
                                       "application/json"
                                   );

            if (sessionId is not null)
                requestContent.Headers.Add("mcp-session-id", sessionId);

            var response         = await httpClient.PostAsync("/", requestContent);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Assert.Fail($"Request to GetSystemState failed with status {response.StatusCode}: {errorContent}");
            }

            var responseContent  = await response.Content.ReadAsStringAsync();
            var lines            = responseContent.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            var jsonData         = String.Empty;
            foreach (var line in lines)
            {
                if (line.StartsWith("data: "))
                {
                    jsonData = line[6..];
                    break;
                }
            }

            if (jsonData is null)
                Assert.Fail("No JSON data found in event-stream response.");

            var result = JsonSerializer.Deserialize<JsonRpcResponse>(jsonData);
            Assert.That(result,                        Is.Not.Null,               "JSON-RPC response should not be null.");
            Assert.That(result.Result,                 Is.Not.Null,               "Result should not be null.");

            var state = JsonSerializer.Deserialize<dynamic>(result.Result.ToString());


        }

        #endregion


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
