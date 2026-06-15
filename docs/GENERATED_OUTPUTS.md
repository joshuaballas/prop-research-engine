# Generated Outputs

Generated endpoint responses, ad-hoc diagnostics, and Codex test outputs should not be written to the project root.

Use these folders instead:

- `tmp-analysis/` for endpoint JSON, diagnostic reports, and analysis snapshots.
- `tmp-build/` for temporary build output directories.
- `logs/` for API run logs and long-running process output.

Example PowerShell pattern:

```powershell
Invoke-RestMethod -Uri "http://localhost:5299/api/debug/system/health" |
  ConvertTo-Json -Depth 10 |
  Set-Content -Path ".\tmp-analysis\system-health.json"
```
