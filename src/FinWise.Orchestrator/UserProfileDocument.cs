using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace FinWise.Orchestrator;

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
    /// Creates a document from a DTO.
    /// </summary>
    public static UserProfileDocument FromDto(UserProfileDto dto)
    {
        // Only Id/UserId are mandatory; others can be null or empty
        return new UserProfileDocument
        {
            Id = EmailToDocumentId(dto.UserId),
            UserId = dto.UserId,
            RiskTolerance = string.IsNullOrWhiteSpace(dto.RiskTolerance) ? null : dto.RiskTolerance,
            InvestmentGoals = string.IsNullOrWhiteSpace(dto.InvestmentGoals) ? null : dto.InvestmentGoals,
            InvestmentTimeframe = string.IsNullOrWhiteSpace(dto.InvestmentTimeframe) ? null : dto.InvestmentTimeframe
        };
    }

    /// <summary>
    /// Creates a document from a DTO, merging with existing document data.
    /// Only updates fields that have non-null/non-empty values in the incoming DTO.
    /// Preserves existing values for fields that are null in the DTO.
    /// </summary>
    /// <param name="dto">The incoming DTO with partial or full data.</param>
    /// <param name="existing">The existing document to merge with, or null if creating new.</param>
    public static UserProfileDocument FromDtoWithMerge(UserProfileDto dto, UserProfileDocument? existing)
    {
        return new UserProfileDocument
        {
            Id = EmailToDocumentId(dto.UserId),
            UserId = dto.UserId,
            // Only overwrite if the DTO has a non-null/non-empty value; otherwise keep existing
            RiskTolerance = !string.IsNullOrWhiteSpace(dto.RiskTolerance)
                ? dto.RiskTolerance
                : existing?.RiskTolerance,
            InvestmentGoals = !string.IsNullOrWhiteSpace(dto.InvestmentGoals)
                ? dto.InvestmentGoals
                : existing?.InvestmentGoals,
            InvestmentTimeframe = !string.IsNullOrWhiteSpace(dto.InvestmentTimeframe)
                ? dto.InvestmentTimeframe
                : existing?.InvestmentTimeframe
        };
    }

    /// <summary>
    /// Converts this document to a DTO.
    /// </summary>
    public UserProfileDto ToDto()
    {
        return new UserProfileDto(
            UserId,
            RiskTolerance,
            InvestmentGoals,
            InvestmentTimeframe
        );
    }
}
