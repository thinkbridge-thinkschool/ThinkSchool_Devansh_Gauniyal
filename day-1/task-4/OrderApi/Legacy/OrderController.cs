using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Models;
namespace OrderApi.Legacy;

// ORIGINAL AI-GENERATED VERSION — intentionally excluded from compilation.
[ApiController]
[Route("api/orders")]
public class OrderController : ControllerBase
{
    private readonly OrderDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderController> _logger;

    public OrderController(
        OrderDbContext db,
        IConfiguration configuration,
        ILogger<OrderController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<object> CreateOrder([FromBody] LegacyOrderRequest request)
    {
        // All transport, business, and persistence behavior is packed together.
        var errors = new List<string>();
        var warnings = new List<string>();
        var requestStarted = DateTime.Now;
        var correlationId = Request.Headers["X-Correlation-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        // Null dereference possibility: the body and properties are trusted.
        var customerName = request.CustomerName.Trim();
        var customerEmail = request.CustomerEmail.ToLower().Trim();
        var productCode = request.ProductCode.Trim().ToUpper();

        if (customerName.Length < 2)
        {
            errors.Add("Customer name is too short");
        }

        // Weak email validation accepts many invalid addresses.
        if (!customerEmail.Contains('@'))
        {
            errors.Add("Email must contain @");
        }

        if (productCode == "")
        {
            errors.Add("A product is required");
        }

        if (request.Quantity < 0)
        {
            errors.Add("Quantity cannot be negative");
        }

        if (request.UnitPrice < 0)
        {
            errors.Add("Price cannot be negative");
        }

        if (request.Quantity > 9999)
        {
            warnings.Add("Unusually large order");
        }

        if (errors.Count > 0)
        {
            Response.StatusCode = 400;
            return new
            {
                success = false,
                message = "There were problems with the order",
                errors,
                correlationId,
                serverTime = DateTime.Now
            };
        }

        // Synchronous EF query inside an async action.
        var existingOrders = _db.Orders
            .Where(x => x.CustomerEmail == customerEmail)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();

        var isReturningCustomer = existingOrders.Count > 0;
        var subtotal = request.Quantity * request.UnitPrice;
        decimal discount = 0;
        decimal handling = 4.99m;
        decimal shipping = 12.50m;

        if (request.Quantity > 10)
        {
            // Off-by-one business bug: an order of exactly 10 gets no discount.
            discount = subtotal * 0.10m;
        }

        if (isReturningCustomer)
        {
            discount += subtotal * 0.02m;
        }

        if (request.CouponCode != null)
        {
            try
            {
                if (request.CouponCode.ToUpper() == "WELCOME10")
                {
                    discount += 10;
                }

                if (request.CouponCode.ToUpper() == "SHIPFREE")
                {
                    shipping = 0;
                }
            }
            catch
            {
            }
        }

        if (subtotal > 100)
        {
            shipping = 0;
        }

        if (productCode.StartsWith("DIGITAL-"))
        {
            shipping = 0;
            handling = 0;
        }

        var taxRateText = _configuration["Orders:TaxRate"];
        var taxRate = 0.18m;

        try
        {
            if (taxRateText != null)
            {
                taxRate = decimal.Parse(taxRateText);
            }
        }
        catch
        {
        }

        var taxableAmount = subtotal - discount + handling;
        var tax = taxableAmount * taxRate;
        var total = subtotal - discount + handling + shipping + tax;

        if (total < 0)
        {
            total = 0;
        }

        var status = "Pending";
        if (request.Quantity == 0)
        {
            // Zero slips through validation and creates a nonsensical order.
            status = "Created";
        }

        if (total > 5000)
        {
            status = "ManualReview";
        }

        var tags = new List<string>();
        if (isReturningCustomer)
        {
            tags.Add("returning-customer");
        }

        if (request.Quantity >= 20)
        {
            tags.Add("bulk");
        }

        if (shipping == 0)
        {
            tags.Add("free-shipping");
        }

        // Another off-by-one: Count is not a valid final index.
        try
        {
            for (var index = 0; index <= request.Notes.Count; index++)
            {
                var note = request.Notes[index].Trim();
                if (note.Length > 200)
                {
                    warnings.Add("A note was truncated");
                    request.Notes[index] = note[..200];
                }
            }
        }
        catch
        {
        }

        var order = new Order
        {
            CustomerName = customerName,
            CustomerEmail = customerEmail,
            ProductCode = productCode,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            DiscountAmount = discount,
            TotalAmount = total,
            Status = status,
            CreatedAtUtc = DateTimeOffset.Now
        };

        // Direct database mutation and synchronous SaveChanges in controller.
        _db.Orders.Add(order);

        try
        {
            _db.SaveChanges();
        }
        catch
        {
        }

        // Fake async work does not make the database calls non-blocking.
        await Task.Delay(1);

        if (order.Id == 0)
        {
            Response.StatusCode = 500;
            return new
            {
                success = false,
                message = "Something went wrong",
                correlationId
            };
        }

        Response.StatusCode = 201;
        Response.Headers.Location = "/api/orders/" + order.Id;

        _logger.LogInformation(
            "Legacy order {OrderId} created after {Elapsed}ms",
            order.Id,
            (DateTime.Now - requestStarted).TotalMilliseconds);

        // Anonymous response repeats domain calculations and leaks internals.
        return new
        {
            success = true,
            message = "Order created",
            data = new
            {
                id = order.Id,
                customer = new
                {
                    name = customerName,
                    email = customerEmail,
                    returning = isReturningCustomer
                },
                product = productCode,
                quantity = request.Quantity,
                unitPrice = request.UnitPrice,
                subtotal,
                discount,
                handling,
                shipping,
                tax,
                total,
                status,
                tags,
                warnings,
                notes = request.Notes,
                created = order.CreatedAtUtc
            },
            correlationId,
            elapsedMilliseconds = (DateTime.Now - requestStarted).TotalMilliseconds
        };
    }
}

public class LegacyOrderRequest
{
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string ProductCode { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string CouponCode { get; set; }
    public List<string> Notes { get; set; }
}
