using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;

namespace CommunitySos.Domain.Tests;

public sealed class LocationKeyTests
{
    private static readonly string Hash = new('a', LocationKey.HashLength);
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Issued_key_is_active_until_revoked_once()
    {
        var key = LocationKey.Issue(Guid.NewGuid(), Hash, "install-1", Now);
        Assert.True(key.IsActive);

        key.Revoke(Now.AddMinutes(1), "turned_off");
        key.Revoke(Now.AddMinutes(2), "logout"); // first reason wins

        Assert.False(key.IsActive);
        Assert.Equal(Now.AddMinutes(1), key.RevokedAt);
        Assert.Equal("turned_off", key.RevokedReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Device_id_is_required(string deviceId) =>
        Assert.Throws<DomainException>(() => LocationKey.Issue(Guid.NewGuid(), Hash, deviceId, Now));

    [Fact]
    public void Hash_must_be_sha256_hex() =>
        Assert.Throws<ArgumentException>(() => LocationKey.Issue(Guid.NewGuid(), "short", "install-1", Now));
}
