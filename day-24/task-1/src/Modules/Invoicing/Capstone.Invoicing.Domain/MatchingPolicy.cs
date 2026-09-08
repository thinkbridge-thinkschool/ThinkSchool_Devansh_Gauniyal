using Capstone.SharedKernel;

namespace Capstone.Invoicing.Domain;

// Configurable, not hardcoded into Invoice - see capstone/DESIGN.md: the default
// (lower of 1% of the PO line value or a small fixed amount) is a placeholder
// pending real input, not a researched figure, and production would likely vary this
// per buyer relationship. Passed into Invoice.Submit() as an explicit parameter so
// the domain logic never has a magic number baked in.
public sealed record MatchingPolicy(decimal TolerancePercentage, Money ToleranceAbsolute)
{
    public static MatchingPolicy Default(string currency) =>
        new(TolerancePercentage: 0.01m, ToleranceAbsolute: new Money(10m, currency));

    // The effective tolerance for one line is the LOWER of the two bounds - a fixed
    // percentage alone would let large lines drift by a lot; a fixed absolute alone
    // would be meaningless for large lines and too strict for small ones.
    public Money EffectiveToleranceFor(Money poLineValue)
    {
        var percentageTolerance = new Money(poLineValue.Amount * TolerancePercentage, poLineValue.Currency);
        return percentageTolerance < ToleranceAbsolute ? percentageTolerance : ToleranceAbsolute;
    }

    public bool IsWithinTolerance(Money invoicedLineAmount, Money poLineValue)
    {
        var difference = invoicedLineAmount > poLineValue
            ? invoicedLineAmount - poLineValue
            : poLineValue - invoicedLineAmount;
        return difference <= EffectiveToleranceFor(poLineValue);
    }
}
