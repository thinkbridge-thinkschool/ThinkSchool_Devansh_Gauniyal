namespace QueryTranslationDemo;

public record QueryEvidence(string RawSql, int RowCount);

public record BrokenQueryEvidence(string ExceptionType, string ExceptionMessage);

public record AsEnumerableEvidence(string RawSql, int RowsPulledIntoMemory, int RowCountAfterFilter);

public record EvidenceReport(
    string GeneratedAtUtc,
    string EfCoreVersion,
    string DotNetVersion,
    string RuntimeIdentifier,
    string Architecture,
    QueryEvidence Before,
    QueryEvidence After,
    BrokenQueryEvidence Broken,
    QueryEvidence FixedQuery,
    AsEnumerableEvidence AsEnumerableVariant);
