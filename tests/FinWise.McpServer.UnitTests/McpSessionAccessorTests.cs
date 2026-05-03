using FinWise.McpServer.Infrastructure.McpSession;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FinWise.McpServer.UnitTests;

[Trait("Category", "Unit")]
public class McpSessionAccessorTests
{
    [Fact]
    public void GetSessionId_ReturnsHeaderValue_WhenPresent()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["MCP-Session-Id"] = "mcp-abc123";

        var result = McpSessionAccessor.GetSessionId(httpContext);

        result.Should().Be("mcp-abc123");
    }

    [Fact]
    public void GetSessionId_ThrowsInvalidOperationException_WhenHeaderMissing()
    {
        var httpContext = new DefaultHttpContext();

        var act = () => McpSessionAccessor.GetSessionId(httpContext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MCP-Session-Id*");
    }

    [Fact]
    public void GetSessionId_ThrowsInvalidOperationException_WhenHeaderEmpty()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["MCP-Session-Id"] = "";

        var act = () => McpSessionAccessor.GetSessionId(httpContext);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MCP-Session-Id*");
    }
}
