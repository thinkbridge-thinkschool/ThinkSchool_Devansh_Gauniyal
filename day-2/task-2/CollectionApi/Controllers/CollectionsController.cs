using CollectionApi.Dtos;
using CollectionApi.Exceptions;
using CollectionApi.Models;
using CollectionApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace CollectionApi.Controllers;

[ApiController]
[Route("api/collections")]
public sealed class CollectionsController(ICollectionService service) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CollectionResponse>> Create(
        CreateCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await service.CreateAsync(
                request.Name,
                request.OwnerId,
                cancellationToken);
            return Created($"/api/collections/{collection.Id}", ToResponse(collection));
        }
        catch (CollectionInvariantException exception)
        {
            return InvariantProblem(exception);
        }
    }

    [HttpPost("{id:int}/items")]
    public async Task<ActionResult<CollectionResponse>> AddQuote(
        int id,
        AddQuoteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = await service.AddQuoteAsync(
                id,
                request.QuoteId,
                cancellationToken);
            if (collection is null)
            {
                return NotFound();
            }

            return Ok(ToResponse(collection));
        }
        catch (CollectionInvariantException exception)
        {
            return InvariantProblem(exception);
        }
    }

    [HttpDelete("{id:int}/items/{quoteId:int}")]
    public async Task<IActionResult> RemoveQuote(
        int id,
        int quoteId,
        CancellationToken cancellationToken)
    {
        var removed = await service.RemoveQuoteAsync(id, quoteId, cancellationToken);
        if (!removed)
        {
            return NotFound();
        }

        return NoContent();
    }

    private ObjectResult InvariantProblem(CollectionInvariantException exception) => Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Collection invariant violated",
        detail: exception.Message);

    private static CollectionResponse ToResponse(Collection collection) => new(
        collection.Id,
        collection.Name,
        collection.OwnerId,
        collection.Items
            .Select(item => new CollectionItemResponse(item.QuoteId, item.AddedAt))
            .ToArray());
}
