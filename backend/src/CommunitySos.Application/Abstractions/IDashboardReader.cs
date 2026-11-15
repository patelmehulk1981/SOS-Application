using CommunitySos.Application.Features.Admin;

namespace CommunitySos.Application.Abstractions;

/// <summary>Dashboard counters in one round trip (dbo.usp_GetDashboardStats on SQL Server).</summary>
public interface IDashboardReader
{
    /// <param name="adminId">null = whole community (Master Admin).</param>
    Task<DashboardStats> GetAsync(Guid? adminId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken);
}
