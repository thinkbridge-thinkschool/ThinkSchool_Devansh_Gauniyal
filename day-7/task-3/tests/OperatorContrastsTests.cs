using Xunit;

namespace Task3.Tests;

public class OperatorContrastsTests
{
    [Fact]
    public void Except_And_EquivalentLeftJoinWithHaving_ReturnIdenticalSets()
    {
        using var db = TestDatabase.Create();
        var contrasts = OperatorContrastsQuery.Execute(db.Connection);

        Assert.Equal(
            new HashSet<string>(contrasts.ExceptAuthorNames),
            new HashSet<string>(contrasts.LeftJoinAuthorNames));
    }
}
