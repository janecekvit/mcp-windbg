using DumpAnalysisService.Factories;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Tests.Security;

public sealed class SessionManagerSecurityTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _dumpPath;
    private readonly Mock<ICdbSessionFactory> _factory = new();
    private readonly Mock<IJobManagerService> _jobManager = new();

    public SessionManagerSecurityTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "dumpsec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _dumpPath = Path.Combine(_tempDirectory, "test.dmp");
        File.WriteAllBytes(_dumpPath, [0x4D, 0x44, 0x4D, 0x50]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup of the temp folder
        }
    }

    private SessionManagerService _CreateManager(SecurityOptions options) =>
        new(NullLogger<SessionManagerService>.Instance, _factory.Object, _jobManager.Object, Options.Create(options));

    private Mock<ICdbSessionService> _SetupSession(bool loadSucceeds = true)
    {
        var session = new Mock<ICdbSessionService>();
        session.Setup(s => s.IsActive).Returns(true);
        var load = session.Setup(s => s.LoadDumpAsync(It.IsAny<string>(), It.IsAny<IProgress<ProgressUpdate>>(), It.IsAny<CancellationToken>()));
        if (loadSucceeds)
            load.Returns(Task.CompletedTask);
        else
            load.ThrowsAsync(new TimeoutException("symbol loading timed out"));

        _factory.Setup(f => f.CreateSession(It.IsAny<string>(), It.IsAny<SymbolsConfiguration?>())).Returns(session.Object);
        return session;
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_LoadFails_DisposesAndForgetsSession()
    {
        var session = _SetupSession(loadSucceeds: false);
        using var manager = _CreateManager(new SecurityOptions { MaxConcurrentSessions = 1 });

        await Assert.ThrowsAsync<TimeoutException>(() => manager.CreateSessionWithDumpAsync("job-1", _dumpPath));

        session.Verify(s => s.Dispose(), Times.Once);

        // The failed session must not count against the limit
        _SetupSession();
        var sessionId = await manager.CreateSessionWithDumpAsync("job-2", _dumpPath);
        Assert.False(string.IsNullOrEmpty(sessionId));
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_LimitReached_Throws()
    {
        _SetupSession();
        using var manager = _CreateManager(new SecurityOptions { MaxConcurrentSessions = 1 });
        await manager.CreateSessionWithDumpAsync("job-1", _dumpPath);

        var second = _SetupSession();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.CreateSessionWithDumpAsync("job-2", _dumpPath));

        Assert.Contains("Maximum number of concurrent sessions", ex.Message);
        second.Verify(s => s.Dispose(), Times.Once);
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_OutsideAllowedDirectories_Throws()
    {
        _SetupSession();
        var otherDirectory = Path.Combine(Path.GetTempPath(), "elsewhere-" + Guid.NewGuid().ToString("N"));
        using var manager = _CreateManager(new SecurityOptions { AllowedDumpDirectories = [otherDirectory] });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CreateSessionWithDumpAsync("job-1", _dumpPath));
        _factory.Verify(f => f.CreateSession(It.IsAny<string>(), It.IsAny<SymbolsConfiguration?>()), Times.Never);
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_InsideAllowedDirectory_Succeeds()
    {
        _SetupSession();
        using var manager = _CreateManager(new SecurityOptions { AllowedDumpDirectories = [_tempDirectory] });

        var sessionId = await manager.CreateSessionWithDumpAsync("job-1", _dumpPath);

        Assert.False(string.IsNullOrEmpty(sessionId));
    }

    [Fact]
    public async Task CreateSessionWithDumpAsync_SiblingDirectoryWithSamePrefix_Throws()
    {
        _SetupSession();
        // "<temp>/dumpsec-x" must not allow "<temp>/dumpsec-x-other"
        var sibling = _tempDirectory + "-other";
        Directory.CreateDirectory(sibling);
        var siblingDump = Path.Combine(sibling, "test.dmp");
        File.WriteAllBytes(siblingDump, [0]);
        using var manager = _CreateManager(new SecurityOptions { AllowedDumpDirectories = [_tempDirectory] });

        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.CreateSessionWithDumpAsync("job-1", siblingDump));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteCommandAsync_BlockedCommand_ThrowsBeforeReachingSession()
    {
        var session = _SetupSession();
        using var manager = _CreateManager(new SecurityOptions());
        var sessionId = await manager.CreateSessionWithDumpAsync("job-1", _dumpPath);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.ExecuteCommandAsync("job-2", sessionId, ".load ext.dll"));

        session.Verify(s => s.ExecuteCommandAsync(It.IsAny<string>(), It.IsAny<IProgress<ProgressUpdate>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
