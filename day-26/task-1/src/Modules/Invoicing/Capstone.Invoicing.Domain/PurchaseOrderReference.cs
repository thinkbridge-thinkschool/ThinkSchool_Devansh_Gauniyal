namespace Capstone.Invoicing.Domain;

// Invoicing's OWN identifier for a purchase order - not a reference to
// Capstone.Procurement.Domain.PurchaseOrderId, and there is no project reference to
// that assembly from here. The two types happen to wrap the same Guid value at
// runtime (translated at the module boundary - see
// Capstone.Invoicing.Infrastructure/ProcurementCapacityAdapter.cs), but Invoicing's
// domain layer only ever knows its own type. This is what "referenced by ID only,
// across a module boundary" looks like in code, not just in prose.
public readonly record struct PurchaseOrderReference(Guid Value)
{
    public override string ToString() => Value.ToString();
}
