namespace QueryTranslationDemo;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public decimal Price { get; set; }
    public DateTime CreatedDate { get; set; }

    // Deliberately large: makes the difference between a whole-entity read and a
    // narrow projection visible in the generated SQL's column list, not just theoretical.
    public string Description { get; set; } = string.Empty;
}
