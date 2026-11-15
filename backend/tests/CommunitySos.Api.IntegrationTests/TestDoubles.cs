using System.Collections.Concurrent;
using System.Linq.Expressions;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CommunitySos.Api.IntegrationTests;

/// <summary>Blob storage in a dictionary; read URLs look like SAS links.</summary>
public sealed class InMemoryBlobStorage : IBlobStorage
{
    public ConcurrentDictionary<string, (byte[] Content, string ContentType)> Blobs { get; } = new();

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (!Blobs.TryAdd(blobName, (buffer.ToArray(), contentType)))
        {
            throw new InvalidOperationException($"Blob {blobName} already exists.");
        }
    }

    /// <summary>Blob names whose delete throws (storage outage), for retention retry tests.</summary>
    public ConcurrentDictionary<string, bool> FailingDeletes { get; } = new();

    public Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        if (FailingDeletes.ContainsKey(blobName))
        {
            throw new IOException($"Simulated storage failure deleting {blobName}.");
        }

        Blobs.TryRemove(blobName, out _);
        return Task.CompletedTask;
    }

    public Task<Uri> GetReadUriAsync(string blobName, CancellationToken cancellationToken) =>
        Task.FromResult(new Uri($"https://blob.test/sos-media/{blobName}?sv=2025&sp=r&sig=fake"));
}

/// <summary>Records enqueued jobs; <see cref="RunAllAsync"/> executes them like Hangfire would (fresh DI scope each).</summary>
public sealed class RecordingJobScheduler(IServiceScopeFactory scopes) : IJobScheduler
{
    private readonly ConcurrentQueue<(string Name, JobQueue Queue, TimeSpan? Delay, Func<IServiceProvider, Task> Run)> _jobs = new();

    public IReadOnlyList<string> Enqueued => _jobs.Select(j => j.Name).ToList();

    /// <summary>Scheduled (delayed) jobs as "Job.Method@delay".</summary>
    public IReadOnlyList<string> Scheduled => _jobs.Where(j => j.Delay is not null).Select(j => $"{j.Name}@{j.Delay}").ToList();

    public ConcurrentDictionary<string, string> Recurring { get; } = new();

    public void AddOrUpdateRecurring<TJob>(string jobId, Expression<Func<TJob, Task>> job, string cron)
        where TJob : class => Recurring[jobId] = cron;

    public void Enqueue<TJob>(Expression<Func<TJob, Task>> job, JobQueue queue = JobQueue.Default)
        where TJob : class => Add(job, queue, null);

    public void Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay, JobQueue queue = JobQueue.Default)
        where TJob : class => Add(job, queue, delay);

    public async Task RunAllAsync()
    {
        while (_jobs.TryDequeue(out var job))
        {
            await using var scope = scopes.CreateAsyncScope();
            await job.Run(scope.ServiceProvider);
        }
    }

    private void Add<TJob>(Expression<Func<TJob, Task>> job, JobQueue queue, TimeSpan? delay)
        where TJob : class
    {
        var compiled = job.Compile();
        var method = job.Body is MethodCallExpression call ? call.Method.Name : "?";
        _jobs.Enqueue(($"{typeof(TJob).Name}.{method}", queue, delay, sp => compiled(sp.GetRequiredService<TJob>())));
    }
}

/// <summary>
/// Records every push. Addresses (FID or token) starting with "dead-" are reported as unregistered,
/// and "flaky-" as a transient failure.
/// </summary>
public sealed class FakePushSender : IPushSender
{
    public ConcurrentQueue<(PushMessage Message, IReadOnlyList<PushTarget> Targets)> Sent { get; } = new();

    /// <summary>(message type, FID or token) per device push.</summary>
    public IEnumerable<(string Type, string Token)> Deliveries =>
        Sent.SelectMany(s => s.Targets.Select(t => (s.Message.Type, t.Address)));

    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushTarget> targets, PushMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue((message, targets));
        return Task.FromResult<IReadOnlyList<PushResult>>(targets
            .Select(t => t.Address switch
            {
                var a when a.StartsWith("dead-", StringComparison.Ordinal) => new PushResult(t.DeviceId, PushOutcome.InvalidToken, "Unregistered"),
                var a when a.StartsWith("flaky-", StringComparison.Ordinal) => new PushResult(t.DeviceId, PushOutcome.Failed, "Unavailable"),
                _ => new PushResult(t.DeviceId, PushOutcome.Sent),
            })
            .ToList());
    }
}

public sealed class FakeGeocoder : IGeocoder
{
    public const string Address = "Law Garden, Ellisbridge, Ahmedabad, Gujarat 380006, India";

    public Task<string?> ReverseGeocodeAsync(GeoCoordinate coordinate, CancellationToken cancellationToken) => Task.FromResult<string?>(Address);
}

/// <summary>InMemory cannot evaluate SQL computed columns; give new SOS rows a reference number like SQL would.</summary>
public sealed class ReferenceNoInterceptor : SaveChangesInterceptor
{
    private static long _sequence;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        foreach (var entry in eventData.Context!.ChangeTracker.Entries<SosRequest>().Where(e => e.State == EntityState.Added))
        {
            if (entry.Entity.ReferenceNo is null)
            {
                var next = Interlocked.Increment(ref _sequence);
                entry.Property(s => s.ReferenceSeq).CurrentValue = next;
                entry.Property(s => s.ReferenceNo).CurrentValue = $"SOS-{next:D8}";
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
