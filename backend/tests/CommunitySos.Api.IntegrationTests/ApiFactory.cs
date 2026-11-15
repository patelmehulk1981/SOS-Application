using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using CommunitySos.Infrastructure.Persistence;
using CommunitySos.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CommunitySos.Api.IntegrationTests;

/// <summary>
/// Hosts the real API pipeline in memory ("Testing" environment) with:
///  - EF Core InMemory instead of SQL Server (business flows only; SQL constraints/triggers/procs are
///    covered by Testcontainers tests in Module 7),
///  - a fake Firebase that accepts "fake:{uid}:{phone}",
///  - a controllable account-status reader.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string PolicyVersion = "2026-11";

    private static readonly string SigningKeyPem = CreateKeyPem();
    private readonly string _databaseName = $"sos-tests-{Guid.NewGuid():N}";

    public FakeUserStatusReader UserStatus { get; } = new();

    public FakeFirebase Firebase { get; } = new();

    public InMemoryBlobStorage Blobs { get; } = new();

    public FakePushSender Push { get; } = new();

    public RecordingJobScheduler Jobs => Services.GetRequiredService<RecordingJobScheduler>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Host settings are visible while Program.cs registers services.
        builder.UseSetting("ConnectionStrings:Sql", "Server=unused;Database=unused;TrustServerCertificate=True");
        builder.UseSetting("Jwt:SigningKeyPem", SigningKeyPem);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Error");
        // All test requests share one client IP; per-IP limits are covered by a dedicated test instead.
        builder.UseSetting("RateLimiting:AuthPerMinute", "100000");
        builder.UseSetting("RateLimiting:GlobalPerMinute", "100000");
        builder.UseSetting("RateLimiting:InviteLookupPerMinute", "100000");
        builder.UseSetting("RateLimiting:SosCreatePer10Minutes", "100000");
        builder.UseSetting("RateLimiting:LocationUpdatesPerMinute", "100000");
        builder.UseSetting("Jobs:Storage", "InMemory");
        builder.UseSetting("Jobs:RunServer", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserStatusReader>();
            services.AddSingleton<IUserStatusReader>(UserStatus);

            services.RemoveAll<IFirebaseAuthService>();
            services.AddSingleton<IFirebaseAuthService>(Firebase);

            services.RemoveAll<IBlobStorage>();
            services.AddSingleton<IBlobStorage>(Blobs);
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
            services.RemoveAll<IGeocoder>();
            services.AddSingleton<IGeocoder, FakeGeocoder>();
            services.RemoveAll<IJobScheduler>();
            services.AddSingleton<RecordingJobScheduler>();
            services.AddSingleton<IJobScheduler>(sp => sp.GetRequiredService<RecordingJobScheduler>());

            // Swap SQL Server for InMemory (EF 9+ keeps provider configuration in IDbContextOptionsConfiguration<T>).
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>((sp, options) => options
                .UseInMemoryDatabase(_databaseName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(
                    new ReferenceNoInterceptor(),
                    sp.GetRequiredService<TimestampInterceptor>(),
                    sp.GetRequiredService<AuditInterceptor>()));
        });
    }

    /// <summary>Runs <paramref name="action"/> against the test database in its own scope.</summary>
    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) =>
        WithDbAsync(async db => { await action(db); return 0; });

    /// <summary>Seeds a Master Admin who has accepted the current policy.</summary>
    public async Task<User> SeedMasterAdminAsync()
    {
        var master = User.CreateMasterAdmin(PhoneNumber.Create(RandomPhone()), "Test Master");
        var consent = master.AcceptPolicy(PolicyVersion, confirmedAdult: true, DateTime.UtcNow, null, null);
        await WithDbAsync(async db =>
        {
            db.Users.Add(master);
            db.UserConsents.Add(consent);
            await db.SaveChangesAsync();
        });
        return master;
    }

    /// <summary>Registers someone through a fresh invitation from <paramref name="inviter"/> (Sub-Admin if the inviter is a Master).</summary>
    public async Task<User> SeedInviteeAsync(User inviter, string fullName)
    {
        var now = DateTime.UtcNow;
        var invitation = Invitation.Create(inviter, null, null, null, now);
        var result = invitation.Redeem(
            PhoneNumber.Create(RandomPhone()), $"uid-{Guid.NewGuid():N}", new RegistrationConsent(PolicyVersion, true, null), now, null, null);
        result.User.UpdateProfile(fullName, null, null, null, null);

        await WithDbAsync(async db =>
        {
            db.Invitations.Add(invitation);
            db.Users.Add(result.User);
            db.ReferralHistory.Add(result.Referral);
            db.UserConsents.Add(result.TermsConsent);
            await db.SaveChangesAsync();
        });
        return result.User;
    }

    public async Task<Invitation> SeedInvitationAsync(User creator, int? maxUses = null)
    {
        var invitation = Invitation.Create(creator, "test", maxUses, null, DateTime.UtcNow);
        await WithDbAsync(async db =>
        {
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync();
        });
        return invitation;
    }

    public HttpClient CreateClientFor(User user)
    {
        var client = CreateClient();
        using var scope = Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        return client;
    }

    public HttpClient CreateClientWithToken(string accessToken)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    public static User NewMasterAdmin() => User.CreateMasterAdmin(PhoneNumber.Create(RandomPhone()), "Test Master");

    public static string RandomPhone() => $"+9198{Random.Shared.NextInt64(10_000_000, 99_999_999)}";

    private static string CreateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}

public sealed class FakeUserStatusReader : IUserStatusReader
{
    private readonly ConcurrentDictionary<Guid, bool> _inactive = new();

    public void Deactivate(Guid userId) => _inactive[userId] = true;

    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(!_inactive.ContainsKey(userId));

    public void Invalidate(Guid userId)
    {
    }
}

/// <summary>Accepts ID tokens of the form "fake:{uid}:{phone}" and records deletions.</summary>
public sealed class FakeFirebase : IFirebaseAuthService
{
    public ConcurrentBag<string> DeletedUids { get; } = [];

    public static string TokenFor(string uid, string phone) => $"fake:{uid}:{phone}";

    public Task<FirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        var parts = idToken.Split(':');
        if (parts is not ["fake", var uid, var phone])
        {
            throw new AuthenticationFailedException("auth.invalid_firebase_token", "Invalid test token.");
        }

        return Task.FromResult(new FirebaseIdentity(uid, phone));
    }

    public Task DeleteUserAsync(string uid, CancellationToken cancellationToken)
    {
        DeletedUids.Add(uid);
        return Task.CompletedTask;
    }
}
