namespace QuotesApi.Queries;

public sealed record AuthorSummary(int Id, string Name, List<string> BookTitles);
