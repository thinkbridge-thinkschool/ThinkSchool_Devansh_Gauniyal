using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Recorded as DATA on a Submitted or Disputed invoice, not modelled as a state -
// matching is synchronous and happens once, during Submit(). See DESIGN.md, "no
// Matched state".
public sealed record MatchResult(bool WithinTolerance, IReadOnlyCollection<LineVariance> LineVariances);

public sealed record LineVariance(int LineNumber, Money Invoiced, Money PurchaseOrderLineValue, Money Variance);
