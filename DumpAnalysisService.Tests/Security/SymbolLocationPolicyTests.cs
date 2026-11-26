using DumpAnalysisService.Security;
using Shared.Configuration;

namespace DumpAnalysisService.Tests.Security;

public class SymbolLocationPolicyTests
{
    private static readonly string[] None = [];
    private static readonly string[] Configured = [@"\\symbols.example\share", "http://symbols.example/store/"];

    [Theory]
    [InlineData(@"C:\symbols")]
    [InlineData(@"D:/pdbs/app")]
    [InlineData("https://symbols.example/download")]
    [InlineData("srv*https://symbols.example/download")]
    [InlineData(@"srv*C:\cache*https://symbols.example/download")]
    public void DefaultAllowed_LocalDirectoriesAndHttpsServers(string location)
    {
        SymbolLocationPolicy.EnsureAllowed(new SymbolsConfiguration(null, location, location), None);
    }

    [Theory]
    [InlineData(@"\\host.example\share")]
    [InlineData("//host.example/share")]
    [InlineData(@"\\?\C:\placeholder")]
    [InlineData("http://host.example/store")]
    [InlineData("file://host.example/share")]
    [InlineData("relative\\dir")]
    [InlineData(@"srv*\\host.example\share")]
    [InlineData(@"C:\ok;\\host.example\share")]
    [InlineData(@"C:\placeholder\..\..\other")]
    public void DefaultRefused_NetworkAndUnqualifiedLocations(string location)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SymbolLocationPolicy.EnsureAllowed(new SymbolsConfiguration(null, location, null), None));
        Assert.Contains("Debugger:AllowedSymbolLocations", ex.Message);
    }

    [Theory]
    [InlineData(@"\\symbols.example\share")]
    [InlineData(@"\\SYMBOLS.example\share\team")]
    [InlineData("srv*http://symbols.example/store/app")]
    public void ConfiguredPrefixes_AreAllowed(string location)
    {
        SymbolLocationPolicy.EnsureAllowed(new SymbolsConfiguration(null, null, location), Configured);
    }

    [Theory]
    [InlineData(@"\\symbols.example\shareother")]
    [InlineData(@"\\symbols.example\share\..\private")]
    [InlineData("http://symbols.example/storefront")]
    [InlineData(@"\\other.example\share")]
    public void OutsideConfiguredPrefixes_IsRefused(string location)
    {
        Assert.Throws<ArgumentException>(() =>
            SymbolLocationPolicy.EnsureAllowed(new SymbolsConfiguration(null, null, location), Configured));
    }

    [Fact]
    public void SymbolCache_IsCheckedToo()
    {
        Assert.Throws<ArgumentException>(() =>
            SymbolLocationPolicy.EnsureAllowed(new SymbolsConfiguration(@"\\host.example\cache", null, null), None));
    }
}
