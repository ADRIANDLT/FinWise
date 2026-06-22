using FluentAssertions;
using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Session;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.Extensions.AI;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

/// <summary>
/// Unit tests for the structured profile-ready state that replaced the legacy
/// <c>PROFILE_READY:</c> chat-history text marker: <see cref="ProfileReadyToken"/>,
/// <see cref="ProfileReadyFlag"/>, <see cref="ProfileSessionState"/>, the
/// <see cref="FinWiseWorkflowService.FormatProfileContext"/> helper, and the legacy
/// fallback migration helper <see cref="AgentSessionConstants.IsProfileReady"/>.
/// </summary>
[Trait("Category", "Unit")]
public class ProfileSessionStateTests
{
    #region ProfileReadyToken

    [Fact]
    public void ProfileReadyToken_Defaults_IsNotReadyAndNoUserId()
    {
        var token = new ProfileReadyToken();

        token.IsReady.Should().BeFalse();
        token.UserId.Should().BeNull();
    }

    [Fact]
    public void ProfileReadyToken_MarkReady_WithoutUserId_SetsReadyOnly()
    {
        var token = new ProfileReadyToken();

        token.MarkReady();

        token.IsReady.Should().BeTrue();
        token.UserId.Should().BeNull();
    }

    [Fact]
    public void ProfileReadyToken_MarkReady_WithUserId_SetsReadyAndUserId()
    {
        var token = new ProfileReadyToken();

        token.MarkReady("a@b.com");

        token.IsReady.Should().BeTrue();
        token.UserId.Should().Be("a@b.com");
    }

    #endregion

    #region ProfileReadyFlag

    [Fact]
    public void ProfileReadyFlag_Initialize_ReturnsTokenAccessibleViaCurrent()
    {
        var token = ProfileReadyFlag.Initialize();

        ProfileReadyFlag.Current.Should().BeSameAs(token);
        token.IsReady.Should().BeFalse();
        token.UserId.Should().BeNull();
        ProfileReadyFlag.Clear();
        ProfileReadyFlag.Current.Should().BeNull();
    }

    [Fact]
    public void ProfileReadyFlag_Initialize_AlreadyReady_SeedsReadyAndUserId()
    {
        var token = ProfileReadyFlag.Initialize(alreadyReady: true, userId: "x@y.com");

        token.IsReady.Should().BeTrue();
        token.UserId.Should().Be("x@y.com");
        ProfileReadyFlag.Current.Should().BeSameAs(token);
        ProfileReadyFlag.Clear();
    }

    [Fact]
    public async Task ProfileReadyFlag_TokenMutationVisibleAcrossAwait()
    {
        // Simulates the parent→child→parent flow:
        // Parent initializes token, child mutates it via async call, parent reads mutation
        var token = ProfileReadyFlag.Initialize();

        await Task.Run(() =>
        {
            // Simulate tool execution in child async context
            ProfileReadyFlag.Current?.MarkReady("u@v.com");
        });

        // Parent reads the mutation after await — this is the critical test
        token.IsReady.Should().BeTrue("mutation via shared reference should be visible to parent");
        token.UserId.Should().Be("u@v.com");
        ProfileReadyFlag.Clear();
    }

    #endregion

    #region ProfileSessionState

    [Fact]
    public void ProfileSessionState_Defaults_NotReadyAndNoUserId()
    {
        var state = new ProfileSessionState();

        state.ProfileReady.Should().BeFalse();
        state.UserId.Should().BeNull();
    }

    #endregion

    #region FormatProfileContext

    [Fact]
    public void FormatProfileContext_WithAllFields_ContainsHeaderAndValues()
    {
        var profile = new UserProfile("user@example.com", "Moderate", "Retirement", "Long-term");

        var context = FinWiseWorkflowService.FormatProfileContext(profile);

        context.Should().Contain("CURRENT USER PROFILE");
        context.Should().Contain("user@example.com");
        context.Should().Contain("Moderate");
        context.Should().Contain("Retirement");
        context.Should().Contain("Long-term");
    }

    [Fact]
    public void FormatProfileContext_WithNullFields_ShowsNotSpecified()
    {
        var profile = new UserProfile("user@example.com", null, null, null);

        var context = FinWiseWorkflowService.FormatProfileContext(profile);

        context.Should().Contain("CURRENT USER PROFILE");
        context.Should().Contain("user@example.com");
        context.Should().Contain("(not specified)");
    }

    #endregion

    #region IsProfileReady (legacy fallback migration helper)

    [Fact]
    public void IsProfileReady_Should_ReturnTrue_WhenAssistantMessageContainsMarker()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "I'm done"),
            new(ChatRole.Assistant, "PROFILE_READY: email=foo@bar.com risk=Moderate")
        };

        AgentSessionConstants.IsProfileReady(history).Should().BeTrue();
    }

    [Fact]
    public void IsProfileReady_Should_ReturnFalse_WhenNoMarkerPresent()
    {
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "What is your email address?")
        };

        AgentSessionConstants.IsProfileReady(history).Should().BeFalse();
    }

    #endregion
}
