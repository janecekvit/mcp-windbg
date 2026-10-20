using System.Security.Claims;
using DumpAnalysisService.Fingerprinting;
using DumpAnalysisService.Providers;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using DumpAnalysisService.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Shared.Configuration;
using Shared.Models;

namespace DumpAnalysisService.Tests.Tools;

/// <summary>The caller of a tool call comes from <see cref="McpCallerContext"/>: refused without one, owner of what it starts.</summary>
public sealed class DebuggerToolsCallerTests : IDisposable
{
    private const string JobId = "job-1";
    private const string SessionId = "s1";

    private static readonly SecurityOptions Jwt = new() { Jwt = new JwtOptions { Authority = "https://issuer.test" } };

    private readonly Mock<IJobManagerService> _jobs = new();
    private readonly Mock<ISessionManagerService> _sessions = new();
    private readonly string _dumpFile = Path.Combine(Path.GetTempPath(), $"caller-{Guid.NewGuid():N}.dmp");

    public DebuggerToolsCallerTests()
    {
        File.WriteAllBytes(_dumpFile, [0]);
        _jobs.Setup(j => j.CreateJob(It.IsAny<JobOperationType>(), It.IsAny<string?>(), It.IsAny<string?>())).Returns(JobId);
        _jobs.Setup(j => j.GetJobStatus(JobId)).Returns(new JobStatus(
            JobId, null, JobOperationType.ExecuteCommand, JobState.Completed, JobPhase.Completed, 100, null,
            DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, "done", null));
    }

    public void Dispose() => File.Delete(_dumpFile);

    private DebuggerTools _Sut()
    {
        var symbols = new Mock<ISymbolConfigurationProvider>();
        symbols.Setup(s => s.GetConfiguration()).Returns(new SymbolsConfiguration(null, null, null));

        return new DebuggerTools(
            NullLogger<DebuggerTools>.Instance,
            _jobs.Object,
            _sessions.Object,
            new Mock<IDiagnosticsService>().Object,
            symbols.Object,
            new Mock<IDumpTriageService>().Object,
            new Mock<ICrashFingerprintService>().Object,
            Options.Create(Jwt),
            new AuditLog(NullLogger<AuditLog>.Instance));
    }

    private static ClaimsPrincipal _User(string oid) =>
        new(new ClaimsIdentity([new Claim("oid", oid), new Claim("roles", "Dumps.Analyze")], "Bearer"));

    private void _SessionOwnedBy(string owner)
    {
        string? sessionOwner = owner;
        _sessions.Setup(s => s.TryGetSessionOwner(SessionId, out sessionOwner)).Returns(true);
    }

    private Task _Call(string tool) => tool switch
    {
        "triage_dump" => _Sut().TriageDump(_dumpFile),
        "fingerprint_crash" => _Sut().FingerprintCrash(SessionId),
        "load_dump" => _Sut().LoadDump(_dumpFile),
        "execute_command" => _Sut().ExecuteCommand(SessionId, "kb"),
        "basic_analysis" => _Sut().BasicAnalysis(SessionId),
        "predefined_analysis" => _Sut().PredefinedAnalysis(SessionId, "threads"),
        "close_session" => _Sut().CloseSession(SessionId),
        "list_jobs" => _Sut().ListJobs(),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null),
    };

    private void _VerifyNoJobCreated() =>
        _jobs.Verify(j => j.CreateJob(It.IsAny<JobOperationType>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);

    [Theory]
    [InlineData("triage_dump")]
    [InlineData("fingerprint_crash")]
    [InlineData("load_dump")]
    [InlineData("execute_command")]
    [InlineData("basic_analysis")]
    [InlineData("predefined_analysis")]
    [InlineData("close_session")]
    [InlineData("list_jobs")]
    public async Task WithAuthentication_ACallWithoutACaller_IsRefusedBeforeAnyWork(string tool)
    {
        using var _ = McpCallerContext.Use(null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await _Call(tool));

        _VerifyNoJobCreated();
        _jobs.Verify(j => j.GetAllJobs(It.IsAny<JobState?>()), Times.Never);
    }

    [Theory]
    [InlineData("fingerprint_crash")]
    [InlineData("execute_command")]
    [InlineData("basic_analysis")]
    [InlineData("predefined_analysis")]
    [InlineData("close_session")]
    public async Task AnotherCallersSession_ReadsAsNotFound(string tool)
    {
        _SessionOwnedBy("bob");
        using var _ = McpCallerContext.Use(_User("alice"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () => await _Call(tool));

        Assert.Contains("not found", ex.Message);
        _VerifyNoJobCreated();
    }

    [Theory]
    [InlineData("load_dump", JobOperationType.LoadDump, null)]
    [InlineData("execute_command", JobOperationType.ExecuteCommand, SessionId)]
    [InlineData("basic_analysis", JobOperationType.BasicAnalysis, SessionId)]
    [InlineData("predefined_analysis", JobOperationType.PredefinedAnalysis, SessionId)]
    [InlineData("close_session", JobOperationType.CloseSession, SessionId)]
    public async Task TheCallersId_OwnsTheJob(string tool, JobOperationType operation, string? sessionId)
    {
        _SessionOwnedBy("alice");
        using var _ = McpCallerContext.Use(_User("alice"));

        await _Call(tool);

        _jobs.Verify(j => j.CreateJob(operation, sessionId, "alice"), Times.Once);
    }

    [Fact]
    public async Task ListJobs_ShowsOnlyTheCallersJobs()
    {
        string? alice = "alice", bob = "bob";
        _jobs.Setup(j => j.GetAllJobs(null)).Returns([
            _Job("job-alice"),
            _Job("job-bob"),
        ]);
        _jobs.Setup(j => j.TryGetJobOwner("job-alice", out alice)).Returns(true);
        _jobs.Setup(j => j.TryGetJobOwner("job-bob", out bob)).Returns(true);
        using var _ = McpCallerContext.Use(_User("alice"));

        var text = await _Sut().ListJobs();

        Assert.Contains("job-alice", text);
        Assert.DoesNotContain("job-bob", text);
    }

    private static JobStatus _Job(string id) => new(
        id, null, JobOperationType.TriageDump, JobState.Completed, JobPhase.Completed, 100, null,
        DateTime.UtcNow, null, null, null, null, null);
}
