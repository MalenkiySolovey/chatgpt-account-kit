$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/ChatGpt.Accounts.Cli/ChatGpt.Accounts.Cli.csproj'
$syntheticSubject = 'synthetic-cli-account'
$claimJson = '{"sub":"' + $syntheticSubject + '","email":"cli@example.invalid"}'
$payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($claimJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
$token = "e30.$payload.synthetic-signature"
$authJson = @{ auth_mode = 'chatgpt'; tokens = @{ id_token = $token; account_id = 'synthetic-cli-id' } } | ConvertTo-Json -Compress -Depth 4
$request = @{ version = 1; operation = 'inspect'; authJson = $authJson } | ConvertTo-Json -Compress -Depth 5
$responseText = $request | dotnet run --project $project -c Release --no-build -- --json
if ($LASTEXITCODE -ne 0) { throw 'JSON CLI returned a failure exit code.' }

$response = $responseText | ConvertFrom-Json
if ($response.version -ne 1 -or -not $response.ok -or $response.result.identity.key -ne "sub:$syntheticSubject") {
    throw 'JSON CLI did not return the expected safe identity response.'
}
if ($responseText.Contains($token, [StringComparison]::Ordinal)) {
    throw 'JSON CLI echoed a synthetic token in its response.'
}

Write-Output 'JSON CLI synthetic identity smoke: PASS'
