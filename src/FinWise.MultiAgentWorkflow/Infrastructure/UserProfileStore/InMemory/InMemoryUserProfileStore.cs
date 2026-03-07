using System.Collections.Concurrent;
using FinWise.MultiAgentWorkflow.DomainModel;
using Serilog;

namespace FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore.InMemory;

/// <summary>
/// In-memory implementation of user profile storage.
/// Thread-safe for concurrent access.
/// </summary>
public class InMemoryUserProfileStore : IUserProfileStore
{
    private readonly ConcurrentDictionary<string, UserProfile> _profiles = new();

    /// <inheritdoc/>
    public Task<UserProfile?> GetProfileAsync(string userId)
    {
        _profiles.TryGetValue(userId, out var profile);
        Log.Debug("ProfileStore.GetProfileAsync({UserId}): {Result}", userId, profile != null ? "FOUND" : "NOT FOUND");
        return Task.FromResult(profile);
    }

    /// <inheritdoc/>
    public Task SetProfileAsync(string userId, UserProfile profile)
    {
        _profiles[userId] = profile;
        Log.Debug("ProfileStore.SetProfileAsync({UserId}): Profile saved. Total profiles in store: {Count}", userId, _profiles.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> HasProfileAsync(string userId)
    {
        var exists = _profiles.ContainsKey(userId);
        Log.Debug("ProfileStore.HasProfileAsync({UserId}): {Result}", userId, exists);
        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public Task DeleteProfileAsync(string userId)
    {
        _profiles.TryRemove(userId, out _);
        Log.Debug("ProfileStore.DeleteProfileAsync({UserId}): Profile removed. Total profiles in store: {Count}", userId, _profiles.Count);
        return Task.CompletedTask;
    }
}