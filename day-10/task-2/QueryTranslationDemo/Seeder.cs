namespace QueryTranslationDemo;

public static class Seeder
{
    public const int ProductCount = 300;

    private static readonly string[] CategoryNames =
    {
        "Kitchen", "Outdoors", "Stationery", "Electronics", "Toys", "Garden"
    };

    // Deterministic (fixed random seed) and safely re-runnable: if the table doesn't
    // already hold exactly ProductCount rows, it is cleared and reseeded from scratch.
    public static void SeedIfNeeded(CatalogContext context)
    {
        context.Database.EnsureCreated();

        if (context.Products.Count() == ProductCount)
        {
            return;
        }

        context.Products.RemoveRange(context.Products);
        context.Categories.RemoveRange(context.Categories);
        context.SaveChanges();

        var categories = CategoryNames.Select(name => new Category { Name = name }).ToList();
        context.Categories.AddRange(categories);
        context.SaveChanges();

        var random = new Random(42);
        var products = new List<Product>(ProductCount);
        for (int i = 1; i <= ProductCount; i++)
        {
            var category = categories[i % categories.Count];
            var name = $"Product {i:D5}";
            if (i % 10 == 0)
            {
                // Deterministic subset carries "Premium" in the name, so the
                // client-side-evaluation queries below have a real, non-empty result set.
                name += " Premium Edition";
            }

            products.Add(new Product
            {
                Id = i,
                Name = name,
                CategoryId = category.Id,
                Price = Math.Round((decimal)(random.NextDouble() * 500 + 1), 2),
                CreatedDate = new DateTime(2026, 1, 1).AddDays(i),
                Description = string.Concat(Enumerable.Repeat($"Synthetic catalogue notes for {name}. ", 20))
            });
        }

        context.Products.AddRange(products);
        context.SaveChanges();
    }
}
