# Module 3a: API foundation

## Request pipeline

```
ForwardedHeaders → CorrelationId → ExceptionHandler → StatusCodePages → HSTS/HTTPS
→ SecurityHeaders → Serilog request log → Swagger UI (dev) → Authentication (JWT + account-status check)
→ RateLimiter → Authorization (fallback: authenticated) → Controllers (+ FluentValidation filter)
```

## Run

```powershell
cd backend
dotnet tool restore                                           # local dotnet-ef 10.0.14
dotnet run --project src/CommunitySos.Api -- --migrate        # migrations + SQL scripts + Master Admin seed, then exit
dotnet run --project src/CommunitySos.Api                     # https://localhost:7180/swagger
dotnet run --project src/CommunitySos.Api --launch-profile mobile-dev   # http://0.0.0.0:5180 for emulators/phones (Development only)
dotnet test
```

In Development, `Firebase:UseDevelopmentVerifier` accepts `dev:+919876543210` in place of a Firebase ID token, so the login endpoints can be tried from Swagger. Startup fails if this flag is set in any other environment.

## Configuration

| Key | Secret? | Notes |
|---|---|---|
| `ConnectionStrings:Sql` | yes | Azure SQL. Prefer Managed Identity: `Authentication=Active Directory Default`. |
| `Jwt:SigningKeyPem` | yes | RSA ≥ 2048-bit private key (PEM). Required outside Development. |
| `Jwt:Issuer`, `Jwt:Audience`, `AccessTokenMinutes` (15), `RefreshTokenDays` (30) | no | |
| `Firebase:ProjectId`, `Firebase:ServiceAccountJson` | JSON is a secret | Verifies ID tokens, including revocation checks |
| `App:PolicyVersion` | no | Changing it makes every user re-accept the notice (DPDP/GDPR) |
| `App:EmergencyNumbers` | no | 112 first |
| `App:GrievanceContactEmail` | no | DPDP s.13 grievance officer |
| `Seed:MasterAdmin:PhoneNumber` | no | Used once, by `--migrate` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | yes | Enables Azure Monitor (traces, logs, metrics) |
| `ForwardedHeaders:TrustAllProxies` | no | `true` only behind App Service / Front Door with access restrictions |
| `OpenApi:EnableUi` | no | Swagger in non-Development (e.g. a staging slot). Never on public production. |

In Azure, secrets come from Key Vault references in App Service settings (`@Microsoft.KeyVault(...)`). Locally, use `dotnet user-secrets` (UserSecretsId `community-sos-api`).

## Error contract (every non-2xx response)

`Content-Type: application/problem+json`

```json
{
  "type": "https://httpstatuses.io/422",
  "title": "Request cannot be processed",
  "status": 422,
  "detail": "This SOS is already Approved.",
  "instance": "/api/v1/admin/sos/7c1e.../approve",
  "code": "sos.already_reviewed",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "correlationId": "mobile-5f2c9a"
}
```

Validation errors (400) add a field map:

```json
{ "status": 400, "code": "validation_failed", "errors": { "location.latitude": ["Latitude must be between -90 and 90."] } }
```

| Status | When | `code` examples |
|---|---|---|
| 400 | Malformed input / validation | `validation_failed`, `bad_request` |
| 401 | Missing, invalid or expired token; suspended account | `unauthenticated`, `auth.invalid_firebase_token` |
| 403 | Authenticated but not allowed | `forbidden` |
| 404 | Not found | `not_found`, `sos.not_found` |
| 409 | Duplicate, stale `RowVersion`, immutable record | `duplicate`, `concurrency_conflict`, `immutable_record` |
| 422 | Domain rule violated | `sos.invalid_transition`, `invitation.unusable`, `registration.adult_required` |
| 429 | Rate limited (with a `Retry-After` header) | `rate_limited` |
| 500 | Unexpected; details are logged, never returned | `unexpected_error` |

The app should show `detail` to the user, branch on `code`, and quote `correlationId` in support requests.

## Authorization

- **Default:** every endpoint requires a valid access token. Public endpoints opt out with `[AllowAnonymous]`.
- **Policies:** `Admin` (SubAdmin or MasterAdmin) and `MasterAdmin`.
- **Per-SOS rules** (`SosOperations` used with `IAuthorizationService`):

| Operation | Allowed |
|---|---|
| Review (approve/reject) | Assigned Sub-Admin, Master Admin |
| View | Requester, assigned admin, Master Admin, an alerted user while the SOS is Approved |
| Cancel | Requester |
| Resolve | Requester, assigned admin, Master Admin |

- Suspended or deleted accounts are rejected on their next request, even with an unexpired token (status cached 30 s).

## Rate limits

| Policy | Limit | Partition |
|---|---|---|
| Global | 300 / min | user (or IP) |
| `auth` | 10 / min | IP |
| `invite-lookup` | 20 / min | IP |
| `sos-create` | 5 / 10 min (sliding) | user |
| `location-update` | 6 / min | user |

## Public endpoints in this module

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/health/live` | anonymous | Liveness (no dependencies) |
| GET | `/health/ready` | anonymous | Readiness (SQL reachable) |
| GET | `/api/v1/app-info` | anonymous | Policy version, minimum app versions, emergency numbers, grievance contact |
| GET | `/api/v1/session` | bearer | Current user id, role and accepted policy version |
