using Microsoft.EntityFrameworkCore;

namespace QueryTranslationDemo;

public static class Queries
{
    // Deliberately opaque to EF Core: a plain C# static method with no SQL mapping.
    // EF Core does not inline arbitrary user method bodies into SQL, so calling this
    // from inside a Where(...) predicate cannot be translated.
    private static bool IsPremiumName(string name) => name.ToUpperInvariant().Contains("PREMIUM");

    // BEFORE: pulls whole Product entities, including the large Description column.
    public static List<Product> ReadProductsAboveMinPrice_WholeEntities(CatalogContext context, decimal minPrice)
    {
        return context.Products
            .Where(p => p.Price > minPrice)
            .OrderBy(p => p.Id)
            .ToList();
    }

    // AFTER: same filter, same ordering - only the columns actually needed, including a
    // joined Category.Name, and never the Description column. Differs from the BEFORE
    // query ONLY in this projection.
    public static List<ProductSummaryDto> ReadProductsAboveMinPrice_Projected(CatalogContext context, decimal minPrice)
    {
        return context.Products
            .Where(p => p.Price > minPrice)
            .OrderBy(p => p.Id)
            .Select(p => new ProductSummaryDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                CategoryName = p.Category.Name
            })
            .ToList();
    }

    // BROKEN: IsPremiumName is a plain C# method EF Core cannot translate. Since EF
    // Core 3.0 this throws InvalidOperationException at enumeration time instead of
    // silently degrading to a client-side filter.
    public static List<Product> ReadProducts_BrokenUntranslatablePredicate(CatalogContext context)
    {
        return context.Products
            .Where(p => IsPremiumName(p.Name))
            .ToList();
    }

    // FIXED: same intent, expressed with operators EF Core CAN translate (ToUpper and
    // Contains map to SQL UPPER/LIKE), so the filter runs in the database.
    public static List<Product> ReadProducts_FixedTranslatablePredicate(CatalogContext context)
    {
        return context.Products
            .Where(p => p.Name.ToUpper().Contains("PREMIUM"))
            .ToList();
    }

    // Explicit client-side boundary: AsEnumerable() switches to LINQ-to-Objects BEFORE
    // the Where runs, so the untranslatable predicate becomes legal here - but every row
    // is pulled into memory first, then filtered in process.
    public static List<Product> ReadProducts_AsEnumerableClientSideBoundary(CatalogContext context)
    {
        return context.Products
            .AsEnumerable()
            .Where(p => IsPremiumName(p.Name))
            .ToList();
    }
}
