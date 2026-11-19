using System.Text;
using System.Text.Json;
using ChatGpt.Accounts;

var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
var store = new MemoryStore();
var service = new AccountRecordService(store);
var first = CreateCredential("synthetic-account", "synthetic-user@example.invalid", "plus");
var refreshed = CreateCredential("synthetic-account", "synthetic-user@example.invalid", "pro");

var created = service.CreateOrUpdate(first, "Synthetic", now);
var updated = service.CreateOrUpdate(refreshed, null, now.AddMinutes(1));
var malformed = CredentialInspector.Inspect(Encoding.UTF8.GetBytes("{broken"), now);

if (!created.Created || updated.Created || store.Records.Count != 1 ||
    updated.Record.PlanType != "pro" || malformed.Status.ToString() != "InvalidJson")
{
    return 1;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    ok = true,
    records = store.Records.Count,
    identity = updated.Record.IdentityKey,
    plan = updated.Record.PlanType,
    malformed = malformed.Status.ToString(),
}));
return 0;

static byte[] CreateCredential(string subject, string email, string plan)
{
    var claims = JsonSerializer.SerializeToUtf8Bytes(new { sub = subject, email, plan });
    var token = $"e30.{Convert.ToBase64String(claims).TrimEnd('=').Replace('+', '-').Replace('/', '_')}.synthetic";
    return JsonSerializer.SerializeToUtf8Bytes(new
    {
        auth_mode = "chatgpt",
        tokens = new { id_token = token, account_id = "synthetic-account" },
    });
}

sealed class MemoryStore : IAccountRecordStore
{
    private readonly Dictionary<string, AccountRecord> _records = new(StringComparer.Ordinal);

    public IReadOnlyCollection<AccountRecord> Records => _records.Values;

    public AccountRecord? FindByIdentity(string identityKey) =>
        _records.TryGetValue(identityKey, out var record) ? record : null;

    public void Save(AccountRecord record) => _records[record.IdentityKey] = record;
}
