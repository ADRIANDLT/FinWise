using System.Text.Json;
using FluentAssertions;
using FinWise.McpServer.Infrastructure.McpSession.Redis;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace FinWise.McpServer.UnitTests;

[Trait("Category", "Unit")]
public class RedisSessionMigrationHandlerTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(60);
    private readonly RedisSessionMigrationHandler _handler;
    private readonly DefaultHttpContext _httpContext = new();

    public RedisSessionMigrationHandlerTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _handler = new RedisSessionMigrationHandler(_redisMock.Object, _ttl);
    }

    [Theory]
    [InlineData("mcp-abc123", "mcpinit:mcp-abc123")]
    [InlineData("session-xyz", "mcpinit:session-xyz")]
    public void GetKey_FormatsWithMcpInitPrefix(string sessionId, string expected)
    {
        RedisSessionMigrationHandler.GetKey(sessionId).Should().Be(expected);
    }

    [Fact]
    public async Task OnSessionInitializedAsync_StoresSerializedParamsWithTtl()
    {
        var initParams = new InitializeRequestParams
        {
            ProtocolVersion = "2025-03-26",
            ClientInfo = new Implementation { Name = "test-client", Version = "1.0" },
            Capabilities = new ClientCapabilities()
        };

        await _handler.OnSessionInitializedAsync(
            _httpContext, "mcp-session-1", initParams, CancellationToken.None);

        var invocation = _dbMock.Invocations
            .SingleOrDefault(i => i.Method.Name == "StringSetAsync");
        invocation.Should().NotBeNull("StringSetAsync should have been called");
        invocation!.Arguments[0].ToString().Should().Be("mcpinit:mcp-session-1");

        // Verify the stored value is valid JSON that deserializes back
        var storedJson = invocation.Arguments[1].ToString()!;
        var deserialized = JsonSerializer.Deserialize<InitializeRequestParams>(storedJson);
        deserialized.Should().NotBeNull();
        deserialized!.ProtocolVersion.Should().Be("2025-03-26");
        deserialized.ClientInfo!.Name.Should().Be("test-client");

        // Verify TTL was passed
        invocation.Arguments[2].ToString().Should().Be($"EX {(int)_ttl.TotalSeconds}");
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_ReturnsParams_ForKnownSession()
    {
        var initParams = new InitializeRequestParams
        {
            ProtocolVersion = "2025-03-26",
            ClientInfo = new Implementation { Name = "test-client", Version = "1.0" },
            Capabilities = new ClientCapabilities()
        };
        var json = JsonSerializer.Serialize(initParams);

        _dbMock.Setup(db => db.StringGetAsync(
                (RedisKey)"mcpinit:mcp-known", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        var result = await _handler.AllowSessionMigrationAsync(
            _httpContext, "mcp-known", CancellationToken.None);

        result.Should().NotBeNull();
        result!.ProtocolVersion.Should().Be("2025-03-26");
        result.ClientInfo!.Name.Should().Be("test-client");
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_ReturnsNull_ForUnknownSession()
    {
        _dbMock.Setup(db => db.StringGetAsync(
                (RedisKey)"mcpinit:mcp-unknown", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _handler.AllowSessionMigrationAsync(
            _httpContext, "mcp-unknown", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_RefreshesTtl_OnSuccessfulRead()
    {
        var json = JsonSerializer.Serialize(new InitializeRequestParams
        {
            ProtocolVersion = "2025-03-26",
            ClientInfo = new Implementation { Name = "test-client", Version = "1.0" },
            Capabilities = new ClientCapabilities()
        });

        _dbMock.Setup(db => db.StringGetAsync(
                (RedisKey)"mcpinit:mcp-refresh", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        await _handler.AllowSessionMigrationAsync(
            _httpContext, "mcp-refresh", CancellationToken.None);

        // Verify KeyExpireAsync was called to refresh TTL (sliding window)
        _dbMock.Verify(db => db.KeyExpireAsync(
            (RedisKey)"mcpinit:mcp-refresh",
            _ttl,
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task AllowSessionMigrationAsync_DoesNotRefreshTtl_ForUnknownSession()
    {
        _dbMock.Setup(db => db.StringGetAsync(
                (RedisKey)"mcpinit:mcp-missing", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        await _handler.AllowSessionMigrationAsync(
            _httpContext, "mcp-missing", CancellationToken.None);

        // KeyExpireAsync should NOT be called when session is not found
        _dbMock.Verify(db => db.KeyExpireAsync(
            It.IsAny<RedisKey>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<ExpireWhen>(),
            It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public void Constructor_ThrowsOnNullRedis()
    {
        var act = () => new RedisSessionMigrationHandler(null!, TimeSpan.FromMinutes(60));
        act.Should().Throw<ArgumentNullException>().WithParameterName("redis");
    }
}
