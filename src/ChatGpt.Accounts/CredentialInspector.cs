using System.Globalization;
using System.Text;
using System.Text.Json;
using ChatGpt.Accounts.Models;

namespace ChatGpt.Accounts;

/// <summary>Reads account metadata from an auth.json-compatible document entirely in memory.</summary>
public static class CredentialInspector
{
    public const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private const int MaximumTokenCharacters = 256 * 1024;
    private const int MaximumIdentifierCharacters = 512;
    private const int MaximumEmailCharacters = 320;
    private const int MaximumDisplayNameCharacters = 120;

    public static CredentialInspection Inspect(ReadOnlyMemory<byte> credentialBytes, DateTimeOffset now)
    {
        if (credentialBytes.IsEmpty || credentialBytes.Length > MaximumDocumentBytes)
        {
            return Empty(CredentialDocumentStatus.InvalidShape);
        }

        try
        {
            using var document = JsonDocument.Parse(credentialBytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Empty(CredentialDocumentStatus.InvalidShape);
            }

            var authMode = SafeString(root, "auth_mode", 64);
            var lastRefresh = ReadDateTime(root, "last_refresh");
            string? token = null;
            string? accountId = null;

            if (root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                token = SafeString(tokens, "id_token", MaximumTokenCharacters);
                accountId = SafeIdentifier(SafeString(tokens, "account_id", MaximumIdentifierCharacters));
            }

            var claims = JwtClaimReader.Read(token);
            var identity = ResolveIdentity(claims, accountId);
            var status = string.IsNullOrWhiteSpace(authMode) && string.IsNullOrWhiteSpace(token)
                ? CredentialDocumentStatus.Unrecognized
                : CredentialDocumentStatus.Valid;
            var subscription = SubscriptionMetadataExtractor.Extract(credentialBytes, now);

            return new CredentialInspection(
                status,
                authMode,
                lastRefresh,
                accountId,
                claims,
                identity,
                subscription,
                CredentialFingerprint.Compute(credentialBytes.Span));
        }
        catch (JsonException)
        {
            return Empty(CredentialDocumentStatus.InvalidJson);
        }
    }

    public static CredentialInspection Validate(ReadOnlyMemory<byte> credentialBytes, DateTimeOffset now)
    {
        var inspection = Inspect(credentialBytes, now);
        if (!inspection.IsRecognizable)
        {
            throw new CredentialValidationException("The credential document is invalid or unrecognized.");
        }

        return inspection;
    }

    private static CredentialInspection Empty(CredentialDocumentStatus status) =>
        new(status, null, null, null, new AccountClaims(null, null, null, null, null), null, null, null);

    private static AccountIdentity? ResolveIdentity(AccountClaims claims, string? accountId)
    {
        if (!string.IsNullOrWhiteSpace(claims.Subject))
        {
            return new AccountIdentity($"sub:{claims.Subject}", AccountIdentitySource.Subject, claims.Subject, accountId, claims.Email);
        }

        if (!string.IsNullOrWhiteSpace(accountId))
        {
            return new AccountIdentity($"account:{accountId}", AccountIdentitySource.AccountId, null, accountId, claims.Email);
        }

        if (!string.IsNullOrWhiteSpace(claims.Email))
        {
            var normalizedEmail = claims.Email.Trim().ToUpperInvariant();
            return new AccountIdentity($"email:{normalizedEmail}", AccountIdentitySource.Email, null, accountId, normalizedEmail);
        }

        return null;
    }

    private static string? SafeIdentifier(string? value) =>
        value is { Length: > 0 and <= MaximumIdentifierCharacters } && !value.Any(char.IsControl)
            ? value.Trim()
            : null;

    private static string? SafeDisplayValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength && !trimmed.Any(char.IsControl) ? trimmed : null;
    }

    private static string? SafeString(JsonElement obj, string name, int maximumLength)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var candidate = value.GetString();
        return candidate is { Length: <= MaximumTokenCharacters }
            ? SafeDisplayValue(candidate, maximumLength)
            : null;
    }

    private static DateTimeOffset? ReadDateTime(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return null;
    }

    internal static string? SafeEmail(string? value) =>
        SafeDisplayValue(value, MaximumEmailCharacters);

    internal static string? SafeDisplayName(string? value) =>
        SafeDisplayValue(value, MaximumDisplayNameCharacters);
}

internal static class JwtClaimReader
{
    private const string OpenAiAuthNamespace = "https://api.openai.com/auth";

    public static AccountClaims Read(string? token)
    {
        if (!TryReadPayload(token, out var payload))
        {
            return new AccountClaims(null, null, null, null, null);
        }

        using (payload)
        {
            var root = payload.RootElement;
            var subject = ReadString(root, "sub", 512);
            var email = CredentialInspector.SafeEmail(ReadString(root, "email", 320));
            var displayName = ReadDisplayName(root);
            var plan = ReadPlan(root);
            return new AccountClaims(subject, email, ReadUnixSeconds(root, "exp"), plan, displayName);
        }
    }

    internal static bool TryReadPayload(string? token, out JsonDocument payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(token) || token.Length > CredentialInspector.MaximumDocumentBytes)
        {
            return false;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        try
        {
            var value = parts[1].Replace('-', '+').Replace('_', '/');
            value += (value.Length % 4) switch
            {
                2 => "==",
                3 => "=",
                _ => string.Empty,
            };
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    internal static string? ReadString(JsonElement obj, string name, int maxLength = 512)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var candidate = value.GetString();
        return candidate is { Length: > 0 } && candidate.Length <= maxLength && !candidate.Any(char.IsControl)
            ? candidate.Trim()
            : null;
    }

    internal static DateTimeOffset? ReadUnixSeconds(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object ||
            !obj.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? ReadDisplayName(JsonElement root)
    {
        var direct = CredentialInspector.SafeDisplayName(ReadString(root, "name", 120));
        if (direct is not null)
        {
            return direct;
        }

        var givenName = CredentialInspector.SafeDisplayName(ReadString(root, "given_name", 120));
        var familyName = CredentialInspector.SafeDisplayName(ReadString(root, "family_name", 120));
        var combined = string.Join(' ', new[] { givenName, familyName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return CredentialInspector.SafeDisplayName(combined);
    }

    private static string? ReadPlan(JsonElement root)
    {
        var plan = ReadString(root, "plan") ??
            ReadString(root, "plan_type") ??
            ReadString(root, "chatgpt_plan_type");

        if (plan is not null)
        {
            return plan;
        }

        if (root.TryGetProperty(OpenAiAuthNamespace, out var auth) && auth.ValueKind == JsonValueKind.Object)
        {
            return ReadString(auth, "chatgpt_plan_type") ?? ReadString(auth, "plan_type");
        }

        return null;
    }
}
