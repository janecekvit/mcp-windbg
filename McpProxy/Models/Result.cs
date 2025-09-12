namespace McpProxy.Models;

public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Cannot access value of failed result: {_error}");
    public string Error => IsFailure ? _error! : throw new InvalidOperationException("Cannot access error of successful result");

    private Result(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private Result(string error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string error) => new(error);
    
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
        => IsSuccess ? Result<TNew>.Success(mapper(Value)) : Result<TNew>.Failure(Error);
        
    public async Task<Result<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
        => IsSuccess ? Result<TNew>.Success(await mapper(Value)) : Result<TNew>.Failure(Error);

    public Result<T> OnFailure(Action<string> onFailure)
    {
        if (IsFailure) onFailure(Error);
        return this;
    }

    public McpToolResult ToMcpToolResult() 
        => IsSuccess ? McpToolResult.Success(Value?.ToString() ?? "") : McpToolResult.Error(Error);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);
}