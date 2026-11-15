# Module 2: Database

## What lives where

| Concern | Source of truth |
|---|---|
| Tables, PKs/FKs, unique and filtered indexes, CHECK constraints, `SosReferenceSeq` sequence | **EF Core model** (`backend/src/CommunitySos.Infrastructure/Persistence/Configurations`) → migrations |
| Spatial indexes, triggers, stored procedures | **T-SQL scripts** in `database/scripts`, embedded in Infrastructure and applied by `DatabaseInitializer` after migrations. All scripts are idempotent. |
| Master Admin seed | `DatabaseInitializer` from config `Seed:MasterAdmin:PhoneNumber` |

## Tables

`Users`, `Invitations`, `ReferralHistory`, `UserConsents`, `RefreshTokens`, `UserDevices`, `UserLocations`, `LocationKeys` (Module 6), `SosRequests`, `SosAttachments`, `SosStatusHistory`, `SosNotifications`, `AuditLogs`. Columns and the ER diagram are in [01-architecture.md §4](01-architecture.md#4-database-design).

## Rules enforced by the database (not just by code)

| Rule | Mechanism |
|---|---|
| Only one Master Admin | Filtered unique index `UX_Users_SingleMasterAdmin` (`WHERE Role = 3`) |
| Everyone except the Master Admin has a referrer | `CK_Users_Referral` |
| The referrer can never change | Trigger `TR_Users_ReferralImmutable` (error 51001) |
| One referral record per user | `UX_ReferralHistory_ReferredUserId` |
| Invites cannot exceed max uses | `CK_Invitations_UsedCount` + row-version concurrency |
| No duplicate SOS from a retried submit | `UX_SosRequests_Requester_ClientRequestId` |
| A user is alerted at most once per SOS | `UX_SosNotifications_Sos_Recipient` + `sp_getapplock` in the dispatch procedure |
| One device belongs to one account | `UX_UserDevices_InstallationId` and `UX_UserDevices_FcmToken` (both filtered `IS NOT NULL`) |
| Each device has exactly one push target (FID, or a legacy token) | `CK_UserDevices_Target` (migration `DeviceInstallationIds`, Module 5) |
| At most one usable background-location key per phone and account | `UX_LocationKeys_User_Device_Active` (filtered `RevokedAt IS NULL`; migration `BackgroundLocationKeys`, Module 6) |
| History, consent and audit records cannot be altered | Append-only triggers on `ReferralHistory`, `SosStatusHistory`, `UserConsents`, `AuditLogs` (error 51002); deletion only through the purge session flag |
| Registration only with Terms consent and an 18+ declaration (DPDP s.9) | Domain rule in `Invitation.Redeem`; `Users.AdultConfirmedAt` / `AcceptedPolicyVersion` columns, plus a `UserConsents` row in the same transaction |
| A rejection must carry remarks | `CK_SosRequests_RejectRemarks` |

## Stored procedures

| Procedure | Used by |
|---|---|
| `usp_SelectSosRecipients @SosRequestId, @RadiusMeters=5000, @FreshHours=12, @MaxAccuracyMeters=500, @MaxAttempts=5` | Alert-dispatch background job (Module 5) |
| `usp_GetDashboardStats @AdminId NULL, @FromUtc, @ToUtc` | `GET /admin/dashboard` (Module 3e) |
| ~~`usp_ExpireStaleSos`~~ | Removed in Module 3e. Expiry runs as the recurring `ExpireStaleSosJob` through the domain model, so it writes the same timeline entries and is unit-tested. |

## Running it locally

Prerequisites: **.NET 10 SDK**, and SQL Server 2022 or LocalDB (installed with Visual Studio), or Docker.

```powershell
# 1. Tools (once)
dotnet tool install --global dotnet-ef

# 2. Build and run the domain tests
cd backend
dotnet build
dotnet test

# 3. Create the initial migration (once; commit the generated files)
dotnet ef migrations add InitialCreate `
  --project src/CommunitySos.Infrastructure `
  --startup-project src/CommunitySos.Infrastructure `
  --output-dir Persistence/Migrations

# 4. Optional: target a different server than LocalDB
$env:COMMUNITYSOS_SQL = "Server=localhost,1433;Database=CommunitySos;User Id=sa;Password=<pwd>;TrustServerCertificate=True"

# 5. Review the full SQL the migration will run
dotnet ef migrations script --idempotent `
  --project src/CommunitySos.Infrastructure `
  --startup-project src/CommunitySos.Infrastructure `
  --output ../database/generated/schema.sql
```

The `InitialCreate` migration is already generated and committed in `Persistence/Migrations`, and `dotnet-ef` is a repo-local tool (`dotnet tool restore`). Apply everything (migrations, then programmability scripts, then the Master Admin seed) with:

```powershell
dotnet run --project src/CommunitySos.Api -- --migrate
```

SQL Server in Docker:

```powershell
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=<StrongPwd!1>" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

## Smoke test for the 5 km search (after seeding test data)

```sql
-- Two users: one ~1 km away and one ~8 km away from the SOS point.
-- Expect exactly one row in SosNotifications after:
EXEC dbo.usp_SelectSosRecipients @SosRequestId = '<approved sos id>';
SELECT RecipientUserId, DistanceMeters FROM dbo.SosNotifications WHERE SosRequestId = '<approved sos id>';
-- Running the procedure again must not add rows.
```

Automated in Module 7: `SqlServerTests` run this and more against SQL Server in Docker ([07](07-testing-deployment.md#sql-server-tests-testcontainers)).

## Deployed databases

In Azure, `--migrate` also installs the Hangfire `jobs` schema and creates the API's least-privilege user for its managed identity (`Database:AppPrincipal:*`), so the running app has no DDL rights. It runs from the deploy pipeline ([07](07-testing-deployment.md#cicd-github-actions)).
