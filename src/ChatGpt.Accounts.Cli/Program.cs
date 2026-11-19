using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatGpt.Accounts;

if (args.Length != 1 || !string.Equals(args[0], "--json", StringComparison.Ordinal))
{
    WriteError("unsupported_mode", "Use --json and provide a versioned request on stdin.");
    return 2;
}

try
{
    var requestText = await ReadBoundedInputAsync(Console.In, CredentialInspector.MaximumDocumentBytes + 1024 * 1024);
    using var request = JsonDocument.Parse(requestText);
    var root = request.RootElement;
    if (root.ValueKind != JsonValueKind.Object ||
        !root.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
        !root.TryGetProperty("operation", out var operation) || operation.GetString() != "inspect" ||
        !root.TryGetProperty("authJson", out var authJsonValue) || authJsonValue.ValueKind != JsonValueKind.String)
    {
        WriteError("invalid_request", "Expected version 1, operation inspect, and an authJson string.");
        return 2;
    }

    var credentialText = authJsonValue.GetString() ?? string.Empty;
    var credentialBytes = Encoding.UTF8.GetBytes(credentialText);
    try
    {
        var result = CredentialInspector.Inspect(credentialBytes, DateTimeOffset.UtcNow);
        Console.Out.WriteLine(JsonSerializer.Serialize(new { version = 1, ok = true, result }, JsonSerializerOptions.Web));
        return 0;
    }
    finally
    {
        CryptographicOperations.ZeroMemory(credentialBytes);
    }
}
catch (JsonException)
{
    WriteError("invalid_request", "The request was not valid JSON.");
    return 2;
}
catch (InvalidDataException)
{
    WriteError("request_too_large", "The request exceeded the supported size limit.");
    return 2;
}
catch (Exception)
{
    WriteError("inspection_failed", "Credential inspection failed.");
    return 1;
}

static async Task<string> ReadBoundedInputAsync(TextReader reader, int maximumCharacters)
{
    var buffer = new char[8192];
    var builder = new StringBuilder();
    while (true)
    {
        var count = await reader.ReadAsync(buffer.AsMemory());
        if (count == 0)
        {
            break;
        }

        if (builder.Length + count > maximumCharacters)
        {
            throw new InvalidDataException();
        }

        builder.Append(buffer, 0, count);
    }

    return builder.ToString();
}

static void WriteError(string code, string message) =>
    Console.Out.WriteLine(JsonSerializer.Serialize(new { version = 1, ok = false, error = new { code, message } }, JsonSerializerOptions.Web));
