namespace ChatGpt.Accounts.Models;

public enum CredentialDocumentStatus
{
    Valid,
    InvalidJson,
    InvalidShape,
    Unrecognized,
}

public enum AccountIdentitySource
{
    Subject,
    AccountId,
    Email,
}

public enum SubscriptionMetadataSource
{
    OAuthTokenClaim,
    EstimatedFromStart,
}

/// <summary>Public account identifiers only. No credential values are included.</summary>
public sealed record AccountIdentity(
    string Key,
    AccountIdentitySource Source,
    string? Subject,
    string? AccountId,
    string? Email);

/// <summary>Non-secret claims read locally from an OAuth ID token payload.</summary>
public sealed record AccountClaims(
    string? Subject,
    string? Email,
    DateTimeOffset? TokenExpiresAt,
    string? PlanType,
    string? DisplayName);

/// <summary>Observed subscription dates. Token expiry and issuance times are never used as subscription dates.</summary>
public sealed record DetectedSubscriptionMetadata(
    DateTimeOffset? ActiveStartUtc,
    DateTimeOffset? ActiveUntilUtc,
    DateTimeOffset ObservedAtUtc,
    string? PlanType,
    SubscriptionMetadataSource Source,
    bool IsStale = false)
{
    public bool HasAnyDate => ActiveStartUtc.HasValue || ActiveUntilUtc.HasValue;
}

/// <summary>Safe inspection output: token and secret fields are intentionally absent.</summary>
public sealed record CredentialInspection(
    CredentialDocumentStatus Status,
    string? AuthMode,
    DateTimeOffset? LastRefresh,
    string? AccountId,
    AccountClaims Claims,
    AccountIdentity? Identity,
    DetectedSubscriptionMetadata? Subscription,
    string? CredentialFingerprint)
{
    public bool IsRecognizable => Status == CredentialDocumentStatus.Valid;
}

public sealed class CredentialValidationException(string message) : Exception(message);
