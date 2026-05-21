using DumpAnalysisService.Providers;
using DumpAnalysisService.Services;
using DumpAnalysisService.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Configuration;

namespace DumpAnalysisService.Tests.Tools;

public class DebuggerToolsErrorHandlingTests
{
    private static DebuggerTools CreateSut(
        Mock<IJobManagerService>? jobs = null,
        Mock<ISessionManagerService>? sessions = null,
        Mock<IDiagnosticsService>? diagnostics = null,
        Mock<ISymbolConfigurationProvider>? symbols = null)
    {
        var symbolProvider = symbols ?? new Mock<ISymbolConfigurationProvider>();
        symbolProvider.Setup(s => s.GetConfiguration())
            .Returns(new SymbolsConfiguration(null, null, null));

        return new DebuggerTools(
            NullLogger<DebuggerTools>.Instance,
            (jobs ?? new Mock<IJobManagerService>()).Object,
            (sessions ?? new Mock<ISessionManagerService>()).Object,
            (diagnostics ?? new Mock<IDiagnosticsService>()).Object,
            symbolProvider.Object);
    }

    [Fact]
    public async Task LoadDump_FileMissing_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var nonExistent = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.dmp");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.LoadDump(nonExistent));

        Assert.Contains(nonExistent, ex.Message);
    }

    [Fact]
    public async Task LoadDump_BlankPath_ThrowsArgumentException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.LoadDump("   "));
    }

    [Fact]
    public void PredefinedAnalysis_AnalysisTypeParameter_HasAllowedValuesAttribute()
    {
        var method = typeof(DebuggerTools).GetMethod(nameof(DebuggerTools.PredefinedAnalysis))!;
        var parameter = method.GetParameters().First(p => p.Name == "analysis_type");

        var attribute = parameter.GetCustomAttributes(false)
            .FirstOrDefault(a => a.GetType().Name == "AllowedValuesAttribute");

        Assert.NotNull(attribute);
    }
}
