using System.Text.Json;
using FluentAssertions;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.InMemory;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class InMemoryAgentSessionStoreTests
{
    private readonly InMemoryAgentSessionStore _store = new();

    private static AgentSessionData CreateSessionData(
        string agentSessionId,
        string userId,
        DateTime? lastMessageAt = null) => new()
    {
        AgentSessionId = agentSessionId,
        UserId = userId,
        SerializedSession = JsonDocument.Parse("{}").RootElement,
        MessageCount = 1,
        LastMessageAt = lastMessageAt ?? DateTime.UtcNow
    };

    [Fact]
    public async Task GetSessionDataAsync_Should_ReturnNull_WhenKeyDoesNotExist()
    {
        var result = await _store.GetSessionDataAsync("nonexistent-conv");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSessionDataAsync_And_GetSessionDataAsync_Should_RoundTrip()
    {
        var session = CreateSessionData("conv-1", "user@test.com");

        await _store.SetSessionDataAsync("conv-1", session);
        var result = await _store.GetSessionDataAsync("conv-1");

        result.Should().NotBeNull();
        result!.AgentSessionId.Should().Be("conv-1");
        result.UserId.Should().Be("user@test.com");
        result.MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task ClearSessionAsync_Should_RemoveExistingSession()
    {
        var session = CreateSessionData("conv-1", "user@test.com");
        await _store.SetSessionDataAsync("conv-1", session);

        await _store.ClearSessionAsync("conv-1");

        var result = await _store.GetSessionDataAsync("conv-1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ClearSessionAsync_Should_NotThrow_WhenKeyDoesNotExist()
    {
        var act = () => _store.ClearSessionAsync("nonexistent-conv");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetSessionDataAsync_Should_OverwriteExistingData()
    {
        var original = CreateSessionData("conv-1", "user@test.com");
        var replacement = new AgentSessionData
        {
            AgentSessionId = "conv-1",
            UserId = "user@test.com",
            SerializedSession = JsonDocument.Parse("{}").RootElement,
            MessageCount = 5
        };

        await _store.SetSessionDataAsync("conv-1", original);
        await _store.SetSessionDataAsync("conv-1", replacement);

        var result = await _store.GetSessionDataAsync("conv-1");
        result.Should().NotBeNull();
        result!.MessageCount.Should().Be(5, because: "replacement should overwrite original");
    }
}
