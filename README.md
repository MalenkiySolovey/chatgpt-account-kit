# ChatGPT Account Kit

**English** | [Русский](./README_RU.md)

`ChatGpt.Accounts` is a standalone .NET library for safely interpreting ChatGPT/OpenAI account credentials and subscription claims. It has no Codex, WinUI, or Control Center dependency and does not own credential storage.

## Capabilities

- Inspect auth JSON locally and validate its recognizable shape.
- Extract account ID, JWT subject, safely bounded email/display name, plan, and token expiry.
- Resolve a stable identity in subject, account ID, then normalized email order.
- Extract observed subscription period metadata without treating JWT `exp` or `iat` as subscription dates.
- Calculate a monthly renewal estimate only for explicitly recognized monthly consumer plans.
- Compute a credential revision fingerprint without retaining the input.
- Create or update a credential-free account record through an injected `IAccountRecordStore`.
- Run a versioned JSON inspection command over stdin/stdout.

Inspection results deliberately contain no access token, refresh token, ID token, or raw credential document. The kit does not validate JWT signatures and must not be treated as an authentication authority; claims are local display and identity metadata only.

## Install

The package is built locally and is not published to NuGet:

```powershell
dotnet pack .\src\ChatGpt.Accounts\ChatGpt.Accounts.csproj -c Release -o .\artifacts\packages
dotnet add <consumer-project> package ChatGpt.Accounts --version 0.1.0-preview.2 --source .\artifacts\packages
```

## API example

```csharp
using ChatGpt.Accounts;

var result = CredentialInspector.Inspect(authJsonBytes, DateTimeOffset.UtcNow);
if (!result.IsRecognizable)
{
    throw new InvalidDataException("Credential document is invalid or unrecognized.");
}

Console.WriteLine($"Identity: {result.Identity?.Key}; plan: {result.Claims.PlanType ?? "unknown"}");
```

`AccountRecordService` accepts an `IAccountRecordStore` implementation supplied by the caller. It persists account metadata only; it never writes the credential bytes. The application's encrypted vault or another credential owner must remain responsible for the actual credential.

## JSON command

Build `ChatGpt.Accounts.Cli`, then send a versioned request to `chatgpt-account --json` on stdin:

```json
{"version":1,"operation":"inspect","authJson":"{\"auth_mode\":\"chatgpt\",\"tokens\":{}}"}
```

The command writes `{ "version": 1, "ok": true, "result": ... }` or a structured error to stdout. Credential values are never included in the response or error text.

## Validation

```powershell
eng\verify.ps1
```

The verification script performs restore, Release build, tests, formatting checks, dependency audit, package creation, and a package-only external consumer smoke run.

## Provenance and license

Extracted from [Codex Control Center](https://github.com/MalenkiySolovey/codex-control-center) source commit `fd590546dc52800e27f420ea5de73a2b2a5e50d0`. See [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md) for the original author attribution and extraction boundaries. Licensed under MIT; original copyright notice is preserved.
