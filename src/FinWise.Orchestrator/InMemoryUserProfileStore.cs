using System.Collections.Concurrent;

namespace FinWise.Orchestrator;

/// <summary>
/// In-memory implementation of user profile storage.
/// Thread-safe for concurrent access.
/// </summary>
public class InMemoryUserProfileStore : IUserProfileStore
{
    private readonly ConcurrentDictionary<string, UserProfileDto> _profiles = new();

    /// <inheritdoc/>
    public Task<UserProfileDto?> GetProfileAsync(string userId)
    {
        _profiles.TryGetValue(userId, out var profile);
        return Task.FromResult(profile);
    }

    /// <inheritdoc/>
    public Task SetProfileAsync(string userId, UserProfileDto profile)
    {
        _profiles[userId] = profile;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> HasProfileAsync(string userId)
    {
        return Task.FromResult(_profiles.ContainsKey(userId));
    }

    /// <inheritdoc/>
    public Task DeleteProfileAsync(string userId)
    {
        _profiles.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
