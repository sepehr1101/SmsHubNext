using SmsHubNext.Features.DispatchOperations;
using Xunit;

namespace SmsHubNext.UnitTests.Features.DispatchOperations;

public sealed class LiveDispatchBucketsTests
{
    [Fact]
    public void Uses_sixty_complete_utc_buckets_and_fills_gaps()
    {
        DateTime now = new(2026, 10, 18, 10, 0, 19, DateTimeKind.Utc);
        IReadOnlyList<LiveBucket> buckets = LiveDispatchBuckets.Fill(now,
            [new(new DateTime(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc), 7)]);
        Assert.Equal(60, buckets.Count);
        Assert.Equal(new DateTime(2026, 10, 18, 9, 50, 10, DateTimeKind.Utc), buckets[0].StartUtc);
        Assert.Equal(7, buckets[^1].Count);
        Assert.All(buckets.Take(59), bucket => Assert.Equal(0, bucket.Count));
        Assert.All(buckets, bucket => { Assert.Equal(0, bucket.StartUtc.Second % 10); Assert.Equal(DateTimeKind.Utc, bucket.StartUtc.Kind); });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 10)]
    [InlineData(59, 50)]
    public void Floors_without_rounding_forward(int seconds, int expected)
    {
        DateTime instant = new(2026, 10, 18, 0, 0, seconds, 999, DateTimeKind.Utc);
        Assert.Equal(expected, LiveDispatchBuckets.Floor(instant).Second);
        Assert.Equal(0, LiveDispatchBuckets.Floor(instant).Millisecond);
    }
}
