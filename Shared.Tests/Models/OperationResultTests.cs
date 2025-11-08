using Shared.Models;

namespace Shared.Tests.Models;

public class OperationResultTests
{
    #region Success Creation Tests

    [Fact]
    public void Success_CreatesSuccessResult()
    {
        // Act
        var result = OperationResult<int>.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Success_WithString_CreatesSuccessResult()
    {
        // Act
        var result = OperationResult<string>.Success("test value");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test value", result.Value);
    }

    [Fact]
    public void Success_WithNull_CreatesSuccessResult()
    {
        // Act
        var result = OperationResult<string?>.Success(null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Success_WithComplexType_CreatesSuccessResult()
    {
        // Arrange
        var data = new { Id = 1, Name = "Test" };

        // Act
        var result = OperationResult<object>.Success(data);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(data, result.Value);
    }

    #endregion

    #region Failure Creation Tests

    [Fact]
    public void Failure_CreatesFailureResult()
    {
        // Act
        var result = OperationResult<int>.Failure("Something went wrong");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("Something went wrong", result.Error);
    }

    [Fact]
    public void Failure_WithDifferentErrorMessages_PreservesMessage()
    {
        // Act
        var result1 = OperationResult<string>.Failure("Error 1");
        var result2 = OperationResult<string>.Failure("Error 2");

        // Assert
        Assert.Equal("Error 1", result1.Error);
        Assert.Equal("Error 2", result2.Error);
    }

    #endregion

    #region Value and Error Access Tests

    [Fact]
    public void Value_OnSuccessResult_ReturnsValue()
    {
        // Arrange
        var result = OperationResult<int>.Success(100);

        // Act
        var value = result.Value;

        // Assert
        Assert.Equal(100, value);
    }

    [Fact]
    public void Value_OnFailureResult_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Error message");

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("Error message", ex.Message);
    }

    [Fact]
    public void Error_OnFailureResult_ReturnsError()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Error message");

        // Act
        var error = result.Error;

        // Assert
        Assert.Equal("Error message", error);
    }

    [Fact]
    public void Error_OnSuccessResult_ThrowsInvalidOperationException()
    {
        // Arrange
        var result = OperationResult<int>.Success(42);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    #endregion

    #region Map Tests

    [Fact]
    public void Map_OnSuccessResult_TransformsValue()
    {
        // Arrange
        var result = OperationResult<int>.Success(10);

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(20, mapped.Value);
    }

    [Fact]
    public void Map_OnFailureResult_PreservesFailure()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Original error");

        // Act
        var mapped = result.Map(x => x * 2);

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("Original error", mapped.Error);
    }

    [Fact]
    public void Map_ChangesType_PreservesSuccess()
    {
        // Arrange
        var result = OperationResult<int>.Success(42);

        // Act
        var mapped = result.Map(x => x.ToString());

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal("42", mapped.Value);
    }

    [Fact]
    public void Map_ChangesType_PreservesFailure()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Error");

        // Act
        var mapped = result.Map(x => x.ToString());

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("Error", mapped.Error);
    }

    [Fact]
    public void Map_ChainedMaps_TransformsMultipleTimes()
    {
        // Arrange
        var result = OperationResult<int>.Success(5);

        // Act
        var mapped = result
            .Map(x => x * 2)      // 10
            .Map(x => x + 5)      // 15
            .Map(x => x.ToString()); // "15"

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal("15", mapped.Value);
    }

    [Fact]
    public void Map_ChainedMaps_StopsAtFirstFailure()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Initial error");

        // Act
        var mappedString = result
            .Map(x => x * 2)           // Not executed
            .Map(x => x + 5)           // Not executed
            .Map(x => x.ToString());   // Not executed

