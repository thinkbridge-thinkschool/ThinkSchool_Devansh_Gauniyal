using Microsoft.Data.Sqlite;

namespace ServiceBusDemo.Core;

/// <summary>
/// Application-level dedupe on message id, backed by SQLite so it is shared safely
/// across multiple competing-consumer instances. Two layers make "have I already
/// handled this message id?" atomic: SQLite's own file locking handles separate
/// processes/connections racing on the same database file, and the internal <see cref="_lock"/>
/// serializes access from multiple threads sharing one <see cref="SqliteConnection"/>
/// instance in this process — Microsoft.Data.Sqlite does not guarantee a single connection
/// object is safe to use concurrently from multiple threads.
/// </summary>
public sealed class IdempotencyStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    public IdempotencyStore(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();

        using var create = _connection.CreateCommand();
        create.CommandText =
            """
            CREATE TABLE IF NOT EXISTS processed_messages (
                message_id TEXT PRIMARY KEY,
                consumer_instance TEXT NOT NULL,
                processed_at_utc TEXT NOT NULL
            );
            """;
        create.ExecuteNonQuery();
    }

    /// <summary>
    /// Atomically records that <paramref name="messageId"/> is being processed by
    /// <paramref name="consumerInstance"/>. Returns true the first time a given message id
    /// is seen (caller should do real work); returns false on every subsequent call for the
    /// same message id (caller should skip work but still acknowledge the message).
    /// </summary>
    public bool TryMarkProcessed(string messageId, string consumerInstance)
    {
        lock (_lock)
        {
            using var insert = _connection.CreateCommand();
            insert.CommandText =
                """
                INSERT OR IGNORE INTO processed_messages (message_id, consumer_instance, processed_at_utc)
                VALUES ($id, $instance, $now);
                """;
            insert.Parameters.AddWithValue("$id", messageId);
            insert.Parameters.AddWithValue("$instance", consumerInstance);
            insert.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

            var rowsInserted = insert.ExecuteNonQuery();
            return rowsInserted == 1;
        }
    }

    public string? GetProcessingInstance(string messageId)
    {
        lock (_lock)
        {
            using var select = _connection.CreateCommand();
            select.CommandText = "SELECT consumer_instance FROM processed_messages WHERE message_id = $id;";
            select.Parameters.AddWithValue("$id", messageId);
            return select.ExecuteScalar() as string;
        }
    }

    public void Dispose() => _connection.Dispose();
}
