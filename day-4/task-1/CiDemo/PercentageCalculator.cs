namespace CiDemo;

public static class PercentageCalculator
{
    public static decimal ApplyDiscount(decimal amount, decimal discountPercent)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        if (discountPercent < 0 || discountPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(discountPercent), "Discount must be between 0 and 100.");
        }

        return amount - (amount * discountPercent / 100m);
    }

    public static decimal PercentageOf(decimal part, decimal whole)
    {
        if (whole == 0)
        {
            throw new DivideByZeroException("Whole cannot be zero.");
        }

        return part / whole * 100m;
    }

    public static bool MeetsThreshold(decimal measuredPercent, decimal thresholdPercent)
    {
        return measuredPercent >= thresholdPercent;
    }
}
