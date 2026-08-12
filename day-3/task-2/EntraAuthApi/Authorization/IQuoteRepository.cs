namespace EntraAuthApi.Authorization;

public interface IQuoteRepository
{
    QuoteResource? Find(int id);
}
