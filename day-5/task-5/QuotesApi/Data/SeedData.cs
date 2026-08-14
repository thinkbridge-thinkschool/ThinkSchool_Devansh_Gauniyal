namespace QuotesApi.Data;

public static class SeedData
{
    // Public so tests can assert against the exact seeded shape instead of a magic number.
    public const int AuthorCount = 30;
    public const int BooksPerAuthor = 4;

    public static void Seed(AppDbContext db)
    {
        for (var a = 1; a <= AuthorCount; a++)
        {
            var author = new Author { Name = $"Author {a}" };
            for (var b = 1; b <= BooksPerAuthor; b++)
            {
                author.Books.Add(new Book { Title = $"Book {b} by Author {a}" });
            }

            db.Authors.Add(author);
        }

        db.SaveChanges();
    }

    public static void EnsureSeeded(AppDbContext db)
    {
        db.Database.EnsureCreated();
        if (db.Authors.Any())
        {
            return;
        }

        Seed(db);
    }
}
