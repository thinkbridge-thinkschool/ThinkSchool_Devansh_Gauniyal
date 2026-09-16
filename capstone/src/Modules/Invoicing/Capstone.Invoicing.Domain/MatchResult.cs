using System.Collections.ObjectModel;
using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Recorded as DATA on a Submitted or Disputed invoice, not modelled as a state -
// matching is synchronous and happens once, during Submit(). See DESIGN.md, "no
// Matched state".
//
// LineVariances is typed ReadOnlyCollection<T>, not IReadOnlyCollection<T> - see
// Invoice.Lines's comment: EF Core's complex-type collection mapping requires the
// property's declared type to implement IList<T>. Behaviourally still read-only
// (every mutating member throws NotSupportedException); only the compile-time
// signature changed.
public sealed record MatchResult(bool WithinTolerance, ReadOnlyCollection<LineVariance> LineVariances)
{
    // EF Core materialization only - LineVariances is itself a complex-type
    // collection, so this constructor's own parameter can't be bound either. See
    // Invoice.cs's private parameterless constructor for the fuller explanation.
    public MatchResult()
        : this(false, [])
    {
    }
}

public sealed record LineVariance(int LineNumber, Money Invoiced, Money PurchaseOrderLineValue, Money Variance)
{
    // EF Core materialization only - Invoiced/PurchaseOrderLineValue/Variance are
    // all Money, a complex property. See InvoiceLineItem.cs's identical constructor.
    public LineVariance()
        : this(0, default, default, default)
    {
    }
}
