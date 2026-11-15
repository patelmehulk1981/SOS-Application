using CommunitySos.Domain.Common;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Domain.Tests;

public sealed class ValueObjectTests
{
    [Theory]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    public void Phone_numbers_are_normalised(string raw, string expected) =>
        Assert.Equal(expected, PhoneNumber.Create(raw).Value);

    [Theory]
    [InlineData("9876543210")]
    [InlineData("+0123456789")]
    [InlineData("+91abc")]
    [InlineData("")]
    public void Invalid_phone_numbers_are_rejected(string raw) =>
        Assert.Throws<DomainException>(() => PhoneNumber.Create(raw));

    [Fact]
    public void Phone_is_masked_for_logs() =>
        Assert.Equal("+91********10", PhoneNumber.Create("+919876543210").Masked);

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, 0)]
    public void Invalid_coordinates_are_rejected(double lat, double lng) =>
        Assert.Throws<DomainException>(() => new GeoCoordinate(lat, lng));

    [Fact]
    public void Point_uses_longitude_as_x_and_srid_4326()
    {
        var point = new GeoCoordinate(23.0225, 72.5714).ToPoint();

        Assert.Equal(72.5714, point.X);
        Assert.Equal(23.0225, point.Y);
        Assert.Equal(4326, point.SRID);
    }

    [Fact]
    public void Sequential_guids_sort_by_time_in_sql_server_order()
    {
        var earlier = SequentialGuid.NewGuid(DateTimeOffset.UnixEpoch.AddDays(1));
        var later = SequentialGuid.NewGuid(DateTimeOffset.UnixEpoch.AddDays(2));

        // SQL Server orders uniqueidentifier by bytes 10..15 first.
        var a = earlier.ToByteArray()[10..];
        var b = later.ToByteArray()[10..];
        Assert.True(a.AsSpan().SequenceCompareTo(b) < 0);
    }
}
