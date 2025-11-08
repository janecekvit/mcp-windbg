using System.Text.Json;
using Shared.Extensions;

namespace Shared.Tests.Extensions;

public class JsonElementExtensionsTests
{
    #region GetRequiredString Tests

    [Fact]
    public void GetRequiredString_ValidProperty_ReturnsSuccess()
    {
        // Arrange
        var json = """{"name": "TestValue"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("TestValue", result.Value);
    }

    [Fact]
    public void GetRequiredString_MissingProperty_ReturnsFailure()
    {
        // Arrange
        var json = """{"other": "value"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing name parameter", result.Error);
    }

    [Fact]
    public void GetRequiredString_EmptyString_ReturnsFailure()
    {
        // Arrange
        var json = """{"name": ""}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty name parameter", result.Error);
    }

    [Fact]
    public void GetRequiredString_WhitespaceString_ReturnsFailure()
    {
        // Arrange
        var json = """{"name": "   "}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty name parameter", result.Error);
    }

    [Fact]
    public void GetRequiredString_NullValue_ReturnsFailure()
    {
        // Arrange
        var json = """{"name": null}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty name parameter", result.Error);
    }

    [Theory]
    [InlineData("""{"sessionId": "abc123"}""", "sessionId", "abc123")]
    [InlineData("""{"command": "!analyze -v"}""", "command", "!analyze -v")]
    [InlineData("""{"path": "C:\\dumps\\crash.dmp"}""", "path", "C:\\dumps\\crash.dmp")]
    public void GetRequiredString_VariousValues_ReturnsCorrectValue(string json, string property, string expected)
    {
        // Arrange
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString(property);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    #endregion

    #region GetRequiredStrings Tests

    [Fact]
    public void GetRequiredStrings_BothPropertiesValid_ReturnsSuccess()
    {
        // Arrange
        var json = """{"sessionId": "session123", "command": "kb"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("session123", result.Value.First);
        Assert.Equal("kb", result.Value.Second);
    }

    [Fact]
    public void GetRequiredStrings_FirstPropertyMissing_ReturnsFailure()
    {
        // Arrange
        var json = """{"command": "kb"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing sessionId parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_SecondPropertyMissing_ReturnsFailure()
    {
        // Arrange
        var json = """{"sessionId": "session123"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing command parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_BothPropertiesMissing_ReturnsFailureForFirst()
    {
        // Arrange
        var json = """{"other": "value"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing sessionId parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_FirstPropertyEmpty_ReturnsFailure()
    {
        // Arrange
        var json = """{"sessionId": "", "command": "kb"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty sessionId parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_SecondPropertyEmpty_ReturnsFailure()
    {
        // Arrange
        var json = """{"sessionId": "session123", "command": ""}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty command parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_FirstPropertyNull_ReturnsFailure()
    {
        // Arrange
        var json = """{"sessionId": null, "command": "kb"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty sessionId parameter", result.Error);
    }

    [Fact]
    public void GetRequiredStrings_SecondPropertyNull_ReturnsFailure()
    {
        // Arrange
        var json = """{"sessionId": "session123", "command": null}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Empty command parameter", result.Error);
    }

    [Theory]
    [InlineData("""{"dump": "C:\\test.dmp", "sessionId": "s1"}""", "dump", "sessionId", "C:\\test.dmp", "s1")]
    [InlineData("""{"first": "value1", "second": "value2"}""", "first", "second", "value1", "value2")]
    [InlineData("""{"a": "alpha", "b": "beta"}""", "a", "b", "alpha", "beta")]
    public void GetRequiredStrings_VariousCombinations_ReturnsCorrectValues(
        string json, string prop1, string prop2, string expected1, string expected2)
    {
        // Arrange
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings(prop1, prop2);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expected1, result.Value.First);
        Assert.Equal(expected2, result.Value.Second);
    }

    #endregion

    #region Complex JSON Structure Tests

    [Fact]
    public void GetRequiredString_NestedObject_AccessesDirectChild()
    {
        // Arrange
        var json = """
        {
            "config": {
                "url": "http://test"
            },
            "name": "test"
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("test", result.Value);
    }

    [Fact]
    public void GetRequiredString_ArrayElement_ThrowsException()
    {
        // Arrange
        var json = """
        {
            "items": ["item1", "item2"],
            "name": "test"
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act & Assert
        // Trying to get an array as string throws InvalidOperationException from JsonElement.GetString()
        Assert.Throws<InvalidOperationException>(() => element.GetRequiredString("items"));
    }

    [Fact]
    public void GetRequiredStrings_LargeJSON_ExtractsCorrectly()
    {
        // Arrange
        var json = """
        {
            "id": "123",
            "name": "TestName",
            "description": "Some description",
            "config": {
                "nested": "value"
            },
            "sessionId": "s456",
            "command": "!analyze"
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredStrings("sessionId", "command");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("s456", result.Value.First);
        Assert.Equal("!analyze", result.Value.Second);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetRequiredString_EmptyJSON_ReturnsFailure()
    {
        // Arrange
        var json = """{}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("anyProperty");

        // Assert
        Assert.True(result.IsFailure);
        Assert.Contains("Missing anyProperty parameter", result.Error);
    }

    [Fact]
    public void GetRequiredString_CaseSensitivePropertyName()
    {
        // Arrange
        var json = """{"Name": "TestValue"}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("name"); // lowercase

        // Assert
        Assert.True(result.IsFailure); // Case-sensitive, should fail
        Assert.Contains("Missing name parameter", result.Error);
    }

    [Fact]
    public void GetRequiredString_SpecialCharactersInValue_Preserved()
    {
        // Arrange
        var json = """{"command": "!analyze -v; .echo \"Done\""}""";
        var element = JsonDocument.Parse(json).RootElement;

        // Act
        var result = element.GetRequiredString("command");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("!analyze -v; .echo \"Done\"", result.Value);
    }

    #endregion
}
