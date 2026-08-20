namespace QueryTranslationDemo;

// The projection target: only the columns a caller who just needs a product summary
// actually needs. This is not an entity - EF Core never tracks instances of this type,
// which is also why projecting avoids change-tracker cost (see Day 10 Task 1).
public class ProductSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}
