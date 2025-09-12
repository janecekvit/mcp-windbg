using McpProxy.Models;

namespace McpProxy.Services;

public interface IApiHttpClient
{
    Task<Result<bool>> CheckHealthAsync();
    Task<Result<TResponse>> PostAsync<TRequest, TResponse>(string endpoint, TRequest request) 
        where TRequest : class 
        where TResponse : class;
    Task<Result<TResponse>> GetAsync<TResponse>(string endpoint) where TResponse : class;
    Task<Result<TResponse>> DeleteAsync<TResponse>(string endpoint) where TResponse : class;
}