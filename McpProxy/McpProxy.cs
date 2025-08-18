using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace CdbMcpServer;

public class McpProxy
{
    private readonly ILogger<McpProxy> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _backgroundServiceUrl;
    private bool _isInitialized = false;

    public McpProxy(ILogger<McpProxy> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _backgroundServiceUrl = Environment.GetEnvironmentVariable("BACKGROUND_SERVICE_URL") ?? "http://localhost:8080";

        _logger.LogInformation("Configured to use background service at: {Url}", _backgroundServiceUrl);
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting CDB MCP Server Proxy...");

        // Číst ze stdin, odpovídat na stdout
        var stdinStream = Console.OpenStandardInput();
        var stdoutStream = Console.OpenStandardOutput();

        using var reader = new StreamReader(stdinStream);
        using var writer = new StreamWriter(stdoutStream) { AutoFlush = true };

        _logger.LogInformation("MCP Server Proxy ready to accept requests");

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            _logger.LogInformation("Received request: {Line}", line);

            try
            {
                var request = JsonSerializer.Deserialize<McpRequest>(line);
                if (request != null)
                {
                    _logger.LogInformation("Processing method: {Method}, ID: {Id}", request.Method, request.Id);
                    var response = await HandleRequestAsync(request, writer);
                    if (response != null)
                    {
                        var responseJson = JsonSerializer.Serialize(response);
                        _logger.LogInformation("Sending response: {Response}", responseJson);
                        await writer.WriteLineAsync(responseJson);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing request: {Line}", line);
                var errorResponse = new McpResponse
                {
                    Id = 0,
                    Error = new McpError { Code = -1, Message = ex.Message }
                };
                var errorJson = JsonSerializer.Serialize(errorResponse);
                await writer.WriteLineAsync(errorJson);
            }
        }

        _logger.LogInformation("MCP Server Proxy shutting down");
    }

    private async Task SendInitializedNotification(StreamWriter writer)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        };

