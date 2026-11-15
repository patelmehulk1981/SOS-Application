using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Domain.Entities;

/// <summary>Append-only timeline of SOS status changes. <see cref="ChangedByUserId"/> null = system.</summary>
public sealed class SosStatusHistory : Entity<long>
{
    private SosStatusHistory()
    {
    }

    public Guid SosRequestId { get; private set; }

    public SosStatus? FromStatus { get; private set; }

    public SosStatus ToStatus { get; private set; }

    public Guid? ChangedByUserId { get; private set; }

    public string? Remarks { get; private set; }

    public DateTime ChangedAt { get; private set; }

    internal static SosStatusHistory Create(Guid sosRequestId, SosStatus? from, SosStatus to, Guid? changedByUserId, string? remarks, DateTime now) => new()
    {
        SosRequestId = sosRequestId,
        FromStatus = from,
        ToStatus = to,
        ChangedByUserId = changedByUserId,
        Remarks = remarks,
        ChangedAt = now,
    };
}
