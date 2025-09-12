using System.Text;
using System.Text.Json;
using McpProxy.Constants;
using McpProxy.Models;
using Microsoft.Extensions.Logging;

namespace McpProxy.Services;

public class ApiHttpClient : IApiHttpClient
{
    private readonly ILogger<ApiHttpClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ApiHttpClient(ILogger<ApiHttpClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _baseUrl = Environment.GetEnvironmentVariable("BACKGROUND_SERVICE_URL") ?? "http://localhost:8080";
        
        _logger.LogInformation("Configured API client for: {BaseUrl}", _baseUrl);
    }

    public async Task<Result<bool>> CheckHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{ApiEndpoints.Health}");
            return Result.Success(response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return Result.Failure<bool>($"Health check failed: {ex.Message}");
        }
    }

    public async Task<Result<TResponse>> PostAsync<TRequest, TResponse>(string endpoint, TRequest request) 
        where TRequest : class 
        where TResponse : class
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}{endpoint}", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData != null 
                    ? Result.Success(responseData) 
                    : Result.Failure<TResponse>("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            return Result.Failure<TResponse>($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "POST request failed for endpoint: {Endpoint}", endpoint);
            return Result.Failure<TResponse>($"Request failed: {ex.Message}");
        }
    }

    public async Task<Result<TResponse>> GetAsync<TResponse>(string endpoint) where TResponse : class
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}{endpoint}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData != null 
                    ? Result.Success(responseData) 
                    : Result.Failure<TResponse>("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            return Result.Failure<TResponse>($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GET request failed for endpoint: {Endpoint}", endpoint);
            return Result.Failure<TResponse>($"Request failed: {ex.Message}");
        }
    }

    public async Task<Result<TResponse>> DeleteAsync<TResponse>(string endpoint) where TResponse : class
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}{endpoint}");
            
            if (response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<TResponse>(responseText, _jsonOptions);
                return responseData != null 
                    ? Result.Success(responseData) 
                    : Result.Failure<TResponse>("Failed to deserialize response");
            }
            
            var errorText = await response.Content.ReadAsStringAsync();
            return Result.Failure<TResponse>($"HTTP {(int)response.StatusCode}: {errorText}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DELETE request failed for endpoint: {Endpoint}", endpoint);
            return Result.Failure<TResponse>($"Request failed: {ex.Message}");
        }
    }
}