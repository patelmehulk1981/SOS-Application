# Community SOS

Invite-only community emergency app: members raise an SOS, their admin approves it, and neighbours within 5 km are alerted.

| Part | Stack | Start here |
|---|---|---|
| API and jobs | ASP.NET Core 10, EF Core, SQL Server (geography), Hangfire | [backend](backend) · [docs/03a](docs/03a-api-foundation.md) |
| Database | Azure SQL / SQL Server, T-SQL scripts | [database](database) · [docs/02](docs/02-database.md) |
| Mobile app | Expo SDK 58 (React Native), Firebase Auth + FCM | [mobile/README.md](mobile/README.md) |
| Invite landing page | Static HTML (App Links / Universal Links) | [web/invite-site](web/invite-site) |
| Infrastructure | Bicep (Azure, Central India) | [infra/bicep](infra/bicep) · [docs/07](docs/07-testing-deployment.md) |
| CI/CD | GitHub Actions, EAS | [.github/workflows](.github/workflows) |

## Documentation

1. [Architecture](docs/01-architecture.md)
2. [Database](docs/02-database.md)
3. API: [foundation](docs/03a-api-foundation.md) · [auth and profile](docs/03b-auth-profile.md) · [invitations](docs/03c-invitations-admin.md) · [SOS](docs/03d-sos.md) · [admin review](docs/03e-admin-review.md)
4. Mobile: [foundation](docs/04a-mobile-foundation.md) · [SOS](docs/04b-mobile-sos.md) · [admin](docs/04c-mobile-admin.md)
5. [Push and neighbour alerts](docs/05-push-alerts.md)
6. [Background location](docs/06-background-location.md)
7. [Testing, retention and deployment](docs/07-testing-deployment.md)
8. [Release checklist](docs/08-release-checklist.md)

## Quick start (local)

```powershell
cd backend; dotnet test                                    # SQL Server tests run too if Docker is running
dotnet run --project src/CommunitySos.Api -- --migrate     # LocalDB by default
dotnet run --project src/CommunitySos.Api --launch-profile mobile-dev
cd ../mobile; npm ci; copy .env.example .env.local; npx expo start
```

Dependency rule: only versions published at least two weeks earlier (see `backend/Directory.Packages.props` and `mobile/README.md`).
