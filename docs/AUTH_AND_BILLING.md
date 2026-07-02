# Auth And Billing

SportsBetting uses Clerk for customer authentication and keeps plan/billing state in local application tables. Stripe is not integrated yet.

## Configuration

Frontend:

- `VITE_CLERK_PUBLISHABLE_KEY`

Backend:

- `Authentication:Provider=Clerk`
- `Clerk:Issuer`
- `Clerk:JwksUrl`
- `Clerk:AuthorizedParties:0`
- `Clerk:SecretKey` optional backend-only secret for future trusted Clerk Backend API calls
- `Auth:AllowFirstUserOwnerBootstrapInDevelopment`
- `Auth:BootstrapOwnerEmails`
- `Auth:BootstrapOwnerExternalSubjectIds`
- `Auth:AllowAnonymousDebugInDevelopment`

Do not commit real Clerk keys. Use `.env.local` for the frontend and user-secrets, environment variables, or deployment secret providers for backend values. In Development, the API loads user-secrets after environment-specific appsettings and before ignored `appsettings.Local.json`, command-line args, and environment variables.

## Runtime Model

The frontend uses Clerk session tokens through `@clerk/clerk-react`. API requests ask Clerk for the current token at request time and attach `Authorization: Bearer <token>`. Tokens are not stored in localStorage and the SportsBetting API does not issue its own JWT.

The backend validates Clerk JWTs with ASP.NET Core JWT bearer auth. It validates issuer, lifetime, signing keys through Clerk metadata/JWKS, and checks `azp` against `Clerk:AuthorizedParties` when the claim is present.

The first authenticated member request syncs a local `app_users` row from trusted token claims:

- `external_auth_provider=Clerk`
- `external_subject_id=sub`
- `email`
- `display_name`
- `last_session_id=sid`
- `role=Member` by default
- `status=Active` by default

Owner bootstrap can be configured by email/subject id. Development first-user owner bootstrap is disabled by default and should only be enabled locally.

Disabled local users can still authenticate with Clerk, but the app returns 403 for member endpoints.

## Anonymous And Protected Endpoints

Anonymous:

- marketing/frontend routes
- `/sign-in`
- `/create-account`
- `/pricing`
- `GET /api/plans`
- `/health` and `/health/ready`

Member protected:

- `/app/*` frontend routes
- `/api/me`
- `/api/account`
- `/api/billing`
- `/api/checkout/*`
- `/api/markets/*`
- `/api/mlb/*`
- `/api/dfs/*`
- `/api/players/*`

Owner/Admin protected:

- `/api/admin/*`
- `/api/debug/*`
- `/research`

In Development only, `/api/debug/*` and `/research` may be anonymous when `Auth:AllowAnonymousDebugInDevelopment=true`.

## Plan Tables

Plans are domain data, not AppConfigs:

- `plans`
- `plan_features`
- `user_plan_states`
- `checkout_intents`

Initial seed plans are `free` and `pro`. Plan features are local DB rows so Pricing and Billing can read the same plan catalog.

## Checkout Intent Scaffold

`POST /api/checkout/intents` creates a local checkout intent. Paid plans return:

- `status=RequiresExternalProcessor`
- `paymentProcessorConnected=false`
- `externalProviderRequired=Stripe`
- `checkoutUrl=null`

This is the handoff point for future Stripe work. Creating a paid checkout intent sets the user plan state to `PendingCheckout`; it does not activate paid access. Free plan selection can activate immediately.

## Local Setup

```powershell
$repo = "D:\Documents\Programs\Web\SportsBetting"
Set-Location $repo

@"
VITE_CLERK_PUBLISHABLE_KEY=pk_test_REPLACE_ME
"@ | Set-Content .\frontend\.env.local

dotnet user-secrets init --project .\api
dotnet user-secrets set "Authentication:Provider" "Clerk" --project .\api
dotnet user-secrets set "Clerk:Issuer" "https://REPLACE_ME.clerk.accounts.dev" --project .\api
dotnet user-secrets set "Clerk:JwksUrl" "https://REPLACE_ME.clerk.accounts.dev/.well-known/jwks.json" --project .\api
dotnet user-secrets set "Clerk:AuthorizedParties:0" "http://localhost:5173" --project .\api
```

## Verification

```powershell
$base = "http://localhost:5299"

Invoke-RestMethod "$base/api/plans" | ConvertTo-Json -Depth 80

try {
  Invoke-RestMethod "$base/api/me"
} catch {
  $_.Exception.Response.StatusCode.value__
}

try {
  Invoke-RestMethod "$base/api/markets/top-picks?sport=All&take=5"
} catch {
  $_.Exception.Response.StatusCode.value__
}

$token = "<paste Clerk session JWT here>"
$headers = @{ Authorization = "Bearer $token" }

Invoke-RestMethod "$base/api/me" -Headers $headers | ConvertTo-Json -Depth 80
Invoke-RestMethod "$base/api/billing" -Headers $headers | ConvertTo-Json -Depth 80

$body = @{
  planSlug = "pro"
  billingInterval = "Monthly"
  returnUrl = "http://localhost:5173/app/billing"
} | ConvertTo-Json

Invoke-RestMethod "$base/api/checkout/intents" -Method Post -Headers $headers -ContentType "application/json" -Body $body |
  ConvertTo-Json -Depth 80
```
