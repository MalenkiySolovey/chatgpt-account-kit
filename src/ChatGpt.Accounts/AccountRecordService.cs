using ChatGpt.Accounts.Models;

namespace ChatGpt.Accounts;

/// <summary>A credential-free account projection suitable for application-owned persistence.</summary>
public sealed record AccountRecord(
    string IdentityKey,
    AccountIdentitySource IdentitySource,
    string? AccountId,
    string? Subject,
    string? Email,
    string? DisplayName,
    string? PlanType,
    string? AuthMode,
    DateTimeOffset? LastRefresh,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastInspectedAt,
    string CredentialFingerprint,
    string Nickname);

public interface IAccountRecordStore
{
    AccountRecord? FindByIdentity(string identityKey);
    void Save(AccountRecord record);
}

public sealed record AccountRecordUpdate(AccountRecord Record, bool Created);

/// <summary>Validates credentials and updates only the caller-provided account metadata store.</summary>
public sealed class AccountRecordService(IAccountRecordStore store)
{
    private readonly IAccountRecordStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public AccountRecordUpdate CreateOrUpdate(ReadOnlyMemory<byte> credentialBytes, string? nickname, DateTimeOffset now)
    {
        var inspection = CredentialInspector.Validate(credentialBytes, now);
        var identity = inspection.Identity
            ?? throw new CredentialValidationException("The credential document does not contain a stable account identity.");

        var existing = _store.FindByIdentity(identity.Key);
        var record = new AccountRecord(
            identity.Key,
            identity.Source,
            identity.AccountId,
            identity.Subject,
            identity.Email,
            inspection.Claims.DisplayName ?? existing?.DisplayName,
            inspection.Claims.PlanType ?? existing?.PlanType,
            inspection.AuthMode ?? existing?.AuthMode,
            inspection.LastRefresh ?? existing?.LastRefresh,
            existing?.FirstSeenAt ?? now,
            now,
            inspection.CredentialFingerprint ?? string.Empty,
            string.IsNullOrWhiteSpace(nickname) ? existing?.Nickname ?? string.Empty : nickname.Trim());

        _store.Save(record);
        return new AccountRecordUpdate(record, existing is null);
    }
}
