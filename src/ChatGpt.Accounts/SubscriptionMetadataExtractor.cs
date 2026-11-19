using System.Globalization;
using System.Text.Json;
using ChatGpt.Accounts.Models;

namespace ChatGpt.Accounts;

public static class SubscriptionMetadataExtractor
{
    private const string OpenAiAuthNamespace = "https://api.openai.com/auth";
    private static readonly HashSet<string> KnownMonthlyPlans = new(StringComparer.OrdinalIgnoreCase) { "plus", "pro" };

    public static DetectedSubscriptionMetadata? Extract(ReadOnlyMemory<byte> authJson, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(authJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("tokens", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var idToken = JwtClaimReader.ReadString(tokens, "id_token", 256 * 1024);
            var accessToken = JwtClaimReader.ReadString(tokens, "access_token", 256 * 1024);
            var result = ExtractFromJwt(idToken, now);
            return result ?? ExtractFromJwt(accessToken, now);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DetectedSubscriptionMetadata? ExtractFromJwt(string? jwtToken, DateTimeOffset now)
    {
        if (!JwtClaimReader.TryReadPayload(jwtToken, out var payload))
        {
            return null;
        }

        using (payload)
        {
            var root = payload.RootElement;
            JsonElement? auth = root.TryGetProperty(OpenAiAuthNamespace, out var authClaim) && authClaim.ValueKind == JsonValueKind.Object
                ? authClaim
                : null;

            var plan = auth.HasValue ? JwtClaimReader.ReadString(auth.Value, "chatgpt_plan_type") : null;
            plan ??= JwtClaimReader.ReadString(root, "chatgpt_plan_type") ??
                JwtClaimReader.ReadString(root, "plan_type") ??
                JwtClaimReader.ReadString(root, "plan");

            var start = auth.HasValue ? ReadSubscriptionTimestamp(auth.Value, "chatgpt_subscription_active_start") : null;
            var until = auth.HasValue ? ReadSubscriptionTimestamp(auth.Value, "chatgpt_subscription_active_until") : null;
            start ??= ReadSubscriptionTimestamp(root, "chatgpt_subscription_active_start");
            until ??= ReadSubscriptionTimestamp(root, "chatgpt_subscription_active_until");

            if (until.HasValue)
            {
                var stale = until.Value < now && !string.Equals(plan, "free", StringComparison.OrdinalIgnoreCase);
                return new DetectedSubscriptionMetadata(start, until, now, plan, SubscriptionMetadataSource.OAuthTokenClaim, stale);
            }

            if (start.HasValue && plan is not null && KnownMonthlyPlans.Contains(plan))
            {
                var next = SubscriptionRenewalCalculator.EstimateNextMonthlyRenewal(
                    DateOnly.FromDateTime(start.Value.UtcDateTime),
                    DateOnly.FromDateTime(now.UtcDateTime));
                if (next.HasValue)
                {
                    return new DetectedSubscriptionMetadata(
                        start,
                        new DateTimeOffset(next.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
                        now,
                        plan,
                        SubscriptionMetadataSource.EstimatedFromStart);
                }
            }

            return start.HasValue
                ? new DetectedSubscriptionMetadata(start, null, now, plan, SubscriptionMetadataSource.OAuthTokenClaim)
                : null;
        }
    }

    private static DateTimeOffset? ReadSubscriptionTimestamp(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) &&
                parsed.Year is >= 2020 and <= 2100)
            {
                return parsed.ToUniversalTime();
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixText))
            {
                return FromBoundedUnixTime(unixText);
            }
        }
        else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unixNumber))
        {
            return FromBoundedUnixTime(unixNumber);
        }

        return null;
    }

    private static DateTimeOffset? FromBoundedUnixTime(long value)
    {
        if (value is >= 1_577_836_800 and <= 4_102_444_800)
        {
            return DateTimeOffset.FromUnixTimeSeconds(value);
        }

        if (value is >= 1_577_836_800_000 and <= 4_102_444_800_000)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }

        return null;
    }
}

public static class SubscriptionRenewalCalculator
{
    public static DateOnly? EstimateNextMonthlyRenewal(DateOnly startedOn, DateOnly today)
    {
        var year = today.Year;
        var month = today.Month;
        var candidate = new DateOnly(year, month, Math.Min(startedOn.Day, DateTime.DaysInMonth(year, month)));
        if (candidate <= today)
        {
            if (month == 12)
            {
                year++;
                month = 1;
            }
            else
            {
                month++;
            }

            candidate = new DateOnly(year, month, Math.Min(startedOn.Day, DateTime.DaysInMonth(year, month)));
        }

        return candidate;
    }
}
