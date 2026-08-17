using Xunit;

namespace Task1.Tests;

public class RecursiveCteTests
{
    [Fact]
    public void ThreeLevelChain_ResolvesToCorrectRootAndDepth()
    {
        using var db = TestDatabase.Create();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("21_recursive_cte_influence_chain.sql");
        using var reader = cmd.ExecuteReader();

        var found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) != "Ryan Holiday")
            {
                continue;
            }

            found = true;
            // Ryan Holiday -> Marcus Aurelius -> Epictetus -> Seneca is three levels deep.
            Assert.Equal("Seneca", reader.GetString(3));
            Assert.Equal(3, reader.GetInt32(4));
        }

        Assert.True(found, "Ryan Holiday should appear in the influence chain result.");
    }

    [Fact]
    public void RootAuthor_HasDepthZero_AndIsItsOwnAncestor()
    {
        using var db = TestDatabase.Create();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("21_recursive_cte_influence_chain.sql");
        using var reader = cmd.ExecuteReader();

        var found = false;
        while (reader.Read())
        {
            if (reader.GetString(1) != "Confucius")
            {
                continue;
            }

            found = true;
            Assert.Equal(reader.GetInt32(0), reader.GetInt32(2)); // RootAncestorId == AuthorId
            Assert.Equal("Confucius", reader.GetString(3));
            Assert.Equal(0, reader.GetInt32(4));
        }

        Assert.True(found, "Confucius should appear in the influence chain result.");
    }

    [Fact]
    public async Task CycleInInfluenceChain_TerminatesInsteadOfLoopingForever()
    {
        // A synthetic 2-author cycle (A influences B, B influences A) that does not exist
        // in the shipped seed data -- this test exists purely to prove the depth cap in
        // 21_recursive_cte_influence_chain.sql actually stops recursion on a cycle, since
        // SQLite itself has no built-in cycle detection for a self-referencing walk.
        using var db = TestDatabase.Create();

        using (var seedCycle = db.Connection.CreateCommand())
        {
            seedCycle.CommandText = """
                PRAGMA foreign_keys = OFF;
                INSERT INTO Authors (Id, Name, InfluencedByAuthorId) VALUES (101, 'Cycle A', 102);
                INSERT INTO Authors (Id, Name, InfluencedByAuthorId) VALUES (102, 'Cycle B', 101);
                PRAGMA foreign_keys = ON;
                """;
            seedCycle.ExecuteNonQuery();
        }

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = SqlFiles.Read("21_recursive_cte_influence_chain.sql");

        var task = Task.Run(() =>
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
            }
        });

        var completed = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))) == task;
        Assert.True(completed, "Query should terminate even with a cycle in InfluencedByAuthorId.");
    }
}
