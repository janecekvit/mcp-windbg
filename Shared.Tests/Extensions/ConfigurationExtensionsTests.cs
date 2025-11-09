using Microsoft.Extensions.Configuration;
using Shared;
using Shared.Extensions;

namespace Shared.Tests.Extensions;

public class ConfigurationExtensionsTests : IDisposable
{
    private readonly List<string> _environmentVariablesToCleanup = new();

    public void Dispose()
    {
        // Clean up any environment variables set during tests
        foreach (var envVar in _environmentVariablesToCleanup)
        {
            Environment.SetEnvironmentVariable(envVar, null);
        }
    }

    private void SetTestEnvironmentVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (!_environmentVariablesToCleanup.Contains(name))
        {
            _environmentVariablesToCleanup.Add(name);
        }
    }

    #region GetValueWithEnvironmentFallback Tests

    [Fact]
    public void GetValueWithEnvironmentFallback_ConfigExists_ReturnsConfigValue()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "TestKey", "ConfigValue" }
            })
            .Build();

        SetTestEnvironmentVariable("TEST_ENV", "EnvValue");

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("TestKey", "TEST_ENV", "DefaultValue");

        // Assert
        Assert.Equal("ConfigValue", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_ConfigMissing_FallsBackToEnvVar()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_ENV", "EnvValue");

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("TestKey", "TEST_ENV", "DefaultValue");

        // Assert
        Assert.Equal("EnvValue", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_BothMissing_ReturnsDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_ENV", null);

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("TestKey", "TEST_ENV", "DefaultValue");

        // Assert
        Assert.Equal("DefaultValue", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_NoEnvVarSpecified_UsesConfigOrDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("TestKey", null, "DefaultValue");

        // Assert
        Assert.Equal("DefaultValue", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_IntegerType_ParsesCorrectly()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_PORT", "8080");

        // Act
        var result = config.GetValueWithEnvironmentFallback<int>("Port", "TEST_PORT", 7997);

        // Assert
        Assert.Equal(8080, result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_BooleanType_ParsesCorrectly()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_ENABLED", "true");

        // Act
        var result = config.GetValueWithEnvironmentFallback<bool>("Enabled", "TEST_ENABLED", false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_InvalidEnvVarValue_ReturnsDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_PORT", "not-a-number");

        // Act
        var result = config.GetValueWithEnvironmentFallback<int>("Port", "TEST_PORT", 7997);

        // Assert
        Assert.Equal(7997, result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_NullableType_HandlesNull()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_VALUE", null);

        // Act
        var result = config.GetValueWithEnvironmentFallback<string?>("Value", "TEST_VALUE", null);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("100", 100)]
    [InlineData("0", 0)]
    public void GetValueWithEnvironmentFallback_IntegerValues_ParseCorrectly(string envValue, int expected)
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_INT", envValue);

        // Act
        var result = config.GetValueWithEnvironmentFallback<int>("IntValue", "TEST_INT", -1);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region GetDebuggerConfiguration Tests

    [Fact]
    public void GetDebuggerConfiguration_AllConfigExists_ReturnsConfigValues()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Debugger:DefaultSymbolCache", "C:\\ConfigSymbols" },
                { "Debugger:DefaultSymbolPathExtra", "C:\\ConfigExtra" },
                { "Debugger:DefaultSymbolServers", "http://config.symbols" }
            })
            .Build();

        // Act
        var result = config.GetDebuggerConfiguration();

        // Assert
        Assert.Equal("C:\\ConfigSymbols", result.DefaultSymbolCache);
        Assert.Equal("C:\\ConfigExtra", result.DefaultSymbolPathExtra);
        Assert.Equal("http://config.symbols", result.DefaultSymbolServers);
    }

    [Fact]
    public void GetDebuggerConfiguration_AllConfigMissing_ReturnsNulls()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();

        // Act
        var result = config.GetDebuggerConfiguration();

        // Assert
        Assert.Null(result.DefaultSymbolCache);
        Assert.Null(result.DefaultSymbolPathExtra);
        Assert.Null(result.DefaultSymbolServers);
    }

    [Fact]
    public void GetDebuggerConfiguration_PartialConfig_ReturnsMixedValues()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Debugger:DefaultSymbolCache", "C:\\Symbols" }
                // Other values missing
            })
            .Build();

        // Act
        var result = config.GetDebuggerConfiguration();

        // Assert
        Assert.Equal("C:\\Symbols", result.DefaultSymbolCache);
        Assert.Null(result.DefaultSymbolPathExtra);
        Assert.Null(result.DefaultSymbolServers);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void GetValueWithEnvironmentFallback_EmptyString_TreatedAsDefault()
    {
        // Arrange
        var config = new ConfigurationBuilder().Build();
        SetTestEnvironmentVariable("TEST_EMPTY", "");

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("Empty", "TEST_EMPTY", "DefaultValue");

        // Assert
        Assert.Equal("DefaultValue", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_WhitespaceString_TreatedAsValue()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Whitespace", "   " }
            })
            .Build();

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("Whitespace", null, "DefaultValue");

        // Assert
        Assert.Equal("   ", result);
    }

    [Fact]
    public void GetValueWithEnvironmentFallback_ConfigPriorityOverEnv()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Priority", "ConfigPriority" }
            })
            .Build();

        SetTestEnvironmentVariable("TEST_PRIORITY", "EnvPriority");

        // Act
        var result = config.GetValueWithEnvironmentFallback<string>("Priority", "TEST_PRIORITY", "DefaultPriority");

        // Assert
        Assert.Equal("ConfigPriority", result);
    }

    #endregion
}
