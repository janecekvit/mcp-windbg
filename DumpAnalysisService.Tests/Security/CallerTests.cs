using System.Security.Claims;
using DumpAnalysisService.Security;
using DumpAnalysisService.Services;
using Moq;

namespace DumpAnalysisService.Tests.Security;

public class CallerTests
{
    private static readonly SecurityOptions Jwt = new()
    {
        Jwt = new JwtOptions { Authority = "https://issuer.test", Audiences = ["api://x"], RoleClaimTypes = ["roles", "scp", "scope"] },
    };

    private static ClaimsPrincipal _Token(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "Bearer"));

    [Fact]
    public void WithoutAuthentication_EveryoneIsUnrestricted()
    {
        var caller = Caller.From(_Token(("oid", "alice")), new SecurityOptions());

        Assert.False(caller.IsRestricted);
        Assert.Equal(DumpPermission.Admin, caller.Permission);
    }

    [Fact]
    public void WithAuthentication_AMissingPrincipalIsUnidentified_NotUnrestricted()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.Same(Caller.Unidentified, Caller.From(null, Jwt));
        Assert.Same(Caller.Unidentified, Caller.From(anonymous, new SecurityOptions { ApiKey = "k" }));
        Assert.True(Caller.Unidentified.IsRestricted);
        Assert.Equal(DumpPermission.None, Caller.Unidentified.Permission);
        Assert.False(Caller.Unidentified.Owns(null));
    }

    [Fact]
    public void ApiKey_IsAnUnrestrictedAdministrator()
    {
        var caller = Caller.From(_Token((Caller.AuthMethodClaim, Caller.ApiKeyMethod)), new SecurityOptions { ApiKey = "k" });

        Assert.False(caller.IsRestricted);
        Assert.Equal(DumpPermission.Admin, caller.Permission);
    }

    [Theory]
    [InlineData("roles", "Dumps.Read", DumpPermission.Read)]
    [InlineData("roles", "Dumps.Analyze", DumpPermission.Analyze)]
    [InlineData("scp", "openid Dumps.Analyze profile", DumpPermission.Analyze)]
    [InlineData("scope", "Dumps.Read", DumpPermission.Read)]
    [InlineData("roles", "Something.Else", DumpPermission.None)]
    public void Roles_ComeFromRolesOrScopeClaims(string type, string value, DumpPermission expected)
    {
        var caller = Caller.From(_Token(("oid", "alice"), (type, value)), Jwt);

        Assert.Equal(expected, caller.Permission);
        Assert.Equal("alice", caller.Id);
    }

    [Fact]
    public void HighestRoleWins_AndAnAdministratorIsUnrestricted()
    {
        var caller = Caller.From(_Token(("oid", "root"), ("roles", "Dumps.Read"), ("roles", "Dumps.Admin")), Jwt);

        Assert.Equal(DumpPermission.Admin, caller.Permission);
        Assert.False(caller.IsRestricted);
    }

    [Fact]
    public void SubjectIsTheIdWithoutOid_AndNameComesFromPreferredUsername()
    {
        var caller = Caller.From(_Token(("sub", "user-7"), ("preferred_username", "u7@example.test"), ("roles", "Dumps.Read")), Jwt);

        Assert.Equal("user-7", caller.Id);
        Assert.Equal("u7@example.test", caller.Name);
    }

    [Fact]
    public void ByDefault_OnlyAppRolesCount_NotConsentedScopes()
    {
        var security = new SecurityOptions { Jwt = new JwtOptions { Authority = "https://issuer.test" } };

        Assert.Equal(DumpPermission.None, Caller.From(_Token(("oid", "a"), ("scp", "Dumps.Admin")), security).Permission);
        Assert.Equal(DumpPermission.Read, Caller.From(_Token(("oid", "a"), ("roles", "Dumps.Read")), security).Permission);
    }

    [Fact]
    public void ConfiguredRoleNamesAreUsed()
    {
        var security = new SecurityOptions { Jwt = new JwtOptions { Authority = "https://issuer.test", AnalystRole = "debugger" } };

        Assert.Equal(DumpPermission.Analyze, Caller.From(_Token(("oid", "a"), ("roles", "debugger")), security).Permission);
        Assert.Equal(DumpPermission.None, Caller.From(_Token(("oid", "a"), ("roles", "Dumps.Analyze")), security).Permission);
    }

    [Fact]
    public void RestrictedCaller_OwnsOnlyItsOwn()
    {
        var alice = new Caller("alice", "alice", DumpPermission.Analyze);

        Assert.True(alice.Owns("alice"));
        Assert.False(alice.Owns("bob"));
        Assert.False(alice.Owns(null));
        Assert.True(Caller.Anonymous.Owns("bob"));
    }

    [Fact]
    public void EnsureSessionAccess_HidesAnotherCallersSession()
    {
        var sessions = new Mock<ISessionManagerService>();
        string? owner = "bob";
        sessions.Setup(s => s.TryGetSessionOwner("s1", out owner)).Returns(true);
        var alice = new Caller("alice", "alice", DumpPermission.Analyze);

        var ex = Assert.Throws<ArgumentException>(() => sessions.Object.EnsureSessionAccess(alice, "s1"));
        Assert.Contains("not found", ex.Message);
        sessions.Object.EnsureSessionAccess(new Caller("bob", "bob", DumpPermission.Analyze), "s1");
        sessions.Object.EnsureSessionAccess(Caller.Anonymous, "s1");
    }

    [Fact]
    public void CanAccessJob_NeedsAnExistingJobOwnedByTheCaller()
    {
        var jobs = new Mock<IJobManagerService>();
        string? owner = "alice";
        jobs.Setup(j => j.TryGetJobOwner("j1", out owner)).Returns(true);
        string? none = null;
        jobs.Setup(j => j.TryGetJobOwner("missing", out none)).Returns(false);

        Assert.True(jobs.Object.CanAccessJob(new Caller("alice", "alice", DumpPermission.Read), "j1"));
        Assert.False(jobs.Object.CanAccessJob(new Caller("bob", "bob", DumpPermission.Read), "j1"));
        Assert.False(jobs.Object.CanAccessJob(Caller.Anonymous, "missing"));
    }
}
