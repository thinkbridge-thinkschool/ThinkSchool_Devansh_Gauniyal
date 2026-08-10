using System.ComponentModel.DataAnnotations;

namespace OrderApi.DTOs;

public sealed class CreateOrderRequest
{
    [Required, StringLength(100)]
    public string? CustomerName { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? CustomerEmail { get; init; }

    [Required, StringLength(40)]
    public string? ProductCode { get; init; }

    [Range(1, 100)]
    public int Quantity { get; init; }

    [Range(typeof(decimal), "0.01", "100000.00")]
    public decimal UnitPrice { get; init; }
}
