# Third-Party Notices and Provenance

## Source provenance

This library extracts account credential interpretation, JWT claim parsing, identity metadata, subscription metadata, and credential fingerprinting from `MalenkiySolovey/codex-control-center` at source commit `fd590546dc52800e27f420ea5de73a2b2a5e50d0`. The extracted code is being adapted into a standalone public API; the original application adapters continue to own vault storage, account persistence, OAuth, and Codex reconciliation.

The source repository preserves the MIT copyright notice for the original `codex-switcher` project (Copyright (c) 2026 unkdevv). That notice is retained in this repository's `LICENSE`.

## Runtime dependencies

`ChatGpt.Accounts` has no third-party runtime NuGet dependencies. Tests use xUnit and Microsoft.NET.Test.Sdk under their respective upstream licenses; they are not package dependencies of the library.

This extraction does not include code attributed to the private `codex-monitor` upstream.
