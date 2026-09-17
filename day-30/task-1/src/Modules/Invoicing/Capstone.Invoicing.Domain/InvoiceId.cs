namespace Capstone.Invoicing.Domain;

public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
