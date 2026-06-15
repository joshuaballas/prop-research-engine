# Documentation Guide

Documentation is part of the change, not a cleanup task for later.

## Rule

Any future code change that adds, changes, or removes any of the following must update the relevant docs in the same change:

- API endpoints,
- workers,
- configuration,
- provider behavior,
- data flow,
- database concepts,
- safety rules,
- dashboard behavior,
- manual workflows,
- operational assumptions.

## What To Update

Use this guide:

- New endpoint or endpoint behavior change: update [API_ENDPOINTS.md](API_ENDPOINTS.md) and, if user-facing through dashboard, [WORKFLOWS.md](WORKFLOWS.md).
- New worker or worker behavior change: update [ARCHITECTURE.md](ARCHITECTURE.md), [APP_HANDBOOK.md](APP_HANDBOOK.md), and [WORKFLOWS.md](WORKFLOWS.md).
- New table/entity/migration: update [DATA_MODEL.md](DATA_MODEL.md).
- New provider or provider behavior: update [ARCHITECTURE.md](ARCHITECTURE.md), [API_ENDPOINTS.md](API_ENDPOINTS.md), and [DECISIONS.md](DECISIONS.md) if it changes strategy.
- Dashboard UI behavior: update [APP_HANDBOOK.md](APP_HANDBOOK.md) and [WORKFLOWS.md](WORKFLOWS.md).
- Safety/security/credit behavior: update [APP_HANDBOOK.md](APP_HANDBOOK.md), [DECISIONS.md](DECISIONS.md), and [WORKFLOWS.md](WORKFLOWS.md).
- New generated-output convention: update [GENERATED_OUTPUTS.md](GENERATED_OUTPUTS.md).
- Planned future work discovered during implementation: update [BACKLOG.md](BACKLOG.md).
- New config, secret, provider token, admin key, or credential handling: update [SECRETS_AND_SOURCE_CONTROL.md](SECRETS_AND_SOURCE_CONTROL.md).

## Endpoint Documentation Checklist

For each endpoint, record:

- method and path,
- purpose,
- read/write behavior,
- whether it calls an external API,
- whether it spends The Odds API credits,
- important query params/body,
- useful PowerShell example when applicable.

## Be Honest About Uncertainty

If a legacy or research area is uncertain, mark it as legacy or needs verification. Do not invent behavior to make docs feel complete.

## Generated Output Convention

PowerShell snippets that save output should write under `tmp-analysis`, not the repo root. See [GENERATED_OUTPUTS.md](GENERATED_OUTPUTS.md).
