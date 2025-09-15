using McpProxy.Models;

namespace McpProxy.Tests;

public class McpModelsTests
{
    [Fact]
    public void McpToolResult_Success_CreatesSuccessResult()
    {
        // Arrange
        var content = "Test content";

        // Act
        var result = McpToolResult.Success(content);

        // Assert
        Assert.False(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal(content, result.Content[0].Text);
    }

    [Fact]
    public void McpToolResult_Error_CreatesErrorResult()
    {
        // Arrange
        var errorMessage = "Test error";

        // Act
        var result = McpToolResult.Error(errorMessage);

        // Assert
        Assert.True(result.IsError);
        Assert.Single(result.Content);
        Assert.Equal("text", result.Content[0].Type);
        Assert.Equal(errorMessage, result.Content[0].Text);
    }

    [Fact]
    public void McpResponse_Success_CreatesSuccessResponse()
    {
        // Arrange
        var id = 123;
        var resultData = new { message = "success" };

        // Act
        var response = McpResponse.Success(id, resultData);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(id, response.Id);
        Assert.NotNull(response.Result);
        Assert.Null(response.Error);
    }

    [Fact]
    public void McpResponse_CreateError_CreatesErrorResponse()
    {
        // Arrange
        var id = 456;
        var error = McpError.Custom(-1, "Test error");

        // Act
        var response = McpResponse.CreateError(id, error);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(id, response.Id);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal(-1, response.Error.Code);
        Assert.Equal("Test error", response.Error.Message);
    }

    [Fact]
    public void McpResponse_NotInitialized_CreatesNotInitializedError()
    {
        // Arrange
        var id = 789;

        // Act
        var response = McpResponse.NotInitialized(id);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(id, response.Id);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal(-32002, response.Error.Code);
        Assert.Equal("Server not initialized", response.Error.Message);
    }

    [Fact]
    public void McpResponse_MethodNotFound_CreatesMethodNotFoundError()
    {
        // Arrange
        var id = 101;
        var method = "unknown_method";

        // Act
        var response = McpResponse.MethodNotFound(id, method);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(id, response.Id);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal(-32601, response.Error.Code);
        Assert.Contains(method, response.Error.Message);
    }

    [Fact]
    public void McpResponse_InvalidParams_CreatesInvalidParamsError()
    {
        // Arrange
        var id = 202;

        // Act
        var response = McpResponse.InvalidParams(id);

        // Assert
        Assert.Equal("2.0", response.JsonRpc);
        Assert.Equal(id, response.Id);
        Assert.Null(response.Result);
        Assert.NotNull(response.Error);
        Assert.Equal(-32602, response.Error.Code);
        Assert.Equal("Invalid params", response.Error.Message);
    }

    [Fact]
    public void McpError_ServerNotInitialized_CreatesCorrectError()
    {
        // Act
        var error = McpError.ServerNotInitialized();

        // Assert
        Assert.Equal(-32002, error.Code);
        Assert.Equal("Server not initialized", error.Message);
    }

    [Fact]
    public void McpError_Custom_CreatesCustomError()
    {
        // Arrange
        var code = -1000;
        var message = "Custom error message";

        // Act
        var error = McpError.Custom(code, message);

        // Assert
        Assert.Equal(code, error.Code);
        Assert.Equal(message, error.Message);
    }
}