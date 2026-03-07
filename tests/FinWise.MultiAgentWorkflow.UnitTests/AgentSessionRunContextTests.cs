using FluentAssertions;
using FinWise.MultiAgentWorkflow.Session;
using Microsoft.Extensions.AI;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class AgentSessionRunContextTests
{
    [Fact]
    public void Current_Should_BeNull_WhenNoSnapshotPushed()
    {
        // Assert
        AgentSessionRunContext.Current.Should().BeNull();
    }

    [Fact]
    public void Current_Should_ReturnPushedSnapshot()
    {
        // Arrange
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hello") };
        var snapshot = new AgentSessionRunSnapshot("conv-1", messages);

        // Act
        using var scope = AgentSessionRunContext.Push(snapshot);

        // Assert
        AgentSessionRunContext.Current.Should().BeSameAs(snapshot);
        AgentSessionRunContext.Current!.AgentSessionId.Should().Be("conv-1");
        AgentSessionRunContext.Current.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void Disposing_Should_RestorePreviousValue()
    {
        // Arrange
        var snapshot = new AgentSessionRunSnapshot("conv-1", []);
        var scope = AgentSessionRunContext.Push(snapshot);
        AgentSessionRunContext.Current.Should().NotBeNull();

        // Act
        scope.Dispose();

        // Assert
        AgentSessionRunContext.Current.Should().BeNull();
    }

    [Fact]
    public void NestedPush_Should_OverrideOuter_AndRestoreOnDispose()
    {
        // Arrange
        var outerSnapshot = new AgentSessionRunSnapshot("outer", []);
        var innerSnapshot = new AgentSessionRunSnapshot("inner", []);

        // Act & Assert - push outer
        using var outerScope = AgentSessionRunContext.Push(outerSnapshot);
        AgentSessionRunContext.Current.Should().BeSameAs(outerSnapshot);

        // Push inner - overrides outer
        var innerScope = AgentSessionRunContext.Push(innerSnapshot);
        AgentSessionRunContext.Current.Should().BeSameAs(innerSnapshot);
        AgentSessionRunContext.Current!.AgentSessionId.Should().Be("inner");

        // Dispose inner - restores outer
        innerScope.Dispose();
        AgentSessionRunContext.Current.Should().BeSameAs(outerSnapshot);
        AgentSessionRunContext.Current!.AgentSessionId.Should().Be("outer");
    }

    [Fact]
    public void Dispose_Should_BeIdempotent()
    {
        // Arrange
        var snapshot = new AgentSessionRunSnapshot("conv-1", []);
        var scope = AgentSessionRunContext.Push(snapshot);

        // Act - dispose twice
        scope.Dispose();
        scope.Dispose();

        // Assert - should not throw, value restored to null
        AgentSessionRunContext.Current.Should().BeNull();
    }
}
