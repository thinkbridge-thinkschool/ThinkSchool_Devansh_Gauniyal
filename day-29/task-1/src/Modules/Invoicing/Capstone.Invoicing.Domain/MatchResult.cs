using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Recorded as DATA on a Submitted or Disputed invoice, not modelled as a state -
// matching is synchronous and happens once, during Submit(). See DESIGN.md, "no
// Matched state". Persisted as a single JSON column on Invoice (see
// InvoiceEntityTypeConfiguration.cs) via a manual System.Text.Json round trip,
// not EF Core's native complex-type collection mapping - the nested collection
// here (LineVariances) hit a real bug in that feature's JSON query-shaping code
// in EF Core 10.0.12 (a NullReferenceException inside
// RelationalShapedQueryCompilingExpressionVisitor.CreateJsonShapers, reproduced
// live against a real SQL Server integration test), so this and Invoice.Lines
// are value-converted JSON strings instead - see
// InvoiceEntityTypeConfiguration.cs's comment for the full story.
public sealed record MatchResult(bool WithinTolerance, IReadOnlyCollection<LineVariance> LineVariances);

public sealed record LineVariance(int LineNumber, Money Invoiced, Money PurchaseOrderLineValue, Money Variance);
