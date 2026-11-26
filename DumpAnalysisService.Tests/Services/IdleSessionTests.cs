using DumpAnalysisService.Factories;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Tests.Services;

public sealed class IdleSessionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    private readonly ManualTimeProvider _clock = new();
    private readonly Mock<ICdbSessionService> _session = new();
    private readonly Mock<IJobManagerService> _jobs = new();
    private readonly SessionManagerService _sessions;
    private readonly string _dumpPath = Path.GetTempFileName();

    public IdleSessionTests()
    {
        _session.Setup(s => s.IsActive).Returns(true);
        _session.Setup(s => s.LoadDumpAsync(It.IsAny<string>(), It.IsAny<IProgress<ProgressUpdate>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var factory = new Mock<ICdbSessionFactory>();
        factory.Setup(f => f.CreateSession(It.IsAny<string>(), It.IsAny<SymbolsConfiguration>())).Returns(_session.Object);

        _sessions = new SessionManagerService(
            NullLogger<SessionManagerService>.Instance,
            factory.Object,
            _jobs.Object,
            Options.Create(new SecurityOptions()),
            _clock);
    }

    public void Dispose()
    {
        _sessions.Dispose();
        File.Delete(_dumpPath);
    }

    private Task<string> _LoadAsync() => _sessions.CreateSessionWithDumpAsync("job", _dumpPath);

    [Fact]
    public async Task CloseIdleSessions_ClosesSessionIdleLongerThanTimeout()
    {
        var sessionId = await _LoadAsync();

        _clock.Advance(Timeout);
        var closed = await _sessions.CloseIdleSessionsAsync(Timeout);

        Assert.Equal([sessionId], closed);
        Assert.False(_sessions.TryGetSessionOwner(sessionId, out _));
        _session.Verify(s => s.CancelAsync(), Times.Once);
    }

    [Fact]
    public async Task CloseIdleSessions_KeepsSessionWithRecentActivity()
    {
        var sessionId = await _LoadAsync();
        _session.Setup(s => s.ExecuteCommandAsync("kb", It.IsAny<IProgress<ProgressUpdate>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("output");

        _clock.Advance(Timeout - TimeSpan.FromMinutes(1));
        await _sessions.ExecuteCommandAsync("job", sessionId, "kb");
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Empty(await _sessions.CloseIdleSessionsAsync(Timeout));
        Assert.True(_sessions.TryGetSessionOwner(sessionId, out _));
    }

    [Fact]
    public async Task CloseIdleSessions_NeverClosesSessionWhileCommandRuns()
    {
        var sessionId = await _LoadAsync();
        var release = new TaskCompletionSource<string>();
        _session.Setup(s => s.ExecuteCommandAsync("kb", It.IsAny<IProgress<ProgressUpdate>>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);

        var running = _sessions.ExecuteCommandAsync("job", sessionId, "kb");
        _clock.Advance(Timeout * 3);

        Assert.Empty(await _sessions.CloseIdleSessionsAsync(Timeout));

        release.SetResult("output");
        Assert.Equal("output", await running);

        // Idle again only from the end of the command.
        Assert.Empty(await _sessions.CloseIdleSessionsAsync(Timeout));
        _clock.Advance(Timeout);
        Assert.Equal([sessionId], await _sessions.CloseIdleSessionsAsync(Timeout));
    }

    [Fact]
    public async Task CloseIdleSessions_ZeroTimeout_ClosesNothing()
    {
        await _LoadAsync();
        _clock.Advance(TimeSpan.FromDays(1));

        Assert.Empty(await _sessions.CloseIdleSessionsAsync(TimeSpan.Zero));
    }

    [Fact]
    public async Task Sweeper_ClosesIdleSessionsWithTheConfiguredTimeout()
    {
        var sessionId = await _LoadAsync();
        var sweeper = new IdleSessionSweeper(
            _sessions,
            new DebuggerConfiguration { SessionIdleTimeout = Timeout },
            new AuditLog(NullLogger<AuditLog>.Instance),
            NullLogger<IdleSessionSweeper>.Instance,
            _clock);

        await sweeper.SweepAsync();
        Assert.True(_sessions.TryGetSessionOwner(sessionId, out _));

        _clock.Advance(Timeout);
        await sweeper.SweepAsync();
        Assert.False(_sessions.TryGetSessionOwner(sessionId, out _));
    }

    [Theory]
    [InlineData(30, 60)]
    [InlineData(2, 30)]
    [InlineData(0.01, 1)]
    public void SweepInterval_IsAQuarterOfTheTimeoutAtMostOneMinute(double timeoutMinutes, double expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), IdleSessionSweeper.SweepInterval(TimeSpan.FromMinutes(timeoutMinutes)));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
