using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace QuotesApi.Caching;

// Counts actual DB round trips at the ADO.NET command level, not cache lookups.
// ReaderExecuting fires once per real SqlLite command sent to the database, so this
// is a true count of round trips - including the N+1 queries inside
// AuthorQuoteSummaryQuery, which is the whole point of measuring the DB-load drop.
public sealed class CountingCommandInterceptor(DbQueryCounter counter) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        counter.Increment();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
