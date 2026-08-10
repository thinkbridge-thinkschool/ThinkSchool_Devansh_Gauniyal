using Microsoft.AspNetCore.Mvc;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Services;

namespace OrderApi.Controllers;

[ApiController]
[Route("api/orders")]
public sealed class OrdersController(
    IOrderService orderService,
    ILogger<OrdersController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await orderService.CreateAsync(request, cancellationToken);
            return Created($"/api/orders/{response.Id}", response);
        }
        catch (OrderValidationException exception)
        {
            logger.LogWarning(exception, "Order request failed a business rule");
            ModelState.AddModelError(nameof(request), exception.Message);
            return ValidationProblem(ModelState);
        }
        catch (OrderPersistenceException exception)
        {
            logger.LogError(exception, "Order creation is unavailable because persistence failed");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Order service unavailable",
                detail: "The order could not be saved. Please try again later.");
        }
    }
}
