using System.Net;
using System.Net.Http.Json;

namespace Capstone.Web.Api.Tests;

// "Integration tests via WebApplicationFactory, exercising real endpoints through
// the real layers" (Day 31 brief) - every test below issues a real HTTP request
// against a real ASP.NET Core pipeline (routing, the EnforceMaxBodySize endpoint
// filter, model binding, validation) backed by a real SQL Server engine. No use
// case or repository is called directly - see tests/Capstone.Integration.Tests for
// that layer instead. Auth is not configured for this fixture (see
// CapstoneApiFixture's comment), matching the same local-dev trade-off
// Program.cs's own `authConfigured` check already documents.
[Collection(CapstoneApiCollection.Name)]
public sealed class InvoiceEndpointTests(CapstoneApiFixture fixture)
{
    private HttpClient Client => fixture.Client;

    private async Task<Guid> CreatePurchaseOrderAsync(Guid vendorId, int orderedQuantity = 10, decimal unitPrice = 50m)
    {
        var request = new CreatePurchaseOrderRequest(
            vendorId,
            Guid.NewGuid(),
            "USD",
            [new CreatePurchaseOrderLineRequest(1, "Widgets", orderedQuantity, unitPrice)]);

        var response = await Client.PostAsJsonAsync("/v1/purchase-orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedPurchaseOrder>();
        return body!.PurchaseOrderId;
    }

    [Fact]
    public async Task SubmitInvoice_WithinTolerance_Returns201AndIsRetrievable()
    {
        var vendorId = Guid.NewGuid();
        var purchaseOrderId = await CreatePurchaseOrderAsync(vendorId);

        var submitRequest = new SubmitInvoiceRequest(
            purchaseOrderId, vendorId, "INV-HTTP-1", "USD",
            [new SubmitInvoiceLineRequest(1, 10, 50m)]);

        var submitResponse = await Client.PostAsJsonAsync("/v1/invoices", submitRequest);
        Assert.Equal(HttpStatusCode.Created, submitResponse.StatusCode);
        var created = await submitResponse.Content.ReadFromJsonAsync<CreatedInvoice>();

        var getResponse = await Client.GetAsync($"/v1/invoices/{created!.InvoiceId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var invoice = await getResponse.Content.ReadFromJsonAsync<InvoiceDto>();

        Assert.Equal("Submitted", invoice!.Status);
        Assert.Equal(500m, invoice.Total);
    }

    [Fact]
    public async Task SubmitInvoice_AgainstUnknownPurchaseOrder_Returns409()
    {
        var request = new SubmitInvoiceRequest(
            Guid.NewGuid(), Guid.NewGuid(), "INV-HTTP-2", "USD",
            [new SubmitInvoiceLineRequest(1, 1, 10m)]);

        var response = await Client.PostAsJsonAsync("/v1/invoices", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitInvoice_WithNoLines_Returns400ValidationProblem()
    {
        var vendorId = Guid.NewGuid();
        var purchaseOrderId = await CreatePurchaseOrderAsync(vendorId);

        var request = new SubmitInvoiceRequest(purchaseOrderId, vendorId, "INV-HTTP-3", "USD", []);

        var response = await Client.PostAsJsonAsync("/v1/invoices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Approve_ThenApproveAgain_SecondCallReturns409()
    {
        var vendorId = Guid.NewGuid();
        var purchaseOrderId = await CreatePurchaseOrderAsync(vendorId);
        var invoiceId = await SubmitAsync(purchaseOrderId, vendorId, "INV-HTTP-4");

        var approveRequest = new ApproveInvoiceRequest(Guid.NewGuid());
        var first = await Client.PostAsJsonAsync($"/v1/invoices/{invoiceId}/approve", approveRequest);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await Client.PostAsJsonAsync($"/v1/invoices/{invoiceId}/approve", approveRequest);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Dispute_ThenReject_ReleasesCapacityForAFreshSubmission()
    {
        var vendorId = Guid.NewGuid();
        // Ordered quantity 10 at 50/unit = 500 capacity - exactly one invoice's
        // worth, so a second submission only succeeds if Reject actually released
        // the first one's reservation back to the PO.
        var purchaseOrderId = await CreatePurchaseOrderAsync(vendorId, orderedQuantity: 10, unitPrice: 50m);
        var invoiceId = await SubmitAsync(purchaseOrderId, vendorId, "INV-HTTP-5");

        var disputeResponse = await Client.PostAsJsonAsync(
            $"/v1/invoices/{invoiceId}/dispute", new DisputeInvoiceRequest("Quantity mismatch"));
        Assert.Equal(HttpStatusCode.NoContent, disputeResponse.StatusCode);

        var rejectResponse = await Client.PostAsJsonAsync(
            $"/v1/invoices/{invoiceId}/reject", new RejectInvoiceRequest("Confirmed incorrect"));
        Assert.Equal(HttpStatusCode.NoContent, rejectResponse.StatusCode);

        var secondInvoiceId = await SubmitAsync(purchaseOrderId, vendorId, "INV-HTTP-6");
        Assert.NotEqual(Guid.Empty, secondInvoiceId);
    }

    [Fact]
    public async Task Withdraw_UnknownInvoice_Returns409()
    {
        var response = await Client.PostAsync($"/v1/invoices/{Guid.NewGuid()}/withdraw", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ListInvoices_ReturnsPagedResults()
    {
        var vendorId = Guid.NewGuid();
        var purchaseOrderId = await CreatePurchaseOrderAsync(vendorId, orderedQuantity: 100);
        await SubmitAsync(purchaseOrderId, vendorId, $"INV-HTTP-LIST-{Guid.NewGuid():N}", quantity: 1);

        var response = await Client.GetAsync("/v1/invoices?page=1&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PagedInvoices>();
        Assert.Equal(5, body!.PageSize);
        Assert.NotNull(body.Items);
    }

    [Fact]
    public async Task EveryResponse_CarriesTheDay27SecurityHeaders()
    {
        var response = await Client.GetAsync("/v1/invoices?pageSize=1");

        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var values));
        Assert.Contains("nosniff", values!);
        Assert.True(response.Headers.Contains("Cross-Origin-Resource-Policy"));
    }

    private async Task<Guid> SubmitAsync(Guid purchaseOrderId, Guid vendorId, string invoiceNumber, int quantity = 10)
    {
        var request = new SubmitInvoiceRequest(
            purchaseOrderId, vendorId, invoiceNumber, "USD",
            [new SubmitInvoiceLineRequest(1, quantity, 50m)]);

        var response = await Client.PostAsJsonAsync("/v1/invoices", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreatedInvoice>();
        return body!.InvoiceId;
    }

    private sealed record CreatedPurchaseOrder(Guid PurchaseOrderId);

    private sealed record CreatedInvoice(Guid InvoiceId);

    private sealed record InvoiceDto(Guid InvoiceId, string Status, decimal Total);

    private sealed record PagedInvoices(int Page, int PageSize, List<object> Items);
}
