using System.Linq.Expressions;
using CommunitySos.Application.Abstractions;
using Hangfire;

namespace CommunitySos.Infrastructure.Jobs;

/// <summary>Hangfire-backed <see cref="IJobScheduler"/>. Jobs persist in SQL Server and survive restarts; failures retry with back-off.</summary>
public sealed class HangfireJobScheduler(IBackgroundJobClient client, IRecurringJobManager recurring) : IJobScheduler
{
    public const string CriticalQueue = "critical";
    public const string DefaultQueue = "default";

    public void Enqueue<TJob>(Expression<Func<TJob, Task>> job, JobQueue queue = JobQueue.Default)
        where TJob : class =>
        client.Enqueue(QueueName(queue), job);

    public void Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay, JobQueue queue = JobQueue.Default)
        where TJob : class =>
        client.Schedule(QueueName(queue), job, delay);

    public void AddOrUpdateRecurring<TJob>(string jobId, Expression<Func<TJob, Task>> job, string cron)
        where TJob : class =>
        recurring.AddOrUpdate(jobId, DefaultQueue, job, cron, new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

    private static string QueueName(JobQueue queue) => queue == JobQueue.Critical ? CriticalQueue : DefaultQueue;
}
