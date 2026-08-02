using System;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class ReconcileCollectionMetadataTaskTests
{
    [Theory]
    [InlineData(1, 15)]
    [InlineData(2, 30)]
    [InlineData(3, 60)]
    [InlineData(8, 1440)]
    [InlineData(20, 1440)]
    public void RetryDelayUsesBoundedExponentialBackoff(int attempts, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            ReconcileCollectionMetadataTask.RetryDelay(attempts));
    }
}
