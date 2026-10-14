using DumpAnalysisService.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DumpAnalysisService.Tests.Security;

public class ApiKeyMiddlewareTests
{
    private const string Key = "test-key-123";

    private static async Task<(int StatusCode, bool NextCalled)> _InvokeAsync(string? configuredKey, Action<HttpContext> arrange)
    {
        var nextCalled = false;
        var middleware = new ApiKeyMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            Options.Create(new SecurityOptions { ApiKey = configuredKey }),
            NullLogger<ApiKeyMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/jobs";
        arrange(context);

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task NoKeyConfigured_AllowsRequest()
    {
        var (_, nextCalled) = await _InvokeAsync(null, _ => { });
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        var (status, nextCalled) = await _InvokeAsync(Key, _ => { });
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var (status, nextCalled) = await _InvokeAsync(Key, c => c.Request.Headers[ApiKeyMiddleware.HeaderName] = "wrong");
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, status);
    }

    [Fact]
    public async Task ApiKeyHeader_AllowsRequest()
    {
        var (_, nextCalled) = await _InvokeAsync(Key, c => c.Request.Headers[ApiKeyMiddleware.HeaderName] = Key);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task BearerToken_AllowsRequest()
    {
        var (_, nextCalled) = await _InvokeAsync(Key, c => c.Request.Headers.Authorization = $"Bearer {Key}");
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AccessTokenQuery_AllowedOnlyForHubs()
    {
        var (_, hubAllowed) = await _InvokeAsync(Key, c =>
        {
            c.Request.Path = "/hubs/progress";
            c.Request.QueryString = new QueryString($"?access_token={Key}");
        });
        var (apiStatus, apiAllowed) = await _InvokeAsync(Key, c =>
            c.Request.QueryString = new QueryString($"?access_token={Key}"));

        Assert.True(hubAllowed);
        Assert.False(apiAllowed);
        Assert.Equal(StatusCodes.Status401Unauthorized, apiStatus);
    }

    [Fact]
    public async Task HealthEndpoint_IsExempt()
    {
        var (_, nextCalled) = await _InvokeAsync(Key, c => c.Request.Path = ApiKeyMiddleware.HealthPath);
        Assert.True(nextCalled);
    }
}
