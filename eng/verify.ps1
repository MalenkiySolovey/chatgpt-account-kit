$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'ChatGpt.Accounts.sln'
$feed = Join-Path $repoRoot 'artifacts/packages'
$consumer = Join-Path $repoRoot 'examples/ChatGpt.Accounts.PackageConsumer/ChatGpt.Accounts.PackageConsumer.csproj'

Push-Location $repoRoot
try {
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

    dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    dotnet test 'tests/ChatGpt.Accounts.Tests/ChatGpt.Accounts.Tests.csproj' -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

    & (Join-Path $PSScriptRoot 'verify-cli-json.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'JSON CLI smoke failed.' }

    dotnet format whitespace $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Whitespace format verification failed.' }

    dotnet format analyzers $solution --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Analyzer format verification failed.' }

    dotnet list $solution package --vulnerable --include-transitive
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }

    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    dotnet pack 'src/ChatGpt.Accounts/ChatGpt.Accounts.csproj' -c Release --no-build -o $feed
    if ($LASTEXITCODE -ne 0) { throw 'Package creation failed.' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = Join-Path $feed 'ChatGpt.Accounts.0.1.0-preview.2.nupkg'
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if ($null -eq $nuspecEntry) { throw 'Package does not contain a nuspec.' }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne 'ChatGpt.Accounts' -or $nuspec.package.metadata.version -ne '0.1.0-preview.2') {
            throw 'Package identity or version did not match the expected pin.'
        }
    }
    finally {
        $archive.Dispose()
    }

    dotnet restore $consumer --configfile 'eng/NuGet.PackageSmoke.config'
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer restore failed.' }

    dotnet run --project $consumer -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Package consumer smoke test failed.' }

    git diff --check
    if ($LASTEXITCODE -ne 0) { throw 'Git whitespace validation failed.' }
    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged Git whitespace validation failed.' }
}
finally {
    Pop-Location
}
