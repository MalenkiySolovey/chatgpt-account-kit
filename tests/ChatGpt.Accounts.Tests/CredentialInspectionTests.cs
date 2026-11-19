using System.Text;
using System.Text.Json;
using ChatGpt.Accounts.Models;
using Xunit;

namespace ChatGpt.Accounts.Tests;

public sealed class CredentialInspectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InspectResolvesSyntheticAccountClaimsWithoutReturningTokenValues()
    {
        var token = CreateJwt(new
        {
            sub = "synthetic-subject-17",
            email = "Synthetic@Example.invalid",
            name = "Synthetic User",
            exp = 1_800_000_000,
            plan = "plus",
        });
        var credential = CreateAuthJson(token, "synthetic-account-id");

        var result = CredentialInspector.Inspect(credential, Now);

        Assert.True(result.IsRecognizable);
        Assert.Equal("synthetic-subject-17", result.Identity?.Key[4..]);
        Assert.Equal(AccountIdentitySource.Subject, result.Identity?.Source);
        Assert.Equal("synthetic-account-id", result.AccountId);
        Assert.Equal("synthetic@example.invalid", result.Claims.Email?.ToLowerInvariant());
        Assert.Equal("Synthetic User", result.Claims.DisplayName);
        Assert.Equal("plus", result.Claims.PlanType);
        Assert.DoesNotContain(token, JsonSerializer.Serialize(result));
        Assert.Equal(64, result.CredentialFingerprint?.Length);
    }

    [Fact]
    public void InspectRejectsMalformedJsonAndUnrecognizedObjectsWithoutThrowing()
    {
        Assert.Equal(CredentialDocumentStatus.InvalidJson, CredentialInspector.Inspect(Encoding.UTF8.GetBytes("{"), Now).Status);
        Assert.Equal(CredentialDocumentStatus.InvalidShape, CredentialInspector.Inspect(Encoding.UTF8.GetBytes("[]"), Now).Status);
        Assert.Equal(CredentialDocumentStatus.Unrecognized, CredentialInspector.Inspect(Encoding.UTF8.GetBytes("{}"), Now).Status);
    }

    [Fact]
    public void InspectUsesAccountIdWhenSubjectIsMissingAndEmailAsLastIdentityFallback()
    {
        var byAccountId = CredentialInspector.Inspect(CreateAuthJson(CreateJwt(new { email = "fallback@example.invalid" }), "account-42"), Now);
        var byEmail = CredentialInspector.Inspect(CreateAuthJson(CreateJwt(new { email = "fallback@example.invalid" }), null), Now);

        Assert.Equal("account:account-42", byAccountId.Identity?.Key);
        Assert.Equal(AccountIdentitySource.AccountId, byAccountId.Identity?.Source);
        Assert.Equal("email:FALLBACK@EXAMPLE.INVALID", byEmail.Identity?.Key);
        Assert.Equal(AccountIdentitySource.Email, byEmail.Identity?.Source);
    }

    [Fact]
    public void InspectDoesNotTreatTokenExpiryAsSubscriptionExpiry()
    {
        var token = CreateJwt(new { exp = 1_800_000_000, iat = 1_700_000_000, plan = "plus" });
        var result = CredentialInspector.Inspect(CreateAuthJson(token, "account"), Now);

        Assert.Null(result.Subscription);
    }

    [Fact]
    public void SubscriptionExtractorReadsNamespacedPeriodAndEstimatesKnownMonthlyPlan()
    {
        var token = CreateJwt(new Dictionary<string, object>
        {
            ["plan"] = "plus",
            ["https://api.openai.com/auth"] = new Dictionary<string, object>
            {
                ["chatgpt_plan_type"] = "plus",
                ["chatgpt_subscription_active_start"] = "2026-10-31T12:00:00Z",
            },
        });

        var result = SubscriptionMetadataExtractor.ExtractFromJwt(token, Now);

        Assert.NotNull(result);
        Assert.Equal(SubscriptionMetadataSource.EstimatedFromStart, result.Source);
        Assert.Equal(new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero), result.ActiveUntilUtc);
    }

    [Fact]
    public void RecordServiceCreatesThenUpdatesSameSyntheticAccountWithoutStoringCredential()
    {
        var store = new MemoryRecordStore();
        var service = new AccountRecordService(store);
        var first = CreateAuthJson(CreateJwt(new { sub = "stable-sub", email = "first@example.invalid", plan = "plus" }), "account-id");
        var refreshed = CreateAuthJson(CreateJwt(new { sub = "stable-sub", email = "updated@example.invalid", plan = "pro" }), "account-id");

        var created = service.CreateOrUpdate(first, "Work", Now);
        var updated = service.CreateOrUpdate(refreshed, null, Now.AddMinutes(1));

        Assert.True(created.Created);
        Assert.False(updated.Created);
        Assert.Equal("sub:stable-sub", updated.Record.IdentityKey);
        Assert.Equal("Work", updated.Record.Nickname);
        Assert.Equal("updated@example.invalid", updated.Record.Email);
        Assert.Equal("pro", updated.Record.PlanType);
        Assert.Single(store.Records);
        Assert.DoesNotContain(Convert.ToBase64String(first), JsonSerializer.Serialize(updated.Record));
    }

    [Fact]
    public void RecordServiceDoesNotPersistInvalidCredentials()
    {
        var store = new MemoryRecordStore();
        var service = new AccountRecordService(store);

        Assert.Throws<CredentialValidationException>(() => service.CreateOrUpdate(Encoding.UTF8.GetBytes("{}"), null, Now));
        Assert.Empty(store.Records);
    }

    private static byte[] CreateAuthJson(string? idToken, string? accountId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["auth_mode"] = "chatgpt",
            ["last_refresh"] = Now.ToString("O"),
            ["tokens"] = new Dictionary<string, object?>
            {
                ["id_token"] = idToken,
                ["account_id"] = accountId,
            },
        };
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static string CreateJwt(object claims)
    {
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}"));
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims));
        return $"{header}.{payload}.synthetic-signature".Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private sealed class MemoryRecordStore : IAccountRecordStore
    {
        private readonly Dictionary<string, AccountRecord> _records = new(StringComparer.Ordinal);

        public IReadOnlyCollection<AccountRecord> Records => _records.Values;

        public AccountRecord? FindByIdentity(string identityKey) =>
            _records.TryGetValue(identityKey, out var record) ? record : null;

        public void Save(AccountRecord record) => _records[record.IdentityKey] = record;
    }
}
