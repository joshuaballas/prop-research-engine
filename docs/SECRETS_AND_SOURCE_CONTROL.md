# Secrets And Source Control

This repository is intended to remain private. A private repository is still not a safe place for secrets.

## Rules

- Never commit real API keys, provider tokens, database passwords, debug admin keys, cookies, browser session state, or `.env` files.
- Never commit generated logs or endpoint outputs that may include provider payloads, headers, stack traces, or keys.
- Store generated analysis output in `tmp-analysis`, temporary build output in `tmp-build`, and runtime logs in `logs`.
- Keep `DebugAdmin:ApiKey` secret.
- Keep `TheOddsApi:ApiKey` secret.
- Keep production connection strings secret.

## Local Development

Use the ignored `api/appsettings.Local.json` file for normal local secrets and real local connection strings.

## How To Run Locally

From PowerShell:

```powershell
cd D:\Documents\Programs\Web\SportsBetting\api
Copy-Item .\appsettings.Local.example.json .\appsettings.Local.json
notepad .\appsettings.Local.json
dotnet run --project .\SportsBetting.Api.csproj --urls "http://localhost:5299"
```

Put real local values in `appsettings.Local.json`, not in committed appsettings files.

Optional: environment variables can still be used for automation or temporary overrides.

Config precedence for local development is:

1. committed `appsettings.json`
2. committed `appsettings.Development.json`
3. ignored `appsettings.Local.json`
4. command-line arguments
5. environment variables

Environment variable examples:

```powershell
$env:ConnectionStrings__SportsBettingDb = "POSTGRES_CONNECTION_STRING"
$env:TheOddsApi__ApiKey = "THE_ODDS_API_KEY"
$env:DebugAdmin__ApiKey = "DEBUG_ADMIN_KEY"
```

Tracked `appsettings*.json` files should contain only safe defaults, placeholders, disabled feature flags, or non-secret public URLs.

## GitHub Actions

Use repository or environment secrets for CI/CD values. The current build workflow intentionally does not require provider API keys and must not call external provider APIs.

Examples of future GitHub secret names:

- `POSTGRES_CONNECTION_STRING`
- `THE_ODDS_API_KEY`
- `DEBUG_ADMIN_KEY`

Do not print these values in workflow logs.

## Production

Production should use cloud-managed secrets or environment variables, not committed files.

Recommended production sources:

- cloud secret manager,
- container/app service environment variables,
- deployment platform encrypted secrets,
- managed database identity/credentials where available.

Do not deploy with `DebugAdmin.Enabled=true` unless an explicit admin key is configured and access is restricted.

## Before Committing

Run a quick redacted scan:

```powershell
Select-String -Path .\api\appsettings*.json -Pattern "ApiKey|Password|DebugAdmin|TheOddsApi"
```

Review any generated files in the repo root:

```powershell
Get-ChildItem -File | Where-Object { $_.Name -match '\.(log|json)$' }
```

If a future change adds config, provider credentials, auth, external tokens, or secret-like values, update this document and the relevant app docs in the same change.
