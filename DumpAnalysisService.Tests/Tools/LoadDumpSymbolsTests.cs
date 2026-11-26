using System.Text.Json;
using DumpAnalysisService.Fingerprinting;
using DumpAnalysisService.Providers;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using DumpAnalysisService.Tests.Minidump;
using DumpAnalysisService.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Moq;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Tests.Tools;

/// <summary>load_dump and set_symbols: symbol argument priority, location limits and the structured result.</summary>
public sealed class LoadDumpSymbolsTests : IDisposable
{
    private const string JobId = "job-1";
    private const string SessionId = "abcd1234";

    private readonly Mock<IJobManagerService> _jobs = new();
    private readonly Mock<ISessionManagerService> _sessions = new();
    private readonly Mock<ISymbolConfigurationProvider> _headers = new();
    private readonly string _dumpFile = Path.Combine(Path.GetTempPath(), $"symbols-{Guid.NewGuid():N}.dmp");
    private SymbolsConfiguration? _loadedWith;

    public LoadDumpSymbolsTests()
    {
        File.WriteAllBytes(_dumpFile, new MinidumpBuilder { Architecture = 9, Flags = 0x2 }.Build());
        _jobs.Setup(j => j.CreateJob(It.IsAny<JobOperationType>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(JobId);
        _jobs.Setup(j => j.GetJobStatus(JobId)).Returns(new JobStatus(
            JobId, null, JobOperationType.LoadDump, JobState.Completed, JobPhase.Completed, 100, null,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, SessionId, null));
        _sessions.Setup(s => s.CreateSessionWithDumpAsync(JobId, _dumpFile, It.IsAny<SymbolsConfiguration?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, SymbolsConfiguration?, string?, CancellationToken>((_, _, symbols, _, _) => _loadedWith = symbols)
            .ReturnsAsync(SessionId);
        _headers.Setup(h => h.GetConfiguration()).Returns(new SymbolsConfiguration(@"C:\header-cache", @"C:\header-extra", "https://header.example/symbols"));
    }

    public void Dispose() => File.Delete(_dumpFile);

    private DebuggerTools _Sut(params string[] allowedLocations) => new(
        NullLogger<DebuggerTools>.Instance,
        _jobs.Object,
        _sessions.Object,
        new Mock<IDiagnosticsService>().Object,
        _headers.Object,
        new Mock<IDumpTriageService>().Object,
        new Mock<ICrashFingerprintService>().Object,
        Options.Create(new SecurityOptions()),
        new AuditLog(NullLogger<AuditLog>.Instance),
        new DebuggerConfiguration { AllowedSymbolLocations = [.. allowedLocations] });

    private async Task _WaitForLoadAsync()
    {
        for (var i = 0; i < 50 && _loadedWith is null; i++)
            await Task.Delay(20);
    }

    [Fact]
    public async Task LoadDump_ToolArguments_OverrideHeaders()
    {
        await _Sut().LoadDump(_dumpFile, symbol_path_extra: @"C:\tool-extra");
        await _WaitForLoadAsync();

        Assert.Equal(new SymbolsConfiguration(@"C:\header-cache", @"C:\tool-extra", "https://header.example/symbols"), _loadedWith);
    }

    [Fact]
    public async Task LoadDump_WithoutArguments_UsesHeaders()
    {
        await _Sut().LoadDump(_dumpFile);
        await _WaitForLoadAsync();

        Assert.Equal(_headers.Object.GetConfiguration(), _loadedWith);
    }

    [Fact]
    public async Task LoadDump_NetworkLocationFromArguments_IsRefusedBeforeAnyWork()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _Sut().LoadDump(_dumpFile, symbol_path_extra: @"\\host.example\share"));

        _jobs.Verify(j => j.CreateJob(It.IsAny<JobOperationType>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LoadDump_NetworkLocationUnderAConfiguredPrefix_IsAccepted()
    {
        await _Sut(@"\\host.example\share").LoadDump(_dumpFile, symbol_servers: @"\\host.example\share\team");
        await _WaitForLoadAsync();

        Assert.Equal(@"\\host.example\share\team", _loadedWith!.SymbolServers);
    }

    [Fact]
    public async Task LoadDump_InvalidArgument_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _Sut().LoadDump(_dumpFile, symbol_cache: @"C:\a;C:\b"));
    }

    [Fact]
    public async Task LoadDump_TextIsTheBareSessionId_StructuredContentDescribesTheDump()
    {
        var result = await _Sut().LoadDump(_dumpFile);

        Assert.Equal(SessionId, Assert.Single(result.Content.OfType<TextContentBlock>()).Text);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(SessionId, structured.GetProperty("sessionId").GetString());
        Assert.Equal(_dumpFile, structured.GetProperty("dumpPath").GetString());
        Assert.Equal("user-full", structured.GetProperty("dumpKind").GetString());
        Assert.Equal("x64", structured.GetProperty("architecture").GetString());
    }

    [Fact]
    public async Task SetSymbols_NetworkLocationFromArguments_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _Sut().SetSymbols(SessionId, symbol_servers: "http://host.example/store"));

        _sessions.Verify(s => s.SetSymbolsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SymbolsConfiguration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetSymbols_PassesTheArgumentsToTheSession()
    {
        SymbolsConfiguration? applied = null;
        _sessions.Setup(s => s.SetSymbolsAsync(JobId, SessionId, It.IsAny<SymbolsConfiguration>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, SymbolsConfiguration, CancellationToken>((_, _, symbols, _) => applied = symbols)
            .ReturnsAsync("reloaded");

        await _Sut().SetSymbols(SessionId, symbol_path_extra: @"C:\pdbs", symbol_servers: "https://symbols.example/store");
        for (var i = 0; i < 50 && applied is null; i++)
            await Task.Delay(20);

        Assert.Equal(new SymbolsConfiguration(null, @"C:\pdbs", "https://symbols.example/store"), applied);
    }

    [Fact]
    public void DumpDescription_UnreadableFile_ReturnsNulls()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.Equal((null, null), DumpDescription.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DumpDescription_MiniDump_IsUserMini()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new MinidumpBuilder { Flags = 0, Architecture = 0 }.Build());
            Assert.Equal(("user-mini", "x86"), DumpDescription.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
