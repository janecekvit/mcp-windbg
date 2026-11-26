using DumpAnalysisService.Tools;
using ModelContextProtocol;

namespace DumpAnalysisService.Tests.Tools;

public class ToolErrorsTests
{
    public static TheoryData<Exception> ClientErrors =>
    [
        new ArgumentException("refused"),
        new FileNotFoundException("refused"),
        new UnauthorizedAccessException("refused"),
        new InvalidDataException("refused"),
        new InvalidOperationException("refused"),
        new TimeoutException("refused"),
    ];

    [Theory]
    [MemberData(nameof(ClientErrors))]
    public void ClientErrors_KeepTheirMessage(Exception ex)
    {
        Assert.Equal("refused", ToolErrors.ClientMessage(ex));
        var mcp = Assert.IsType<McpException>(ToolErrors.ToMcpException(ex));
        Assert.Equal("refused", mcp.Message);
    }

    [Fact]
    public void UnexpectedErrors_StayGeneric()
    {
        var ex = new NullReferenceException("internal detail");

        Assert.Equal(ToolErrors.UnexpectedError, ToolErrors.ClientMessage(ex));
        Assert.Null(ToolErrors.ToMcpException(ex));
    }

    [Fact]
    public void Cancellation_IsNotConverted()
    {
        Assert.Null(ToolErrors.ToMcpException(new OperationCanceledException()));
    }
}
