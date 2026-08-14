using Microsoft.EntityFrameworkCore;
using QuotesApi.Telemetry;
using Xunit;

namespace QuotesApi.Tests;

public class RoundTripCountingInterceptorTests
{
    [Fact]
    public void NewInterceptor_StartsAtZero()
    {
        var interceptor = new RoundTripCountingInterceptor();

        Assert.Equal(0, interceptor.Count);
    }

    [Fact]
    public async Task Interceptor_IncrementsOnEachRealQuery()
    {
        using var db = TestDatabase.CreateSeeded();

        Assert.Equal(0, db.Interceptor.Count);

        await db.Context.Authors.ToListAsync();
        Assert.Equal(1, db.Interceptor.Count);

        await db.Context.Authors.ToListAsync();
        Assert.Equal(2, db.Interceptor.Count);
    }

    [Fact]
    public async Task Interceptor_ResetSetsCountBackToZero()
    {
        using var db = TestDatabase.CreateSeeded();
        await db.Context.Authors.ToListAsync();
        Assert.True(db.Interceptor.Count > 0);

        db.Interceptor.Reset();

        Assert.Equal(0, db.Interceptor.Count);
    }
}
