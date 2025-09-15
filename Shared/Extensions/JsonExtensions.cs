using System.Text.Json;
using Shared.Models;

namespace Shared.Extensions;

public static class JsonElementExtensions
{
    /// <summary>
    /// Gets a required string property from JSON element with proper error handling
    /// </summary>
    /// <param name="element">The JSON element to parse</param>
    /// <param name="propertyName">Name of the property to extract</param>
    /// <returns>Result containing the string value or error message</returns>
    public static OperationResult<string> GetRequiredString(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return OperationResult<string>.Failure($"Missing {propertyName} parameter");

        var value = prop.GetString();
        if (string.IsNullOrWhiteSpace(value))
            return OperationResult<string>.Failure($"Empty {propertyName} parameter");

        return OperationResult<string>.Success(value);
    }

    /// <summary>
    /// Gets two required string properties from JSON element
    /// </summary>
    /// <param name="element">The JSON element to parse</param>
    /// <param name="firstProperty">Name of the first property</param>
    /// <param name="secondProperty">Name of the second property</param>
    /// <returns>Result containing both values as tuple or error message</returns>
    public static OperationResult<(string First, string Second)> GetRequiredStrings(this JsonElement element,
        string firstProperty, string secondProperty)
    {
        var firstResult = element.GetRequiredString(firstProperty);
        if (firstResult.IsFailure) return OperationResult<(string, string)>.Failure(firstResult.Error);

        var secondResult = element.GetRequiredString(secondProperty);
        if (secondResult.IsFailure) return OperationResult<(string, string)>.Failure(secondResult.Error);

        return OperationResult<(string, string)>.Success((firstResult.Value, secondResult.Value));
    }

    /// <summary>
    /// Tries to get an optional boolean property from JSON element
    /// </summary>
    /// <param name="element">The JSON element to parse</param>
    /// <param name="propertyName">Name of the property to extract</param>
    /// <returns>Result containing the boolean value if present, or failure if property doesn't exist</returns>
    public static OperationResult<bool> TryGetBool(this JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return OperationResult<bool>.Failure($"Property {propertyName} not found");

        if (prop.ValueKind == JsonValueKind.True)
            return OperationResult<bool>.Success(true);

        if (prop.ValueKind == JsonValueKind.False)
            return OperationResult<bool>.Success(false);

        return OperationResult<bool>.Failure($"Property {propertyName} is not a boolean");
    }
}