        var json = JsonSerializer.Serialize(notification);
        await writer.WriteLineAsync(json);
    }

    private async Task SendProgressNotification(StreamWriter writer, string progressToken, double progress, string? message = null)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = "notifications/progress",
            @params = new
            {
                progressToken,
                progress,
                total = 1.0,
                message
            }
        };

        var json = JsonSerializer.Serialize(notification);
        await writer.WriteLineAsync(json);
    }

    private async Task<McpResponse?> HandleRequestAsync(McpRequest request, StreamWriter writer)
    {
        try
        {
            return request.Method switch
            {
                "initialize" => await HandleInitializeAsync(request, writer),
                "tools/list" => _isInitialized ? HandleListToolsAsync(request) : CreateNotInitializedError(request),
                "tools/call" => _isInitialized ? await HandleCallToolAsync(request, writer) : CreateNotInitializedError(request),
                _ => new McpResponse
                {
                    Id = request.Id,
                    Error = new McpError { Code = -32601, Message = $"Method not found: {request.Method}" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling request method: {Method}", request.Method);
            return new McpResponse
            {
                Id = request.Id,
                Error = new McpError { Code = -1, Message = ex.Message }
            };
        }
    }

    private async Task<McpResponse?> HandleInitializeAsync(McpRequest request, StreamWriter writer)
    {
        _logger.LogInformation("Received initialize request");

        // Check if background service is available
        try
        {
            var healthResponse = await _httpClient.GetAsync($"{_backgroundServiceUrl}/health");
            if (!healthResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Background service health check failed");
            }
            else
            {
                _logger.LogInformation("Background service is healthy");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to background service");
        }

        var initializeResponse = new McpResponse
        {
            Id = request.Id,
            Result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new
                    {
                        listChanged = false
                    }
                },
                serverInfo = new
                {
                    name = "cdb-mcp-server-proxy",
                    version = "2.0.0"
                }
            }
        };

        _isInitialized = true;

        // Po inicializaci pošleme notification
        await SendInitializedNotification(writer);

        // Vracíme response normálně
        return initializeResponse;
    }

    private McpResponse CreateNotInitializedError(McpRequest request)
    {
        return new McpResponse
        {
            Id = request.Id,
            Error = new McpError { Code = -32002, Message = "Server not initialized" }
        };
    }

    private McpResponse HandleListToolsAsync(McpRequest request)
    {
        var tools = new[]
        {
            new McpTool
            {
                Name = "load_dump",
                Description = "Load a memory dump file and create a new CDB debugging session",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        dump_file_path = new
                        {
                            type = "string",
                            description = "Path to the memory dump file (.dmp)"
                        }
                    },
                    required = new[] { "dump_file_path" }
                }
            },
            new McpTool
            {
                Name = "execute_command",
                Description = "Execute a WinDbg/CDB command in an existing debugging session",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        },
                        command = new
                        {
                            type = "string",
                            description = "WinDbg/CDB command to execute (e.g., 'kb', '!analyze -v', 'dt')"
                        }
                    },
                    required = new[] { "session_id", "command" }
                }
            },
            new McpTool
            {
                Name = "basic_analysis",
                Description = "Run a comprehensive basic analysis of the loaded dump (equivalent to the PowerShell script)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        }
                    },
                    required = new[] { "session_id" }
                }
            },
            new McpTool
            {
                Name = "list_sessions",
                Description = "List all active debugging sessions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = "close_session",
                Description = "Close a debugging session and free resources",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session to close"
                        }
                    },
                    required = new[] { "session_id" }
                }
            },
            new McpTool
            {
                Name = "predefined_analysis",
                Description = "Run a predefined analysis on the loaded dump (basic, exception, threads, heap, modules, handles, locks, memory, drivers, processes)",
                InputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        session_id = new
                        {
                            type = "string",
                            description = "ID of the debugging session"
                        },
                        analysis_type = new
                        {
                            type = "string",
                            description = "Type of analysis to run",
                            @enum = new[] { "basic", "exception", "threads", "heap", "modules", "handles", "locks", "memory", "drivers", "processes" }
                        }
                    },
                    required = new[] { "session_id", "analysis_type" }
                }
            },
            new McpTool
            {
                Name = "list_analyses",
                Description = "List all available predefined analyses with descriptions",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            },
            new McpTool
            {
                Name = "detect_debuggers",
                Description = "Detect available CDB/WinDbg installations on the system",
                InputSchema = new
                {
                    type = "object",
                    properties = new { }
                }
            }
        };

        return new McpResponse
        {
            Id = request.Id,
            Result = new { tools }
        };
    }

    private async Task<McpResponse> HandleCallToolAsync(McpRequest request, StreamWriter writer)
    {
        if (request.Params == null || !request.Params.Value.TryGetProperty("name", out var nameElement) ||
            !request.Params.Value.TryGetProperty("arguments", out var argsElement))
        {
            return new McpResponse
            {
                Id = request.Id,
                Error = new McpError { Code = -32602, Message = "Invalid params" }
            };
        }

        var toolName = nameElement.GetString() ?? "";
        var args = argsElement;
        
        // Extract progress token if available
        string? progressToken = null;
        if (request.Params.Value.TryGetProperty("_meta", out var metaElement) &&
            metaElement.TryGetProperty("progressToken", out var tokenElement))
        {
            progressToken = tokenElement.GetString();
        }

        var result = toolName switch
        {
            "load_dump" => await HandleLoadDumpAsync(args, progressToken, writer),
            "execute_command" => await HandleExecuteCommandAsync(args, progressToken, writer),
            "basic_analysis" => await HandleBasicAnalysisAsync(args, progressToken, writer),
            "predefined_analysis" => await HandlePredefinedAnalysisAsync(args, progressToken, writer),
            "list_sessions" => await HandleListSessionsAsync(),
            "list_analyses" => await HandleListAnalysesAsync(),
            "detect_debuggers" => await HandleDetectDebuggersAsync(),
            "close_session" => await HandleCloseSessionAsync(args),
            _ => new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Unknown tool: {toolName}" } },
                IsError = true
            }
        };

        return new McpResponse
        {
            Id = request.Id,
            Result = result
        };
    }

    private async Task<McpToolResult> HandleLoadDumpAsync(JsonElement args, string? progressToken = null, StreamWriter? writer = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(progressToken) && writer != null)
            {
                await SendProgressNotification(writer, progressToken, 0.1, "Validating dump file path...");
            }

            if (!args.TryGetProperty("dump_file_path", out var dumpFileElement))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Missing dump_file_path parameter" } },
                    IsError = true
                };
            }

            var dumpFilePath = dumpFileElement.GetString();
            if (string.IsNullOrEmpty(dumpFilePath))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Empty dump_file_path parameter" } },
                    IsError = true
                };
            }

            if (!string.IsNullOrEmpty(progressToken) && writer != null)
            {
                await SendProgressNotification(writer, progressToken, 0.3, "Loading dump file...");
            }

            var requestBody = JsonSerializer.Serialize(new { dumpFilePath });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/load-dump", content);

            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(progressToken) && writer != null)
                {
                    await SendProgressNotification(writer, progressToken, 0.8, "Creating debugging session...");
                }

                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);

                var sessionId = responseData.GetProperty("sessionId").GetString();
                var message = responseData.GetProperty("message").GetString();

                if (!string.IsNullOrEmpty(progressToken) && writer != null)
                {
                    await SendProgressNotification(writer, progressToken, 1.0, "Dump loaded successfully!");
                }

                return new McpToolResult
                {
                    Content = new[]
                    {
                        new McpContent
                        {
                            Type = "text",
                            Text = $"Session created successfully!\nSession ID: {sessionId}\nDump file: {dumpFilePath}\n\n{message}"
                        }
                    },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Failed to load dump: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleExecuteCommandAsync(JsonElement args, string? progressToken = null, StreamWriter? writer = null)
    {
        try
        {
            if (!args.TryGetProperty("session_id", out var sessionIdElement) ||
                !args.TryGetProperty("command", out var commandElement))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Missing session_id or command parameter" } },
                    IsError = true
                };
            }

            var sessionId = sessionIdElement.GetString();
            var command = commandElement.GetString();

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(command))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Empty session_id or command parameter" } },
                    IsError = true
                };
            }

            var requestBody = JsonSerializer.Serialize(new { sessionId, command });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/execute-command", content);

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var result = responseData.GetProperty("result").GetString();

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = result ?? "" } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleBasicAnalysisAsync(JsonElement args, string? progressToken = null, StreamWriter? writer = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(progressToken) && writer != null)
            {
                await SendProgressNotification(writer, progressToken, 0.1, "Preparing basic analysis...");
            }

            if (!args.TryGetProperty("session_id", out var sessionIdElement))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Missing session_id parameter" } },
                    IsError = true
                };
            }

            var sessionId = sessionIdElement.GetString();
            if (string.IsNullOrEmpty(sessionId))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Empty session_id parameter" } },
                    IsError = true
                };
            }

            if (!string.IsNullOrEmpty(progressToken) && writer != null)
            {
                await SendProgressNotification(writer, progressToken, 0.3, "Running comprehensive analysis...");
            }

            var requestBody = JsonSerializer.Serialize(new { sessionId });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/basic-analysis", content);

            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(progressToken) && writer != null)
                {
                    await SendProgressNotification(writer, progressToken, 0.9, "Processing analysis results...");
                }

                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var result = responseData.GetProperty("result").GetString();

                if (!string.IsNullOrEmpty(progressToken) && writer != null)
                {
                    await SendProgressNotification(writer, progressToken, 1.0, "Analysis completed!");
                }

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = result ?? "" } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandlePredefinedAnalysisAsync(JsonElement args, string? progressToken = null, StreamWriter? writer = null)
    {
        try
        {
            if (!args.TryGetProperty("session_id", out var sessionIdElement) ||
                !args.TryGetProperty("analysis_type", out var analysisTypeElement))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Missing session_id or analysis_type parameter" } },
                    IsError = true
                };
            }

            var sessionId = sessionIdElement.GetString();
            var analysisType = analysisTypeElement.GetString();

            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(analysisType))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Empty session_id or analysis_type parameter" } },
                    IsError = true
                };
            }

            var requestBody = JsonSerializer.Serialize(new { sessionId, analysisType });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/predefined-analysis", content);

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var result = responseData.GetProperty("result").GetString();

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = result ?? "" } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleListSessionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backgroundServiceUrl}/api/sessions");

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var sessions = responseData.GetProperty("sessions");

                var sessionList = new StringBuilder();
                sessionList.AppendLine("Active sessions:");

                foreach (var session in sessions.EnumerateArray())
                {
                    var sessionId = session.GetProperty("SessionId").GetString();
                    var dumpFile = session.GetProperty("DumpFile").GetString();
                    var isActive = session.GetProperty("IsActive").GetBoolean();
                    sessionList.AppendLine($"  Session ID: {sessionId}");
                    sessionList.AppendLine($"    Dump File: {dumpFile}");
                    sessionList.AppendLine($"    Active: {isActive}");
                    sessionList.AppendLine();
                }

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = sessionList.ToString() } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleCloseSessionAsync(JsonElement args)
    {
        try
        {
            if (!args.TryGetProperty("session_id", out var sessionIdElement))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Missing session_id parameter" } },
                    IsError = true
                };
            }

            var sessionId = sessionIdElement.GetString();
            if (string.IsNullOrEmpty(sessionId))
            {
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = "Empty session_id parameter" } },
                    IsError = true
                };
            }

            var response = await _httpClient.DeleteAsync($"{_backgroundServiceUrl}/api/sessions/{sessionId}");

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var message = responseData.GetProperty("message").GetString();

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = message ?? "" } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleDetectDebuggersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backgroundServiceUrl}/api/detect-debuggers");

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);

                var result = new StringBuilder();
                result.AppendLine("🔍 Debugger Detection Results:");
                result.AppendLine();

                var cdbPath = responseData.GetProperty("cdbPath").GetString();
                var winDbgPath = responseData.GetProperty("winDbgPath").GetString();

                if (!string.IsNullOrEmpty(cdbPath))
                {
                    result.AppendLine($"✅ Primary debugger: {cdbPath}");
                }
                else
                {
                    result.AppendLine("❌ No CDB found");
                }

                if (!string.IsNullOrEmpty(winDbgPath) && winDbgPath != cdbPath)
                {
                    result.AppendLine($"📊 WinDbg available: {winDbgPath}");
                }

                result.AppendLine();
                result.AppendLine("🔧 Environment variables:");

                var envVars = responseData.GetProperty("environmentVariables");
                foreach (var envVar in envVars.EnumerateObject())
                {
                    var value = envVar.Value.ValueKind == JsonValueKind.Null ? "(not set)" : envVar.Value.GetString();
                    result.AppendLine($"  {envVar.Name}: {value}");
                }

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = result.ToString() } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error detecting debuggers: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private async Task<McpToolResult> HandleListAnalysesAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backgroundServiceUrl}/api/analyses");

            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var analyses = responseData.GetProperty("analyses");

                var result = new StringBuilder();
                result.AppendLine("Available predefined analyses:");
                result.AppendLine();

                foreach (var analysis in analyses.EnumerateArray())
                {
                    var name = analysis.GetProperty("name").GetString();
                    var description = analysis.GetProperty("description").GetString();
                    result.AppendLine($"{name}: {description}");
                }

                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = result.ToString() } },
                    IsError = false
                };
            }
            else
            {
                var errorText = await response.Content.ReadAsStringAsync();
                return new McpToolResult
                {
                    Content = new[] { new McpContent { Type = "text", Text = $"Error: {errorText}" } },
                    IsError = true
                };
            }
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new[] { new McpContent { Type = "text", Text = $"Error: {ex.Message}" } },
                IsError = true
            };
        }
    }
}

// MCP Protocol DTOs
public class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

public class McpResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new();
}

public class McpToolResult
{
    [JsonPropertyName("content")]
    public McpContent[] Content { get; set; } = Array.Empty<McpContent>();
    
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

public class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";
    
    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}