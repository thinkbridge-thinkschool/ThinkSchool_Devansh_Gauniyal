namespace Capstone.SharedKernel;

// A record, not a ValueObject subclass - see the note on ValueObject.cs. Deliberately
// carries its currency rather than assuming one: "currency consistency" is a real
// invariant this domain needs (an invoice and its purchase order must agree), and
// that's only checkable if Money never lets you forget which currency you're holding.
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new ArgumentException("Currency is required.", nameof(currency));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amounts cannot be negative.");
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0, currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        if (right.Amount > left.Amount)
        {
            throw new InvalidOperationException(
                $"Cannot subtract {right} from {left}: result would be negative.");
        }
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static bool operator >(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount > right.Amount;
    }

    public static bool operator <(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount < right.Amount;
    }

    public static bool operator >=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount >= right.Amount;
    }

    public static bool operator <=(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount <= right.Amount;
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot compare or combine {left.Currency} with {right.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
