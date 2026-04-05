using FinWise.MultiAgentWorkflow.DomainModel;

namespace FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;

/// <summary>
/// Interface for storing and retrieving user profiles.
/// </summary>
public interface IUserProfileStore
{
    /// <summary>
    /// Gets a user profile by user ID.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The user profile if found, null otherwise.</returns>
    Task<UserProfile?> GetProfileAsync(string userId);

    /// <summary>
    /// Stores or updates a user profile.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="profile">The profile to store.</param>
    Task SetProfileAsync(string userId, UserProfile profile);

    /// <summary>
    /// Checks if a profile exists for the given user ID.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>True if a profile exists, false otherwise.</returns>
    Task<bool> HasProfileAsync(string userId);

    /// <summary>
    /// Removes a user profile.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    Task DeleteProfileAsync(string userId);
}