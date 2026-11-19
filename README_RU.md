# ChatGPT Account Kit

[English](./README.md) | **Русский**

`ChatGpt.Accounts` — независимая .NET-библиотека для безопасного разбора credentials и метаданных подписки аккаунтов ChatGPT/OpenAI. У неё нет зависимостей от Codex, WinUI или Control Center, и она не владеет хранилищем credentials.

## Возможности

- Локальный разбор auth JSON и проверка его распознаваемой структуры.
- Извлечение account ID, JWT subject, email и отображаемого имени с ограничениями длины, тарифа и срока токена.
- Стабильная идентификация в порядке subject, account ID, нормализованный email.
- Извлечение наблюдаемых дат подписки без использования JWT `exp` или `iat` как даты окончания подписки.
- Расчёт месячного продления только для явно известных месячных тарифов.
- Отпечаток версии credential без сохранения исходных байтов.
- Создание и обновление записи аккаунта без credentials через переданный `IAccountRecordStore`.
- Версионированная JSON-команда через stdin/stdout.

Результаты проверки не содержат access token, refresh token, ID token или исходный документ credential. Подписи JWT не проверяются: claims служат только локальными метаданными отображения и идентификации, а не подтверждением подлинности.

## Установка

Пакет собирается локально и пока не публикуется в NuGet:

```powershell
dotnet pack .\src\ChatGpt.Accounts\ChatGpt.Accounts.csproj -c Release -o .\artifacts\packages
dotnet add <проект-потребитель> package ChatGpt.Accounts --version 0.1.0-preview.3 --source .\artifacts\packages
```

## Пример API

```csharp
using ChatGpt.Accounts;

var result = CredentialInspector.Inspect(authJsonBytes, DateTimeOffset.UtcNow);
if (!result.IsRecognizable)
{
    throw new InvalidDataException("Credential document is invalid or unrecognized.");
}

Console.WriteLine($"Identity: {result.Identity?.Key}; plan: {result.Claims.PlanType ?? "unknown"}");
```

`AccountRecordService` использует `IAccountRecordStore`, предоставленный потребителем. В хранилище попадают только метаданные аккаунта. Ответственность за исходные credentials остаётся у приложения, например у его зашифрованного vault.

## JSON-команда

Соберите `ChatGpt.Accounts.Cli` и передайте версионированный запрос в stdin команде `chatgpt-account --json`:

```json
{"version":1,"operation":"inspect","authJson":"{\"auth_mode\":\"chatgpt\",\"tokens\":{}}"}
```

Команда выводит `{ "version": 1, "ok": true, "result": ... }` либо структурированную ошибку. Ответы и ошибки не содержат credential values.

## Проверка

```powershell
eng\verify.ps1
```

Скрипт выполняет restore, Release-сборку, тесты, проверку форматирования, аудит зависимостей, создание пакета и smoke-проверку внешнего потребителя, который использует только NuGet-пакет.

## Происхождение и лицензия

Код выделен из [Codex Control Center](https://github.com/MalenkiySolovey/codex-control-center), исходный commit `fd590546dc52800e27f420ea5de73a2b2a5e50d0`. Атрибуция исходного автора и границы выделения приведены в [THIRD_PARTY_NOTICES.md](./THIRD_PARTY_NOTICES.md). Лицензия MIT; исходное copyright-уведомление сохранено.
