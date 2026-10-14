using DumpAnalysisService.Security;
using Shared.Configuration;

namespace DumpAnalysisService.Tests.Security;

public class SymbolInputValidatorTests
{
    [Fact]
    public void Validate_Null_DoesNotThrow()
    {
        SymbolInputValidator.Validate(null);
    }

    [Fact]
    public void Validate_TypicalValues_DoesNotThrow()
    {
        SymbolInputValidator.Validate(new SymbolsConfiguration(
            SymbolCache: @"C:\symbols",
            SymbolPathExtra: @"C:\MySymbols;\\server\share\symbols",
            SymbolServers: "https://artifacts.dev.azure.com/org/_apis/Symbol/symsrv;srv*https://msdl.microsoft.com/download/symbols"));
    }

    [Theory]
    [InlineData("C:\\sym\"bols", null, null)]
    [InlineData(null, "C:\\a\"b", null)]
    [InlineData(null, null, "https://x\"y")]
    [InlineData("C:\\symbols\n", null, null)]
    [InlineData(null, "C:\\a\rC:\\b", null)]
    public void Validate_QuotesOrControlCharacters_Throws(string? cache, string? extra, string? servers)
    {
        Assert.Throws<ArgumentException>(() =>
            SymbolInputValidator.Validate(new SymbolsConfiguration(cache, extra, servers)));
    }

    [Theory]
    [InlineData(@"C:\a;C:\b")]
    [InlineData(@"srv*C:\a")]
    public void Validate_SymbolCacheWithPathSeparators_Throws(string cache)
    {
        Assert.Throws<ArgumentException>(() =>
            SymbolInputValidator.Validate(new SymbolsConfiguration(SymbolCache: cache)));
    }

    [Fact]
    public void Validate_TooLong_Throws()
    {
        var value = new string('a', SymbolInputValidator.MaxLength + 1);
        Assert.Throws<ArgumentException>(() =>
            SymbolInputValidator.Validate(new SymbolsConfiguration(SymbolServers: value)));
    }
}
