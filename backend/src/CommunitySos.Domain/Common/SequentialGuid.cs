using System.Buffers.Binary;
using System.Security.Cryptography;

namespace CommunitySos.Domain.Common;

/// <summary>
/// Generates GUIDs that are ordered the way SQL Server orders <c>uniqueidentifier</c>
/// (SQL Server compares bytes 10-15 first). The last 6 bytes carry a big-endian millisecond
/// timestamp, so new rows append to the end of the clustered index instead of fragmenting it.
/// The first 10 bytes are cryptographically random, so ids are not guessable.
/// </summary>
public static class SequentialGuid
{
    public static Guid NewGuid() => NewGuid(DateTimeOffset.UtcNow);

    internal static Guid NewGuid(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes[..10]);

        Span<byte> millis = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(millis, timestamp.ToUnixTimeMilliseconds());
        millis[2..].CopyTo(bytes[10..]); // low 48 bits (good until year ~10889)

        return new Guid(bytes);
    }
}
