using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CommunitySos.Infrastructure.Persistence.Converters;

/// <summary>
/// SQL Server datetime2 has no time-zone. We always write UTC and tag values read back as UTC,
/// so serialisers emit a trailing "Z" and clients never misinterpret emergency timestamps.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
