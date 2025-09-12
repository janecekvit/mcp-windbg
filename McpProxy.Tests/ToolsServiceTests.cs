using McpProxy.Services;

namespace McpProxy.Tests;

public class ToolsServiceTests
{
    private readonly ToolsService _toolsService;

    public ToolsServiceTests()
    {
        _toolsService = new ToolsService();
    }

    [Fact]
    public void CreateListToolsResponse_ReturnsValidResponse()
    {
        // Arrange
        var requestId = 123;

        // Act
        var response = _toolsService.CreateListToolsResponse(requestId);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(requestId, response.Id);
        Assert.NotNull(response.Result);
        Assert.Null(response.Error);
    }

    [Fact]
    public void CreateListToolsResponse_ContainsExpectedTools()
    {
        // Arrange
        var requestId = 456;
        var expectedToolNames = new[]
        {
            "load_dump",
            "execute_command",
            "basic_analysis",
            "list_sessions",
            "close_session",
            "predefined_analysis",
            "list_analyses",
            "detect_debuggers"
        };

        // Act
        var response = _toolsService.CreateListToolsResponse(requestId);

        // Assert
        Assert.NotNull(response.Result);
        var resultJson = System.Text.Json.JsonSerializer.Serialize(response.Result);
        var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);

        Assert.True(resultDoc.RootElement.TryGetProperty("tools", out var toolsElement));
        Assert.Equal(expectedToolNames.Length, toolsElement.GetArrayLength());

        var toolNames = toolsElement.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToArray();

        foreach (var expectedName in expectedToolNames)
        {
            Assert.Contains(expectedName, toolNames);
        }
    }

    [Theory]
    [InlineData("load_dump", "Load a memory dump file and create a new CDB debugging session")]
    [InlineData("execute_command", "Execute a WinDbg/CDB command in an existing debugging session")]
    [InlineData("basic_analysis", "Run a comprehensive basic analysis of the loaded dump (equivalent to the PowerShell script)")]
    public void CreateListToolsResponse_ContainsCorrectDescriptions(string toolName, string expectedDescription)
    {
        // Arrange
        var requestId = 789;

        // Act
        var response = _toolsService.CreateListToolsResponse(requestId);

        // Assert
        var resultJson = System.Text.Json.JsonSerializer.Serialize(response.Result);
        var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);

        var tool = resultDoc.RootElement
            .GetProperty("tools")
            .EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == toolName);

        Assert.Equal(expectedDescription, tool.GetProperty("description").GetString());
    }
}