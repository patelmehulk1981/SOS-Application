using CommunitySos.Application.Common.Notifications;

namespace CommunitySos.Api.IntegrationTests;

public sealed class PushMessageTests
{
    [Theory]
    [InlineData(0, "Less than 500 m from you.")]
    [InlineData(499, "Less than 500 m from you.")]
    [InlineData(500, "About 0.5 km from you.")]
    [InlineData(1_200, "About 1 km from you.")]
    [InlineData(1_250, "About 1.5 km from you.")]
    [InlineData(4_999, "About 5 km from you.")]
    public void Distance_is_described_in_half_kilometre_steps(double meters, string expected) =>
        Assert.Equal(expected, PushMessages.DescribeDistance(meters));

    [Fact]
    public void At_most_ten_distance_groups_exist_within_5_km()
    {
        var groups = Enumerable.Range(0, 5_001).Select(m => PushMessages.DescribeDistance(m)).Distinct().Count();
        Assert.True(groups <= 11, $"{groups} groups");
    }
}