        // Assert
        Assert.True(mappedString.IsFailure);
        Assert.Equal("Initial error", mappedString.Error);
    }

    #endregion

    #region MapAsync Tests

    [Fact]
    public async Task MapAsync_OnSuccessResult_TransformsValueAsync()
    {
        // Arrange
        var result = OperationResult<int>.Success(10);

        // Act
        var mapped = await result.MapAsync(async x =>
        {
            await Task.Delay(1);
            return x * 2;
        });

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal(20, mapped.Value);
    }

    [Fact]
    public async Task MapAsync_OnFailureResult_PreservesFailure()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Async error");

        // Act
        var mapped = await result.MapAsync(async x =>
        {
            await Task.Delay(1);
            return x * 2;
        });

        // Assert
        Assert.True(mapped.IsFailure);
        Assert.Equal("Async error", mapped.Error);
    }

    [Fact]
    public async Task MapAsync_ChangesType_PreservesSuccess()
    {
        // Arrange
        var result = OperationResult<int>.Success(42);

        // Act
        var mapped = await result.MapAsync(async x =>
        {
            await Task.Delay(1);
            return x.ToString();
        });

        // Assert
        Assert.True(mapped.IsSuccess);
        Assert.Equal("42", mapped.Value);
    }

    #endregion

    #region OnFailure Tests

    [Fact]
    public void OnFailure_OnFailureResult_InvokesCallback()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Test error");
        var callbackInvoked = false;
        var capturedError = "";

        // Act
        result.OnFailure(error =>
        {
            callbackInvoked = true;
            capturedError = error;
        });

        // Assert
        Assert.True(callbackInvoked);
        Assert.Equal("Test error", capturedError);
    }

    [Fact]
    public void OnFailure_OnSuccessResult_DoesNotInvokeCallback()
    {
        // Arrange
        var result = OperationResult<int>.Success(42);
        var callbackInvoked = false;

        // Act
        result.OnFailure(error => callbackInvoked = true);

        // Assert
        Assert.False(callbackInvoked);
    }

    [Fact]
    public void OnFailure_ReturnsOriginalResult()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Error");

        // Act
        var returned = result.OnFailure(error => { });

        // Assert
        Assert.Equal(result, returned);
    }

    [Fact]
    public void OnFailure_CanBeChained()
    {
        // Arrange
        var result = OperationResult<int>.Failure("Chain error");
        var callCount = 0;

        // Act
        result
            .OnFailure(error => callCount++)
            .OnFailure(error => callCount++)
            .OnFailure(error => callCount++);

        // Assert
        Assert.Equal(3, callCount);
    }

    #endregion

    #region Static Result Helper Tests

    [Fact]
    public void Result_Success_CreatesSuccessResult()
    {
        // Act
        var result = Result.Success(42);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Result_Failure_CreatesFailureResult()
    {
        // Act
        var result = Result.Failure<int>("Helper error");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Helper error", result.Error);
    }

    #endregion

    #region Integration and Complex Scenarios

    [Fact]
    public void ComplexWorkflow_SuccessPath_WorksCorrectly()
    {
        // Arrange & Act
        var result = OperationResult<string>.Success("session123")
            .Map(sessionId => $"Loaded: {sessionId}")
            .Map(message => message.ToUpper());

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("LOADED: SESSION123", result.Value);
    }

    [Fact]
    public void ComplexWorkflow_FailurePath_PropagatesError()
    {
        // Arrange & Act
        var result = OperationResult<string>.Failure("Session not found")
            .Map(sessionId => $"Loaded: {sessionId}")
            .Map(message => message.ToUpper())
            .OnFailure(error => Console.WriteLine($"Error: {error}"));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Session not found", result.Error);
    }

    [Fact]
    public async Task ComplexAsyncWorkflow_SuccessPath_WorksCorrectly()
    {
        // Arrange & Act
        var result = await OperationResult<int>.Success(10)
            .MapAsync(async x =>
            {
                await Task.Delay(1);
                return x * 2;
            });

        var final = result.Map(x => x.ToString());

        // Assert
        Assert.True(final.IsSuccess);
        Assert.Equal("20", final.Value);
    }

    [Fact]
    public void OperationResult_IsValueType_BehavesCorrectly()
    {
        // Arrange
        var result1 = OperationResult<int>.Success(42);
        var result2 = result1; // Copy (value type)

        // Act - result2 is a copy, not a reference

        // Assert
        Assert.Equal(result1.Value, result2.Value);
    }

    #endregion
}
