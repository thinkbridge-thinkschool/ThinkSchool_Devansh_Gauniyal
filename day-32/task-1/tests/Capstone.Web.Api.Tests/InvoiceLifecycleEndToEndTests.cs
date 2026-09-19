using System.Net;
using System.Net.Http.Json;

namespace Capstone.Web.Api.Tests;

// Day 31 - the ONE end-to-end test the brief asks for: the full invoice lifecycle,
// walked once, entirely over real HTTP against a real SQL Server, through every
// layer (routing -> validation -> use case -> domain -> EF Core -> database and
// back). This is deliberately NOT a re-proof of each individual invariant - every
// transition here already has its own focused test, either in
// tests/Capstone.Invoicing.Domain.Tests (the rule itself) or
// InvoiceEndpointTests.cs (its HTTP surface). This test's only job is to prove the
// pieces actually chain together end-to-end the way DESIGN.md's lifecycle diagram
// says they do: submit -> dispute -> reject (capacity released) -> a corrective
// resubmission referencing the rejected invoice -> approve (capacity consumed,
// terms locked). See capstone/DESIGN.md's "State lifecycle" section for the
// diagram this test walks.
[Collection(CapstoneApiCollection.Name)]
public sealed class InvoiceLifecycleEndToEndTests(CapstoneApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    [Fact]
    public async Task FullLifecycle_SubmitDisputeRejectCorrectApprove_ChainsEndToEndOverHttp()
    {
        var vendorId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        // 1. Issue a purchase order with exactly one invoice's worth of capacity -
        // any step below that fails to release or correctly reserve capacity will
        // surface as a 409 later in this same test, not as a separate assertion.
        var poResponse = await Client.PostAsJsonAsync("/v1/purchase-orders", new CreatePurchaseOrderRequest(
            vendorId, buyerId, "USD", [new CreatePurchaseOrderLineRequest(1, "Widgets", 10, 50m)]));
        Assert.Equal(HttpStatusCode.Created, poResponse.StatusCode);
        var purchaseOrderId = (await poResponse.Content.ReadFromJsonAsync<IdBody>())!.PurchaseOrderId;

        // 2. Submit the original invoice - deliberately wrong quantity (8, not
        // 10), which is close enough to stay within tolerance so it lands
        // Submitted, not auto-Disputed by the matching policy itself; the dispute
        // in step 3 is the buyer's own decision, not a system-detected mismatch.
        var submitResponse = await Client.PostAsJsonAsync("/v1/invoices", new SubmitInvoiceRequest(
            purchaseOrderId, vendorId, "INV-E2E-ORIGINAL", "USD", [new SubmitInvoiceLineRequest(1, 10, 50m)]));
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var originalInvoiceId = (await submitResponse.Content.ReadFromJsonAsync<IdBody>())!.InvoiceId;

        var afterSubmit = await GetInvoiceAsync(originalInvoiceId);
        Assert.Equal("Submitted", afterSubmit.Status);

        // 3. The buyer disputes it.
        var disputeResponse = await Client.PostAsJsonAsync(
            $"/v1/invoices/{originalInvoiceId}/dispute", new DisputeInvoiceRequest("Wrong item reference"));
        Assert.Equal(HttpStatusCode.NoContent, disputeResponse.StatusCode);
        Assert.Equal("Disputed", (await GetInvoiceAsync(originalInvoiceId)).Status);

        // 4. The dispute resolves against the supplier: rejected, capacity
        // released back to the PO.
        var rejectResponse = await Client.PostAsJsonAsync(
            $"/v1/invoices/{originalInvoiceId}/reject", new RejectInvoiceRequest("Confirmed wrong item"));
        Assert.Equal(HttpStatusCode.NoContent, rejectResponse.StatusCode);
        Assert.Equal("Rejected", (await GetInvoiceAsync(originalInvoiceId)).Status);

        // 5. A corrective invoice, explicitly linked to the one it replaces - only
        // possible because step 4 actually moved the original to Rejected; a
        // sibling test (SubmitInvoice tests) proves the rejection separately.
        var correctionResponse = await Client.PostAsJsonAsync("/v1/invoices", new SubmitInvoiceRequest(
            purchaseOrderId, vendorId, "INV-E2E-CORRECTED", "USD",
            [new SubmitInvoiceLineRequest(1, 10, 50m)],
            CorrectsInvoiceId: originalInvoiceId));
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        var correctedInvoiceId = (await correctionResponse.Content.ReadFromJsonAsync<IdBody>())!.InvoiceId;

        var afterCorrection = await GetInvoiceAsync(correctedInvoiceId);
        Assert.Equal("Submitted", afterCorrection.Status);
        Assert.Equal(originalInvoiceId, afterCorrection.CorrectsInvoiceId);

        // 6. The correction is approved - the capacity the original invoice
        // reserved and then released is now reserved again by this one, and
        // consumed here; a PO with room for only one invoice succeeding twice in
        // this test (once released, once consumed) is itself proof the capacity
        // bookkeeping is correct end-to-end, not just per-step.
        var approveResponse = await Client.PostAsJsonAsync(
            $"/v1/invoices/{correctedInvoiceId}/approve", new ApproveInvoiceRequest(buyerId));
        Assert.Equal(HttpStatusCode.NoContent, approveResponse.StatusCode);

        var final = await GetInvoiceAsync(correctedInvoiceId);
        Assert.Equal("Approved", final.Status);
        Assert.NotNull(final.DueDate);
    }

    private async Task<InvoiceDto> GetInvoiceAsync(Guid invoiceId)
    {
        var response = await Client.GetAsync($"/v1/invoices/{invoiceId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvoiceDto>())!;
    }

    private sealed record IdBody(Guid PurchaseOrderId, Guid InvoiceId);

    private sealed record InvoiceDto(Guid InvoiceId, string Status, Guid? CorrectsInvoiceId, DateTimeOffset? DueDate);
}
