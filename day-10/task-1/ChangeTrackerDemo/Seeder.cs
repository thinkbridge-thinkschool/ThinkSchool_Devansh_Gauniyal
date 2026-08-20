namespace ChangeTrackerDemo;

public static class Seeder
{
    public const int RowCount = 10_000;

    private static readonly string[] Categories =
    {
        "Kitchen", "Outdoors", "Stationery", "Electronics", "Toys", "Garden", "Fitness", "Automotive"
    };

    // Deterministic (fixed random seed) and safely re-runnable: if the table doesn't
    // already hold exactly rowCount rows, it is cleared and reseeded from scratch, so
    // re-running against the same database file always ends in the same state.
    public static void SeedIfNeeded(CatalogContext context, int rowCount = RowCount)
    {
        context.Database.EnsureCreated();

        if (context.Products.Count() == rowCount)
        {
            return;
        }

        context.Products.RemoveRange(context.Products);
        context.SaveChanges();

        var random = new Random(42);
        var products = new List<Product>(rowCount);
        for (int i = 1; i <= rowCount; i++)
        {
            var category = Categories[i % Categories.Length];
            products.Add(new Product
            {
                Id = i,
                Name = $"Product {i:D5}",
                Category = category,
                Price = Math.Round((decimal)(random.NextDouble() * 500 + 1), 2),
                Description = $"Synthetic catalogue row {i:D5} in category {category}. " +
                              "Generated for the Day 10 change-tracker benchmark; not real-world data."
            });
        }

        context.Products.AddRange(products);
        context.SaveChanges();
    }
}
