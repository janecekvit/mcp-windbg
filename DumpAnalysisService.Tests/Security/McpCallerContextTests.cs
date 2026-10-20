using System.Security.Claims;
using DumpAnalysisService.Security;

namespace DumpAnalysisService.Tests.Security;

public class McpCallerContextTests
{
    private static ClaimsPrincipal _User(string oid) => new(new ClaimsIdentity([new Claim("oid", oid)], "Bearer"));

    [Fact]
    public void Use_SetsTheCaller_AndDisposingRestoresThePreviousOne()
    {
        var alice = _User("alice");
        var bob = _User("bob");

        using (McpCallerContext.Use(alice))
        {
            Assert.Same(alice, McpCallerContext.User);
            using (McpCallerContext.Use(bob))
                Assert.Same(bob, McpCallerContext.User);
            Assert.Same(alice, McpCallerContext.User);
        }

        Assert.Null(McpCallerContext.User);
    }

    [Fact]
    public async Task TheCaller_FlowsIntoWorkTheToolStarts_ButNotIntoOtherCalls()
    {
        var alice = _User("alice");
        Task<ClaimsPrincipal?> started;
        using (McpCallerContext.Use(alice))
            started = Task.Run(async () => { await Task.Yield(); return McpCallerContext.User; });

        Assert.Same(alice, await started);
        Assert.Null(await Task.Run(() => McpCallerContext.User));
    }
}
