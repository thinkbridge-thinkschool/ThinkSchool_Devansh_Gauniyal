namespace DapperComparison.Tests;

public class BehaviourTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BehaviourTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Ef_projection_leaves_the_change_tracker_empty()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        EfQueries.RunProjection(context, Comparison.SubmittedSinceUtc);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void Ef_tracked_leaves_the_change_tracker_populated()
    {
        using var context = new QuotesDbContext(_fixture.DbPath);

        EfQueries.RunTracked(context, Comparison.SubmittedSinceUtc);

        Assert.NotEmpty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void Dapper_sql_is_a_parameterised_compile_time_constant_with_no_interpolation_or_concatenation()
    {
        var path = TaskPaths.SourceFile("DapperQueries.cs");
        var text = File.ReadAllText(path);

        Assert.Contains("public const string Sql", text);
        Assert.DoesNotContain("$@\"", text);
        Assert.DoesNotContain("@$\"", text);
        Assert.DoesNotContain("string.Format", text);
        Assert.DoesNotContain("string.Concat", text);

        Assert.Contains("@SubmittedSinceUtc", DapperQueries.Sql);
    }
}
