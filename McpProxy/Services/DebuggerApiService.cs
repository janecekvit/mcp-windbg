using System.Text;
using System.Text.Json;
using CdbMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class DebuggerApiService : IDebuggerApiService
{
    private readonly ILogger<DebuggerApiService> _logger;
    private readonly HttpClient _httpClient;
    private readonly INotificationService _notificationService;
    private readonly string _backgroundServiceUrl;

    public DebuggerApiService(ILogger<DebuggerApiService> logger, HttpClient httpClient, INotificationService notificationService)
    {
        _logger = logger;
        _httpClient = httpClient;
        _notificationService = notificationService;
        _backgroundServiceUrl = Environment.GetEnvironmentVariable("BACKGROUND_SERVICE_URL") ?? "http://localhost:8080";

        _logger.LogInformation("Configured to use background service at: {Url}", _backgroundServiceUrl);
    }

    public async Task<bool> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_backgroundServiceUrl}/health");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to background service");
            return false;
        }
    }

    public async Task<McpToolResult> LoadDumpAsync(JsonElement args, string? progressToken = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(progressToken))
                await _notificationService.SendProgressNotificationAsync(progressToken, 0.1, "Validating dump file path...");

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

            if (!string.IsNullOrEmpty(progressToken))
                await _notificationService.SendProgressNotificationAsync(progressToken, 0.3, "Loading dump file...");

            var requestBody = JsonSerializer.Serialize(new { dumpFilePath });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/load-dump", content);

            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(progressToken))
                    await _notificationService.SendProgressNotificationAsync(progressToken, 0.8, "Creating debugging session...");

                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);

                var sessionId = responseData.GetProperty("sessionId").GetString();
                var message = responseData.GetProperty("message").GetString();

                if (!string.IsNullOrEmpty(progressToken))
                    await _notificationService.SendProgressNotificationAsync(progressToken, 1.0, "Dump loaded successfully!");

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

    public async Task<McpToolResult> ExecuteCommandAsync(JsonElement args, string? progressToken = null)
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

    public async Task<McpToolResult> BasicAnalysisAsync(JsonElement args, string? progressToken = null)
    {
        try
        {
            if (!string.IsNullOrEmpty(progressToken))
                await _notificationService.SendProgressNotificationAsync(progressToken, 0.1, "Preparing basic analysis...");

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

            if (!string.IsNullOrEmpty(progressToken))
                await _notificationService.SendProgressNotificationAsync(progressToken, 0.3, "Running comprehensive analysis...");

            var requestBody = JsonSerializer.Serialize(new { sessionId });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_backgroundServiceUrl}/api/basic-analysis", content);

            if (response.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(progressToken))
                    await _notificationService.SendProgressNotificationAsync(progressToken, 0.9, "Processing analysis results...");

                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                var result = responseData.GetProperty("result").GetString();

                if (!string.IsNullOrEmpty(progressToken))
                    await _notificationService.SendProgressNotificationAsync(progressToken, 1.0, "Analysis completed!");

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

    public async Task<McpToolResult> PredefinedAnalysisAsync(JsonElement args, string? progressToken = null)
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

    public async Task<McpToolResult> ListSessionsAsync()
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

    public async Task<McpToolResult> CloseSessionAsync(JsonElement args)
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

    public async Task<McpToolResult> DetectDebuggersAsync()
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
                    result.AppendLine($"📊 WinDbg available: {winDbgPath}");

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

    public async Task<McpToolResult> ListAnalysesAsync()
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