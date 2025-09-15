namespace Shared.Models;

public readonly record struct OperationResult<T>
{
    private readonly T? _value;
    private readonly string? _error;
    
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    
    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Cannot access value of failed result: {_error}");
    public string Error => IsFailure ? _error! : throw new InvalidOperationException("Cannot access error of successful result");

    private OperationResult(T value)
    {
        _value = value;
        _error = null;
        IsSuccess = true;
    }

    private OperationResult(string error)
    {
        _value = default;
        _error = error;
        IsSuccess = false;
    }

    public static OperationResult<T> Success(T value) => new(value);
    public static OperationResult<T> Failure(string error) => new(error);
    
    public OperationResult<TNew> Map<TNew>(Func<T, TNew> mapper)
        => IsSuccess ? OperationResult<TNew>.Success(mapper(Value)) : OperationResult<TNew>.Failure(Error);
        
    public async Task<OperationResult<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> mapper)
        => IsSuccess ? OperationResult<TNew>.Success(await mapper(Value)) : OperationResult<TNew>.Failure(Error);

    public OperationResult<T> OnFailure(Action<string> onFailure)
    {
        if (IsFailure) onFailure(Error);
        return this;
    }

}

public static class Result
{
    public static OperationResult<T> Success<T>(T value) => OperationResult<T>.Success(value);
    public static OperationResult<T> Failure<T>(string error) => OperationResult<T>.Failure(error);
}