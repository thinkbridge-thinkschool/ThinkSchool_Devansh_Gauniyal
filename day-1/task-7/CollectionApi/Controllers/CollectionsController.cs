using CollectionApi.Dtos;
using CollectionApi.Exceptions;
using CollectionApi.Models;
using CollectionApi.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace CollectionApi.Controllers;

[ApiController]
[Route("api/collections")]
public sealed class CollectionsController(ICollectionRepository repository) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CollectionResponse>> Create(
        CreateCollectionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var collection = new Collection(request.Name, request.OwnerId);
            await repository.AddAsync(collection, cancellationToken);
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
        var collection = await repository.GetByIdAsync(id, cancellationToken);
        if (collection is null)
        {
            return NotFound();
        }

        try
        {
            collection.AddItem(request.QuoteId);
            await repository.UpdateAsync(collection, cancellationToken);
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
        var collection = await repository.GetByIdAsync(id, cancellationToken);
        if (collection is null || !collection.RemoveItem(quoteId))
        {
            return NotFound();
        }

        await repository.UpdateAsync(collection, cancellationToken);
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
