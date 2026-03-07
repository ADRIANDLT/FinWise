using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using FinWise.MultiAgentWorkflow.DomainModel;

namespace FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore.CosmosDb;

/// <summary>
/// CosmosDB document model for user profiles.
/// Maps to the UserProfiles container with userId as partition key.
/// </summary>
public class UserProfileDocument
{
    /// <summary>
    /// Document ID - SHA256 hash of the email address (hex encoded).
    /// This ensures the ID contains only safe characters (0-9, a-f) for CosmosDB.
    /// Required by CosmosDB.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// User identifier (email address).
    /// Also used as partition key.
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// User's risk tolerance preference.
    /// </summary>
    [JsonPropertyName("riskTolerance")]
    public string? RiskTolerance { get; set; }

    /// <summary>
    /// User's investment goals.
    /// </summary>
    [JsonPropertyName("investmentGoals")]
    public string? InvestmentGoals { get; set; }

    /// <summary>
    /// User's investment timeframe.
    /// </summary>
    [JsonPropertyName("investmentTimeframe")]
    public string? InvestmentTimeframe { get; set; }

    /// <summary>
    /// ETag for optimistic concurrency control.
    /// Automatically managed by CosmosDB.
    /// </summary>
    [JsonPropertyName("_etag")]
    public string? ETag { get; set; }

    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    [JsonPropertyName("_ts")]
    public long? Timestamp { get; set; }

    /// <summary>
    /// Converts an email address to a CosmosDB-safe document ID using SHA256 hash.
    /// The email is normalized (lowercase, trimmed) before hashing to ensure consistency.
    /// Returns a 64-character hex string containing only characters 0-9 and a-f.
    /// </summary>
    /// <param name="email">The email address to convert.</param>
    /// <returns>A 64-character lowercase hex string (SHA256 hash).</returns>
    public static string EmailToDocumentId(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be null or empty", nameof(email));
        }

        // Normalize: lowercase and trim to ensure consistent hashing
        var normalizedEmail = email.ToLowerInvariant().Trim();

        // Hash with SHA256
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));

        // Convert to hex (lowercase, no special chars)
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Creates a document from a domain model.
    /// </summary>
    public static UserProfileDocument FromModel(UserProfile profile)
    {
        // Only Id/UserId are mandatory; others can be null or empty
        return new UserProfileDocument
        {
            Id = EmailToDocumentId(profile.UserId),
            UserId = profile.UserId,
            RiskTolerance = string.IsNullOrWhiteSpace(profile.RiskTolerance) ? null : profile.RiskTolerance,
            InvestmentGoals = string.IsNullOrWhiteSpace(profile.InvestmentGoals) ? null : profile.InvestmentGoals,
            InvestmentTimeframe = string.IsNullOrWhiteSpace(profile.InvestmentTimeframe) ? null : profile.InvestmentTimeframe
        };
    }

    /// <summary>
    /// Creates a document from a domain model, merging with existing document data.
    /// Only updates fields that have non-null/non-empty values in the incoming model.
    /// Preserves existing values for fields that are null in the model.
    /// </summary>
    /// <param name="profile">The incoming domain model with partial or full data.</param>
    /// <param name="existing">The existing document to merge with, or null if creating new.</param>
    public static UserProfileDocument FromModelWithMerge(UserProfile profile, UserProfileDocument? existing)
    {
        return new UserProfileDocument
        {
            Id = EmailToDocumentId(profile.UserId),
            UserId = profile.UserId,
            // Only overwrite if the model has a non-null/non-empty value; otherwise keep existing
            RiskTolerance = !string.IsNullOrWhiteSpace(profile.RiskTolerance)
                ? profile.RiskTolerance
                : existing?.RiskTolerance,
            InvestmentGoals = !string.IsNullOrWhiteSpace(profile.InvestmentGoals)
                ? profile.InvestmentGoals
                : existing?.InvestmentGoals,
            InvestmentTimeframe = !string.IsNullOrWhiteSpace(profile.InvestmentTimeframe)
                ? profile.InvestmentTimeframe
                : existing?.InvestmentTimeframe
        };
    }

    /// <summary>
    /// Converts this document to a domain model.
    /// </summary>
    public UserProfile ToModel()
    {
        return new UserProfile(
            UserId,
            RiskTolerance,
            InvestmentGoals,
            InvestmentTimeframe
        );
    }
}